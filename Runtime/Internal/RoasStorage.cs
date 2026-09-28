using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoasSensor.Internal
{
    /// <summary>
    /// On-device state: the stable visitor id, the "install already reported" flag,
    /// session state, and a persisted beacon queue — the Unity analogue of the
    /// Kotlin SDK's <c>Storage.kt</c> (SharedPreferences) and the Swift SDK's
    /// <c>Storage.swift</c> (UserDefaults).
    ///
    /// Backed by <see cref="PlayerPrefs"/> because it is the one storage guaranteed on
    /// every Unity target (desktop, console, mobile) without an extra native plugin.
    /// The queue survives process death on purpose: an install that happens with no
    /// network must still be reported on the next launch, never lost.
    /// </summary>
    internal sealed class RoasStorage
    {
        private const string KeyVid = "roas_vid";
        private const string KeyInstallReported = "roas_install_reported";
        private const string KeyClockOffset = "roas_clock_offset";
        private const string KeyFirstInstallTime = "roas_first_install_time";

        private const string KeySessionId = "roas_session_id";
        private const string KeySessionNumber = "roas_session_number";
        private const string KeySessionPvId = "roas_session_pv_id";
        private const string KeySessionSequence = "roas_session_sequence";
        private const string KeySessionForegroundMs = "roas_session_foreground_ms";
        private const string KeySessionDay = "roas_session_day";
        private const string KeySessionLastActive = "roas_session_last_active";

        private const string KeyQueueCount = "roas_queue_count";
        private const string KeyQueueUrlPrefix = "roas_queue_url_";
        private const string KeyQueuePathPrefix = "roas_queue_path_";
        private const string KeyQueueBodyPrefix = "roas_queue_body_";

        private const int MaxQueue = 500;

        private static readonly object Lock = new object();

        public string VisitorId
        {
            get
            {
                lock (Lock)
                {
                    var existing = PlayerPrefs.GetString(KeyVid, string.Empty);
                    if (!string.IsNullOrEmpty(existing)) return existing;
                    var vid = "rs" + Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(KeyVid, vid);
                    PlayerPrefs.Save();
                    return vid;
                }
            }
        }

        public bool InstallReported
        {
            get => PlayerPrefs.GetInt(KeyInstallReported, 0) == 1;
            set { lock (Lock) { PlayerPrefs.SetInt(KeyInstallReported, value ? 1 : 0); PlayerPrefs.Save(); } }
        }

        /// <summary>Seconds to ADD to this device's clock to get server time, learned
        /// from the HTTP response's Date header. Persisted for the same reason the
        /// native SDKs persist it: the very first beacon of a cold launch is the
        /// install, and a handset with a badly wrong clock would have it refused.</summary>
        public long ClockOffsetSeconds
        {
            get => long.Parse(PlayerPrefs.GetString(KeyClockOffset, "0"));
            set { lock (Lock) { PlayerPrefs.SetString(KeyClockOffset, value.ToString()); PlayerPrefs.Save(); } }
        }

        /// <summary>An OS-level install anchor (Android's <c>firstInstallTime</c>, iOS's
        /// Documents-directory creation date) so a reinstall that somehow inherited old
        /// persisted data — a cloud backup/restore — can be told apart from a genuinely
        /// continuing install. 0 means "never recorded".</summary>
        public double FirstInstallAnchor
        {
            get => double.Parse(PlayerPrefs.GetString(KeyFirstInstallTime, "0"));
            set { lock (Lock) { PlayerPrefs.SetString(KeyFirstInstallTime, value.ToString("R")); PlayerPrefs.Save(); } }
        }

        // ── Session state (see RoasSessionTracker) ──────────────────────────────

        public string SessionId
        {
            get => PlayerPrefs.GetString(KeySessionId, string.Empty);
            set { lock (Lock) { PlayerPrefs.SetString(KeySessionId, value); PlayerPrefs.Save(); } }
        }

        public int SessionNumber
        {
            get => PlayerPrefs.GetInt(KeySessionNumber, 0);
            set { lock (Lock) { PlayerPrefs.SetInt(KeySessionNumber, value); PlayerPrefs.Save(); } }
        }

        public string SessionPvId
        {
            get => PlayerPrefs.GetString(KeySessionPvId, string.Empty);
            set { lock (Lock) { PlayerPrefs.SetString(KeySessionPvId, value); PlayerPrefs.Save(); } }
        }

        public int SessionSequence
        {
            get => PlayerPrefs.GetInt(KeySessionSequence, 0);
            set { lock (Lock) { PlayerPrefs.SetInt(KeySessionSequence, value); PlayerPrefs.Save(); } }
        }

        public long SessionForegroundMs
        {
            get => long.Parse(PlayerPrefs.GetString(KeySessionForegroundMs, "0"));
            set { lock (Lock) { PlayerPrefs.SetString(KeySessionForegroundMs, value.ToString()); PlayerPrefs.Save(); } }
        }

        /// <summary>The visitor's local calendar day ("yyyy-M-d", not zero-padded — same
        /// format the web/Android/iOS SDKs use), for the midnight rollover.</summary>
        public string SessionDay
        {
            get => PlayerPrefs.GetString(KeySessionDay, string.Empty);
            set { lock (Lock) { PlayerPrefs.SetString(KeySessionDay, value); PlayerPrefs.Save(); } }
        }

        public long SessionLastActiveAtMs
        {
            get => long.Parse(PlayerPrefs.GetString(KeySessionLastActive, "0"));
            set { lock (Lock) { PlayerPrefs.SetString(KeySessionLastActive, value.ToString()); PlayerPrefs.Save(); } }
        }

        /// <summary>
        /// Wipe every field tied to "this specific install" — vid, install/session
        /// state and the pending beacon queue — mirroring the native SDKs'
        /// <c>resetForNewInstall()</c>. Called only when an OS-level install anchor
        /// proves this process is not the install our persisted state thinks it is.
        /// </summary>
        public void ResetForNewInstall()
        {
            lock (Lock)
            {
                PlayerPrefs.DeleteKey(KeyVid);
                PlayerPrefs.DeleteKey(KeyInstallReported);
                PlayerPrefs.DeleteKey(KeySessionId);
                PlayerPrefs.DeleteKey(KeySessionNumber);
                PlayerPrefs.DeleteKey(KeySessionPvId);
                PlayerPrefs.DeleteKey(KeySessionSequence);
                PlayerPrefs.DeleteKey(KeySessionForegroundMs);
                PlayerPrefs.DeleteKey(KeySessionDay);
                PlayerPrefs.DeleteKey(KeySessionLastActive);
                ClearQueue();
                PlayerPrefs.Save();
            }
        }

        // ── Persisted beacon queue ───────────────────────────────────────────────

        public readonly struct QueueEntry
        {
            public readonly string Url;
            public readonly string Path;
            public readonly string Body;

            public QueueEntry(string url, string path, string body)
            {
                Url = url;
                Path = path;
                Body = body;
            }
        }

        public void Enqueue(string url, string path, string body)
        {
            lock (Lock)
            {
                var count = PlayerPrefs.GetInt(KeyQueueCount, 0);
                if (count >= MaxQueue)
                {
                    // Bounded so a permanently-offline device can't grow this without
                    // limit — oldest drops first, same policy as the native SDKs.
                    ShiftLeft();
                    count--;
                }
                PlayerPrefs.SetString(KeyQueueUrlPrefix + count, url);
                PlayerPrefs.SetString(KeyQueuePathPrefix + count, path);
                PlayerPrefs.SetString(KeyQueueBodyPrefix + count, body);
                PlayerPrefs.SetInt(KeyQueueCount, count + 1);
                PlayerPrefs.Save();
            }
        }

        public List<QueueEntry> QueuedBeacons()
        {
            lock (Lock)
            {
                var count = PlayerPrefs.GetInt(KeyQueueCount, 0);
                var list = new List<QueueEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    list.Add(new QueueEntry(
                        PlayerPrefs.GetString(KeyQueueUrlPrefix + i, string.Empty),
                        PlayerPrefs.GetString(KeyQueuePathPrefix + i, string.Empty),
                        PlayerPrefs.GetString(KeyQueueBodyPrefix + i, string.Empty)));
                }
                return list;
            }
        }

        /// <summary>
        /// Remove exactly the entries at <paramref name="deliveredIndexes"/> from whatever
        /// the queue holds RIGHT NOW, re-reading fresh rather than trusting a snapshot taken
        /// before delivery — the same "read and write in one locked step" fix the Kotlin
        /// SDK's <c>Storage.removeDelivered</c> needed after a real bug where two sends
        /// fired back-to-back raced each other and silently dropped one.
        /// </summary>
        public void RemoveDelivered(HashSet<int> deliveredIndexes)
        {
            if (deliveredIndexes.Count == 0) return;
            lock (Lock)
            {
                var count = PlayerPrefs.GetInt(KeyQueueCount, 0);
                var remaining = new List<QueueEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    if (deliveredIndexes.Contains(i)) continue;
                    remaining.Add(new QueueEntry(
                        PlayerPrefs.GetString(KeyQueueUrlPrefix + i, string.Empty),
                        PlayerPrefs.GetString(KeyQueuePathPrefix + i, string.Empty),
                        PlayerPrefs.GetString(KeyQueueBodyPrefix + i, string.Empty)));
                }
                ClearQueue();
                for (int i = 0; i < remaining.Count; i++)
                {
                    PlayerPrefs.SetString(KeyQueueUrlPrefix + i, remaining[i].Url);
                    PlayerPrefs.SetString(KeyQueuePathPrefix + i, remaining[i].Path);
                    PlayerPrefs.SetString(KeyQueueBodyPrefix + i, remaining[i].Body);
                }
                PlayerPrefs.SetInt(KeyQueueCount, remaining.Count);
                PlayerPrefs.Save();
            }
        }

        private void ClearQueue()
        {
            var count = PlayerPrefs.GetInt(KeyQueueCount, 0);
            for (int i = 0; i < count; i++)
            {
                PlayerPrefs.DeleteKey(KeyQueueUrlPrefix + i);
                PlayerPrefs.DeleteKey(KeyQueuePathPrefix + i);
                PlayerPrefs.DeleteKey(KeyQueueBodyPrefix + i);
            }
            PlayerPrefs.DeleteKey(KeyQueueCount);
        }

        private void ShiftLeft()
        {
            var count = PlayerPrefs.GetInt(KeyQueueCount, 0);
            for (int i = 1; i < count; i++)
            {
                PlayerPrefs.SetString(KeyQueueUrlPrefix + (i - 1), PlayerPrefs.GetString(KeyQueueUrlPrefix + i, string.Empty));
                PlayerPrefs.SetString(KeyQueuePathPrefix + (i - 1), PlayerPrefs.GetString(KeyQueuePathPrefix + i, string.Empty));
                PlayerPrefs.SetString(KeyQueueBodyPrefix + (i - 1), PlayerPrefs.GetString(KeyQueueBodyPrefix + i, string.Empty));
            }
            PlayerPrefs.DeleteKey(KeyQueueUrlPrefix + (count - 1));
            PlayerPrefs.DeleteKey(KeyQueuePathPrefix + (count - 1));
            PlayerPrefs.DeleteKey(KeyQueueBodyPrefix + (count - 1));
            PlayerPrefs.SetInt(KeyQueueCount, count - 1);
        }
    }
}
