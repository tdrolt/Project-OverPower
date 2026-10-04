using System;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 7b: what a player's own client asks before an enemy's status or push lands on them (PlayerStatusEffects.Apply,
    /// PlayerDisplacement.Displace). RespawnShield implements it; the callers hold only this interface, so a test can stand a fake in. True =
    /// the respawn shield stops it: do nothing, and BLOCKED has already been stamped.
    /// </summary>
    public interface IEffectShield
    {
        /// <summary>True when the shield is up and sourceActor is an enemy of this player (own and teammate effects are never stopped).</summary>
        bool StopsEnemyEffectFrom(int sourceActor);
    }

    /// <summary>
    /// The shield's per-hit judgement with the engine taken out: it asks RespawnShieldRules, remembers when it last stamped BLOCKED, and writes
    /// a new stamp when one is due. RespawnShield builds one over the real room and clock; the tests build one over plain values. Every damage,
    /// status and push on a shielded victim goes through one of the two methods, so one stamp spacing covers all of them.
    /// </summary>
    public sealed class ShieldJudge
    {
        private readonly Func<bool> shieldUp;
        private readonly Func<int> nowMs;
        private readonly Func<int> popupMs;
        private readonly Action<int> writeStamp;
        private int lastStampMs;

        public ShieldJudge(Func<bool> shieldUp, Func<int> nowMs, Func<int> popupMs, Action<int> writeStamp)
        {
            this.shieldUp = shieldUp;
            this.nowMs = nowMs;
            this.popupMs = popupMs;
            this.writeStamp = writeStamp;
        }

        /// <summary>Damage: true = stop the hit before anything changes. An enemy's shows BLOCKED; a self-hit is stopped silently; a teammate's is left to friendly fire.</summary>
        public bool JudgeHit(RespawnShieldRules.Origin origin) => Settle(RespawnShieldRules.OnIncomingHit(shieldUp(), origin, lastStampMs, nowMs(), popupMs()));

        /// <summary>A status or a push: true = stop it. Only an enemy's is stopped (and shows BLOCKED).</summary>
        public bool JudgeEffect(RespawnShieldRules.Origin origin) => Settle(RespawnShieldRules.OnIncomingEffect(shieldUp(), origin, lastStampMs, nowMs(), popupMs()));

        private bool Settle(RespawnShieldRules.HitDecision decision)
        {
            int now = nowMs();
            if (decision.WriteStamp && now != 0) // no server clock yet: nothing to stamp with, the hit is still stopped
            {
                lastStampMs = now;
                writeStamp(now);
            }
            return decision.Blocked;
        }
    }
}
