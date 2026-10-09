using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// Who a NEUTRAL zone's capture belongs to right now, worked out fresh every tick from who is actually
    /// listed inside it. Never from a collider trigger: a remote player's collider fires OnTriggerEnter on every
    /// client with no IsMine check, while only a player reported by their OWN client ever gets listed, so a
    /// trigger-set claim could lock out a lone real capturer behind another team's write nobody could clear.
    /// Owned zones and drains are not decided here - DrainRule does that from the same listed-players list.
    /// </summary>
    public static class CaptureClaimRule
    {
        /// <param name="capturingId">The zone's current claim, -1 = unset.</param>
        /// <param name="captureProgress">One-player-seconds banked under that claim so far.</param>
        /// <param name="listedTeamsInOrder">playersInZone's teams, in the order they were added - a lone
        /// caller-owned buffer, not allocated here.</param>
        /// <returns>The claim to keep this tick: the current claim and its progress if a listed player is
        /// still on that team (even if contested by another team too); otherwise the first listed player's
        /// team from 0 progress; (-1, 0) if nobody is listed at all.</returns>
        public static (int CapturingId, float CaptureProgress) Resolve(int capturingId, float captureProgress,
                                                                        IReadOnlyList<int> listedTeamsInOrder)
        {
            int count = listedTeamsInOrder.Count;
            if (count == 0)
                return (-1, 0f);

            for (int i = 0; i < count; i++)
            {
                if (listedTeamsInOrder[i] == capturingId)
                    return (capturingId, captureProgress);
            }

            // capturingId is -1 (never set, or just cleared) or nobody currently listed is on that team
            // (a stale claim, from any source): the claim moves to whoever is actually here, from zero.
            return (listedTeamsInOrder[0], 0f);
        }
    }
}
