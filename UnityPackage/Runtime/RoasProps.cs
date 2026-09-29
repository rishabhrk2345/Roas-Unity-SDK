namespace RoasSensor
{
    /// <summary>
    /// The property keys ROASSensor understands in <see cref="Roas.Track"/>. Matches the
    /// Android/iOS SDKs' <c>RoasProps</c> constants exactly.
    ///
    /// Free-form on purpose — send anything alongside these — but reporting can only group
    /// by a key it can predict, so use these where they fit. <see cref="ProductId"/> is the
    /// one that actually matters: it should be the SAME identifier the purchase will arrive
    /// with (the store product id), so an <c>AddToCart</c> can be lined up against the
    /// purchase that did or did not follow it. <see cref="Price"/> is reporting colour only —
    /// money in an event is never revenue; see <see cref="RoasEvent"/>.
    /// </summary>
    public static class RoasProps
    {
        /// <summary>Store product id. Must match what the purchase will report.</summary>
        public const string ProductId = "product_id";

        /// <summary>Display name. Never a join key — names change, ids do not.</summary>
        public const string ProductName = "product_name";

        /// <summary>Grouping for reports, e.g. "courses".</summary>
        public const string Category = "category";

        public const string Quantity = "quantity";

        /// <summary>Minor units (paise/cents). Reporting only — an event never contributes
        /// to revenue.</summary>
        public const string Price = "price";

        /// <summary>ISO-4217, e.g. "INR".</summary>
        public const string Currency = "currency";

        /// <summary>Free-text, for <see cref="RoasEvent.Search"/>.</summary>
        public const string Query = "query";

        /// <summary>Where in the app this happened ("home", "product_detail").</summary>
        public const string Source = "source";
    }
}
