namespace Overpower.Net
{
    /// <summary>
    /// The Player Property that lets every client show STUNNED / SLOWED over a player (Tudor D18). Statuses
    /// are victim-side: only the victim's own client applies and ticks them, so a remote copy of that player
    /// knows nothing. The victim's client therefore publishes the label it is wearing under one key, only
    /// when it changes: { label, endServerMs, durationMs } - end as Photon server time, so every client
    /// reads the same seconds left whatever its own clock says.
    /// </summary>
    public static class StatusLabelProperty
    {
        public const string Key = "st";

        /// <summary>No label: the value a cleared status publishes.</summary>
        public static int[] None() => new int[] { 0, 0, 0 };

        public static int[] Encode(int label, int endServerMs, int durationMs) =>
            new int[] { label, endServerMs, durationMs };

        /// <summary>Reads the value a player published, never throwing: a missing key or a value of the
        /// wrong shape (a stale build, a player who has not published yet) reads as no label.</summary>
        public static bool TryDecode(object raw, out int label, out int endServerMs, out int durationMs)
        {
            label = 0;
            endServerMs = 0;
            durationMs = 0;

            if (!(raw is int[] values) || values.Length < 3)
                return false;

            label = values[0];
            endServerMs = values[1];
            durationMs = values[2];
            return label == 1 || label == 2; // Slowed or Stunned only: a mismatched build cannot show a label by accident.
        }
    }
}
