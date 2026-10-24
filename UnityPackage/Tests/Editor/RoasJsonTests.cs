using System;
using System.Collections.Generic;
using NUnit.Framework;
using RoasSensor.Internal;

namespace RoasSensor.Tests
{
    public sealed class RoasJsonTests
    {
        private static string Props(Dictionary<string, object> properties) =>
            RoasJson.FromProperties(properties).ToString();

        [Test]
        public void FromProperties_PassesPlainValuesThrough()
        {
            Assert.AreEqual(
                "{\"plan\":\"pro\",\"trial\":true,\"level\":3,\"score\":2.5}",
                Props(new Dictionary<string, object> { { "plan", "pro" }, { "trial", true }, { "level", 3 }, { "score", 2.5 } }));
        }

        [Test]
        public void FromProperties_DropsNaNAndInfinity()
        {
            // Before 0.1.8 these were written as the bare words NaN / Infinity, which is not
            // JSON, and the server refused the whole event.
            Assert.AreEqual(
                "{\"ok\":1}",
                Props(new Dictionary<string, object>
                {
                    { "nan", double.NaN }, { "inf", double.PositiveInfinity }, { "fnan", float.NaN },
                    { "ok", 1 }, { "nothing", null },
                }));
        }

        [Test]
        public void FromProperties_DatesBecomeIso8601Utc()
        {
            var utc = new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc);
            var offset = new DateTimeOffset(2026, 10, 23, 15, 0, 0, TimeSpan.FromHours(5.5));
            Assert.AreEqual(
                "{\"at\":\"2026-10-23T09:30:00Z\",\"local\":\"2026-10-23T09:30:00Z\"}",
                Props(new Dictionary<string, object> { { "at", utc }, { "local", offset } }));
        }

        [Test]
        public void FromProperties_UriAndUnknownTypesBecomeStrings()
        {
            Assert.AreEqual(
                "{\"link\":\"https://example.com/a?b=1\",\"day\":\"Friday\",\"letter\":\"x\"}",
                Props(new Dictionary<string, object>
                {
                    { "link", new Uri("https://example.com/a?b=1") }, { "day", DayOfWeek.Friday }, { "letter", 'x' },
                }));
        }

        [Test]
        public void FromProperties_CleansNestedCollections()
        {
            var nested = new Dictionary<string, object>
            {
                { "items", new List<object> { 1, double.NaN, "two", null } },
                { "meta", new Dictionary<string, int> { { "a", 1 } } },
                { "ids", new[] { 7L, 8L } },
            };
            Assert.AreEqual(
                "{\"items\":[1,\"two\"],\"meta\":{\"a\":1},\"ids\":[7,8]}",
                Props(nested));
        }

        [Test]
        public void FromProperties_StopsAtASelfContainingList()
        {
            var loop = new List<object>();
            loop.Add(loop);
            // Must return rather than overflow the stack; what is left is empty nesting.
            Assert.DoesNotThrow(() => Props(new Dictionary<string, object> { { "loop", loop } }));
        }

        [Test]
        public void WriteValue_NeverWritesBareNaN()
        {
            var body = new RoasJson().Put("a", double.NaN).Put("b", float.PositiveInfinity).Put("c", 1.5m);
            Assert.AreEqual("{\"a\":null,\"b\":null,\"c\":1.5}", body.ToString());
        }

        [Test]
        public void FromProperties_NullIsAnEmptyObject()
        {
            Assert.AreEqual("{}", RoasJson.FromProperties(null).ToString());
        }
    }
}
