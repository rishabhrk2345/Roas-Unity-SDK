using System.Text;
using NUnit.Framework;
using RoasSensor.Internal;

namespace RoasSensor.Tests
{
    public sealed class RoasSignerTests
    {
        [Test]
        public void Sign_NullSecretReturnsNull()
        {
            var body = Encoding.UTF8.GetBytes("{\"site\":\"x\"}");
            Assert.IsNull(RoasSigner.Sign(null, body, 1700000000));
            Assert.IsNull(RoasSigner.Sign(string.Empty, body, 1700000000));
        }

        [Test]
        public void Sign_HeaderShapeIsTEqualsCommaV1Equals()
        {
            var body = Encoding.UTF8.GetBytes("{\"site\":\"x\"}");
            var header = RoasSigner.Sign("s3cret", body, 1700000000);
            Assert.IsNotNull(header);
            Assert.IsTrue(header.StartsWith("t=1700000000,v1="));
            var hex = header.Substring(header.IndexOf("v1=") + 3);
            Assert.AreEqual(64, hex.Length); // hex-encoded SHA-256
        }

        [Test]
        public void Sign_IsDeterministicForSameInputs()
        {
            var body = Encoding.UTF8.GetBytes("{\"a\":1}");
            var first = RoasSigner.Sign("secret", body, 42);
            var second = RoasSigner.Sign("secret", body, 42);
            Assert.AreEqual(first, second);
        }

        [Test]
        public void Sign_DifferentBodyProducesDifferentSignature()
        {
            var a = RoasSigner.Sign("secret", Encoding.UTF8.GetBytes("{\"a\":1}"), 42);
            var b = RoasSigner.Sign("secret", Encoding.UTF8.GetBytes("{\"a\":2}"), 42);
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Sign_DifferentTimestampProducesDifferentSignature()
        {
            var body = Encoding.UTF8.GetBytes("{\"a\":1}");
            var a = RoasSigner.Sign("secret", body, 1);
            var b = RoasSigner.Sign("secret", body, 2);
            Assert.AreNotEqual(a, b);
        }
    }
}
