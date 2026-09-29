using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RoasSensor.Internal
{
    /// <summary>
    /// PII hashing, mirrored BYTE-FOR-BYTE from the backend's <c>security.py</c>
    /// (<c>hash_email</c> / <c>normalize_phone</c> / <c>hash_phone</c>) and from the web SDK's
    /// <c>sdk/src/hash.ts</c> and the native Kotlin/Swift SDKs' <c>Hashing.kt</c> / <c>Hashing.swift</c>.
    ///
    /// Email/phone are hashed ON-DEVICE so the raw value never leaves the device; the server
    /// only ever sees the hash and matches on it. Break this parity — a phone typed on a
    /// non-Latin keypad hashing differently here than on the server — and identity matching
    /// silently fails with no error anywhere. Do not "improve" this without checking
    /// docs/tracking-pipeline-guide.md in the main roas-sensor-service repo first.
    /// </summary>
    internal static class RoasHashing
    {
        private const int MinPhoneDigits = 7;

        public static string Sha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(bytes.Length * 2);
                // .NET's byte is already unsigned (0-255), unlike Kotlin's signed Byte —
                // so the "& 0xFF" sign-extension trap the native SDKs have to guard
                // against does not exist here. Kept as a lowercase hex format regardless,
                // to match the server's `%02x` exactly.
                foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Unsalted SHA-256 of the trimmed, lowercased email (Meta CAPI form).
        /// Returns "" for a blank/null email — an empty hash is never sent.</summary>
        public static string HashEmail(string email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
            return normalized.Length == 0 ? string.Empty : Sha256Hex(normalized);
        }

        /// <summary>Unsalted SHA-256 of the normalized phone (leading '+' kept).
        /// Returns "" for fewer than 7 digits, so a garbled number never becomes a
        /// matchable key — matching the backend's `hash_phone` guard exactly.</summary>
        public static string HashPhone(string phone)
        {
            var normalized = NormalizePhone(phone);
            var digitCount = 0;
            foreach (var c in normalized) if (c != '+') digitCount++;
            return digitCount < MinPhoneDigits ? string.Empty : Sha256Hex(normalized);
        }

        /// <summary>
        /// Three identical steps to the backend's `normalize_phone`:
        ///  1. NFKC-normalize (folds full-width digits and other compatibility forms).
        ///  2. Fold Arabic-Indic (U+0660-0669) and Extended/Persian (U+06F0-06F9) digits
        ///     to ASCII — NFKC alone does not do this fold.
        ///  3. Keep only ASCII digits and a leading '+'.
        /// </summary>
        private static string NormalizePhone(string phone)
        {
            var nfkc = (phone ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC);
            var folded = new StringBuilder(nfkc.Length);
            for (int i = 0; i < nfkc.Length; i++)
            {
                var c = nfkc[i];
                if (c >= 0x0660 && c <= 0x0669) folded.Append((char)('0' + (c - 0x0660)));
                else if (c >= 0x06F0 && c <= 0x06F9) folded.Append((char)('0' + (c - 0x06F0)));
                else folded.Append(c);
            }
            var result = new StringBuilder(folded.Length);
            foreach (var c in folded.ToString())
            {
                if ((c >= '0' && c <= '9') || c == '+') result.Append(c);
            }
            return result.ToString();
        }
    }
}
