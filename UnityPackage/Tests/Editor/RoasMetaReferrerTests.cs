using System.Collections.Generic;
using NUnit.Framework;
using RoasSensor.Internal;

namespace RoasSensor.Tests
{
    /// <summary>
    /// The platform-independent half of reading Meta's own install referrer. These mirror the Kotlin
    /// SDK's <c>MetaInstallReferrerReaderTest</c> case for case, because the three SDKs must send the
    /// collector the same thing. The Android read itself needs a device and a Facebook app, so it is
    /// not covered here.
    /// </summary>
    public sealed class RoasMetaReferrerTests
    {
        private static RoasMetaReferrer.Answer Ok(string referrer, bool? click, long? at) =>
            new RoasMetaReferrer.Answer { Referrer = referrer, IsClick = click, AtSeconds = at, Status = "OK" };

        [Test]
        public void NormalizeAppId_AcceptsDigitsAndTrimsWhitespace()
        {
            Assert.AreEqual("1234567890", RoasMetaReferrer.NormalizeAppId("1234567890"));
            Assert.AreEqual("1022926943439793", RoasMetaReferrer.NormalizeAppId("  1022926943439793 \n"));
            Assert.AreEqual(new string('9', 20), RoasMetaReferrer.NormalizeAppId(new string('9', 20)));
        }

        [Test]
        public void NormalizeAppId_RefusesAnythingThatCouldReshapeTheProviderUri()
        {
            foreach (var bad in new[] { null, "", "   ", "12/../34", "123?x=1", "abc", "1 2", new string('9', 21), "../", "12#3", "-12" })
            {
                Assert.IsNull(RoasMetaReferrer.NormalizeAppId(bad), bad ?? "(null)");
            }
        }

        [Test]
        public void Sources_AreMetasThreeAppsInDocumentedOrder()
        {
            Assert.AreEqual(3, RoasMetaReferrer.Sources.Length);
            Assert.AreEqual("com.facebook.katana.provider.InstallReferrerProvider", RoasMetaReferrer.Sources[0].Authority);
            Assert.AreEqual("com.facebook.katana", RoasMetaReferrer.Sources[0].Package);
            Assert.AreEqual("com.instagram.contentprovider.InstallReferrerProvider", RoasMetaReferrer.Sources[1].Authority);
            Assert.AreEqual("com.instagram.android", RoasMetaReferrer.Sources[1].Package);
            Assert.AreEqual("com.facebook.lite.provider.InstallReferrerProvider", RoasMetaReferrer.Sources[2].Authority);
            Assert.AreEqual("com.facebook.lite", RoasMetaReferrer.Sources[2].Package);
        }

        [Test]
        public void Best_ClickBeatsViewThenTheNewerWins()
        {
            var answers = new List<RoasMetaReferrer.Answer>
            {
                Ok("fb-view", false, 1_700_000_900L),
                Ok("ig-click-old", true, 1_700_000_100L),
                Ok("lite-click-new", true, 1_700_000_500L),
            };
            Assert.AreEqual("lite-click-new", RoasMetaReferrer.Best(answers).Referrer);
        }

        [Test]
        public void Best_AnAnswerWithNoTimeCountsAsTheOldest_AndATieKeepsTheEarlier()
        {
            Assert.AreEqual("dated", RoasMetaReferrer.Best(new List<RoasMetaReferrer.Answer> { Ok("undated", true, null), Ok("dated", true, 5L) }).Referrer);
            Assert.AreEqual("first", RoasMetaReferrer.Best(new List<RoasMetaReferrer.Answer> { Ok("first", true, 7L), Ok("second", true, 7L) }).Referrer);
        }

        [Test]
        public void Best_AnUnknownClickFlagReadsAsAView()
        {
            var answers = new List<RoasMetaReferrer.Answer> { Ok("unknown", null, 9_000L), Ok("click", true, 1L) };
            Assert.AreEqual("click", RoasMetaReferrer.Best(answers).Referrer);
        }

        [Test]
        public void Best_NothingIsNull()
        {
            Assert.IsNull(RoasMetaReferrer.Best(null));
            Assert.IsNull(RoasMetaReferrer.Best(new List<RoasMetaReferrer.Answer>()));
            Assert.IsNull(RoasMetaReferrer.Best(new List<RoasMetaReferrer.Answer> { null }));
        }

        [Test]
        public void WriteTo_PutsTheThreeFieldsUnderTheNamesTheCollectorReads()
        {
            var body = new RoasJson();
            RoasMetaReferrer.WriteTo(body, Ok("blob", true, 1_700_000_000L));
            Assert.AreEqual("blob", body.Get("meta_install_referrer"));
            Assert.AreEqual(1, body.Get("meta_is_ct"));
            Assert.AreEqual(1_700_000_000L, body.Get("meta_click_at"));

            var view = new RoasJson();
            RoasMetaReferrer.WriteTo(view, Ok("blob", false, null));
            Assert.AreEqual(0, view.Get("meta_is_ct"));
            Assert.IsFalse(view.Has("meta_click_at"));

            var unknown = new RoasJson();
            RoasMetaReferrer.WriteTo(unknown, Ok("blob", null, null));
            Assert.IsTrue(unknown.Has("meta_install_referrer"));
            Assert.IsFalse(unknown.Has("meta_is_ct"));
            Assert.IsFalse(unknown.Has("meta_click_at"));
        }

        [Test]
        public void WriteTo_AnAnswerWithNoUsableReferrerAddsNothing()
        {
            foreach (var status in new[] { "NO_APP_ID", "NOT_AVAILABLE", "OK_EMPTY", "ERROR" })
            {
                var body = new RoasJson();
                RoasMetaReferrer.WriteTo(body, RoasMetaReferrer.Answer.None(status));
                Assert.IsFalse(body.Has("meta_install_referrer"), status);
            }

            var empty = new RoasJson();
            RoasMetaReferrer.WriteTo(empty, Ok("", true, 1L));
            Assert.IsFalse(empty.Has("meta_install_referrer"));

            var oversized = new RoasJson();
            RoasMetaReferrer.WriteTo(oversized, Ok(new string('x', RoasMetaReferrer.MaxReferrerLength + 1), true, 1L));
            Assert.IsFalse(oversized.Has("meta_install_referrer"));

            var atTheLimit = new RoasJson();
            RoasMetaReferrer.WriteTo(atTheLimit, Ok(new string('x', RoasMetaReferrer.MaxReferrerLength), true, 1L));
            Assert.IsTrue(atTheLimit.Has("meta_install_referrer"));

            RoasMetaReferrer.WriteTo(null, Ok("blob", true, 1L)); // no body: nothing to do, no throw
            RoasMetaReferrer.WriteTo(new RoasJson(), null);       // no answer: nothing to do, no throw
        }
    }
}
