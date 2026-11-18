using System.Collections.Generic;

namespace RoasSensor.Internal
{
    /// <summary>
    /// The platform-independent half of reading Meta's own install referrer: a
    /// <c>ContentProvider</c> in the Facebook, Instagram and Facebook Lite apps that answers, for a
    /// Facebook app id, the last Meta ad this device saw or clicked before the install
    /// (https://developers.facebook.com/documentation/app-ads/meta-install-referrer). The Android
    /// read itself is <c>RoasAndroidBridge.ReadMetaInstallReferrer</c>; what is judged here is
    /// everything that needs no Android: the app id, which of several answers to send, and the
    /// fields of the first-open beacon. It mirrors the Kotlin SDK's <c>MetaInstallReferrerReader</c>
    /// one for one, so the three platforms send the collector the same thing.
    ///
    /// It exists beside Google's Play Install Referrer because Play's answers only for a
    /// same-session click. Meta's also covers an ad that was only SEEN (view-through) and a click
    /// from an earlier session. The answer stays encrypted until the collector opens it with the
    /// app's Install Referrer Decryption Key, which this SDK never holds.
    /// </summary>
    internal static class RoasMetaReferrer
    {
        /// <summary>One Meta app that serves the referrer: the authority to ask and the only
        /// package allowed to own it. A ContentProvider authority is first come, first served,
        /// and a device without Facebook lets any app claim one, so an answer is trusted only
        /// from the package meant to own that authority.</summary>
        public sealed class Source
        {
            public readonly string Authority;
            public readonly string Package;

            public Source(string authority, string package)
            {
                Authority = authority;
                Package = package;
            }
        }

        /// <summary>Facebook, Instagram, Facebook Lite (Meta's documented order; the order is only the
        /// tie-break between two equally good answers).</summary>
        public static readonly Source[] Sources =
        {
            new Source("com.facebook.katana.provider.InstallReferrerProvider", "com.facebook.katana"),
            new Source("com.instagram.contentprovider.InstallReferrerProvider", "com.instagram.android"),
            new Source("com.facebook.lite.provider.InstallReferrerProvider", "com.facebook.lite"),
        };

        /// <summary>An encrypted campaign is a few hundred characters; anything past this is not
        /// Meta's, and the collector would refuse it anyway.</summary>
        public const int MaxReferrerLength = 8192;

        /// <summary>
        /// <see cref="Referrer"/>: Meta's string, untouched. <see cref="IsClick"/>: <c>is_ct</c>
        /// (true click-through, false impression; null when it could not be read).
        /// <see cref="AtSeconds"/>: when the click or impression happened, epoch seconds (null when
        /// absent). <see cref="Status"/>: "OK", "OK_EMPTY" (a Meta app answered with nothing for
        /// this device), "NOT_AVAILABLE" (no trusted provider on this device: the usual case),
        /// "NO_APP_ID" (nothing configured) or "ERROR".
        /// </summary>
        public sealed class Answer
        {
            public string Referrer;
            public bool? IsClick;
            public long? AtSeconds;
            public string Status = "NOT_AVAILABLE";

            public static Answer None(string status) => new Answer { Status = status };
        }

        /// <summary>The Facebook app id as it may be appended to the provider URI, or null. Digits
        /// only, one to twenty of them, so it can never reshape the URI it is appended to (a "/" or
        /// "?" would). Surrounding whitespace is ignored.</summary>
        public static string NormalizeAppId(string metaAppId)
        {
            if (metaAppId == null) return null;
            var id = metaAppId.Trim();
            if (id.Length < 1 || id.Length > 20) return null;
            foreach (var c in id)
            {
                if (c < '0' || c > '9') return null;
            }
            return id;
        }

        /// <summary>Whether a referrer string is worth sending at all: present and not absurdly long.</summary>
        public static bool IsUsableReferrer(string referrer) =>
            !string.IsNullOrEmpty(referrer) && referrer.Length <= MaxReferrerLength;

        /// <summary>Two Meta apps can both answer. A click is worth more than a view (the collector
        /// ranks them the same way), and among equals the more recent wins; an answer with no time
        /// counts as the oldest we can say. On a complete tie the earlier answer in the list stays.
        /// Null for no answers.</summary>
        public static Answer Best(IList<Answer> answers)
        {
            Answer best = null;
            if (answers == null) return null;
            foreach (var candidate in answers)
            {
                if (candidate == null) continue;
                if (best == null || Better(candidate, best)) best = candidate;
            }
            return best;
        }

        // A click beats a view; inside the same kind the later time wins; equal keeps the earlier.
        private static bool Better(Answer a, Answer b)
        {
            var aClick = a.IsClick == true;
            var bClick = b.IsClick == true;
            if (aClick != bClick) return aClick;
            return (a.AtSeconds ?? 0L) > (b.AtSeconds ?? 0L);
        }

        /// <summary>
        /// Adds an answer to a first-open body under the names the collector reads
        /// (<c>meta_install_referrer</c>, <c>meta_is_ct</c>, <c>meta_click_at</c>). An answer with
        /// no referrer adds nothing; an unreadable click flag or time is left out rather than
        /// guessed, and the collector reads a missing flag as a view.
        /// </summary>
        public static void WriteTo(RoasJson body, Answer answer)
        {
            if (body == null || answer == null || !IsUsableReferrer(answer.Referrer)) return;
            body.Put("meta_install_referrer", answer.Referrer);
            if (answer.IsClick.HasValue) body.Put("meta_is_ct", answer.IsClick.Value ? 1 : 0);
            if (answer.AtSeconds.HasValue) body.Put("meta_click_at", answer.AtSeconds.Value);
        }
    }
}
