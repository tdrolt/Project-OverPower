using UnityEngine;
using UnityEngine.Serialization;

namespace Overpower.Data
{
    /// <summary>Gold-per-second reference lines for the four GDD "economy scenario" bands in the
    /// 3-team phase (p.37) - the HTML report's Phase 1 tab (and the whole-match tab, up to the
    /// transition) draws these as horizontal lines on the team-income chart so Tudor can see which
    /// band a team is tracking in, at a glance. Task T7: renamed from the pre-phase-split
    /// ScenarioIncomeTargets - same four bands, now explicitly Phase 1's own set now that Phase 2 has
    /// a different one (see ScenarioIncomeTargetsPhase2).</summary>
    [System.Serializable]
    public struct ScenarioIncomeTargetsPhase1
    {
        public float Losing;
        public float Struggling;
        public float Average;
        public float Dominant;
    }

    /// <summary>Task T7: the GDD's own, DIFFERENT 2-team-phase economy scenario bands (p.38) - three
    /// bands, not four, and none of them share Phase 1's names ("Even"/"Winning" replace "Struggling"/
    /// "Dominant" - the GDD's own wording for the 2-team phase). Drawn on the Phase 2 tab (and the
    /// whole-match tab, after the transition).</summary>
    [System.Serializable]
    public struct ScenarioIncomeTargetsPhase2
    {
        public float Losing;
        public float Even;
        public float Winning;
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
        public ScenarioIncomeTargetsPhase1 Phase1ScenarioIncomePerTeam = new ScenarioIncomeTargetsPhase1
        {
            Losing = 5f,
            Struggling = 15f,
            Average = 23f,
            Dominant = 33f,
        };

        /// <summary>Task T7 (GDD p.38).</summary>
        public ScenarioIncomeTargetsPhase2 Phase2ScenarioIncomePerTeam = new ScenarioIncomeTargetsPhase2
        {
            Losing = 5f,
            Even = 15f,
            Winning = 25f,
        };

        public PurchaseTimingTargets PurchaseTargetMinutes = new PurchaseTimingTargets
        {
            PrimaryUpgrade1Minutes = 6.5f,
            Armor1Minutes = 10f,
            UltimateMinutes = 13.5f,
            PrimaryUpgrade2Minutes = 16.5f,
            Armor2Minutes = 20f,
        };

        /// <summary>Task T7 (GDD p.36): the 3-team phase's own expected duration.</summary>
        public float Phase1DurationSeconds = 900f;

        /// <summary>Task T7 (GDD p.36): the 2-team phase's own expected duration.</summary>
        public float Phase2DurationSeconds = 450f;

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
        // Task T7: renamed to Phase1* (the 3-team phase's own scenarios - Phase 2 has a different
        // set, below) via FormerlySerializedAs, so the existing asset's serialized values
        // (losingIncomePerSecond: 5, strugglingIncomePerSecond: 15, averageIncomePerSecond: 23,
        // dominantIncomePerSecond: 33) keep loading into these renamed fields with no YAML edit
        // needed at all - a missing/renamed key just falls back to Unity's own FormerlySerializedAs
        // migration on load. Confirmed via eval: git diff on BalanceTargets.asset stays empty.
        [Header("Phase 1 (3-team) gold-flow scenarios (GDD p.37)")]
        [FormerlySerializedAs("losingIncomePerSecond")]
        [Tooltip("Gold/s per team the GDD calls 'Losing' in the 3-team phase (p.37). Reference line only.")]
        [SerializeField] private float phase1LosingIncomePerSecond = 5f;
        public float Phase1LosingIncomePerSecond => phase1LosingIncomePerSecond;

        [FormerlySerializedAs("strugglingIncomePerSecond")]
        [Tooltip("Gold/s per team the GDD calls 'Struggling' in the 3-team phase (p.37). Reference line only.")]
        [SerializeField] private float phase1StrugglingIncomePerSecond = 15f;
        public float Phase1StrugglingIncomePerSecond => phase1StrugglingIncomePerSecond;

        [FormerlySerializedAs("averageIncomePerSecond")]
        [Tooltip("Gold/s per team the GDD calls 'Average' in the 3-team phase (p.37). Reference line only.")]
        [SerializeField] private float phase1AverageIncomePerSecond = 23f;
        public float Phase1AverageIncomePerSecond => phase1AverageIncomePerSecond;

        [FormerlySerializedAs("dominantIncomePerSecond")]
        [Tooltip("Gold/s per team the GDD calls 'Dominant' in the 3-team phase (p.37). Reference line only.")]
        [SerializeField] private float phase1DominantIncomePerSecond = 33f;
        public float Phase1DominantIncomePerSecond => phase1DominantIncomePerSecond;

        [Header("Phase 2 (2-team) gold-flow scenarios (GDD p.38)")]
        [Tooltip("Gold/s per team the GDD calls 'Losing' in the 2-team phase (p.38). Reference line only.")]
        [SerializeField] private float phase2LosingIncomePerSecond = 5f;
        public float Phase2LosingIncomePerSecond => phase2LosingIncomePerSecond;

        [Tooltip("Gold/s per team the GDD calls 'Even' in the 2-team phase (p.38). Reference line only.")]
        [SerializeField] private float phase2EvenIncomePerSecond = 15f;
        public float Phase2EvenIncomePerSecond => phase2EvenIncomePerSecond;

        [Tooltip("Gold/s per team the GDD calls 'Winning' in the 2-team phase (p.38). Reference line only.")]
        [SerializeField] private float phase2WinningIncomePerSecond = 25f;
        public float Phase2WinningIncomePerSecond => phase2WinningIncomePerSecond;

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

        [Header("Phase durations (GDD p.36)")]
        [Tooltip("Expected Phase 1 (3-team) duration in seconds (GDD p.36).")]
        [SerializeField] private float phase1DurationSeconds = 900f;
        public float Phase1DurationSeconds => phase1DurationSeconds;

        [Tooltip("Expected Phase 2 (2-team) duration in seconds (GDD p.36).")]
        [SerializeField] private float phase2DurationSeconds = 450f;
        public float Phase2DurationSeconds => phase2DurationSeconds;

        [Header("Match length (GDD p.36)")]
        [Tooltip("Expected match length in seconds (GDD p.36) = Phase 1 + Phase 2. Reference line only.")]
        [SerializeField] private float targetMatchSeconds = 1350f;
        public float TargetMatchSeconds => targetMatchSeconds;

        /// <summary>A plain, asset-free copy for the aggregator/HTML writer - see BalanceTargetsData's
        /// own comment on why this exists.</summary>
        public BalanceTargetsData ToData()
        {
            return new BalanceTargetsData
            {
                Phase1ScenarioIncomePerTeam = new ScenarioIncomeTargetsPhase1
                {
                    Losing = phase1LosingIncomePerSecond,
                    Struggling = phase1StrugglingIncomePerSecond,
                    Average = phase1AverageIncomePerSecond,
                    Dominant = phase1DominantIncomePerSecond,
                },
                Phase2ScenarioIncomePerTeam = new ScenarioIncomeTargetsPhase2
                {
                    Losing = phase2LosingIncomePerSecond,
                    Even = phase2EvenIncomePerSecond,
                    Winning = phase2WinningIncomePerSecond,
                },
                PurchaseTargetMinutes = new PurchaseTimingTargets
                {
                    PrimaryUpgrade1Minutes = primaryUpgrade1TargetMinutes,
                    Armor1Minutes = armor1TargetMinutes,
                    UltimateMinutes = ultimateTargetMinutes,
                    PrimaryUpgrade2Minutes = primaryUpgrade2TargetMinutes,
                    Armor2Minutes = armor2TargetMinutes,
                },
                Phase1DurationSeconds = phase1DurationSeconds,
                Phase2DurationSeconds = phase2DurationSeconds,
                TargetMatchSeconds = targetMatchSeconds,
            };
        }
    }
}
