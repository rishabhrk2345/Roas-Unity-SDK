namespace RoasSensor.Internal
{
    /// <summary>
    /// How Google's two consent-mode answers (<c>ad_user_data</c> and <c>ad_personalization</c>) are
    /// written onto a beacon. Platform-independent, so it can be tested in the Editor; mirrors the native
    /// Android SDK's <c>baseBody</c>, so the platforms send the collector the same thing.
    ///
    /// A key is written ONLY when the game has relayed an answer. Absent means "not said", which the
    /// collector treats differently from a denial: for people in the EU, the EEA and the UK it will not
    /// report them to Google until both answers are stated.
    /// </summary>
    internal static class RoasGoogleConsent
    {
        public const string AdUserDataKey = "ad_user_data";
        public const string AdPersonalizationKey = "ad_personalization";

        public static void WriteTo(RoasJson body, bool? adUserData, bool? adPersonalization)
        {
            if (body == null) return;
            if (adUserData.HasValue) body.Put(AdUserDataKey, adUserData.Value);
            if (adPersonalization.HasValue) body.Put(AdPersonalizationKey, adPersonalization.Value);
        }
    }
}
