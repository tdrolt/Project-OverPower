using UnityEngine;

namespace Overpower.Dominion
{
    /// <summary>
    /// How fast a player's own spawn heals them in Dominion (Task 6). In Conquest health only comes back out of combat in an owned zone; a
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

        /// <summary>The fixed respawn wait for the match size: 2 teams = 2v2, otherwise 3v3v3.</summary>
        public static float RespawnSeconds(int teamCount, float seconds2v2, float seconds3v3v3) =>
            teamCount <= 2 ? seconds2v2 : seconds3v3v3;
    }
}
