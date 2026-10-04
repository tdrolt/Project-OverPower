namespace Overpower.Dominion
{
    /// <summary>
    /// The respawn shield (Task 1 rules; the component is Task 7). A player who respawns is shielded for a few seconds so a spawn camper
    /// cannot kill them the instant they appear. The shield ends at a server-clock time (an int in ms that wraps, so it is only compared as
    /// unchecked(now - end)); 0 means no shield is up. While it is up the player neither captures a zone nor blocks an enemy capture, so the
    /// shield cannot be used to hold a zone for free.
    /// </summary>
    public static class RespawnShieldRules
    {
        /// <summary>True while now is before the end time. 0 = never any shield (the cleared value).</summary>
        public static bool IsUp(int endMs, int nowMs) => endMs != 0 && unchecked(nowMs - endMs) < 0;

        /// <summary>The value the shield owner writes when it learns it dealt damage: 0 = cleared, shield down. Attacking from inside the
        /// shield would be a free shot, so the first hit dealt drops it.</summary>
        public static int EndAfterDamageDealt() => 0;

        /// <summary>An up shield blocks incoming damage (and shows BLOCKED).</summary>
        public static bool BlocksDamage(bool up) => up;

        /// <summary>A shielded player neither captures nor blocks a capture; once the shield is down they count as anyone.</summary>
        public static bool CountsForCapture(bool shieldUp) => !shieldUp;

        /// <summary>Casting an ability that hits nothing keeps the shield: only dealing damage ends it early.</summary>
        public static bool EndsOnAbilityWithoutHit => false;
    }
}
