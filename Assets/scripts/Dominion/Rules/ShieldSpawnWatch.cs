using System;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 17 (Tudor A51), the respawn shield's "leave your spawn and it drops" with the engine taken out: each frame the owner's client
    /// asks whether a shield is up and, only then, whether the player stands in their own spawn; RespawnShieldRules.DropsOnLeavingSpawn decides, and the
    /// drop action is the shield's existing clear (dShd and dShs to 0, which every client reads). RespawnShield builds one over the real room and
    /// position; the tests build one over plain values. The spawn lookup is behind a delegate that is only called while a shield is up, so the many
    /// frames without a shield cost nothing.
    /// </summary>
    public sealed class ShieldSpawnWatch
    {
        private readonly Func<bool> shieldUp;
        private readonly Func<bool> inOwnSpawn;
        private readonly Action drop;

        public ShieldSpawnWatch(Func<bool> shieldUp, Func<bool> inOwnSpawn, Action drop)
        {
            this.shieldUp = shieldUp;
            this.inOwnSpawn = inOwnSpawn;
            this.drop = drop;
        }

        /// <summary>One frame. True when the shield was dropped on this call.</summary>
        public bool Tick()
        {
            if (!shieldUp()) return false;
            if (!RespawnShieldRules.DropsOnLeavingSpawn(true, inOwnSpawn())) return false;
            drop();
            return true;
        }
    }
}
