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
    }
}
