using System;
using RoasSensor;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

/// <summary>
/// Real purchase verification test -- wires Unity IAP to a real Play Billing / StoreKit
/// product so <see cref="Roas.VerifyPurchase"/> is tested against an actual store
/// transaction, not a fabricated token string. TestProject-only scaffolding, not part of
/// the published package (Unity IAP is deliberately NOT a dependency of the SDK itself --
/// see the main README: the SDK owns no purchase API, by design, since Unity IAP and
/// third-party plugins differ on how they expose a hook for the buyer-identifier field).
///
/// Attach to any GameObject, set <see cref="productId"/> to the exact id configured in
/// BOTH Play Console and App Store Connect, press Play (or build to device), then invoke
/// <see cref="BuyTestProduct"/> via its Inspector context menu once Unity IAP reports
/// initialized.
/// </summary>
public sealed class RoasPurchaseTest : MonoBehaviour, IDetailedStoreListener
{
    [Tooltip("Must exactly match the product id configured in both Play Console and App Store Connect.")]
    [SerializeField] private string productId = "roas_test_product";

    private IStoreController _controller;

    private void Start()
    {
        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
        builder.AddProduct(productId, ProductType.Consumable);

        // Ties the sale to this install on Android -- set once, before the billing client
        // connects, not per-purchase. Null before Roas.Initialize() has run, which is why
        // this script should sit in a scene where RoasSettings.autoInitialize is on (it
        // runs via [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)], ahead of this Start()).
        var vid = Roas.ObfuscatedAccountId();
        if (!string.IsNullOrEmpty(vid))
        {
            builder.Configure<IGooglePlayConfiguration>().SetObfuscatedAccountId(vid);
        }
        else
        {
            Debug.LogWarning("[RoasPurchaseTest] Roas.ObfuscatedAccountId() was null at IAP init -- " +
                              "the Android purchase won't carry the visitor id. Make sure Roas.Initialize " +
                              "has already run (check RoasSettings.autoInitialize).");
        }

        UnityPurchasing.Initialize(this, builder);
    }

    [ContextMenu("Buy Test Product")]
    public void BuyTestProduct()
    {
        if (_controller == null)
        {
            Debug.LogError("[RoasPurchaseTest] Store not initialized yet -- wait for OnInitialized.");
            return;
        }
        _controller.InitiatePurchase(productId);
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        _controller = controller;
        Debug.Log("[RoasPurchaseTest] Unity IAP initialized -- ready to buy.");
    }

    public void OnInitializeFailed(InitializationFailureReason error) =>
        Debug.LogError($"[RoasPurchaseTest] Init failed: {error}");

    public void OnInitializeFailed(InitializationFailureReason error, string message) =>
        Debug.LogError($"[RoasPurchaseTest] Init failed: {error} -- {message}");

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        var product = args.purchasedProduct;
        // Logged in full deliberately: this is the one place to actually SEE the receipt
        // shape Unity IAP hands back on this exact version/platform, rather than trusting
        // ExtractGooglePlayPurchaseToken's assumption about it sight unseen.
        Debug.Log($"[RoasPurchaseTest] Purchase succeeded: {product.definition.id}\nreceipt: {product.receipt}\ntransactionID: {product.transactionID}");

#if UNITY_IOS
        // StoreKit's transaction id -- NOT appleOriginalTransactionID, which names the
        // subscription's FIRST purchase and would misattribute every renewal to it.
        Roas.VerifyPurchase(transactionId: product.transactionID);
#elif UNITY_ANDROID
        var purchaseToken = ExtractGooglePlayPurchaseToken(product.receipt);
        if (!string.IsNullOrEmpty(purchaseToken))
        {
            Roas.VerifyPurchase(purchaseToken: purchaseToken, productId: product.definition.id, isSubscription: false);
        }
        else
        {
            Debug.LogError("[RoasPurchaseTest] Could not extract purchaseToken -- check the logged receipt JSON above against ExtractGooglePlayPurchaseToken's assumed shape.");
        }
#endif
        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason reason) =>
        Debug.LogError($"[RoasPurchaseTest] Purchase failed: {reason}");

    public void OnPurchaseFailed(Product product, PurchaseFailureDescription description) =>
        Debug.LogError($"[RoasPurchaseTest] Purchase failed: {description.reason} -- {description.message}");

#if UNITY_ANDROID
    /// <summary>
    /// Unity IAP wraps a Google Play purchase as a triple-nested JSON string:
    /// <c>{ "Store": "...", "TransactionID": "...", "Payload": "{...}" }</c>, where
    /// <c>Payload</c> is itself a JSON-encoded string holding
    /// <c>{ "json": "{...}", "signature": "..." }</c>, and THAT inner <c>json</c> is the
    /// actual Play purchase data (orderId, purchaseToken, productId, ...). This shape has
    /// held across recent Unity IAP versions but was not independently re-verified against
    /// this exact installed version -- if this returns null, read the raw receipt
    /// <see cref="ProcessPurchase"/> already logged and adjust the three classes below to
    /// match what actually came back, rather than guessing at a fix blind.
    /// </summary>
    private static string ExtractGooglePlayPurchaseToken(string receiptJson)
    {
        try
        {
            var receipt = JsonUtility.FromJson<UnityIAPReceipt>(receiptJson);
            var payload = JsonUtility.FromJson<GooglePlayReceiptPayload>(receipt.Payload);
            var purchaseData = JsonUtility.FromJson<GooglePlayPurchaseData>(payload.json);
            return purchaseData.purchaseToken;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoasPurchaseTest] Failed to parse receipt: {e}");
            return null;
        }
    }

    [Serializable] private sealed class UnityIAPReceipt { public string Store; public string TransactionID; public string Payload; }
    [Serializable] private sealed class GooglePlayReceiptPayload { public string json; public string signature; }
    [Serializable] private sealed class GooglePlayPurchaseData { public string orderId; public string packageName; public string productId; public string purchaseToken; }
#endif
}
