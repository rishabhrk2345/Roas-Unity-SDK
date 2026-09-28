namespace RoasSensor
{
    /// <summary>
    /// Funnel / behaviour events — the app equivalent of <c>roas.track()</c> on the web SDK.
    /// These feed the funnel and intent signals; they are NEVER revenue (revenue enters only
    /// through the signed store-to-server webhook or <see cref="Roas.VerifyPurchase"/>, for the
    /// same reason the web SDK can't book money client-side: a device can be tampered with).
    ///
    /// The taxonomy covers commerce and game funnels, matching the Android/iOS SDKs' taxonomy
    /// exactly (same wire <c>key</c> values). Anything not listed can be sent as
    /// <see cref="Custom"/> plus a name.
    /// </summary>
    public enum RoasEvent
    {
        ViewContent,
        AddToCart,
        AddToWishlist,
        BeginCheckout,
        Search,
        Lead,
        SignUp,
        Login,
        StartTrial,
        Subscribe,
        LevelStart,
        LevelComplete,
        TutorialComplete,
        Share,
        Custom,
    }

    internal static class RoasEventKeys
    {
        public static string Key(RoasEvent e)
        {
            switch (e)
            {
                case RoasEvent.ViewContent: return "view_content";
                case RoasEvent.AddToCart: return "add_to_cart";
                case RoasEvent.AddToWishlist: return "add_to_wishlist";
                case RoasEvent.BeginCheckout: return "begin_checkout";
                case RoasEvent.Search: return "search";
                case RoasEvent.Lead: return "lead";
                case RoasEvent.SignUp: return "sign_up";
                case RoasEvent.Login: return "login";
                case RoasEvent.StartTrial: return "start_trial";
                case RoasEvent.Subscribe: return "subscribe";
                case RoasEvent.LevelStart: return "level_start";
                case RoasEvent.LevelComplete: return "level_complete";
                case RoasEvent.TutorialComplete: return "tutorial_complete";
                case RoasEvent.Share: return "share";
                default: return "custom";
            }
        }
    }
}
