using NUnit.Framework;
using RoasSensor.Internal;

namespace RoasSensor.Tests
{
    public sealed class RoasHostsTests
    {
        [Test]
        public void Normalize_BlankMeansProduction()
        {
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize(null));
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize(""));
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize("   "));
        }

        [Test]
        public void Normalize_TheDeadDefaultBecomesProduction()
        {
            // What every RoasSettings.asset saved before 0.1.8 holds.
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize("https://api.roassensor.com"));
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize("https://api.roassensor.com/"));
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize(" HTTPS://API.roassensor.com "));
        }

        [Test]
        public void Normalize_KeepsAHostTheDeveloperChose()
        {
            Assert.AreEqual("https://staging.example.com", RoasHosts.Normalize("https://staging.example.com/"));
            Assert.AreEqual("https://example.com/_rs", RoasHosts.Normalize("https://example.com/_rs"));
            Assert.AreEqual("https://core.roassensor.com", RoasHosts.Normalize("https://core.roassensor.com"));
        }

        [Test]
        public void IsDead_OnlyForTheDeadHost()
        {
            Assert.IsTrue(RoasHosts.IsDead("https://api.roassensor.com"));
            Assert.IsTrue(RoasHosts.IsDead("https://api.roassensor.com/"));
            Assert.IsFalse(RoasHosts.IsDead(null));
            Assert.IsFalse(RoasHosts.IsDead("https://core.roassensor.com"));
            Assert.IsFalse(RoasHosts.IsDead("https://api.roassensor.com.example.com"));
        }

        [Test]
        public void Retarget_MovesABeaconQueuedAgainstTheDeadHost()
        {
            Assert.AreEqual(
                "https://core.roassensor.com/api/tracking/mobile/events",
                RoasHosts.Retarget("https://api.roassensor.com/api/tracking/mobile/events", "https://core.roassensor.com"));
            // Onto whatever host the game uses now, not necessarily production.
            Assert.AreEqual(
                "https://staging.example.com/api/tracking/mobile/install",
                RoasHosts.Retarget("https://api.roassensor.com/api/tracking/mobile/install", "https://staging.example.com/"));
        }

        [Test]
        public void Retarget_LeavesEveryOtherUrlAlone()
        {
            const string staging = "https://staging.example.com/api/tracking/mobile/events";
            Assert.AreEqual(staging, RoasHosts.Retarget(staging, "https://core.roassensor.com"));
            // A host that merely starts with the dead one's text is not the dead host.
            const string lookalike = "https://api.roassensor.com.example.com/api/tracking/mobile/events";
            Assert.AreEqual(lookalike, RoasHosts.Retarget(lookalike, "https://core.roassensor.com"));
            Assert.AreEqual("", RoasHosts.Retarget("", "https://core.roassensor.com"));
            Assert.IsNull(RoasHosts.Retarget(null, "https://core.roassensor.com"));
        }
    }
}
