using NUnit.Framework;
using RoasSensor.Internal;
using UnityEngine;

namespace RoasSensor.Tests
{
    /// <summary>
    /// Uses real PlayerPrefs (Editor prefs) since RoasStorage has no in-memory test seam --
    /// matching the intent, if not the mechanism, of the Kotlin SDK's Robolectric-backed
    /// SharedPreferences tests. Every test clears its own keys first/last so the suite is
    /// order-independent.
    /// </summary>
    public sealed class RoasSessionTrackerTests
    {
        [SetUp]
        public void ClearPrefs() => PlayerPrefs.DeleteAll();

        [TearDown]
        public void ClearPrefsAfter() => PlayerPrefs.DeleteAll();

        [Test]
        public void Current_FirstCallStartsANewSession()
        {
            var tracker = new RoasSessionTracker(new RoasStorage());
            var session = tracker.Current();
            Assert.IsTrue(session.Started);
            Assert.AreEqual(1, session.Number);
            Assert.IsNotEmpty(session.Id);
            Assert.IsTrue(session.PvId.StartsWith("pv"));
        }

        [Test]
        public void Current_SecondCallWithinIdleWindowReusesSession()
        {
            var tracker = new RoasSessionTracker(new RoasStorage());
            var first = tracker.Current();
            var second = tracker.Current();
            Assert.IsFalse(second.Started);
            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(first.Number, second.Number);
        }

        [Test]
        public void NextSequence_IncrementsPerCall()
        {
            var tracker = new RoasSessionTracker(new RoasStorage());
            tracker.Current();
            var first = tracker.NextSequence();
            var second = tracker.NextSequence();
            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
        }

        [Test]
        public void MarkBackground_AccumulatesForegroundTimeAcrossStretches()
        {
            var tracker = new RoasSessionTracker(new RoasStorage());
            tracker.Current();
            tracker.MarkForeground();
            System.Threading.Thread.Sleep(10);
            var firstTotal = tracker.MarkBackground();
            Assert.Greater(firstTotal, 0);

            tracker.MarkForeground();
            System.Threading.Thread.Sleep(10);
            var secondTotal = tracker.MarkBackground();
            // Cumulative, not per-stretch -- the second total must be at least the first.
            Assert.GreaterOrEqual(secondTotal, firstTotal);
        }

        [Test]
        public void Current_StaleSessionRollsOverToANewOne()
        {
            var storage = new RoasStorage();
            var tracker = new RoasSessionTracker(storage);
            var first = tracker.Current();

            // Simulate 31 minutes of inactivity by rewinding the stored last-active timestamp.
            storage.SessionLastActiveAtMs -= 31 * 60 * 1000L;

            var second = tracker.Current();
            Assert.IsTrue(second.Started);
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(2, second.Number);
        }
    }
}
