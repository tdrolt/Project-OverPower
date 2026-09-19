using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// How many rounds one trigger pull of a charging weapon sends out, and where along the hold each extra round is
    /// earned. Pulled out of WeaponFiring (charge step 1) for two reasons: it is the arithmetic a live bug hid in
    /// (Tudor, 2026-09-17: "spamming the burst upgrade made the burst have more bullets than 3"), and the charge ring
    /// on the ground has to mark the SAME step positions the gun actually uses - one home, so a tick can never promise
    /// a round the gun does not give.
    ///
    /// Plain numbers, no WeaponDefinition: a ScriptableObject cannot be built in a plain edit-mode test without
    /// reflection, and nothing here needs anything but four numbers. WeaponFiring reads them off the asset.
    /// </summary>
    public static class ChargeCountRule
    {
        /// <summary>The quantised hold, 0..1: which of the Steps + 1 even levels this fraction has REACHED.
        /// steps of 0 or less leaves the hold smooth, for a weapon that charges something continuous
        /// (damage or range, not a round count).</summary>
        public static float QuantisedFraction(float chargeFraction, int steps)
        {
            // NaN is possible: chargeFraction arrives as an RPC parameter, so a broken or mismatched client can put
            // anything in it, and FloorToInt(NaN) is a large negative number rather than an error. "> 0" is false for
            // NaN, so this one test covers NaN, negatives and a dead-zero hold together.
            if (!(chargeFraction > 0f))
                return 0f;

            float clamped = Mathf.Clamp01(chargeFraction);
            if (steps <= 0)
                return clamped;

            // FloorToInt, not RoundToInt (Tudor, 2026-09-17: "spamming the burst upgrade made the burst have more
            // bullets than 3"). Rounding gave a step away at the HALFWAY point to it, so on a 0.35 s bar an ordinary
            // 90 ms click already sat above 25% and bought the 4th round for nothing - the charge time is meant to BE
            // the price of the payoff. Flooring means a step is only ever reached by completing it, and the charge
            // ring's ticks mark exactly where that happens.
            return Mathf.Min(steps, Mathf.FloorToInt(clamped * steps)) / (float)steps;
        }

        /// <summary>The hold fraction at which step <paramref name="index"/> is reached - where the charge ring's tick
        /// marks go. 0 for step 0, 1 for the last step.</summary>
        public static float StepFraction(int index, int steps) =>
            steps <= 0 ? 0f : Mathf.Clamp01(index / (float)steps);

        /// <summary>
        /// Rounds one pull sends out. A weapon that does not stack rounds while held (Charge Max Projectiles at or
        /// below Projectiles Per Shot - every weapon but 06 today) always sends Projectiles Per Shot.
        ///
        /// The count is rounded rather than floored HERE on purpose: at every quantised level the lerp already lands
        /// on a whole number for a weapon whose Charge Steps equal its round span (06: 2 steps across 3 -> 5), and
        /// rounding only decides the shape of a config where they do not. Mathf.RoundToInt rounds an exact .5 to
        /// EVEN, so such a config is lopsided - see the tooltip on WeaponDefinition.ChargeSteps.
        /// </summary>
        public static int Rounds(int projectilesPerShot, int chargeMaxProjectiles, int steps, float chargeFraction)
        {
            if (chargeMaxProjectiles <= projectilesPerShot)
                return Mathf.Max(1, projectilesPerShot);

            float quantised = QuantisedFraction(chargeFraction, steps);
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(projectilesPerShot, chargeMaxProjectiles, quantised)));
        }
    }
}
