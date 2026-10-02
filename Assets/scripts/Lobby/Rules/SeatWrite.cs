using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>
    /// One seat change as a Photon-style compare-and-swap: Props are the values to write, Expected are the values the
    /// room must still hold for the write to apply. Two players clicking the same seat both send a write that expects
    /// it empty, so the server accepts only the first and the second leaves the room untouched. None means "nothing
    /// to send".
    /// </summary>
    public sealed class SeatWrite
    {
        public static readonly SeatWrite None = new SeatWrite(null, null);

        public readonly Dictionary<string, object> Props;
        public readonly Dictionary<string, object> Expected;

        public SeatWrite(Dictionary<string, object> props, Dictionary<string, object> expected)
        {
            Props = props;
            Expected = expected;
        }

        public bool IsNone => Props == null;

        /// <summary>The write as one comparable text: every key with its new and expected value, keys in order. Two writes that change
        /// the same seats the same way have the same signature.</summary>
        public string Signature()
        {
            if (IsNone) return "";
            var keys = new List<string>(Props.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var text = new System.Text.StringBuilder();
            foreach (string key in keys)
            {
                text.Append(key).Append('=').Append(Props[key] ?? "-").Append('?');
                if (Expected != null && Expected.TryGetValue(key, out object expected)) text.Append(expected ?? "-");
                else text.Append('*');
                text.Append(';');
            }
            if (Expected != null)
            {
                var extra = new List<string>();
                foreach (string key in Expected.Keys) if (!Props.ContainsKey(key)) extra.Add(key);
                extra.Sort(System.StringComparer.Ordinal);
                foreach (string key in extra) text.Append('+').Append(key).Append('?').Append(Expected[key] ?? "-").Append(';');
            }
            return text.ToString();
        }
    }
}
