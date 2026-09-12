using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The armor tiers a player can buy, as two parallel arrays rather than three named fields.
    /// Arrays mean that adding a fourth tier is a data change a designer makes in the Inspector,
    /// not a code change - which is the entire point of this asset.
    ///
    /// The index into both arrays is the tier, counting from 0. Entry 0 of Absorb Levels pairs
    /// with entry 0 of Recharge Seconds, and so on, so the two arrays must stay the same length.
    ///
    /// Fields are [SerializeField] private with read-only properties for the same reason as the
    /// rest of the Data folder: a ScriptableObject is one shared instance per process, so
    /// mutating one at runtime edits the asset in the Editor and does nothing in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Armor Config")]
    public sealed class ArmorConfig : ScriptableObject
    {
        [Header("Tiers")]
        [Tooltip("How much damage each armor tier soaks up before it breaks, one entry per tier, " +
                 "cheapest tier first. Adding a fourth number here adds a fourth tier to the " +
                 "game - no script has to change. Keep this the same length as Recharge Seconds.")]
        [SerializeField] private float[] absorbLevels = { 25f, 50f, 100f };
        public float[] AbsorbLevels => absorbLevels;

        [Tooltip("How many seconds a player must be out of combat before that tier's armor " +
                 "refills, one entry per tier, in the same order as Absorb Levels. Armor refills " +
                 "all at once when the wait is up, it does not trickle back in.")]
        [SerializeField] private float[] rechargeSeconds = { 6f, 4f, 2f };
        public float[] RechargeSeconds => rechargeSeconds;

        /// <summary>
        /// How much the given tier absorbs. The index is clamped into range instead of throwing,
        /// because a tier number can arrive from the network, and a malformed packet from another
        /// client must never be able to take down this one with an IndexOutOfRangeException.
        /// Returns 0 when the array is empty, which is the safe reading of "no armor configured".
        /// </summary>
        public float AbsorbFor(int tier)
        {
            if (absorbLevels == null || absorbLevels.Length == 0)
                return 0f;

            return absorbLevels[Mathf.Clamp(tier, 0, absorbLevels.Length - 1)];
        }

        /// <summary>
        /// How long the given tier waits out of combat before refilling. Clamped rather than
        /// throwing, for the same reason as AbsorbFor.
        /// </summary>
        public float RechargeSecondsFor(int tier)
        {
            if (rechargeSeconds == null || rechargeSeconds.Length == 0)
                return 0f;

            return rechargeSeconds[Mathf.Clamp(tier, 0, rechargeSeconds.Length - 1)];
        }
    }
}
