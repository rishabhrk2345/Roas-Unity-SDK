using System;
using System.Security.Cryptography;
using System.Text;

namespace RoasSensor.Internal
{
    /// <summary>
    /// HMAC-SHA256 beacon signing — byte-for-byte the same wire format the Kotlin/Swift
    /// SDKs use and the backend's <c>apps/tracking/signing.py</c> verifies:
    ///
    ///   X-Roas-Signature: t=&lt;epoch seconds&gt;,v1=&lt;hex&gt;
    ///   signed payload   = "&lt;t&gt;." + raw request body bytes
    ///
    /// A native/Unity app sends no Origin header, so the collector has no way to tell a
    /// real install apart from a script carrying the (public) site key. Signing raises
    /// that from "read the key off a network trace" to "reverse-engineer the binary" —
    /// it is not claimed to be unforgeable, and rotating the secret invalidates every
    /// build carrying the old one.
    /// </summary>
    internal static class RoasSigner
    {
        public const string Header = "X-Roas-Signature";

        /// <summary>
        /// The header value for <paramref name="body"/> at <paramref name="epochSeconds"/>,
        /// or null when no secret was configured — the server treats a missing signature
        /// as an old/unsigned build and accepts it until the site turns enforcement on.
        /// </summary>
        public static string Sign(string secret, byte[] body, long epochSeconds)
        {
            if (string.IsNullOrEmpty(secret)) return null;
            try
            {
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                {
                    var prefix = Encoding.UTF8.GetBytes(epochSeconds.ToString() + ".");
                    var buffer = new byte[prefix.Length + body.Length];
                    Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
                    Buffer.BlockCopy(body, 0, buffer, prefix.Length, body.Length);
                    var digest = hmac.ComputeHash(buffer);
                    var sb = new StringBuilder(digest.Length * 2);
                    foreach (var b in digest) sb.Append(b.ToString("x2"));
                    return "t=" + epochSeconds + ",v1=" + sb;
                }
            }
            catch
            {
                // A missing crypto provider is not worth losing an install over — an
                // unsigned beacon is still accepted unless the site enforces signing.
                return null;
            }
        }
    }
}
