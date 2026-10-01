using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RoasSensor.Internal
{
    /// <summary>
    /// Delivers beacons: enqueue (persisted), then flush on a coroutine. Delivery is
    /// at-least-once and idempotent — the server dedupes on <c>pv_id</c>/<c>external_id</c> —
    /// so retrying a first-open after a flaky network is always safe. Mirrors the Kotlin
    /// SDK's <c>Transport.kt</c> as closely as Unity's main-thread-only networking allows:
    /// where the native SDKs use a background thread, this uses a coroutine driven by
    /// <see cref="RoasRuntime"/>, since <see cref="UnityWebRequest"/> must be pumped from
    /// the main thread.
    /// </summary>
    internal sealed class RoasTransport
    {
        private const string Tag = "[RoasSensor]";
        private const long ClockSkewThresholdSeconds = 30L;

        private readonly string _baseUrl;
        private readonly RoasStorage _storage;
        private readonly string _appSecret;
        private readonly RoasRuntime _runtime;
        private bool _flushing;

        public RoasLogLevel LogLevel = RoasLogLevel.Error;
        public Action<string, bool, string> DeliveryCallback;

        public RoasTransport(string baseUrl, RoasStorage storage, string appSecret, RoasRuntime runtime)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _storage = storage;
            _appSecret = appSecret;
            _runtime = runtime;
        }

        public void Send(string path, RoasJson body)
        {
            var bodyString = body.ToString();
            _storage.Enqueue(_baseUrl + path, path, bodyString);
            Flush();
        }

        public void Flush()
        {
            if (_flushing) return;
            _runtime.StartCoroutine(FlushCoroutine());
        }

        private IEnumerator FlushCoroutine()
        {
            _flushing = true;
            // Looped rather than a single pass: Send() enqueues unconditionally and then
            // calls Flush(), which is a no-op while _flushing is already true (confirmed on a
            // real device -- HandleDeepLink()'s beacon, enqueued while the session-resume
            // identify() calls were already mid-flush, sat in storage for an entire extra app
            // background/foreground cycle before it finally went out, since nothing else
            // happened to call Send()/Flush() again in between). A single pass only ever sees
            // the snapshot taken at the top, so anything enqueued during it would otherwise
            // wait for some unrelated later call to flush it. Looping until a pass neither
            // delivers nor finds anything new closes that gap without changing the "ignore a
            // concurrent Flush() call" contract Send()'s callers rely on.
            while (true)
            {
                var entries = _storage.QueuedBeacons();
                if (entries.Count == 0) break;
                var delivered = new HashSet<int>();
                for (int i = 0; i < entries.Count; i++)
                {
                    yield return PostCoroutine(entries[i], i, delivered, isClockRetry: false);
                }
                // Re-read-and-remove at removal time, not from the snapshot taken above — two
                // Send() calls fired back-to-back (an app_open + its deferred-link probe,
                // exactly like RoasSessionTracker's callers do) can enqueue between this flush
                // starting and finishing; removing by original index against a fresh read
                // would be wrong, so RemoveDelivered re-reads under its own lock and only the
                // entries actually delivered are ever dropped.
                _storage.RemoveDelivered(delivered);
                if (delivered.Count == 0) break; // every remaining entry is retry-later (5xx/network); stop spinning
            }
            _flushing = false;
        }

        private IEnumerator PostCoroutine(RoasStorage.QueueEntry entry, int index, HashSet<int> delivered, bool isClockRetry)
        {
            var payload = Encoding.UTF8.GetBytes(entry.Body);
            using (var request = new UnityWebRequest(entry.Url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;

                // Signed HERE, at transmit, not at enqueue — a queued beacon may sit on the
                // device for days waiting for a network, and a signature minted at enqueue
                // time would be long outside the server's freshness window by the time it
                // actually went out.
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _storage.ClockOffsetSeconds;
                var signature = RoasSigner.Sign(_appSecret, payload, now);
                if (signature != null) request.SetRequestHeader(RoasSigner.Header, signature);

                yield return request.SendWebRequest();

                var code = (int)request.responseCode;
                var corrected = LearnClockOffset(request);

                if (code == 401 && corrected && !isClockRetry)
                {
                    if (LogLevel >= RoasLogLevel.Debug) Debug.Log($"{Tag} {entry.Path} -> 401; re-signing with corrected clock");
                    yield return PostCoroutine(entry, index, delivered, isClockRetry: true);
                    yield break;
                }

                var networkError = request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.DataProcessingError;
                // 2xx delivered; 4xx = the server rejected it (bad/duplicate), so drop it
                // rather than retry forever. Only 5xx and network errors retry.
                var wasDelivered = !networkError && code > 0 && code < 500;

                if (wasDelivered) delivered.Add(index);

                if (LogLevel >= RoasLogLevel.Debug || (!wasDelivered && LogLevel >= RoasLogLevel.Error))
                {
                    Debug.Log($"{Tag} {entry.Path} -> {(networkError ? request.error : "HTTP " + code)}{(wasDelivered ? "" : " (will retry)")}");
                }
                DeliveryCallback?.Invoke(entry.Path, wasDelivered, wasDelivered ? null : (networkError ? request.error : "HTTP " + code));
            }
        }

        /// <summary>Learn the device-to-server clock delta from the response's Date header.
        /// Returns true when the correction MOVED, which is what makes a 401 worth one retry.</summary>
        private bool LearnClockOffset(UnityWebRequest request)
        {
            var dateHeader = request.GetResponseHeader("Date");
            if (string.IsNullOrEmpty(dateHeader)) return false;
            if (!DateTimeOffset.TryParse(dateHeader, out var serverTime)) return false;
            var offset = serverTime.ToUnixTimeSeconds() - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var previous = _storage.ClockOffsetSeconds;
            if (Math.Abs(offset - previous) < ClockSkewThresholdSeconds) return false;
            _storage.ClockOffsetSeconds = offset;
            if (LogLevel >= RoasLogLevel.Debug) Debug.Log($"{Tag} clock offset {previous}s -> {offset}s (from server Date)");
            return true;
        }
    }
}
