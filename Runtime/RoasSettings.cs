using UnityEngine;

namespace RoasSensor
{
    /// <summary>
    /// Per-project configuration, read once at <see cref="Roas.AutoInitialize"/> time.
    ///
    /// ROASSensor's own docs are explicit that iOS and Android are separate app records with
    /// separate public keys and separate signing secrets — even for "the same app" — because
    /// they are two different store listings. This asset therefore carries one pair per
    /// platform rather than one shared pair, so a build never accidentally signs an Android
    /// beacon with the iOS secret or vice versa.
    ///
    /// Create via the Unity menu: ROASSensor → Create Settings Asset (writes to
    /// <c>Assets/Resources/RoasSettings.asset</c> so <see cref="Resources.Load"/> can find it
    /// without any manual wiring in a scene).
    /// </summary>
    [CreateAssetMenu(fileName = "RoasSettings", menuName = "ROASSensor/Settings", order = 1)]
    public sealed class RoasSettings : ScriptableObject
    {
        public const string ResourcePath = "RoasSettings";
        internal const string DefaultBaseUrl = "https://api.roassensor.com";

        [Header("Android")]
        [Tooltip("Setup -> your Android app -> Public key")]
        public string androidPublicKey;

        [Tooltip("Setup -> your Android app -> Beacon signing (optional). Do NOT commit a " +
                 "real secret into source control -- inject it at build time instead.")]
        public string androidAppSecret;

        [Header("iOS")]
        [Tooltip("Setup -> your iOS app -> Public key")]
        public string iosPublicKey;

        [Tooltip("Setup -> your iOS app -> Beacon signing (optional). Do NOT commit a " +
                 "real secret into source control -- inject it at build time instead.")]
        public string iosAppSecret;

        [Header("Options")]
        [Tooltip("Call Roas.Initialize automatically on game start, as early as possible -- " +
                 "the Unity equivalent of calling Roas.initialize() from Application.onCreate. " +
                 "Turn this off to call Roas.Initialize(...) yourself.")]
        public bool autoInitialize = true;


        [Tooltip("Override the collector host. Leave blank for production.")]
        public string baseUrl = DefaultBaseUrl;

        [Tooltip("Your own user id, if already known at launch. Usually left blank and set " +
                 "later via Roas.Identify(customerUserId: ...).")]
        public string customerUserId;

        [Tooltip("Present the App Tracking Transparency prompt automatically on first " +
                 "launch (iOS only). Turn this off to call Roas.RequestTrackingAuthorization() " +
                 "yourself at a better moment -- e.g. after onboarding, per Apple's guidance.")]
        public bool requestTrackingAuthorizationOnLaunch = true;

        [Tooltip("Console verbosity. Debug is useful while integrating; leave at Error for a release build.")]
        public RoasLogLevel logLevel = RoasLogLevel.Error;

        internal string PublicKeyForCurrentPlatform()
        {
#if UNITY_ANDROID
            return androidPublicKey;
#elif UNITY_IOS
            return iosPublicKey;
#else
            return androidPublicKey; // editor/desktop testing default
#endif
        }

        internal string AppSecretForCurrentPlatform()
        {
#if UNITY_ANDROID
            return androidAppSecret;
#elif UNITY_IOS
            return iosAppSecret;
#else
            return androidAppSecret;
#endif
        }

        internal string ResolvedBaseUrl() => string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl;
    }
}
