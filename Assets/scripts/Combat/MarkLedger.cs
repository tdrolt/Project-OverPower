using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>What one landed hit did to its attacker's mark on this target.</summary>
    public enum MarkOutcome : byte
    {
        /// <summary>The weapon does not mark, or the attacker is unknown. Marks are untouched.</summary>
        None = 0,
        /// <summary>No live mark from this attacker, so this hit placed one. Normal damage.</summary>
        Applied = 1,
        /// <summary>This attacker's mark was live: this hit uses it up and deals the bonus.</summary>
        Cashed = 2,
    }

    /// <summary>
    /// The Mark laser's rule (Tudor, 2026-09-18). One TARGET's marks, kept on that target's own client, because
    /// damage is victim-side: the client that applies the damage is the one that decides the bonus, with no message.
    /// PlayerHealth.ApplyDamage asks it only for a hit that LANDS (HitVerdict.Lands), so self, teammate, shield-blocked
    /// hits and hits on the dead neither mark nor cash. Keyed per attacker: at most one mark per attacker, cashable only
    /// by whoever placed it; another attacker's hit places their own mark and leaves yours alone. Cashing never re-marks,
    /// so a steady stream alternates mark, cash, mark... The window is inclusive and measured on this client's clock,
    /// hit landing to hit landing.
    /// No allocation per hit once an attacker has an entry (Dictionary reuses freed slots); entries are bounded by the
    /// players in the room.
    /// </summary>
    public sealed class MarkLedger
    {
        private readonly Dictionary<int, float> expiresAtByAttacker = new Dictionary<int, float>();

        public MarkOutcome OnLandedHit(int attackerActor, float now, float windowSeconds)
        {
            if (attackerActor <= 0 || !(windowSeconds > 0f))
                return MarkOutcome.None;

            if (expiresAtByAttacker.TryGetValue(attackerActor, out float expiresAt) && now <= expiresAt)
            {
                expiresAtByAttacker.Remove(attackerActor);
                return MarkOutcome.Cashed;
            }

            expiresAtByAttacker[attackerActor] = now + windowSeconds;
            return MarkOutcome.Applied;
        }

        public float SecondsLeft(int attackerActor, float now) =>
            expiresAtByAttacker.TryGetValue(attackerActor, out float expiresAt) && now <= expiresAt ? expiresAt - now : 0f;

        /// <summary>The most time left on ANY live mark on this target, regardless of attacker - Tudor's
        /// answer 1: the marked player also sees a diamond, at most one whoever marked them, so the
        /// longest live mark is enough (step 5 reads this through PlayerHealth.LongestMarkSecondsLeft).
        /// An expired entry that was never cashed still sits in the dictionary (SecondsLeft's own
        /// comment - bounded by the players in the room) but is filtered out here the same way
        /// SecondsLeft filters it for a single attacker. No allocation: foreach over Dictionary uses
        /// its own struct enumerator.</summary>
        public float LongestSecondsLeft(float now)
        {
            float longest = 0f;
            foreach (KeyValuePair<int, float> entry in expiresAtByAttacker)
            {
                if (now > entry.Value)
                    continue;

                float left = entry.Value - now;
                if (left > longest)
                    longest = left;
            }
            return longest;
        }

        public void Clear() => expiresAtByAttacker.Clear();

        public static float ScaledAmount(float amount, MarkOutcome outcome, float markedDamageMultiplier) =>
            outcome == MarkOutcome.Cashed ? amount * System.Math.Max(0f, markedDamageMultiplier) : amount;
    }
}
