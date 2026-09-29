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
    }

    private void Start()
    {
        Roas.RequestTrackingAuthorization(); // no-op on Android
        Debug.Log($"[RoasSmokeTest] VisitorId={Roas.VisitorId()} " +
                   $"ObfuscatedAccountId={Roas.ObfuscatedAccountId()} " +
                   $"AppAccountToken={Roas.AppAccountToken()}");
    }

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
