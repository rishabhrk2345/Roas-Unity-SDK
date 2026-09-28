using RoasSensor;
using UnityEngine;

/// <summary>
/// A minimal walkthrough of every public call in the ROASSensor Unity SDK. Attach to any
/// GameObject in your first/bootstrap scene, or just read it as a reference -- this is a
/// sample, not something the package itself depends on.
/// </summary>
public sealed class RoasSampleUsage : MonoBehaviour
{
    [SerializeField] private string publicKey = "YOUR-SITE-PUBLIC-KEY";

    private void Awake()
    {
        // Skip this if RoasSettings.autoInitialize is on (the default) -- the SDK will
        // already have initialized itself from Assets/Resources/RoasSettings.asset before
        // any scene's Awake() runs. This call is here to show the manual path.
        Roas.SetLogLevel(RoasLogLevel.Debug);
        Roas.Initialize(publicKey);

        Roas.OnDeliveryResult += (path, success, error) =>
        {
            if (!success) Debug.LogWarning($"[RoasSample] {path} failed: {error}");
        };
    }

    private void Start()
    {
        // iOS: ask for tracking permission after the player has seen something worth
        // trusting the app with -- never at cold start. No-op on Android.
        Roas.RequestTrackingAuthorization();
    }

    public void OnSignUp(string email)
    {
        Roas.Identify(email: email);
        Roas.Track(RoasEvent.SignUp);
    }

    public void OnViewShopItem(string productId, string productName)
    {
        Roas.Track(RoasEvent.ViewContent, properties: new System.Collections.Generic.Dictionary<string, object>
        {
            { RoasProps.ProductId, productId },
            { RoasProps.ProductName, productName },
            { RoasProps.Source, "shop_screen" },
        });
    }

    /// <summary>Call from your billing plugin's purchase-confirmed callback.</summary>
    public void OnAndroidPurchaseConfirmed(string purchaseToken, string productId, bool isSubscription)
    {
        Roas.VerifyPurchase(purchaseToken: purchaseToken, productId: productId, isSubscription: isSubscription);
    }

    /// <summary>Call from your StoreKit 2 transaction-updates loop, with `Transaction.id` --
    /// not `originalID` -- as a string.</summary>
    public void OnIOSPurchaseConfirmed(string transactionId)
    {
        Roas.VerifyPurchase(transactionId: transactionId);
    }

    /// <summary>Wire this to your deep link handler (Application.deepLinkActivated, or your
    /// own universal-link/App-Link plumbing).</summary>
    public void OnDeepLinkOpened(string url)
    {
        Roas.HandleDeepLink(url);
    }
}
