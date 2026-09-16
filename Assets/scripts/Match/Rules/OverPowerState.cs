using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// The OverPower comeback rule (GDD p.20) as a small state machine: being hit by both enemy teams
    /// within a short window near your own territory arms it; dropping low while armed fires it once;
    /// leaving that territory's area ends it either way. Rewards an outnumbered defender and punishes
    /// third-partying without touching anyone who isn't defending.
    /// </summary>
    public sealed class OverPowerState
    {
        private readonly float windowSeconds;
        private readonly float maxDistance;
        private readonly float healthThreshold;
        private readonly Dictionary<int, float> lastHitTimeByTeam = new Dictionary<int, float>();

        public bool Armed { get; private set; }
        public bool Active { get; private set; }

        public OverPowerState(float windowSeconds, float maxDistance, float healthThreshold)
        {
            this.windowSeconds = windowSeconds;
            this.maxDistance = maxDistance;
            this.healthThreshold = healthThreshold;
        }

        public void RecordEnemyHit(int attackerTeam, float time, float distanceToOwnedZone)
        {
            if (attackerTeam < 0 || distanceToOwnedZone > maxDistance) return;
            lastHitTimeByTeam[attackerTeam] = time;

            int teamsInWindow = 0;
            foreach (float hitTime in lastHitTimeByTeam.Values)
                if (time - hitTime <= windowSeconds) teamsInWindow++;
            if (teamsInWindow >= 2) Armed = true;
        }

        public void UpdateDistance(float distanceToOwnedZone)
        {
            if (distanceToOwnedZone <= maxDistance) return;
            Armed = false;
            Active = false;
            lastHitTimeByTeam.Clear();
        }

        /// <returns>True exactly once: the moment the buff should apply.</returns>
        public bool CheckTrigger(float health)
        {
            if (!Armed || Active || health >= healthThreshold) return false;
            Active = true;
            return true;
        }

        /// <summary>
        /// Ends the buff outright, armed or active, the instant its holder dies - [C, Task 2.6, not
        /// in the GDD: see assumptions-for-tudor.md, "OverPower (built)"]. A buff surviving a respawn
        /// at base (full health, full shield, still 3-second-window-armed from a fight that ended in
        /// a death) would be an odd carry-over the GDD never considered, so death clears this exactly
        /// like distancing yourself from the territory does (see UpdateDistance) rather than leaving
        /// it to expire on its own on the next tick.
        /// </summary>
        public void EndOnDeath()
        {
            Armed = false;
            Active = false;
            lastHitTimeByTeam.Clear();
        }
    }
}
