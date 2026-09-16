using UnityEngine;

namespace Overpower.Data
{
    /// <summary>Gold-per-second reference lines for the four GDD "economy scenario" bands (p.37) - the
    /// HTML report draws these as horizontal lines on the team-income chart so Tudor can see which
    /// band a team is tracking in, at a glance.</summary>
    [System.Serializable]
    public struct ScenarioIncomeTargets
    {
        public float Losing;
        public float Struggling;
        public float Average;
        public float Dominant;
    }

    /// <summary>Minutes into the match each purchase is expected to have happened (GDD p.37) - the HTML
    /// report compares these against the purchase timeline.</summary>
    [System.Serializable]
    public struct PurchaseTimingTargets
    {
        public float PrimaryUpgrade1Minutes;
        public float Armor1Minutes;
        public float UltimateMinutes;
        public float PrimaryUpgrade2Minutes;
        public float Armor2Minutes;
    }

    /// <summary>Plain data copy of <see cref="BalanceTargets"/> - Task T6's HTML writer takes this
    /// directly (no ScriptableObject reference needed), so tests can hand it hand-written numbers
    /// without loading the asset. These are reference lines ONLY: nothing in the game reads them,
    /// they exist purely so the report can show Tudor how a match compares to the GDD's own numbers.</summary>
    [System.Serializable]
    public sealed class BalanceTargetsData
    {
        public ScenarioIncomeTargets ScenarioIncomePerTeam = new ScenarioIncomeTargets
        {
            Losing = 5f,
            Struggling = 15f,
            Average = 23f,
            Dominant = 33f,
        };

        public PurchaseTimingTargets PurchaseTargetMinutes = new PurchaseTimingTargets
        {
            PrimaryUpgrade1Minutes = 6.5f,
            Armor1Minutes = 10f,
            UltimateMinutes = 13.5f,
            PrimaryUpgrade2Minutes = 16.5f,
            Armor2Minutes = 20f,
        };

        public float TargetMatchSeconds = 1350f;
    }

    /// <summary>
    /// The GDD's own balance numbers (Task T6), one asset with a tooltip on every field citing the
    /// page it came from - same shape as every other Data config in this project. These are reference
    /// lines drawn on the HTML report only; nothing in the game reads this asset at runtime (unlike
    /// GameplayConfig/TerritoryConfig/ArmorConfig, which the match itself simulates against).
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Balance Targets")]
    public sealed class BalanceTargets : ScriptableObject
    {
        [Header("Gold-flow scenarios (GDD p.37)")]
        [Tooltip("Gold/s per team the GDD calls 'Losing' (p.37). Reference line only.")]
        [SerializeField] private float losingIncomePerSecond = 5f;
        public float LosingIncomePerSecond => losingIncomePerSecond;

        [Tooltip("Gold/s per team the GDD calls 'Struggling' (p.37). Reference line only.")]
        [SerializeField] private float strugglingIncomePerSecond = 15f;
        public float StrugglingIncomePerSecond => strugglingIncomePerSecond;

        [Tooltip("Gold/s per team the GDD calls 'Average' (p.37). Reference line only.")]
        [SerializeField] private float averageIncomePerSecond = 23f;
        public float AverageIncomePerSecond => averageIncomePerSecond;

        [Tooltip("Gold/s per team the GDD calls 'Dominant' (p.37). Reference line only.")]
        [SerializeField] private float dominantIncomePerSecond = 33f;
        public float DominantIncomePerSecond => dominantIncomePerSecond;

        [Header("Purchase timing targets (GDD p.37)")]
        [Tooltip("Minute a team is expected to have bought Primary Upgrade 1 (GDD p.37).")]
        [SerializeField] private float primaryUpgrade1TargetMinutes = 6.5f;
        public float PrimaryUpgrade1TargetMinutes => primaryUpgrade1TargetMinutes;

        [Tooltip("Minute a team is expected to have bought Armor 1 (GDD p.37).")]
        [SerializeField] private float armor1TargetMinutes = 10f;
        public float Armor1TargetMinutes => armor1TargetMinutes;

        [Tooltip("Minute a team is expected to have bought the Ultimate (GDD p.37).")]
        [SerializeField] private float ultimateTargetMinutes = 13.5f;
        public float UltimateTargetMinutes => ultimateTargetMinutes;

        [Tooltip("Minute a team is expected to have bought Primary Upgrade 2 (GDD p.37).")]
        [SerializeField] private float primaryUpgrade2TargetMinutes = 16.5f;
        public float PrimaryUpgrade2TargetMinutes => primaryUpgrade2TargetMinutes;

        [Tooltip("Minute a team is expected to have bought Armor 2 (GDD p.37).")]
        [SerializeField] private float armor2TargetMinutes = 20f;
        public float Armor2TargetMinutes => armor2TargetMinutes;

        [Header("Match length (GDD p.36)")]
        [Tooltip("Expected match length in seconds (GDD p.36). Reference line only.")]
        [SerializeField] private float targetMatchSeconds = 1350f;
        public float TargetMatchSeconds => targetMatchSeconds;

        /// <summary>A plain, asset-free copy for the aggregator/HTML writer - see BalanceTargetsData's
        /// own comment on why this exists.</summary>
        public BalanceTargetsData ToData()
        {
            return new BalanceTargetsData
            {
                ScenarioIncomePerTeam = new ScenarioIncomeTargets
                {
                    Losing = losingIncomePerSecond,
                    Struggling = strugglingIncomePerSecond,
                    Average = averageIncomePerSecond,
                    Dominant = dominantIncomePerSecond,
                },
                PurchaseTargetMinutes = new PurchaseTimingTargets
                {
                    PrimaryUpgrade1Minutes = primaryUpgrade1TargetMinutes,
                    Armor1Minutes = armor1TargetMinutes,
                    UltimateMinutes = ultimateTargetMinutes,
                    PrimaryUpgrade2Minutes = primaryUpgrade2TargetMinutes,
                    Armor2Minutes = armor2TargetMinutes,
                },
                TargetMatchSeconds = targetMatchSeconds,
            };
        }
    }
}
