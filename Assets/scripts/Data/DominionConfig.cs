using Overpower.Dominion;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every Dominion number in one asset, so the mode is tuned in one place. The rules that use these numbers live in
    /// Overpower.Dominion (DominionRules and friends) and take them as arguments, so they are tested with made-up numbers,
    /// never with these. Read-only properties, never written at runtime (see GameplayConfig).
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Dominion Config", fileName = "DominionConfig")]
    public sealed class DominionConfig : ScriptableObject
    {
        [Header("Rounds")]
        [Tooltip("How many rounds a team must win to win the match. 2 means best of three.")]
        [SerializeField, Min(1)] private int roundsToWin = 2;

        [Tooltip("The most rounds played before sudden death can start. With Rounds To Win at 2 this is 3 (best of three).")]
        [SerializeField, Min(1)] private int maxRounds = 3;

        [Tooltip("How long one round lasts, in seconds, before the team with the most points wins it.")]
        [SerializeField, Min(1f)] private float roundSeconds = 180f;

        [Tooltip("How long the break between rounds lasts, in seconds (the shop is open during it).")]
        [SerializeField, Min(0f)] private float breakSeconds = 20f;

        [Tooltip("The last seconds of the break that read Round N starts in ... as a countdown.")]
        [SerializeField, Min(0f)] private float breakCountdownSeconds = 5f;

        [Header("Overtime")]
        [Tooltip("The extra time, in seconds, a close round gets when the clock runs out. Every team plays it, even one far behind. If nobody pulls ahead of every other team by the lead before it ends, every team within the lead of the top team gets a round win. 0 turns overtime off: the team with the most points simply wins the round, and a tie counts for nobody.")]
        [SerializeField, Min(0f)] private float overtimeSeconds = 60f;

        [Tooltip("How many points ahead a team must be to win a round. At the buzzer, a team this far ahead of every other team wins the round; if not, and the top teams are less than this far apart, every team plays overtime, and there the first team this far ahead of every other team wins the round at once. When the overtime runs out, each team less than this far behind the top team gets a round win. 0 turns overtime off (the team with the most points simply wins the round, and a tie counts for nobody).")]
        [SerializeField, Min(0)] private int overtimeLeadPoints = 200;

        [Header("Points")]
        [Tooltip("Points per second a team earns for each zone of that tier it holds. The four entries are Tier 1 (capitals), Tier 2, Tier 3 and Tier 4 (the centre), in that order. " +
                 "Capitals and spawns, and the centre, never pay per second whatever is typed here (the rule enforces it); in 2v2 the two zones are Tier 3.")]
        [SerializeField] private int[] pointsPerZonePerSecond = { 0, 5, 5, 0 };

        [Header("Centre (3v3v3 only)")]
        [Tooltip("Points the team holding the centre receives each time the centre pays out.")]
        [SerializeField, Min(0)] private int centrePayoutPoints = 200;

        [Tooltip("Seconds between the centre's payouts after the first one.")]
        [SerializeField, Min(1f)] private float centrePayoutIntervalSeconds = 30f;

        [Tooltip("Seconds after a round starts until the centre pays out for the first time.")]
        [SerializeField, Min(0f)] private float centreFirstPayoutSeconds = 30f;

        [Header("Bounties")]
        [Tooltip("How many seconds a team must hold a zone without losing it before the team that takes it earns a bounty.")]
        [SerializeField, Min(0f)] private float bountyHoldSeconds = 60f;

        [Tooltip("Points the team that takes a long-held zone earns as its bounty.")]
        [SerializeField, Min(0)] private int bountyPoints = 150;

        [Header("Respawns")]
        [Tooltip("Seconds a knocked-out player waits before coming back, in a 2v2 match.")]
        [SerializeField, Min(0f)] private float respawnSeconds2v2 = 6f;

        [Tooltip("Seconds a knocked-out player waits before coming back, in a 3v3v3 match.")]
        [SerializeField, Min(0f)] private float respawnSeconds3v3v3 = 6f;

        [Tooltip("Health per second a player regains at their own spawn once they have been out of combat long enough.")]
        [SerializeField, Min(0f)] private float spawnHealOutOfCombatPerSecond = 10f;

        [Tooltip("Health per second a player regains at their own spawn while still in combat.")]
        [SerializeField, Min(0f)] private float spawnHealInCombatPerSecond = 4f;

        [Tooltip("Seconds out of combat before the faster out-of-combat rate starts.")]
        [SerializeField, Min(0f)] private float spawnHealOutOfCombatDelaySeconds = 6f;

        [Header("Respawn shield")]
        [Tooltip("How many seconds a freshly respawned player is shielded. Hitting an enemy with anything (damage, a stun, a slow or a push) ends it at once; effects they set up before dying (a mine, a fire field, a burn) do not.")]
        [SerializeField, Min(0f)] private float shieldSeconds = 5f;

        [Tooltip("How big the shield's bubble is, as a multiple of the Invulnerability ability's shield bubble.")]
        [SerializeField, Min(0.1f)] private float shieldBubbleScale = 1.5f;

        [Tooltip("How many seconds the word BLOCKED stays up when the shield stops a hit.")]
        [SerializeField, Min(0f)] private float blockedPopupSeconds = 0.8f;

        [Header("Sudden death")]
        [Tooltip("How many seconds the sudden-death circle takes to shrink from its starting size to its final size.")]
        [SerializeField, Min(1f)] private float suddenDeathShrinkSeconds = 60f;

        [Tooltip("The circle's size when sudden death starts in a 2v2 match, in metres (its radius). It should cover the playable map.")]
        [SerializeField, Min(1f)] private float suddenDeathStartRadius2v2 = 30f;

        [Tooltip("The circle's size when sudden death starts in a 3v3v3 match, in metres (its radius). It should cover the playable map.")]
        [SerializeField, Min(1f)] private float suddenDeathStartRadius3v3v3 = 60f;

        [Tooltip("How small the circle gets in a 2v2 match, in metres (its radius). Must be smaller than the starting size.")]
        [SerializeField, Min(0.5f)] private float finalRadius2v2 = 4f;

        [Tooltip("How small the circle gets in a 3v3v3 match, in metres (its radius). Must be smaller than the starting size.")]
        [SerializeField, Min(0.5f)] private float finalRadius3v3v3 = 8f;

        [Tooltip("Health per second a player loses while standing outside the circle.")]
        [SerializeField, Min(0f)] private float damagePerSecondOutside = 10f;

        [Tooltip("In sudden death, the team whose last player falls last wins, even if it is only a moment after the others. Only when the last players of the teams still in it fall at the very same moment does nobody win and sudden death start over. This is how many seconds apart two falls may be and still count as the very same moment (about one network frame). Keep it small: the longer it is, the more often a player who really lasted a little longer than the others is denied the win.")]
        [SerializeField, Min(0f)] private float sameInstantToleranceSeconds = 0.05f;

        [Header("Build per round")]
        [Tooltip("How far up the weapon tree the shop lets you buy, for rounds 1, 2 and 3. 0 = the Baseline pistol only, 1 = a weapon family, 2 = a family's upgrade. A round past the list uses the last entry.")]
        [SerializeField] private int[] weaponDepthByRound = { 0, 1, 2 };

        [Tooltip("How many armour upgrades the shop lets you buy, for rounds 1, 2 and 3. A round past the list uses the last entry.")]
        [SerializeField] private int[] armorUpgradesByRound = { 0, 1, 2 };

        [Tooltip("What a locked shop tier says, with {0} replaced by the first round that opens it (so Round 2).")]
        [SerializeField] private string lockedTierLabelFormat = "Round {0}";

        public int RoundsToWin => roundsToWin;
        public int MaxRounds => maxRounds;
        public float RoundSeconds => roundSeconds;
        public float BreakSeconds => breakSeconds;
        public float BreakCountdownSeconds => breakCountdownSeconds;
        public float OvertimeSeconds => overtimeSeconds;
        public int OvertimeLeadPoints => overtimeLeadPoints;

        /// <summary>The wait before sudden death starts and the earliest a fall can count: the same countdown the break ends with. One place for the mapping, so the
        /// director's flow numbers and the arrival stamp always agree.</summary>
        public float SuddenDeathCountdownSeconds => breakCountdownSeconds;

        /// <summary>Indexed like TerritoryConfig tiers: 0 = Tier 1 (capital), 1 = Tier 2, 2 = Tier 3, 3 = Tier 4 (centre).</summary>
        public int[] PointsPerZonePerSecond => pointsPerZonePerSecond;

        public int CentrePayoutPoints => centrePayoutPoints;
        public float CentrePayoutIntervalSeconds => centrePayoutIntervalSeconds;
        public float CentreFirstPayoutSeconds => centreFirstPayoutSeconds;
        public float BountyHoldSeconds => bountyHoldSeconds;
        public int BountyPoints => bountyPoints;
        public float RespawnSeconds2v2 => respawnSeconds2v2;
        public float RespawnSeconds3v3v3 => respawnSeconds3v3v3;
        public float SpawnHealOutOfCombatPerSecond => spawnHealOutOfCombatPerSecond;
        public float SpawnHealInCombatPerSecond => spawnHealInCombatPerSecond;
        public float SpawnHealOutOfCombatDelaySeconds => spawnHealOutOfCombatDelaySeconds;
        public float ShieldSeconds => shieldSeconds;
        public float ShieldBubbleScale => shieldBubbleScale;
        public float BlockedPopupSeconds => blockedPopupSeconds;
        public float SuddenDeathShrinkSeconds => suddenDeathShrinkSeconds;
        public float SuddenDeathStartRadius2v2 => suddenDeathStartRadius2v2;
        public float SuddenDeathStartRadius3v3v3 => suddenDeathStartRadius3v3v3;
        public float FinalRadius2v2 => finalRadius2v2;
        public float FinalRadius3v3v3 => finalRadius3v3v3;
        public float DamagePerSecondOutside => damagePerSecondOutside;
        public float SameInstantToleranceSeconds => sameInstantToleranceSeconds;
        public int[] WeaponDepthByRound => weaponDepthByRound;
        public int[] ArmorUpgradesByRound => armorUpgradesByRound;
        public string LockedTierLabelFormat => lockedTierLabelFormat;

        /// <summary>How far up the weapon tree the shop opens in this round (1-based; a round past the list uses the last entry).</summary>
        public int MaxWeaponDepthForRound(int round) => DominionShopRules.MaxWeaponDepth(round, weaponDepthByRound);

        /// <summary>How many armour upgrades the shop opens in this round (1-based; a round past the list uses the last entry).</summary>
        public int ArmorUpgradesForRound(int round) => DominionShopRules.ArmorUpgradesAllowed(round, armorUpgradesByRound);
    }
}
