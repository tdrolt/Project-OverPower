using System.Globalization;
using System.Text;

namespace Overpower.Telemetry
{
    /// <summary>Builds one telemetry event as a single flat JSON line. It reuses one StringBuilder, so logging
    /// dozens of events a second doesn't churn memory. Numbers are always written the same way whatever language
    /// Windows is set to (invariant culture), or a Dutch or German PC would write "12,5" and break the report.</summary>
    public sealed class TelemetryLine
    {
        private readonly StringBuilder sb = new StringBuilder(256);

        public TelemetryLine Begin(string eventName, double matchSeconds)
        {
            sb.Clear();
            sb.Append("{\"e\":");
            AppendString(eventName);
            sb.Append(",\"t\":");
            sb.Append(System.Math.Round(matchSeconds, 3).ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryLine Int(string key, int value) { Key(key); sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

        public TelemetryLine Float(string key, float value)
        {
            Key(key);
            if (float.IsNaN(value) || float.IsInfinity(value)) sb.Append("null");
            else sb.Append(value.ToString("0.####", CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryLine Bool(string key, bool value) { Key(key); sb.Append(value ? "true" : "false"); return this; }

        public TelemetryLine String(string key, string value) { Key(key); AppendString(value ?? ""); return this; }

        public TelemetryLine Ints(string key, int[] values)
        {
            Key(key); sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return this;
        }

        /// <summary>A raw, already-valid JSON value (used only for the session header's tuning snapshot).</summary>
        public TelemetryLine Raw(string key, string json) { Key(key); sb.Append(json); return this; }

        public string End() { sb.Append('}'); return sb.ToString(); }

        private void Key(string key) { sb.Append(','); AppendString(key); sb.Append(':'); }

        private void AppendString(string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
