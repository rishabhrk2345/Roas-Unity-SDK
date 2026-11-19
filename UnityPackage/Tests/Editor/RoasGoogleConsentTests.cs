using NUnit.Framework;
using RoasSensor.Internal;
using UnityEngine;

namespace RoasSensor.Tests
{
    /// <summary>
    /// Google's two consent-mode answers: what is written onto a beacon, and that the stored answer
    /// survives as three states (granted, denied, never said). Mirrors the Android SDK's consent tests.
    /// Uses real PlayerPrefs like <see cref="RoasSessionTrackerTests"/>.
    /// </summary>
    public sealed class RoasGoogleConsentTests
    {
        [SetUp]
        public void ClearPrefs() => PlayerPrefs.DeleteAll();

        [TearDown]
        public void ClearPrefsAfter() => PlayerPrefs.DeleteAll();

        [Test]
        public void WriteTo_NothingIsWrittenUntilTheGameHasSaidSomething()
        {
            var body = new RoasJson();
            RoasGoogleConsent.WriteTo(body, null, null);
            Assert.IsFalse(body.Has("ad_user_data"));
            Assert.IsFalse(body.Has("ad_personalization"));
        }

        [Test]
        public void WriteTo_AGrantAndADenialAreBothWritten_ADenialIsNeverDropped()
        {
            var body = new RoasJson();
            RoasGoogleConsent.WriteTo(body, true, false);
            Assert.AreEqual(true, body.Get("ad_user_data"));
            Assert.AreEqual(false, body.Get("ad_personalization"));
        }

        [Test]
        public void WriteTo_OneAnswerAloneIsWrittenAlone()
        {
            var body = new RoasJson();
            RoasGoogleConsent.WriteTo(body, false, null);
            Assert.AreEqual(false, body.Get("ad_user_data"));
            Assert.IsFalse(body.Has("ad_personalization"));
        }

        [Test]
        public void WriteTo_ANullBodyIsIgnored()
        {
            Assert.DoesNotThrow(() => RoasGoogleConsent.WriteTo(null, true, true));
        }

        [Test]
        public void Storage_NeverSaidIsNotTheSameAsDenied()
        {
            var storage = new RoasStorage();
            Assert.IsNull(storage.AdUserData);
            Assert.IsNull(storage.AdPersonalization);

            storage.AdUserData = false;
            Assert.AreEqual(false, storage.AdUserData);
            Assert.IsNull(storage.AdPersonalization);
        }

        [Test]
        public void Storage_AnAnswerIsPersistedAcrossInstances_AndCanBeChanged()
        {
            new RoasStorage().AdUserData = true;
            new RoasStorage().AdPersonalization = false;

            var reopened = new RoasStorage();
            Assert.AreEqual(true, reopened.AdUserData);
            Assert.AreEqual(false, reopened.AdPersonalization);

            reopened.AdUserData = false;
            Assert.AreEqual(false, new RoasStorage().AdUserData);
        }

        [Test]
        public void Storage_AReinstallDoesNotClearThePersonsChoice()
        {
            var storage = new RoasStorage();
            storage.AdUserData = true;
            storage.AdPersonalization = true;

            storage.ResetForNewInstall();

            Assert.AreEqual(true, storage.AdUserData);
            Assert.AreEqual(true, storage.AdPersonalization);
        }
    }
}
