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
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int:
                case long:
                case short:
                case byte:
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
