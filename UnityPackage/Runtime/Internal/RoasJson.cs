using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoasSensor.Internal
{
    /// <summary>
    /// A minimal ordered JSON object, standing in for Android's <c>org.json.JSONObject</c>
    /// and Swift's <c>[String: Any]</c> in this port. Unity's own <c>JsonUtility</c> cannot
    /// serialize a free-form <c>Dictionary&lt;string, object&gt;</c> (the shape every beacon
    /// body needs — see <c>Roas.baseBody()</c> in the Kotlin/Swift SDKs), so this exists
    /// instead of pulling in a third-party JSON package as a hard dependency.
    ///
    /// Insertion order is preserved (a <see cref="List{T}"/> of keys beside the dictionary)
    /// purely so a captured beacon body reads the same way across platforms during
    /// debugging — the server does not care about key order.
    /// </summary>
    internal sealed class RoasJson
    {
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        public RoasJson Put(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            if (!_values.ContainsKey(key)) _order.Add(key);
            _values[key] = value;
            return this;
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public object Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

        /// <summary>Deeper than any real properties map; the cap exists for a collection that
        /// contains itself, which would otherwise recurse until the stack overflows.</summary>
        private const int MaxDepth = 8;

        /// <summary>
        /// The game's <c>Track()</c> properties as a JSON object, by the same rules as the native
        /// SDKs' <c>JsonProps</c> (Kotlin) and <c>JSONSafe</c> (Swift), so one event tracked from
        /// a Unity game, an Android app and an iOS app stores the same props:
        ///
        /// - strings, bools and finite numbers pass through;
        /// - NaN and infinity are dropped: there is no JSON for them, and a made-up stand-in
        ///   would be read as a real figure (before 0.1.8 a NaN made the server refuse the event);
        /// - <see cref="DateTime"/> / <see cref="DateTimeOffset"/> become ISO-8601 in UTC
        ///   (<c>2026-10-23T09:30:00Z</c>); a <see cref="Uri"/> its string form;
        /// - dictionaries and lists are cleaned recursively; a non-string key becomes its
        ///   <c>ToString()</c>;
        /// - null is dropped; anything else becomes its <c>ToString()</c>.
        /// </summary>
        public static RoasJson FromProperties(IDictionary<string, object> properties)
        {
            var obj = new RoasJson();
            if (properties == null) return obj;
            foreach (var kv in properties)
            {
                var clean = Clean(kv.Value, 1);
                if (clean != null) obj.Put(kv.Key, clean);
            }
            return obj;
        }

        /// <summary>One value, cleaned; null means "leave it out".</summary>
        internal static object Clean(object raw, int depth)
        {
            if (raw == null || depth > MaxDepth) return null;
            switch (raw)
            {
                case string s:
                    return s;
                case bool b:
                    return b;
                case char c:
                    return c.ToString();
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d) ? null : (object)d;
                case float f:
                    return float.IsNaN(f) || float.IsInfinity(f) ? null : (object)f;
                case int _:
                case long _:
                case short _:
                case byte _:
                case sbyte _:
                case uint _:
                case ulong _:
                case ushort _:
                case decimal _:
                    return raw;
                case DateTime dt:
                    // ToUniversalTime treats an Unspecified kind as local time, which is what
                    // DateTime.Now-derived values are; DateTime.UtcNow passes through unchanged.
                    return dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                case Uri uri:
                    return uri.ToString();
                case RoasJson json:
                    return json;
                case IDictionary dict:
                {
                    var obj = new RoasJson();
                    foreach (DictionaryEntry entry in dict)
                    {
                        var clean = Clean(entry.Value, depth + 1);
                        if (clean != null) obj.Put(entry.Key?.ToString(), clean);
                    }
                    return obj;
                }
                case IEnumerable items:
                {
                    var list = new List<object>();
                    foreach (var item in items)
                    {
                        var clean = Clean(item, depth + 1);
                        if (clean != null) list.Add(clean);
                    }
                    return list;
                }
                default:
                    return raw.ToString();
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < _order.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var key = _order[i];
                WriteString(sb, key);
                sb.Append(':');
                WriteValue(sb, _values[key]);
            }
            sb.Append('}');
            return sb.ToString();
        }

        internal static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case RoasJson obj:
                    sb.Append(obj.ToString());
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                // JSON has no NaN or infinity: "R" would write the bare word NaN and the server
                // would refuse the whole body. Track() already drops such values (Clean); this is
                // the backstop for every other body.
                case float f:
                    if (float.IsNaN(f) || float.IsInfinity(f)) sb.Append("null");
                    else sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int:
                case long:
                case short:
                case byte:
                case sbyte:
                case uint:
                case ulong:
                case ushort:
                case decimal:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool firstEntry = true;
                    foreach (var kv in dict)
                    {
                        if (!firstEntry) sb.Append(',');
                        firstEntry = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable enumerable:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in enumerable)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
