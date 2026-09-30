using UnityEngine;

namespace Overpower.Net
{
    /// <summary>
    /// Whether a remote copy of a player jumps straight to the owner's newest position instead of gliding there
    /// (movement step 5). Pure, tested in edit mode (RemoteSnapRuleTests).
    ///
    /// Decided on the jump between two updates the owner actually sent, not on how far the smoothed copy trails: a zip
    /// pull at 25 m/s leaves that copy metres behind, and judging the trail snapped ordinary fast movement. Ordinary
    /// movement covers at most about 1.25 m per update at 20 updates a second; a blink, a portal, a respawn or a
    /// teleport covers many metres in one.
    ///
    /// Lost updates stretch an ordinary jump, so the allowance grows with the number of send intervals between the two
    /// updates' server timestamps - up to MaxCountedIntervals, past which the copy is so stale that catching up at once
    /// reads better than a long glide. The first update always snaps: until then the copy sits wherever it spawned.
    /// </summary>
    public static class RemoteSnapRule
    {
        /// <summary>Not a tuning value: after this many missed updates the copy is stale rather than moving.</summary>
        public const int MaxCountedIntervals = 4;

        public static bool ShouldSnap(bool hasPrevious, Vector3 previous, int previousStampMs,
                                       Vector3 next, int nextStampMs, float snapDistance, float sendIntervalMs)
        {
            if (!hasPrevious)
                return true;

            float jump = Vector3.Distance(previous, next);
            return jump > snapDistance * ElapsedIntervals(previousStampMs, nextStampMs, sendIntervalMs);
        }

        /// <summary>Send intervals between two server timestamps, 1..MaxCountedIntervals. The subtraction is unchecked
        /// because ServerTimestamp wraps about every 49.7 days (see DeployableAge); an earlier, equal or unusable stamp
        /// counts as one interval.</summary>
        public static int ElapsedIntervals(int previousStampMs, int nextStampMs, float sendIntervalMs)
        {
            int elapsedMs = unchecked(nextStampMs - previousStampMs);
            if (elapsedMs <= 0 || sendIntervalMs <= 0f)
                return 1;

            return Mathf.Clamp(Mathf.RoundToInt(elapsedMs / sendIntervalMs), 1, MaxCountedIntervals);
        }
    }
}
