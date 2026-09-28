using NUnit.Framework;
using RoasSensor.Internal;

namespace RoasSensor.Tests
{
    /// <summary>
    /// Parity vectors mirrored from the backend's `security.py` and the Kotlin/Swift SDKs'
    /// own `HashingParityTest`/`HashingParityTests`. If one of these ever needs to change,
    /// the backend and every other platform's SDK need the identical change first -- see
    /// RoasHashing's class doc.
    /// </summary>
    public sealed class RoasHashingTests
    {
        [Test]
        public void HashEmail_TrimsAndLowercases()
        {
            var withSpacing = RoasHashing.HashEmail("  Buyer@Example.com  ");
            var canonical = RoasHashing.HashEmail("buyer@example.com");
            Assert.AreEqual(canonical, withSpacing);
            Assert.AreEqual(64, canonical.Length);
        }

        [Test]
        public void HashEmail_EmptyReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, RoasHashing.HashEmail(null));
            Assert.AreEqual(string.Empty, RoasHashing.HashEmail(""));
            Assert.AreEqual(string.Empty, RoasHashing.HashEmail("   "));
        }

        [Test]
        public void HashEmail_MatchesRawSha256OfNormalizedForm()
        {
            Assert.AreEqual(RoasHashing.Sha256Hex("buyer@example.com"), RoasHashing.HashEmail("buyer@example.com"));
        }

        [Test]
        public void HashPhone_FoldsArabicIndicDigits()
        {
            // ٠١٢٣٤٥٦٧ (Arabic-Indic 0-7) should fold to "01234567" before hashing.
            var folded = RoasHashing.HashPhone("٠١٢٣٤٥٦٧");
            var ascii = RoasHashing.HashPhone("01234567");
            Assert.AreEqual(ascii, folded);
            Assert.AreNotEqual(string.Empty, ascii);
        }

        [Test]
        public void HashPhone_ShortNumberReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, RoasHashing.HashPhone("12345")); // 5 digits < MIN_PHONE_DIGITS
        }

        [Test]
        public void HashPhone_KeepsLeadingPlus()
        {
            var withPlus = RoasHashing.HashPhone("+1 (555) 123-4567");
            var withoutPlus = RoasHashing.HashPhone("15551234567");
            Assert.AreNotEqual(withPlus, withoutPlus); // '+' is part of the normalized/hashed string
            Assert.AreNotEqual(string.Empty, withPlus);
        }

        [Test]
        public void Sha256Hex_NeverSignExtends()
        {
            // A regression guard for the exact class of bug the Kotlin SDK's docs call out:
            // a byte >= 0x80 must still format as two lowercase hex digits, never "ff..".
            var digest = RoasHashing.Sha256Hex("test-value-that-hashes-to-high-bytes");
            Assert.AreEqual(64, digest.Length);
            foreach (var c in digest)
            {
                Assert.IsTrue((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), $"unexpected hex char '{c}'");
            }
        }
    }
}
