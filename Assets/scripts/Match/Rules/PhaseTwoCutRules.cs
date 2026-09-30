using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// GDD p.20-21 and its p.27 drawing (Tudor, 2026-09-25): when the first team is knocked out, its corner of the
    /// triangle closes - its capital, its Tier II and the two Tier III next to that Tier II leave play - and the centre
    /// plays as a Tier III. A host-started two-team match plays on that cut map from going live (the left-out team's
    /// corner). Pure C#: every client derives the same answer from the room's facts (MatchDirector.CutTeam), and the
    /// zones are found from the territory links and the towers' own tiers, never from zone numbers.
    /// </summary>
    public static class PhaseTwoCutRules
    {
        public const int NoCut = -1;
        private const int TeamCount = 3;

        /// <summary>The team whose corner is closed - centre-circle-and-cut-rule, 2026-09-26 (Tudor: "for choosing
        /// which corner to eliminate you can take the original spawn of each team and eliminate the spawn of the
        /// team that also got eliminated"): the corner is always the FIRST team knocked out, so this is a pure
        /// function of facts the room already has, no Room Property of its own needed. None before live; in a
        /// two-team match (a host start), the team left out of it (unchanged - there was never an elimination to
        /// read there); with three teams in the match, the first team eliminated (eliminated[0]) once there is one,
        /// else none yet.
        ///
        /// One cosmetic edge, cut-rule-followups review, 2026-09-26: the phase rules' fixpoint can in principle knock
        /// out two teams in the very same write, going straight from three teams to Over. Room state then looks
        /// identical to the normal three -> two -> over path (teamsInMatch never drops to two; eliminated simply
        /// arrives with both teams already in it), so this still answers eliminated[0] and the FIRST team's corner
        /// closes on the result screen, same as if it had been knocked out alone. Harmless - the match is already
        /// final by then.</summary>
        public static int CutTeam(bool live, IReadOnlyList<int> teamsInMatch, IReadOnlyList<int> eliminated)
        {
            if (!live)
                return NoCut;
            if (teamsInMatch != null && teamsInMatch.Count == TeamCount - 1)
            {
                for (int team = 0; team < TeamCount; team++)
                    if (!Contains(teamsInMatch, team))
                        return team;
                return NoCut;
            }
            return eliminated != null && eliminated.Count > 0 ? eliminated[0] : NoCut;
        }

        /// <summary>The zones a cut removes, ascending: the cut team's capital, the Tier II next to it, and every Tier III
        /// next to that Tier II. The centre (Tier IV) is next to every Tier II but is never cut - it becomes a Tier III.
        /// </summary>
        /// <param name="baseTierOf">A zone's own tier as set on its tower (1-4), never the phase-two stand-in.</param>
        public static List<int> CutZones(TerritoryMap map, int cutTeam, Func<int, int> baseTierOf)
        {
            var zones = new List<int>();
            if (map == null || cutTeam < 0 || baseTierOf == null)
                return zones;
            int capital = map.CapitalOf(cutTeam);
            if (capital < 0)
                return zones;

            zones.Add(capital);
            IReadOnlyList<int> nextToCapital = map.AdjacentTo(capital);
            for (int i = 0; i < nextToCapital.Count; i++)
            {
                int tierTwo = nextToCapital[i];
                if (baseTierOf(tierTwo) != 2)
                    continue;
                AddOnce(zones, tierTwo);
                IReadOnlyList<int> nextToTierTwo = map.AdjacentTo(tierTwo);
                for (int j = 0; j < nextToTierTwo.Count; j++)
                    if (baseTierOf(nextToTierTwo[j]) == 3)
                        AddOnce(zones, nextToTierTwo[j]);
            }
            zones.Sort();
            return zones;
        }

        /// <summary>The same question as CutZones for one zone, without allocating: the ring, the tower and the minimap
        /// ask it every frame (through MatchDirector.IsOutOfPlay).</summary>
        public static bool IsZoneCut(TerritoryMap map, int zone, int cutTeam, Func<int, int> baseTierOf)
        {
            if (map == null || cutTeam < 0 || baseTierOf == null)
                return false;
            int capital = map.CapitalOf(cutTeam);
            if (capital < 0)
                return false;
            if (zone == capital)
                return true;

            IReadOnlyList<int> nextToCapital = map.AdjacentTo(capital);
            for (int i = 0; i < nextToCapital.Count; i++)
            {
                int tierTwo = nextToCapital[i];
                if (baseTierOf(tierTwo) != 2)
                    continue;
                if (zone == tierTwo)
                    return true;
                if (baseTierOf(zone) != 3)
                    continue;
                IReadOnlyList<int> nextToTierTwo = map.AdjacentTo(tierTwo);
                for (int j = 0; j < nextToTierTwo.Count; j++)
                    if (nextToTierTwo[j] == zone)
                        return true;
            }
            return false;
        }

        /// <summary>Tier IV (the centre) plays as Tier III while a corner is cut - GDD p.27 draws a III where the IV was;
        /// Tudor: "no tier 4, only 2 tier 3 in the middle". Every other tier is unchanged.</summary>
        public static int EffectiveTier(int baseTier, bool cutActive) => cutActive && baseTier == 4 ? 3 : baseTier;

        /// <summary>What the master sets neutral (without bounty history) at the knockout, ascending: every Tier III and
        /// the centre (GDD p.21, "all tier 3 territories become neutral" - the centre is one now), plus every cut zone, so
        /// nobody keeps an income or a way in from behind the wall.</summary>
        public static List<int> ZonesToNeutralise(TerritoryMap map, int cutTeam, Func<int, int> baseTierOf, int zoneCount)
        {
            List<int> zones = CutZones(map, cutTeam, baseTierOf);
            if (baseTierOf != null)
                for (int zone = 0; zone < zoneCount; zone++)
                {
                    int tier = baseTierOf(zone);
                    if (tier == 3 || tier == 4)
                        AddOnce(zones, zone);
                }
            zones.Sort();
            return zones;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value)
                    return true;
            return false;
        }

        private static void AddOnce(List<int> list, int value)
        {
            if (!list.Contains(value))
                list.Add(value);
        }
    }
}
