using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// Whether a zone is "under attack" (Tudor, 2026-09-16): a living player of any team other than the owner stands in
    /// it, or left it less than the linger time ago. The linger stops the state flickering while someone steps on and
    /// off the edge. A neutral zone is never under attack; there is nothing to defend.
    ///
    /// Pure, so every client reaches the same answer from the same replicated arrays: a team bitmask per zone of who
    /// stands there now, and a server-ms stamp per zone per team of when that team last left.
    /// </summary>
    public static class ZoneThreat
    {
        public const int MaxTeams = 3;

        public static int TeamBit(int team) => team >= 0 && team < MaxTeams ? 1 << team : 0;

        /// <param name="owner">The zone's owner, or -1 for neutral.</param>
        /// <param name="presentMask">Bit t set = a living player of team t is inside the zone now.</param>
        /// <param name="lastSeenMs">Server ms each team last left a zone; this zone's three entries start at
        /// <paramref name="offset"/> (zone × MaxTeams). 0 = never.</param>
        /// <param name="nowMs">PhotonNetwork.ServerTimestamp. 0 = the clock isn't synced yet, so only players standing
        /// inside count.</param>
        public static bool IsUnderAttack(int owner, int presentMask, int[] lastSeenMs, int offset, int nowMs, int lingerMs)
        {
            if (owner < 0)
                return false;

            for (int team = 0; team < MaxTeams; team++)
            {
                if (team == owner)
                    continue;
                if ((presentMask & TeamBit(team)) != 0)
                    return true;
                if (nowMs == 0 || lastSeenMs == null || offset + team >= lastSeenMs.Length)
                    continue;
                int seen = lastSeenMs[offset + team];
                // unchecked: the server clock is an int that wraps, and a wrapped subtraction is still the true gap.
                // Compared as unsigned so a stamp AHEAD of this clock (a negative gap) never counts: otherwise a stale
                // stamp would keep a zone under attack until the clock caught up with it, possibly for minutes. A small
                // negative gap from clients' slightly different clock estimates just means "not under attack" for a few
                // ms, which is harmless.
                if (seen != 0 && unchecked((uint)(nowMs - seen) < (uint)lingerMs))
                    return true;
            }
            return false;
        }

        /// <summary>A team bitmask per zone from each living player's (team, zone). Zone -1 (standing in no zone) and
        /// team ids outside 0..MaxTeams-1 are ignored.</summary>
        public static int[] PresenceMasks(int zoneCount, IEnumerable<(int team, int zone)> players)
        {
            var masks = new int[zoneCount];
            foreach ((int team, int zone) in players)
                if (zone >= 0 && zone < zoneCount)
                    masks[zone] |= TeamBit(team);
            return masks;
        }

        /// <summary>Stamps lastSeenMs[zone × MaxTeams + team] with nowMs for every team present in
        /// <paramref name="before"/> and gone in <paramref name="after"/>. Returns true if it stamped anything.</summary>
        public static bool StampDepartures(int[] before, int[] after, int[] lastSeenMs, int nowMs)
        {
            bool stamped = false;
            int zones = System.Math.Min(before.Length, after.Length);
            for (int zone = 0; zone < zones; zone++)
            {
                int left = before[zone] & ~after[zone];
                if (left == 0) continue;
                for (int team = 0; team < MaxTeams; team++)
                {
                    if ((left & TeamBit(team)) == 0) continue;
                    int index = zone * MaxTeams + team;
                    if (index < lastSeenMs.Length)
                    {
                        lastSeenMs[index] = nowMs;
                        stamped = true;
                    }
                }
            }
            return stamped;
        }
    }
}
