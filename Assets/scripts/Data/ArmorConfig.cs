using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The two armor upgrade paths a player can buy into independently - more absorption, or a
    /// shorter recharge wait - plus the one shared number that controls how fast a refill climbs
    /// once it starts. Absorb Levels and Recharge Seconds are two SEPARATE arrays, not paired
    /// tiers: a player's absorb level and recharge level are two independent counters (see
    /// Combat/ArmorUpgradePath), each indexing into its own array here. A player who has bought two
    /// absorb upgrades and zero recharge upgrades reads AbsorbLevels[2] and RechargeSeconds[0], not
    /// the same index into both. Level 0 in each array is what everyone starts the match with, not
    /// "no armor".
    ///
    /// Arrays mean that adding a level to either path is a data change a designer makes in the
    /// Inspector, not a code change - which is the entire point of this asset.
    ///
    /// Fields are [SerializeField] private with read-only properties for the same reason as the
    /// rest of the Data folder: a ScriptableObject is one shared instance per process, so
    /// mutating one at runtime edits the asset in the Editor and does nothing in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Armor Config")]
    public sealed class ArmorConfig : ScriptableObject
    {
        [Header("Absorb path")]
        [Tooltip("How much damage the absorb path soaks up at each level it is upgraded to, " +
                 "cheapest first. Level 0 is the armor everyone starts a match with. This path is " +
                 "independent of Recharge Seconds below - see the class comment - so its length " +
                 "does not need to match.")]
        [SerializeField] private float[] absorbLevels = { 25f, 50f, 100f };
        public float[] AbsorbLevels => absorbLevels;

        [Header("Recharge path")]
        [Tooltip("How many seconds a player must be out of combat before armor starts refilling, " +
                 "at each level the recharge path is upgraded to, fastest last. Level 0 is the " +
                 "wait everyone starts a match with. Independent of Absorb Levels above - see the " +
                 "class comment.")]
        [SerializeField] private float[] rechargeSeconds = { 6f, 4f, 2f };
        public float[] RechargeSeconds => rechargeSeconds;

        [Header("Refill")]
        [Tooltip("Seconds for armor to climb from empty to full once a refill starts - one number " +
                 "shared by every tier of every path. Only the WAIT before a refill starts " +
                 "(Recharge Seconds, above) changes with the recharge path; the speed of the climb " +
                 "itself does not. A pool that was only partly drained reaches full sooner, in " +
                 "proportion, since it has less distance to climb at this same rate.")]
        [SerializeField] private float refillSeconds = 2.5f;
        public float RefillSeconds => refillSeconds;

        [Header("Upgrades")]
        [Tooltip("How many armor upgrades a player may buy this match, absorb and recharge " +
                 "combined - each purchase is a choice between the two paths, not two separate " +
                 "currencies. The GDD text says armor upgrades three times, but the price sheet " +
                 "only prices two, so this stays at 2 until a third price is agreed. Raise it to 3 " +
                 "to turn the third upgrade on - no code change needed.")]
        [SerializeField] private int maxArmorUpgrades = 2;
        public int MaxArmorUpgrades => maxArmorUpgrades;

        [Tooltip("Gold cost of the 1st, 2nd and 3rd armor upgrade bought overall - absorb or " +
                 "recharge, whichever is chosen - in the order they are bought. Entries beyond Max " +
                 "Armor Upgrades are unreachable until that cap is raised.")]
        [SerializeField] private int[] upgradeCosts = { 1400, 1800, 2200 };
        public int[] UpgradeCosts => upgradeCosts;

        [Header("Respawn")]
        [Tooltip("On: a respawned player starts with full armor for their levels. Off: they start " +
                 "empty and earn it back through the out-of-combat timer, the same as anyone who " +
                 "broke their armor mid-fight.")]
        [SerializeField] private bool respawnWithFullArmor = true;
        public bool RespawnWithFullArmor => respawnWithFullArmor;

        /// <summary>
        /// How much the given absorb level holds. The index is clamped into range instead of
        /// throwing, because a level number can arrive from the network, and a malformed packet
        /// from another client must never be able to take down this one with an
        /// IndexOutOfRangeException. Returns 0 when the array is empty, which is the safe reading
        /// of "no armor configured".
        /// </summary>
        public float AbsorbFor(int level)
        {
            if (absorbLevels == null || absorbLevels.Length == 0)
                return 0f;

            return absorbLevels[Mathf.Clamp(level, 0, absorbLevels.Length - 1)];
        }

        /// <summary>
        /// How long the given recharge level waits out of combat before refilling. Clamped rather
        /// than throwing, for the same reason as AbsorbFor.
        /// </summary>
        public float RechargeSecondsFor(int level)
        {
            if (rechargeSeconds == null || rechargeSeconds.Length == 0)
                return 0f;

            return rechargeSeconds[Mathf.Clamp(level, 0, rechargeSeconds.Length - 1)];
        }

        /// <summary>How many levels the absorb path has, top level included. ArmorUpgradePath reads
        /// this to know when that path is maxed out, without reaching into the array directly.</summary>
        public int AbsorbLevelCount => absorbLevels != null ? absorbLevels.Length : 0;

        /// <summary>How many levels the recharge path has - see AbsorbLevelCount.</summary>
        public int RechargeLevelCount => rechargeSeconds != null ? rechargeSeconds.Length : 0;

        /// <summary>Gold cost of the given purchase number (0 = the first upgrade bought this match,
        /// regardless of path). Clamped rather than throwing, for the same reason as AbsorbFor.</summary>
        public int CostFor(int purchaseIndex)
        {
            if (upgradeCosts == null || upgradeCosts.Length == 0)
                return 0;

            return upgradeCosts[Mathf.Clamp(purchaseIndex, 0, upgradeCosts.Length - 1)];
        }
    }
}
