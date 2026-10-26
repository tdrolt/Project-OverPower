using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The two armor upgrade paths (absorption, recharge wait) plus the one shared refill speed. Absorb Levels
    /// and Recharge Seconds are two SEPARATE arrays, not paired tiers: a player's two level counters (see
    /// Combat/ArmorUpgradePath) each index their own array, so two absorb upgrades and none in recharge read
    /// AbsorbLevels[2] and RechargeSeconds[0]. Level 0 is the starting armor, not "no armor". Adding a level
    /// is a data change. Read-only properties, never written at runtime (see GameplayConfig).
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
        /// The index is clamped, not thrown on: a level can arrive from the network and a malformed packet
        /// must never raise an IndexOutOfRangeException here. 0 when the array is empty ("no armor configured").
        /// </summary>
        public float AbsorbFor(int level)
        {
            if (absorbLevels == null || absorbLevels.Length == 0)
                return 0f;

            return absorbLevels[Mathf.Clamp(level, 0, absorbLevels.Length - 1)];
        }

        /// <summary>The longest recharge wait over ALL levels - what "out of combat" must clear so that selling
        /// armour back to a slower level later cannot leave the clock short of the delay.</summary>
        public float MaxRechargeSeconds
        {
            get
            {
                float max = 0f;
                if (rechargeSeconds != null)
                    foreach (float sec in rechargeSeconds) if (sec > max) max = sec;
                return max;
            }
        }

        /// <summary>Clamped rather than thrown on, as AbsorbFor.</summary>
        public float RechargeSecondsFor(int level)
        {
            if (rechargeSeconds == null || rechargeSeconds.Length == 0)
                return 0f;

            return rechargeSeconds[Mathf.Clamp(level, 0, rechargeSeconds.Length - 1)];
        }

        /// <summary>ArmorUpgradePath reads this to know when the path is maxed out.</summary>
        public int AbsorbLevelCount => absorbLevels != null ? absorbLevels.Length : 0;

        public int RechargeLevelCount => rechargeSeconds != null ? rechargeSeconds.Length : 0;

        /// <summary>purchaseIndex 0 is the first upgrade bought this match, whichever path. Clamped, as AbsorbFor.</summary>
        public int CostFor(int purchaseIndex)
        {
            if (upgradeCosts == null || upgradeCosts.Length == 0)
                return 0;

            return upgradeCosts[Mathf.Clamp(purchaseIndex, 0, upgradeCosts.Length - 1)];
        }
    }
}
