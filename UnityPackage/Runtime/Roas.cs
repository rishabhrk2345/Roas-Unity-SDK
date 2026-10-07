using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RoasSensor.Internal;
using UnityEngine;

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using RoasSensor.Platform;
#endif

namespace RoasSensor
{
    /// <summary>
    /// ROASSensor Unity SDK — the public entry point.
    ///
    /// <code>
    /// // as early as possible -- a bootstrap scene's Awake(), or leave RoasSettings.autoInitialize on
    /// Roas.Initialize("YOUR-SITE-PUBLIC-KEY");
    ///
    /// // when the user is known
    /// Roas.Identify(email: "buyer@example.com");
    ///
    /// // pass the visitor id to RevenueCat so purchases attribute to this install
    /// // Purchases.SharedInstance.LogIn(Roas.VisitorId(), ...);
    /// </code>
    ///
    /// On first launch it reports the install -- reading the Play Install Referrer (Android)
    /// or Apple Search Ads token (iOS) and, with consent, the advertising id -- which links
    /// this install to the ad click that drove it. Everything is delivered through a persisted
    /// queue, so an install that happens offline is reported on the next launch, never lost.
    ///
    /// This is a from-scratch Unity port of the same wire contract the Kotlin (Android) and
    /// Swift (iOS) ROASSensor SDKs speak -- see those repos' <c>Roas.kt</c> / <c>Roas.swift</c>
    /// for the canonical behaviour this mirrors. Session rules, HMAC signing and PII hashing
    /// MUST NOT drift from them; anything else here follows their comments closely on purpose.
    /// </summary>
    public static class Roas
    {
        private static readonly object InitLock = new object();
        private static bool _initialized;
        private static string _publicKey;
        private static RoasStorage _storage;
        private static RoasTransport _transport;
        private static RoasSessionTracker _sessions;
        private static RoasRuntime _runtime;
        public static RoasLogLevel LogLevel { get; private set; } = RoasLogLevel.Error;

        /// <summary>Fired after every delivery attempt, success or failure. Optional -- most
        /// games don't need it -- but without it there is no way to know a beacon ever failed
        /// short of watching the console. <c>error</c> is null on success.</summary>
        public static event Action<string, bool, string> OnDeliveryResult;

        public static void SetLogLevel(RoasLogLevel level)
        {
            LogLevel = level;
            if (_transport != null) _transport.LogLevel = level;
        }

        // ── Bootstrap (auto-init from a RoasSettings asset) ─────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitializeOnLoad()
        {
            var settings = Resources.Load<RoasSettings>(RoasSettings.ResourcePath);
            if (settings == null || !settings.autoInitialize) return;
            var publicKey = settings.PublicKeyForCurrentPlatform();
            if (string.IsNullOrEmpty(publicKey))
            {
                Debug.LogWarning("[RoasSensor] autoInitialize is on but no public key is set for this " +
                                  "platform in Assets/Resources/RoasSettings.asset -- skipping.");
                return;
            }
            SetLogLevel(settings.logLevel);
            Initialize(publicKey, settings.customerUserId, settings.ResolvedBaseUrl(), settings.AppSecretForCurrentPlatform());
            if (settings.requestTrackingAuthorizationOnLaunch)
            {
#if UNITY_IOS && !UNITY_EDITOR
                RequestTrackingAuthorization();
#endif
            }
        }

        /// <summary>
        /// Start the SDK. Call once, as early as possible. Idempotent -- only the first call
        /// initializes.
        /// </summary>
        /// <param name="publicKey">the app property's public key from ROASSensor setup.</param>
        /// <param name="customerUserId">your own user id if already known; bound as an external
        /// id so the same person on web, another device, or this app collapses into one identity.</param>
        /// <param name="baseUrl">override the collector host (defaults to production).</param>
        /// <param name="appSecret">this property's beacon signing secret, from Setup -> your app
        /// -> Beacon signing. Optional: without it beacons go unsigned, which the collector
        /// accepts until the customer turns on "require signed beacons". Do NOT hardcode a real
        /// secret in source -- inject it at build time. It is not a revenue credential; the worst
        /// an extracted one allows is forging beacons, which the public key alone already allowed,
        /// and rotating it invalidates every build carrying the old one.
        ///
        /// LAST on purpose, and it must stay last -- mirrors the Kotlin/Swift SDKs' parameter
        /// order exactly, since a Flutter-style bridge binding these positionally is exactly how
        /// a parameter inserted mid-list once silently rebound every existing call one slot along.</param>
        public static void Initialize(string publicKey, string customerUserId = null, string baseUrl = null, string appSecret = null)
        {
            lock (InitLock)
            {
                if (_initialized) return;
                _publicKey = publicKey;
                _storage = new RoasStorage();
                ResetIfDataWasResurrected();
                _runtime = RoasRuntime.GetOrCreate();
                _transport = new RoasTransport(string.IsNullOrEmpty(baseUrl) ? RoasSettings.DefaultBaseUrl : baseUrl, _storage, appSecret, _runtime)
                {
                    LogLevel = LogLevel,
                };
                _transport.DeliveryCallback = (path, success, error) => OnDeliveryResult?.Invoke(path, success, error);
                _sessions = new RoasSessionTracker(_storage);
                _runtime.OnPauseChanged = OnApplicationPauseChanged;
                _runtime.OnQuit = () => _transport.Flush();
#if UNITY_IOS && !UNITY_EDITOR
                _runtime.OnTrackingAuthorizationStatus = OnTrackingAuthorizationStatusReceived;
#endif
                _initialized = true;
            }

            // Resolve the session BEFORE anything reports, mirroring the native SDKs' ordering
            // note: otherwise the very first foreground callback could race reportFirstOpen and
            // produce a phantom duplicate touch a millisecond apart.
            var session = _sessions.Current();
            _sessions.MarkForeground();

            _transport.Flush(); // deliver anything queued from a previous offline launch

            if (!_storage.InstallReported)
            {
                ReportFirstOpen(customerUserId);
            }
            else
            {
                if (session.Started) SendSessionStart(session);
                // != null is not enough: a RoasSettings asset serializes an unset string field
                // as "", never null, so a blank Customer User Id would still pass that check
                // and send external_id: "" -- rejected by the collector with HTTP 400 on every
                // launch after the first. Found and fixed by a customer integrating this SDK.
                if (!string.IsNullOrEmpty(customerUserId)) Identify(customerUserId: customerUserId);
            }
        }

        /// <summary>Read configuration from the <see cref="RoasSettings"/> asset in
        /// <c>Resources/RoasSettings.asset</c> and initialize with it. Equivalent to what
        /// <see cref="AutoInitializeOnLoad"/> does when <c>autoInitialize</c> is on --
        /// call this yourself if you turned that off but still want to keep the config in one
        /// asset instead of hardcoding it at the call site.</summary>
        public static void InitializeFromSettings()
        {
            var settings = Resources.Load<RoasSettings>(RoasSettings.ResourcePath);
            if (settings == null)
            {
                Debug.LogError("[RoasSensor] No RoasSettings asset found at Resources/RoasSettings.asset. " +
                                "Create one via ROASSensor -> Create Settings Asset.");
                return;
            }
            SetLogLevel(settings.logLevel);
            Initialize(settings.PublicKeyForCurrentPlatform(), settings.customerUserId, settings.ResolvedBaseUrl(), settings.AppSecretForCurrentPlatform());
        }

        /// <summary>The stable visitor id for this install. Pass it to RevenueCat/your billing
        /// SDK as the app user id so a purchase carries it back and attributes to the install
        /// (and its ad click). Null before <see cref="Initialize"/>.</summary>
        public static string VisitorId() => _initialized ? _storage.VisitorId : null;

        /// <summary>The value to set as Android Play Billing's <c>obfuscatedAccountId</c> so a
        /// Real-Time Developer Notification attributes the sale to this install. Returns the
        /// visitor id unchanged -- Play accepts any string up to 64 characters and ours is 34.
        /// Needed only for the direct-to-Play revenue path. For iOS, use
        /// <see cref="AppAccountToken"/> instead -- StoreKit requires a real UUID, not this
        /// string, and passing the wrong one on iOS fails SILENTLY.</summary>
        public static string ObfuscatedAccountId() => VisitorId();

        /// <summary>A UUID derived from the visitor id, to set as StoreKit's
        /// <c>appAccountToken</c> on a purchase so an App Store Server Notification attributes
        /// the sale to this install. iOS only -- returns null on every other platform, and null
        /// if the SDK has not been initialized. The backend reconstructs the vid from it.</summary>
        public static string AppAccountToken()
        {
            if (!_initialized) return null;
#if UNITY_IOS && !UNITY_EDITOR
            return RoasIOSBridge.AppAccountToken(_storage.VisitorId);
#else
            return null;
#endif
        }

        /// <summary>Bind the user's identity. At least one argument must be non-null.</summary>
        public static void Identify(string email = null, string phone = null, string customerUserId = null)
        {
            if (!_initialized) return;
            var body = BaseBody();
            var emailHash = RoasHashing.HashEmail(email);
            if (!string.IsNullOrEmpty(emailHash)) body.Put("email_hash", emailHash);
            var phoneHash = RoasHashing.HashPhone(phone);
            if (!string.IsNullOrEmpty(phoneHash)) body.Put("phone_hash", phoneHash);
            // != null is not enough -- see the identical note in Initialize().
            if (!string.IsNullOrEmpty(customerUserId)) body.Put("external_id", customerUserId);
            if (!body.Has("email_hash") && !body.Has("phone_hash") && !body.Has("external_id")) return;

            // The advertising id belongs on THIS beacon too, not just the install -- the server
            // binds it as an IdentityKey alongside the email/phone, merging "this ad id" and
            // "this person" into one identity. That single read at first-open is exactly the
            // one most likely to have come back null (permission not yet granted, ATT still
            // undetermined), so it is worth reading again here.
            body.Put("os", CurrentOsField());
#if UNITY_ANDROID && !UNITY_EDITOR
            // AdvertisingId() makes a blocking Binder IPC call to Play Services -- never call
            // it on the main thread, or a slow device stalls this call for its duration.
            // Task.Run is a .NET ThreadPool thread, which the JVM has never seen -- any
            // AndroidJavaObject call from it aborts the process (SIGABRT, so the try/catch
            // inside RoasAndroidBridge never sees it either) unless the thread is explicitly
            // attached first. A real crash on every Android launch, found and fixed by a
            // customer integrating this SDK into a production game.
            Task.Run(() =>
            {
                AndroidJNI.AttachCurrentThread();
                try
                {
                    var deviceId = RoasAndroidBridge.AdvertisingId();
                    _runtime.RunOnMainThread(() =>
                    {
                        if (!string.IsNullOrEmpty(deviceId)) body.Put("device_id", deviceId);
                        _transport.Send("/api/tracking/mobile/identify", body);
                    });
                }
                finally
                {
                    AndroidJNI.DetachCurrentThread();
                }
            });
#elif UNITY_IOS && !UNITY_EDITOR
            var idfa = RoasIOSBridge.AdvertisingIdentifier();
            if (!string.IsNullOrEmpty(idfa)) body.Put("device_id", idfa);
            _transport.Send("/api/tracking/mobile/identify", body);
#else
            _transport.Send("/api/tracking/mobile/identify", body);
#endif
        }

        /// <summary>Record a funnel/behaviour event (never revenue -- see <see cref="RoasEvent"/>).</summary>
        public static void Track(RoasEvent evt, string name = null, IDictionary<string, object> properties = null)
        {
            if (!_initialized) return;
            var body = BaseBody().Put("name", RoasEventKeys.Key(evt));
            if (name != null) body.Put("label", name);
            if (properties != null)
            {
                var props = new RoasJson();
                foreach (var kv in properties) props.Put(kv.Key, kv.Value);
                body.Put("props", props);
            }
            _transport.Send("/api/tracking/mobile/events", body);
        }

        /// <summary>
        /// Ask the collector to VERIFY a purchase against the store's own records the moment
        /// your billing plugin reports it, rather than waiting on the asynchronous
        /// RTDN/App Store Server Notification. This never asserts revenue -- it only NAMES a
        /// receipt; the collector calls the store's API itself and books whatever amount IT
        /// reports back. Fire-and-forget; the outcome surfaces through
        /// <see cref="OnDeliveryResult"/> for <c>/api/tracking/mobile/purchase</c>.
        /// </summary>
        /// <param name="purchaseToken">Android (Play Billing): <c>Purchase.purchaseToken</c>.
        /// Ignored on iOS.</param>
        /// <param name="productId">Android: the SKU/subscription id. Ignored on iOS.</param>
        /// <param name="isSubscription">Android: true for a subscription. Ignored on iOS.</param>
        /// <param name="transactionId">iOS (StoreKit 2): <c>Transaction.id</c> as a string --
        /// NOT <c>originalID</c>, which would misattribute every renewal to the original sale.
        /// Ignored on Android.</param>
        public static void VerifyPurchase(string purchaseToken = null, string productId = null, bool isSubscription = false, string transactionId = null)
        {
            if (!_initialized) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(purchaseToken) || string.IsNullOrEmpty(productId)) return;
            var body = BaseBody()
                .Put("platform", "android")
                .Put("purchase_token", purchaseToken)
                .Put("product_id", productId)
                .Put("is_subscription", isSubscription);
            _transport.Send("/api/tracking/mobile/purchase", body);
#elif UNITY_IOS && !UNITY_EDITOR
            var id = (transactionId ?? string.Empty).Trim();
            if (id.Length == 0 || id.Length > 64) return;
            var body = BaseBody().Put("platform", "ios").Put("transaction_id", id);
            _transport.Send("/api/tracking/mobile/purchase", body);
#endif
            // No store to verify against outside Android/iOS (Editor, Standalone, ...) -- a no-op there.
        }

        /// <summary>
        /// Forward a deep/universal link that opened this app while it was already installed,
        /// so its campaign context attributes this open deterministically. This is different
        /// from the install referrer/Apple Search Ads token (that's the INSTALL); this is a
        /// later open of an app already on the device. The FULL query string is forwarded, not
        /// just a single click-id param -- the server's own parser already recognizes every
        /// click-id shape (rsclid/gclid/fbclid/...), and forwarding less would silently drop
        /// utm_*/rs_* context. A no-op if the URL carries no query string at all.
        /// </summary>
        public static void HandleDeepLink(string url)
        {
            if (!_initialized || string.IsNullOrEmpty(url)) return;
            string query;
            try
            {
                var uri = new Uri(url);
                query = uri.Query.TrimStart('?');
            }
            catch (Exception)
            {
                return;
            }
            if (string.IsNullOrEmpty(query)) return;

            var body = BaseBody()
                .Put("os", CurrentOsField())
                .Put("event_type", "app_open")
                .Put("session_number", _storage.SessionNumber)
                .Put("referrer_source", "deeplink")
                .Put("install_referrer", query);
            RoasDeviceInfo.Describe(body);
            body.Put("app_version", RoasDeviceInfo.AppVersion());
            _transport.Send("/api/tracking/mobile/first-open", body);
        }

        /// <summary>
        /// Present the App Tracking Transparency prompt at a moment of your choosing, and bind
        /// the IDFA if the user allows it. iOS only -- a no-op on every other platform, so
        /// shared game code can call this unconditionally. Pair with turning off
        /// <c>RoasSettings.requestTrackingAuthorizationOnLaunch</c>.
        ///
        /// The default is to prompt on first launch, which is the worst possible moment -- a
        /// cold-start system alert, before the player has seen anything worth trusting the app
        /// with, is the classic way to depress opt-in, and every denial costs the IDFA, the
        /// strongest identity key an iOS install can carry.
        ///
        /// Safe to call more than once: once the user has answered, the system returns the
        /// existing status without re-prompting.
        /// </summary>
        public static void RequestTrackingAuthorization()
        {
            if (!_initialized || _runtime == null) return;
#if UNITY_IOS && !UNITY_EDITOR
            RoasIOSBridge.RequestTrackingAuthorization(_runtime.GameObjectName);
#endif
        }

        /// <summary>
        /// Report a SKAdNetwork/AdAttributionKit conversion value -- the only post-install
        /// signal that reaches an ad network for a player who denied ATT, which is most of
        /// them. iOS only; a no-op elsewhere.
        /// </summary>
        /// <param name="value">the fine value, 0-63. Its meaning is the site's SKAN schema.</param>
        /// <param name="coarse">"low"/"medium"/"high" -- send it: below Apple's install-volume
        /// privacy threshold the fine value is withheld and coarse is all that survives.</param>
        /// <param name="lockWindow">end the measurement window and post immediately. Only when
        /// the value is genuinely final -- it discards every later conversion.</param>
        public static void UpdateConversionValue(int value, string coarse = null, bool lockWindow = false)
        {
#if UNITY_IOS && !UNITY_EDITOR
            RoasIOSBridge.UpdateConversionValue(value, coarse, lockWindow);
#endif
        }

        /// <summary>Test-only: tears the singleton back down so a test suite starts clean.
        /// Never call from game code.</summary>
        internal static void ResetForTests()
        {
            lock (InitLock)
            {
                _initialized = false;
                _storage = null;
                _transport = null;
                _sessions = null;
                _publicKey = null;
                LogLevel = RoasLogLevel.Error;
            }
        }

        // ── internals ────────────────────────────────────────────────────────────

        private static void OnApplicationPauseChanged(bool pauseStatus)
        {
            if (!_initialized) return;
            if (pauseStatus) OnEnterBackground();
            else OnEnterForeground();
        }

        private static void OnEnterForeground()
        {
            var session = _sessions.Current();
            _sessions.MarkForeground();
            if (session.Started) SendSessionStart(session);
        }

        private static void OnEnterBackground()
        {
            var foregroundMs = _sessions.MarkBackground();
            // Upserts onto the session's opening touch via its pv_id, so a visit is one row
            // that gains its duration -- not two rows that split its credit.
            var body = BaseBody()
                .Put("os", CurrentOsField())
                .Put("event_type", "app_open")
                .Put("pv_id", _storage.SessionPvId)
                .Put("engagement_ms", foregroundMs);
            _transport.Send("/api/tracking/mobile/first-open", body);
        }

        /// <summary>The opening beacon for a session that is not an install, plus a
        /// best-effort same-IP deferred match for a user who was already installed when they
        /// tapped an ad link (the backend no-ops this unless the site opted into
        /// <c>allow_probabilistic</c>).</summary>
        private static void SendSessionStart(RoasSessionTracker.Session session)
        {
            var body = BaseBody()
                .Put("os", CurrentOsField())
                .Put("app_version", RoasDeviceInfo.AppVersion())
                .Put("pv_id", session.PvId)
                .Put("session_number", session.Number);
            RoasDeviceInfo.Describe(body);
            _transport.Send("/api/tracking/mobile/first-open", body);

            _transport.Send("/api/tracking/mobile/deferred-link",
                new RoasJson().Put("site", _publicKey).Put("vid", _storage.VisitorId));
        }

        private static void ReportFirstOpen(string customerUserId)
        {
            var body = BaseBody()
                .Put("os", CurrentOsField())
                .Put("app_version", RoasDeviceInfo.AppVersion())
                // The install IS session 1's opening touch, so it carries the session's
                // pv_id -- the closing beacon then folds foreground time into this row
                // instead of writing a second one beside it.
                .Put("pv_id", _storage.SessionPvId)
                .Put("session_number", _storage.SessionNumber);
            RoasDeviceInfo.Describe(body);

            var integritySignals = RoasDeviceIntegrity.Signals();
            if (integritySignals.Count > 0) body.Put("integrity_signals", integritySignals);
            // != null is not enough -- see the identical note in Initialize().
            if (!string.IsNullOrEmpty(customerUserId)) body.Put("external_id", customerUserId);

#if UNITY_ANDROID && !UNITY_EDITOR
            // AdvertisingId()/AppSetId() each make a blocking Binder IPC call to Play Services
            // -- AppSetId specifically can block up to 5 seconds on its own internal timeout.
            // Never call these on the main thread: on a slow device that turns the very first
            // beacon this SDK ever sends into a startup hitch, or worse, brushes against
            // Android's ANR threshold. The native Kotlin SDK backgrounds this for the same
            // reason (see DeviceId.kt/AppSetId.kt); this mirrors it rather than trusting a
            // coroutine to count as "background" the way it would for a pure network call.
            //
            // Task.Run is a .NET ThreadPool thread, which the JVM has never seen -- any
            // AndroidJavaObject call from it aborts the process (SIGABRT, so the try/catch
            // inside RoasAndroidBridge never sees it either) unless the thread is explicitly
            // attached first. A real crash on every Android launch, found and fixed by a
            // customer integrating this SDK into a production game.
            Task.Run(() =>
            {
                AndroidJNI.AttachCurrentThread();
                try
                {
                    var gaid = RoasAndroidBridge.AdvertisingId();
                    var appSetId = RoasAndroidBridge.AppSetId();
                    _runtime.RunOnMainThread(() =>
                    {
                        if (!string.IsNullOrEmpty(gaid)) body.Put("device_id", gaid);
                        if (!string.IsNullOrEmpty(appSetId)) body.Put("app_set_id", appSetId);

                        RoasAndroidBridge.FetchInstallReferrer(result =>
                        {
                            ApplyReferrerResult(body, result);
                            _transport.Send("/api/tracking/mobile/first-open", body);
                            _storage.InstallReported = true;
                        });
                    });
                }
                finally
                {
                    AndroidJNI.DetachCurrentThread();
                }
            });
#elif UNITY_IOS && !UNITY_EDITOR
            var idfa = RoasIOSBridge.AdvertisingIdentifier();
            if (!string.IsNullOrEmpty(idfa)) body.Put("device_id", idfa);
            var idfv = RoasIOSBridge.IdentifierForVendor();
            if (!string.IsNullOrEmpty(idfv)) body.Put("idfv", idfv);

            var asaToken = RoasIOSBridge.AppleSearchAdsToken();
            body.Put("referrer_source", "asa");
            if (!string.IsNullOrEmpty(asaToken))
            {
                body.Put("asa_token", asaToken);
                body.Put("referrer_status", "OK");
            }
            else
            {
                body.Put("referrer_status", "UNAVAILABLE");
            }
            _transport.Send("/api/tracking/mobile/first-open", body);
            _storage.InstallReported = true;
#else
            // Editor / standalone: no store attribution signals exist here at all -- still
            // report the install so identify()/track() calls during in-editor testing have
            // somewhere to attach.
            body.Put("referrer_status", "OK_NOT_SET").Put("referrer_source", "");
            _transport.Send("/api/tracking/mobile/first-open", body);
            _storage.InstallReported = true;
#endif
        }

        private static void ApplyReferrerResult(RoasJson body, object referrerResult)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var result = (RoasAndroidBridge.InstallReferrerResult)referrerResult;
            body.Put("referrer_status", result.Status);
            body.Put("referrer_source", string.IsNullOrEmpty(result.Referrer) ? "" : "google");
            if (!string.IsNullOrEmpty(result.Referrer)) body.Put("install_referrer", result.Referrer);
            if (result.ClickTimestampSeconds.HasValue) body.Put("referrer_click_timestamp", result.ClickTimestampSeconds.Value);
            if (result.InstallBeginTimestampSeconds.HasValue) body.Put("install_begin_timestamp", result.InstallBeginTimestampSeconds.Value);
            if (result.ClickTimestampSeconds.HasValue && result.InstallBeginTimestampSeconds.HasValue && result.ClickTimestampSeconds.Value > 0)
            {
                body.Put("click_to_install_seconds", result.InstallBeginTimestampSeconds.Value - result.ClickTimestampSeconds.Value);
            }
#endif
        }

#if UNITY_IOS
        private static void OnTrackingAuthorizationStatusReceived(int status)
        {
            // 3 == ATTrackingManagerAuthorizationStatusAuthorized.
            if (status != 3 || !_initialized) return;
#if !UNITY_EDITOR
            var idfa = RoasIOSBridge.AdvertisingIdentifier();
            if (string.IsNullOrEmpty(idfa)) return;
            var body = BaseBody().Put("os", "iOS").Put("device_id", idfa);
            _transport.Send("/api/tracking/mobile/identify", body);
#endif
        }
#endif

        /// <summary>
        /// Detect a device whose private app data survived an actual OS-level reinstall --
        /// confirmed on Android via <c>PackageManager.firstInstallTime</c>, mirrored here with
        /// <see cref="Application.installMode"/> not being a reliable per-install anchor on
        /// Unity, so this uses <see cref="Application.persistentDataPath"/> directory creation
        /// time where available and otherwise skips the check rather than risking a false
        /// reset. Best-effort, matching the native SDKs' own "never risk a false reset" stance.
        /// </summary>
        private static void ResetIfDataWasResurrected()
        {
            try
            {
                var path = Application.persistentDataPath;
                if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path)) return;
                var created = System.IO.Directory.GetCreationTimeUtc(path);
                var actual = ((DateTimeOffset)created).ToUnixTimeSeconds();
                if (actual <= 0) return;
                if (Math.Abs(_storage.FirstInstallAnchor - actual) > 1)
                {
                    _storage.ResetForNewInstall();
                    _storage.FirstInstallAnchor = actual;
                }
            }
            catch (Exception)
            {
                // Leave storage untouched rather than risk wiping a real install's data.
            }
        }

        /// <summary>
        /// Fields every beacon carries. <c>sequence</c> is minted here, per beacon, because
        /// beacons are fire-and-forget and race each other over the network -- the order they
        /// arrive in is not the order they happened in, so a server timestamp would
        /// reconstruct the wrong path. <c>pv_id</c> is deliberately NOT here -- it belongs only
        /// on the two touchpoint beacons that open and close a session.
        /// </summary>
        private static RoasJson BaseBody()
        {
            return new RoasJson()
                .Put("site", _publicKey)
                .Put("vid", _storage.VisitorId)
                .Put("session_id", _sessions.Current().Id)
                .Put("sequence", _sessions.NextSequence())
                .Put("ts", NowIso())
                .Put("clock_offset_seconds", _storage.ClockOffsetSeconds);
        }

        private static string NowIso()
        {
            try
            {
                var now = DateTimeOffset.UtcNow.AddSeconds(_storage.ClockOffsetSeconds);
                return now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return string.Empty; // blank simply omits it; the server falls back to ingest time
            }
        }

        private static string CurrentOsField()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                default: return "Unity";
            }
        }
    }
}
