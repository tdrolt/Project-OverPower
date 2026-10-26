using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Dominion
{
    /// <summary>
    /// How fast a player's own spawn heals them in Dominion. In Conquest health only comes back out of combat in an owned zone; a
    /// Dominion spawn heals even mid-fight, slowly, and quickly once the player has been out of combat for a while. All numbers come in as
    /// arguments (DominionConfig holds them), so the tests use made-up ones.
    /// </summary>
    public static class DominionHealRules
    {
        /// <summary>Health per second from the player's own spawn. 0 when the player is not standing in their own spawn (somewhere else, or in an
        /// enemy's), so the caller falls back to the ordinary zone regen. In the spawn: the out-of-combat rate once the player has been out of
        /// combat for the delay, the in-combat rate before that.</summary>
        public static float RatePerSecond(bool inOwnSpawn, float secondsSinceCombat, float outOfCombatPerSecond, float inCombatPerSecond,
                                          float outOfCombatDelaySeconds)
        {
            if (!inOwnSpawn) return 0f;
            float rate = secondsSinceCombat >= outOfCombatDelaySeconds ? outOfCombatPerSecond : inCombatPerSecond;
            return Mathf.Max(0f, rate);
        }

        /// <summary>The rate to heal at: the spawn's rate while the player stands in their own spawn, else the ordinary zone regen.</summary>
        public static float HealRate(bool inOwnSpawn, float secondsSinceCombat, float outOfCombatPerSecond, float inCombatPerSecond,
                                     float outOfCombatDelaySeconds, float ordinaryZoneRate) =>
            inOwnSpawn ? RatePerSecond(true, secondsSinceCombat, outOfCombatPerSecond, inCombatPerSecond, outOfCombatDelaySeconds)
                       : ordinaryZoneRate;

        /// <summary>The rate a player actually heals at in this stage (A29): none at all in sudden death - neither the spawn's healing nor an owned
        /// zone's regen - and the given rate in every other stage (a Conquest room reads as stage None). Health packs do not come through here.</summary>
        public static float RateInStage(DominionStage stage, float rate) => stage == DominionStage.SuddenDeath ? 0f : rate;

        /// <summary>The fixed respawn wait for the match size: 2 teams = 2v2, otherwise 3v3v3.</summary>
        public static float RespawnSeconds(int teamCount, float seconds2v2, float seconds3v3v3) =>
            teamCount <= 2 ? seconds2v2 : seconds3v3v3;

        /// <summary>Is the player in their own spawn: inside a spawn area the scene registered for their team (the 2v2 pocket), or - in a 3v3v3
        /// match only - inside their own capital circle (the 3v3v3 map needs no scene object). An enemy's capital is never this team's, and a
        /// 2v2 match has no capital circle.</summary>
        public static bool InOwnSpawn(bool inRegisteredArea, int teamCount, bool zoneIsOwnCapital) =>
            inRegisteredArea || (teamCount == 3 && zoneIsOwnCapital);

        /// <summary>The teams of a match that have no healing area registered (empty when the match has three teams: the capital circle is the
        /// spawn there). Used to warn once when a 2v2 match goes live without its spawn areas.</summary>
        public static List<int> TeamsMissingHealArea(int[] teamsInMatch, Func<int, bool> hasArea)
        {
            var missing = new List<int>();
            if (teamsInMatch == null || hasArea == null || teamsInMatch.Length >= 3) return missing;
            foreach (int team in teamsInMatch)
                if (!hasArea(team)) missing.Add(team);
            return missing;
        }
    }
}
