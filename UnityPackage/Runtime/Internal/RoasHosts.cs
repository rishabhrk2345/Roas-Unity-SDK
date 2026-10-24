using System;

namespace RoasSensor.Internal
{
    /// <summary>
    /// The collector host, and the dead one every release up to 0.1.7 shipped as its default.
    ///
    /// <c>api.roassensor.com</c> never had DNS, so a game that kept the default delivered nothing.
    /// Changing <see cref="RoasSettings.DefaultBaseUrl"/> alone would not reach those games:
    /// <c>baseUrl</c> is a serialized field, so every existing <c>RoasSettings.asset</c> holds the
    /// old string, and every beacon already queued on a device stored its full URL. Both are
    /// mapped here instead, at the point of use. A host the developer chose themselves (a staging
    /// server, a proxy) is left exactly as written.
    /// </summary>
    internal static class RoasHosts
    {
        public const string Default = "https://core.roassensor.com";

        /// <summary>No DNS, ever. Only ever compared against, never sent to.</summary>
        public const string Dead = "https://api.roassensor.com";

        /// <summary>The base URL to use: blank or the dead host become <see cref="Default"/>,
        /// anything else is kept, minus surrounding whitespace and a trailing slash.</summary>
        public static string Normalize(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return Default;
            return IsDead(baseUrl) ? Default : baseUrl.Trim().TrimEnd('/');
        }

        /// <summary>True for the dead host, ignoring case, whitespace and a trailing slash.</summary>
        public static bool IsDead(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            return string.Equals(baseUrl.Trim().TrimEnd('/'), Dead, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A queued beacon's URL, moved onto <paramref name="baseUrl"/> when it was queued against
        /// the dead host. Those entries could never be delivered (a DNS failure is a network error,
        /// which retries forever), so without this every event a player recorded before the update
        /// would sit in the queue for good.
        /// </summary>
        public static string Retarget(string queuedUrl, string baseUrl)
        {
            if (string.IsNullOrEmpty(queuedUrl)) return queuedUrl;
            if (queuedUrl.Length > Dead.Length
                && queuedUrl.StartsWith(Dead, StringComparison.OrdinalIgnoreCase)
                && queuedUrl[Dead.Length] == '/')
            {
                return Normalize(baseUrl) + queuedUrl.Substring(Dead.Length);
            }
            return queuedUrl;
        }
    }
}
