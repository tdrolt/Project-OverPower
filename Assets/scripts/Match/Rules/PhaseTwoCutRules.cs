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

        /// <summary>The team whose corner is closed: none before live; the one the master stored at the knockout once
        /// there is one; otherwise, in a two-team match, the team left out of it.</summary>
        public static int CutTeam(bool live, IReadOnlyList<int> teamsInMatch, int storedCutTeam)
        {
            if (!live)
                return NoCut;
            if (storedCutTeam >= 0)
                return storedCutTeam;
            if (teamsInMatch == null || teamsInMatch.Count != TeamCount - 1)
                return NoCut;
            for (int team = 0; team < TeamCount; team++)
                if (!Contains(teamsInMatch, team))
                    return team;
            return NoCut;
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

        /// <summary>
        /// Which corner closes at the knockout ([C], 2026-09-25). The knocked-out team's - unless a surviving team holds
        /// that corner's capital and no other (it lost its own and took theirs): closing it would knock that team out
        /// too. Then the first team (lowest number) whose corner strands nobody and whose capital is not held by its own
        /// team (nobody lives there). Every corner stranding someone can't happen with two survivors; the knocked-out
        /// team's corner is the fallback anyway.
        /// </summary>
        /// <param name="ownerOfCapitalOf">For a team, who holds that team's own starting capital now (-1 = nobody).</param>
        public static int ChooseCutTeam(int knockedOut, IReadOnlyList<int> survivors, Func<int, int> ownerOfCapitalOf)
        {
            if (knockedOut < 0 || ownerOfCapitalOf == null)
                return NoCut;
            if (!StrandsASurvivor(knockedOut, survivors, ownerOfCapitalOf))
                return knockedOut;
            for (int team = 0; team < TeamCount; team++)
            {
                if (team == knockedOut || ownerOfCapitalOf(team) == team)
                    continue;
                if (!StrandsASurvivor(team, survivors, ownerOfCapitalOf))
                    return team;
            }
            return knockedOut;
        }

        // Closing this corner strands a survivor who holds its capital and no other.
        private static bool StrandsASurvivor(int corner, IReadOnlyList<int> survivors, Func<int, int> ownerOfCapitalOf)
        {
            int holder = ownerOfCapitalOf(corner);
            if (holder < 0 || !Contains(survivors, holder))
                return false;
            for (int team = 0; team < TeamCount; team++)
                if (team != corner && ownerOfCapitalOf(team) == holder)
                    return false;
            return true;
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
