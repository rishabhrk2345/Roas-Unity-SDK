using System.Collections.Generic;
using RoasSensor;
using UnityEngine;

/// <summary>
/// Attach to any GameObject in an empty scene to manually smoke-test the SDK on a real
/// device/simulator -- this is throwaway scaffolding for the TestProject, not part of the
/// published package (see ../../Samples~/BasicIntegration for the documented sample).
///
/// Watch the device console for "[RoasSensor]" lines (LogLevel is set to Debug below) and
/// confirm delivery results come back through <see cref="Roas.OnDeliveryResult"/>.
/// </summary>
public sealed class RoasSmokeTest : MonoBehaviour
{
    [Tooltip("Leave blank to rely on Assets/Resources/RoasSettings.asset (ROASSensor -> " +
             "Create Settings Asset) instead of initializing manually here.")]
    [SerializeField] private string publicKey;

    private void Awake()
    {
        Roas.SetLogLevel(RoasLogLevel.Debug);
        Roas.OnDeliveryResult += (path, success, error) =>
            Debug.Log($"[RoasSmokeTest] delivery {path} -> success={success} error={error}");

        if (!string.IsNullOrEmpty(publicKey))
        {
            Roas.Initialize(publicKey);
        }

        // Real OS-level deep links -- NOT the same thing TestDeepLink() below exercises.
        // That method only proves Roas.HandleDeepLink() parses a URL correctly once it
        // already has one; this is the half that gets a REAL tap on a real "roastest://"
        // link (see Assets/Plugins/Android/AndroidManifest.xml and
        // Assets/Editor/RoasTestProjectIOSPostProcess.cs for where that scheme is
        // registered) to actually reach this code at all. Warm open:
        Application.deepLinkActivated += OnDeepLinkActivated;
    }

    private void Start()
    {
        Roas.RequestTrackingAuthorization(); // no-op on Android
        Debug.Log($"[RoasSmokeTest] VisitorId={Roas.VisitorId()} " +
                   $"ObfuscatedAccountId={Roas.ObfuscatedAccountId()} " +
                   $"AppAccountToken={Roas.AppAccountToken()}");

        // Cold start: Application.absoluteURL is already set by the time Start() runs if
        // this launch WAS a deep link open, empty otherwise.
        if (!string.IsNullOrEmpty(Application.absoluteURL))
        {
            OnDeepLinkActivated(Application.absoluteURL);
        }
    }

    private void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
    }

    private void OnDeepLinkActivated(string url)
    {
        Debug.Log($"[RoasSmokeTest] Real deep link activated: {url}");
        Roas.HandleDeepLink(url);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private string _lastHandledIntentData;

    /// <summary>
    /// Fallback for a real gap confirmed on a real device: Android's own log confirmed the
    /// "roastest://" intent WAS delivered to the running Activity ("delivered to currently
    /// running top-most instance"), yet Application.deepLinkActivated never fired --
    /// confirmed via the backend DB showing no deeplink-sourced row at all, not just a
    /// missing log line. Reads the Activity's current intent data directly via JNI instead
    /// of trusting Unity's own deep-link event plumbing, triggered on every focus-regain
    /// (which reliably fires when a backgrounded/already-running app is brought back via a
    /// new intent, independent of whatever is or isn't wiring deepLinkActivated correctly
    /// on this Unity version/Activity combination).
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) return;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                var dataString = intent.Call<string>("getDataString");
                if (string.IsNullOrEmpty(dataString) || dataString == _lastHandledIntentData) return;
                _lastHandledIntentData = dataString;
                Debug.Log($"[RoasSmokeTest] (JNI fallback) getIntent().getDataString() = {dataString}");
                Roas.HandleDeepLink(dataString);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RoasSmokeTest] JNI fallback intent read failed: {e}");
        }
    }
#endif

    // [ContextMenu] makes each of these runnable from the ⋮ menu on this component in the
    // Inspector while in Play Mode (right-click the component header, or its overflow menu) --
    // no need to wire uGUI buttons just to smoke-test. Also wire them to real buttons if you'd
    // rather tap through on a device.

    [ContextMenu("Test: Identify")]
    public void TestIdentify()
    {
        Roas.Identify(email: "smoke-test@example.com");
    }

    [ContextMenu("Test: Track Event")]
    public void TestTrackEvent()
    {
        Roas.Track(RoasEvent.ViewContent, properties: new Dictionary<string, object>
        {
            { RoasProps.ProductId, "smoke_test_sku" },
            { RoasProps.Source, "smoke_test_button" },
        });
    }

    // Both VerifyPurchase test methods are no-ops in the Editor/Standalone (by design -- see
    // Roas.VerifyPurchase's platform guards) since there is no store to verify a receipt
    // against. Only meaningful on an actual Android/iOS build.
    [ContextMenu("Test: Verify Purchase (Android)")]
    public void TestVerifyPurchaseAndroid()
    {
        Roas.VerifyPurchase(purchaseToken: "test-token", productId: "smoke_test_sku", isSubscription: false);
    }

    [ContextMenu("Test: Verify Purchase (iOS)")]
    public void TestVerifyPurchaseIOS()
    {
        Roas.VerifyPurchase(transactionId: "1234567890");
    }

    [ContextMenu("Test: Deep Link")]
    public void TestDeepLink()
    {
        Roas.HandleDeepLink("https://example.com/open?utm_source=smoke_test&rsclid=abc123");
    }
}
