using System;

namespace RoasSensor.Internal
{
    /// <summary>
    /// Sessions — one continuous visit, so the backend can replay a path rather than hold a
    /// bag of unordered touches. The rules are mirrored DELIBERATELY from the web SDK's
    /// <c>session.ts</c> and the native Kotlin/Swift <c>SessionTracker</c>s, and must not
    /// drift: a customer with a website and an app compares the two in one dashboard, and a
    /// session that means "30 idle minutes" on web but something else here makes that
    /// comparison quietly meaningless.
    ///
    ///  * 30 minutes of inactivity ends a session;
    ///  * so does the visitor's LOCAL midnight — theirs, not UTC's;
    ///  * <c>sequence</c> is assigned HERE, on the device, because beacons race: the order
    ///    they arrive in over the network is not the order they happened in, so a server
    ///    timestamp would reconstruct the wrong path.
    ///
    /// State lives in <see cref="RoasStorage"/> rather than memory, because a mobile OS
    /// (and Unity's own domain reload / app-pause behaviour) can tear the process down
    /// between two foreground stretches — a session that lived only in RAM would restart
    /// every time, inflating session counts and destroying the retention numbers sessions
    /// exist to produce.
    /// </summary>
    internal sealed class RoasSessionTracker
    {
        /// <summary>Same 30 minutes as every other platform's session.</summary>
        private const long IdleMs = 30 * 60 * 1000L;

        private readonly RoasStorage _storage;
        private readonly object _lock = new object();

        /// <summary>When the current foreground stretch began (0 = backgrounded). In memory
        /// on purpose — a stretch cannot span a process death.</summary>
        private long _foregroundSinceMs;

        public RoasSessionTracker(RoasStorage storage)
        {
            _storage = storage;
        }

        public readonly struct Session
        {
            public readonly string Id;
            public readonly int Number;
            public readonly string PvId;
            public readonly bool Started;

            public Session(string id, int number, string pvId, bool started)
            {
                Id = id;
                Number = number;
                PvId = pvId;
                Started = started;
            }
        }

        /// <summary>
        /// The live session, rolling it over when stale. Callers should send a
        /// session-start beacon only when <see cref="Session.Started"/> comes back true —
        /// otherwise every foreground would write a duplicate touch and dilute every
        /// multi-touch credit split.
        /// </summary>
        public Session Current()
        {
            lock (_lock)
            {
                var now = NowMs();
                var id = _storage.SessionId;
                var alive = !string.IsNullOrEmpty(id)
                    && now - _storage.SessionLastActiveAtMs < IdleMs
                    && _storage.SessionDay == LocalDay();
                if (alive)
                {
                    return new Session(id, _storage.SessionNumber, _storage.SessionPvId, started: false);
                }

                var fresh = Guid.NewGuid().ToString();
                _storage.SessionId = fresh;
                _storage.SessionNumber = _storage.SessionNumber + 1;
                // One pv_id per session — the backend UPSERTS on it, so the session's
                // closing beacon folds its foreground time into the row the opening
                // beacon created instead of writing a second touch.
                _storage.SessionPvId = "pv" + Guid.NewGuid().ToString("N");
                _storage.SessionSequence = 0;
                _storage.SessionForegroundMs = 0L;
                _storage.SessionDay = LocalDay();
                _storage.SessionLastActiveAtMs = now;
                return new Session(fresh, _storage.SessionNumber, _storage.SessionPvId, started: true);
            }
        }

        /// <summary>This event's index within the session, and a touch of the idle clock.</summary>
        public int NextSequence()
        {
            lock (_lock)
            {
                Current(); // roll over first, so an event after 30 idle minutes starts a new session
                var next = _storage.SessionSequence + 1;
                _storage.SessionSequence = next;
                _storage.SessionLastActiveAtMs = NowMs();
                return next;
            }
        }

        /// <summary>The app came to the front. Call AFTER <see cref="Current"/>, so the
        /// rollover decision still sees how long the app was away.</summary>
        public void MarkForeground()
        {
            lock (_lock) { _foregroundSinceMs = NowMs(); }
        }

        /// <summary>
        /// The app went to the background. Returns the session's TOTAL foreground
        /// milliseconds so far — cumulative, not per-stretch, because a session routinely
        /// spans several foreground stretches. The backend folds it in with a MAX, so
        /// re-reporting a total is safe.
        /// </summary>
        public long MarkBackground()
        {
            lock (_lock)
            {
                var now = NowMs();
                if (_foregroundSinceMs > 0L)
                {
                    _storage.SessionForegroundMs += now - _foregroundSinceMs;
                    _foregroundSinceMs = 0L;
                }
                // The idle clock runs from when they LEFT, not from their last event —
                // otherwise an app left open in the background would keep one session
                // alive indefinitely.
                _storage.SessionLastActiveAtMs = now;
                return _storage.SessionForegroundMs;
            }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>"yyyy-M-d" in the DEVICE's local timezone, not zero-padded — the exact
        /// format every other ROASSensor SDK uses, so the day string can never mismatch
        /// on a `2026-9-5` vs `2026-09-05` technicality.</summary>
        private static string LocalDay()
        {
            var now = DateTime.Now;
            return now.Year + "-" + now.Month + "-" + now.Day;
        }
    }
}
