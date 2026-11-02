using System;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every number that shapes how a bot plays, in one asset (Design/Settings/BotConfig). One level ("easy"): tune it here
    /// after watching people play against them. Read-only properties, defaults in the initialisers (the rule for every Data asset).
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Bot Config")]
    public sealed class BotConfig : ScriptableObject
    {
        /// <summary>One of the three builds a bot plays toward. Index 0 Shotgun, 1 Burst, 2 Laser (the build a bot draws when added).</summary>
        [Serializable]
        public sealed class Build
        {
            [Tooltip("Only for the match logs and this asset: never shown to players (a bot's name never says its build).")]
            [SerializeField] private string name = "";
            [Tooltip("The weapon family the bot buys first (after the pistol).")]
            [SerializeField] private WeaponDefinition familyWeapon;
            [Tooltip("The upgrade of that family it buys next.")]
            [SerializeField] private WeaponDefinition upgradeWeapon;
            [Tooltip("Its attachment (right mouse button).")]
            [SerializeField] private AbilityDefinition attachment;
            [Tooltip("Its movement ability (Shift).")]
            [SerializeField] private AbilityDefinition movement;
            [Tooltip("Its ultimate (Space).")]
            [SerializeField] private AbilityDefinition ultimate;
            [Tooltip("How many Absorb armour upgrades it buys (Absorb + Recharge together are at most what the shop allows).")]
            [SerializeField, Range(0, 2)] private int absorbUpgrades;
            [Tooltip("How many Recharge armour upgrades it buys.")]
            [SerializeField, Range(0, 2)] private int rechargeUpgrades;
            [Tooltip("The closest it likes to fight, in metres: nearer than this it backs away (and gets the backwards slow, like a player).")]
            [SerializeField] private float fightRangeMin = 8f;
            [Tooltip("The furthest it likes to fight, in metres: further than this it walks in.")]
            [SerializeField] private float fightRangeMax = 14f;

            public string Name => name;
            public WeaponDefinition FamilyWeapon => familyWeapon;
            public WeaponDefinition UpgradeWeapon => upgradeWeapon;
            public AbilityDefinition Attachment => attachment;
            public AbilityDefinition Movement => movement;
            public AbilityDefinition Ultimate => ultimate;
            public int AbsorbUpgrades => absorbUpgrades;
            public int RechargeUpgrades => rechargeUpgrades;
            public float FightRangeMin => fightRangeMin;
            public float FightRangeMax => fightRangeMax;
        }

        public const int Shotgun = 0, Burst = 1, Laser = 2;

        [Header("Names and builds")]
        [Tooltip("The names bots get, picked at random. Never one already in the lobby. Keep them like real players' names: never a build's name.")]
        [SerializeField] private string[] names =
        {
            "Albert", "Allen", "Bert", "Bob", "Cecil", "Clarence", "Elliot", "Elmer", "Ernie", "Eugene", "Fergus", "Ferris", "Frank", "Fred",
            "George", "Graham", "Harvey", "Irwin", "Larry", "Lester", "Marvin", "Neil", "Niles", "Oliver", "Ryan", "Ted", "Toby", "Ulric",
            "Ulysses", "Uri", "Vinny", "Wade", "Waldo", "Wally", "Walt", "Wesley", "Xavier", "Yogi", "Yuri", "Zach",
        };
        [Tooltip("The three builds, in this order: Shotgun, Burst, Laser. A bot draws one at random when it is added.")]
        [SerializeField] private Build[] builds = new Build[3];

        [Header("Seeing and reacting")]
        [Tooltip("How long a bot waits after an enemy appears before it shoots, in seconds. 1 = an easy delay for people who never played.")]
        [SerializeField, Min(0f)] private float reactionSeconds = 1f;
        [Tooltip("How long a bot remembers where it last saw an enemy, in seconds; it goes to look there in that time (only near the zone it is going for).")]
        [SerializeField, Min(0f)] private float memorySeconds = 3f;
        [Tooltip("It keeps its target this many seconds after the target slips out of sight, toward where it was last seen, before it lets go. Stops it turning round each time someone steps behind a wall.")]
        [SerializeField, Min(0f)] private float targetLostSeconds = 1f;
        [Tooltip("A bot keeps its target unless another enemy is this many metres closer.")]
        [SerializeField, Min(0f)] private float targetSwitchMetres = 3f;
        [Tooltip("How often a bot re-decides what to do, in seconds. Smaller = sharper, larger = slower to change its mind.")]
        [SerializeField, Range(0.05f, 1f)] private float thinkSeconds = 0.1f;

        [Header("Aim")]
        [Tooltip("How far its shots miss at first, in metres to the side of the target.")]
        [SerializeField, Min(0f)] private float aimErrorStartMetres = 2.2f;
        [Tooltip("How far its shots still miss once settled, in metres.")]
        [SerializeField, Min(0f)] private float aimErrorMinMetres = 0.9f;
        [Tooltip("How many seconds of shooting the same target it takes to settle from the first error to the settled one.")]
        [SerializeField, Min(0.01f)] private float aimSettleSeconds = 2f;
        [Tooltip("How often the miss changes direction, in seconds (a new random offset each time).")]
        [SerializeField, Min(0.05f)] private float aimWobbleSeconds = 0.3f;
        [Tooltip("How fast it can turn its aim, in degrees per second. No instant flicks.")]
        [SerializeField, Min(10f)] private float turnDegreesPerSecond = 360f;
        [Tooltip("How much it leads a moving target with a slow shot: 0 = aims at where the target is, 1 = exactly where it will be.")]
        [SerializeField, Range(0f, 1f)] private float leadFraction = 0.5f;

        [Header("Objective first")]
        [Tooltip("On its way to a zone it turns to fight an enemy only within the furthest it likes to fight (its build's range) plus this many metres. Further away it keeps walking and only shoots enemies that come into its weapon's range.")]
        [SerializeField, Min(0f)] private float engageExtraMetres = 3f;
        [Tooltip("An enemy within this many metres of the zone it is going for (or holding) is always fought: it defends its zone. Also how near enemies must be to a zone to count as defending it.")]
        [SerializeField, Min(0f)] private float objectiveGuardMetres = 12f;
        [Tooltip("An enemy that hit it in the last this-many seconds is always fought back, wherever it was going.")]
        [SerializeField, Min(0f)] private float hitMemorySeconds = 3f;
        [Tooltip("Once it picks where to go (a zone, a fight, a pack, a teammate) it keeps that at least this many seconds, unless a fight starts or ends, it backs off or regroups, or a zone of its team is being taken. Stops it turning back and forth.")]
        [SerializeField, Min(0f)] private float goalHoldSeconds = 1.5f;

        [Header("Moving in a fight")]
        [Tooltip("It strafes left and right while it fights and switches side after a random time between these two, in seconds.")]
        [SerializeField, Min(0.1f)] private float strafeSwitchMinSeconds = 1.5f;
        [Tooltip("The longest it strafes one way before switching, in seconds (see the line above).")]
        [SerializeField, Min(0.1f)] private float strafeSwitchMaxSeconds = 2.5f;
        [Tooltip("Each time it would switch strafing side, the chance it stands still instead until the next switch (0.3 = 30%).")]
        [SerializeField, Range(0f, 1f)] private float fightStandStillChance = 0.3f;
        [Tooltip("Once it starts backing off (or walking in) to keep its range, it keeps going until it is this many metres inside its range. Stops it stepping back and forth at the edge.")]
        [SerializeField, Min(0f)] private float rangeDeadBandMetres = 1f;

        [Header("Standing still now and then")]
        [Tooltip("With no enemy to fight (walking to a zone or holding one) it thinks about standing still once every random time between this...")]
        [SerializeField, Min(0.5f)] private float idlePauseEveryMinSeconds = 6f;
        [Tooltip("...and this many seconds.")]
        [SerializeField, Min(0.5f)] private float idlePauseEveryMaxSeconds = 12f;
        [Tooltip("Each time, the chance it does stand still (0.25 = 25%). Never while its capital or a zone of its team is being taken.")]
        [SerializeField, Range(0f, 1f)] private float idlePauseChance = 0.25f;
        [Tooltip("How long it stands still: a random time between this...")]
        [SerializeField, Min(0f)] private float pauseMinSeconds = 1f;
        [Tooltip("...and this many seconds.")]
        [SerializeField, Min(0f)] private float pauseMaxSeconds = 2f;

        [Header("Team play")]
        [Tooltip("It counts the enemies it just saw and its own team (itself included) within this many metres of it...")]
        [SerializeField, Min(0f)] private float outnumberedRadiusMetres = 15f;
        [Tooltip("...and when the enemies are at least its team plus this many it falls back to the nearest teammate (or home) to regroup, shooting only what comes into range, until it is no longer outnumbered.")]
        [SerializeField, Min(1)] private int outnumberedMargin = 1;
        [Tooltip("When it picks a zone, each enemy known near that zone makes it count this many metres further, so it pushes zones nobody defends.")]
        [SerializeField, Min(0f)] private float enemyNearZonePenaltyMetres = 10f;
        [Tooltip("How long an enemy it saw (or the centre scan showed its team) still counts as near that spot when it weighs the zones, in seconds.")]
        [SerializeField, Min(0f)] private float zoneIntelSeconds = 8f;
        [Tooltip("With no enemy of its own it joins the fight of a teammate within this many metres of it...")]
        [SerializeField, Min(0f)] private float helpTeammateMetres = 12f;
        [Tooltip("...where the teammate's fight is an enemy within this many metres of that teammate.")]
        [SerializeField, Min(0f)] private float teammateFightMetres = 15f;

        [Header("Heat")]
        [Tooltip("It stops pulling the trigger at this much heat (0.8 = 80% of the bar)...")]
        [SerializeField, Range(0f, 1f)] private float stopFiringHeat = 0.8f;
        [Tooltip("...unless its target is below this much health (0.25 = 25%): then it keeps shooting.")]
        [SerializeField, Range(0f, 1f)] private float finishTargetHealth = 0.25f;
        [Tooltip("When it overheats anyway, the chance it presses Vent inside the window (0.5 = half the time).")]
        [SerializeField, Range(0f, 1f)] private float ventSuccessChance = 0.5f;

        [Header("Health and packs")]
        [Tooltip("Below this much health (0.35 = 35%) it backs off to a health pack or toward its own side.")]
        [SerializeField, Range(0f, 1f)] private float retreatHealth = 0.35f;
        [Tooltip("It goes back to fighting above this much health.")]
        [SerializeField, Range(0f, 1f)] private float returnHealth = 0.7f;
        [Tooltip("While backing off it fights back at once if an enemy comes this close, in metres (cornered).")]
        [SerializeField, Min(0f)] private float corneredMetres = 5f;
        [Tooltip("It goes for a health pack when below this much health and no enemy is in sight...")]
        [SerializeField, Range(0f, 1f)] private float healthPackHealth = 0.5f;
        [Tooltip("...if a pack it knows is up is within this many metres.")]
        [SerializeField, Min(0f)] private float healthPackMetres = 25f;
        [Tooltip("It goes for an ultimate pack when its meter is not full, if one is up within this many metres.")]
        [SerializeField, Min(0f)] private float ultimatePackMetres = 12f;

        [Header("Abilities - Shotgun bot")]
        [Tooltip("Flame when its target is within this many metres in front of it.")]
        [SerializeField, Min(0f)] private float flameMetres = 5f;
        [Tooltip("Zip to close in on a target between these two distances, in metres...")]
        [SerializeField, Min(0f)] private float zipMinMetres = 8f;
        [Tooltip("The furthest target it zips toward, in metres. Also how far behind it a wall may be for an escape Zip.")]
        [SerializeField, Min(0f)] private float zipMaxMetres = 16f;
        [Tooltip("...and only above this much health.")]
        [SerializeField, Range(0f, 1f)] private float zipMinHealth = 0.5f;
        [Tooltip("Zip away to escape when below this much health...")]
        [SerializeField, Range(0f, 1f)] private float zipEscapeHealth = 0.35f;
        [Tooltip("...with an enemy within this many metres, straight away from the nearest enemy, when a wall behind it gives the bolt something to pull it to.")]
        [SerializeField, Min(0f)] private float zipEscapeEnemyMetres = 10f;
        [Tooltip("Fence when an enemy is within this many metres...")]
        [SerializeField, Min(0f)] private float fenceMetres = 5f;
        [Tooltip("...or two enemies are within this many metres.")]
        [SerializeField, Min(0f)] private float fenceTwoEnemiesMetres = 8f;

        [Header("Abilities - Burst bot")]
        [Tooltip("Cover when it is fighting below this much health...")]
        [SerializeField, Range(0f, 1f)] private float coverHealth = 0.6f;
        [Tooltip("...and its target is further than this many metres.")]
        [SerializeField, Min(0f)] private float coverMinMetres = 6f;
        [Tooltip("Sprint while walking somewhere with no enemy seen for this many seconds...")]
        [SerializeField, Min(0f)] private float sprintQuietSeconds = 3f;
        [Tooltip("...and its heat below this (0.5 = half the bar).")]
        [SerializeField, Range(0f, 1f)] private float sprintMaxHeat = 0.5f;
        [Tooltip("Heal Zone when it is in a fight below this much health, or a teammate near it is.")]
        [SerializeField, Range(0f, 1f)] private float healZoneHealth = 0.5f;
        [Tooltip("How near a hurt teammate must be for its Heal Zone, in metres.")]
        [SerializeField, Min(0f)] private float healZoneTeammateMetres = 5f;

        [Header("Abilities - Laser bot")]
        [Tooltip("It holds Scope while its target is further than this many metres.")]
        [SerializeField, Min(0f)] private float scopeMinMetres = 15f;
        [Tooltip("On its way to a zone, with no enemy in sight, it stops once between this...")]
        [SerializeField, Min(0f)] private float scoutMinMetres = 15f;
        [Tooltip("...and this many metres from the zone, faces it and looks ahead through its Scope before it walks in.")]
        [SerializeField, Min(0f)] private float scoutMaxMetres = 30f;
        [Tooltip("How long it stops to look ahead, in seconds.")]
        [SerializeField, Min(0f)] private float scoutSeconds = 1.5f;
        [Tooltip("Blink away when below this much health...")]
        [SerializeField, Range(0f, 1f)] private float blinkHealth = 0.35f;
        [Tooltip("...with an enemy within this many metres.")]
        [SerializeField, Min(0f)] private float blinkEnemyMetres = 10f;
        [Tooltip("Raybeam when its target is within this many metres...")]
        [SerializeField, Min(0f)] private float raybeamMetres = 20f;
        [Tooltip("...and has been in sight this many seconds.")]
        [SerializeField, Min(0f)] private float raybeamSeenSeconds = 1f;
        [Tooltip("When an ability refuses a press (a blocked spot for Cover, nowhere to land a Blink), how long the bot waits before it tries that key again, in seconds.")]
        [SerializeField, Min(0f)] private float pressRetrySeconds = 1.5f;

        [Header("Conquest")]
        [Tooltip("A Tier III zone counts as this many metres closer when it picks a zone to capture.")]
        [SerializeField, Min(0f)] private float tierThreeBonusMetres = 10f;
        [Tooltip("A zone with a bounty counts as this many metres closer.")]
        [SerializeField, Min(0f)] private float bountyBonusMetres = 10f;
        [Tooltip("A human teammate within this many metres: the bot picks its zone from the human's position and joins the human's fight.")]
        [SerializeField, Min(0f)] private float humanTeammateMetres = 20f;
        [Tooltip("It pushes an enemy capital once this many of its team (itself included) are near it.")]
        [SerializeField, Min(1)] private int pushCapitalTeammates = 2;
        [Tooltip("How near the enemy capital they must be, in metres.")]
        [SerializeField, Min(0f)] private float pushCapitalMetres = 20f;
        [Tooltip("It turns back to defend a zone of its team that an enemy is taking within this many metres (its capital it always defends).")]
        [SerializeField, Min(0f)] private float defendZoneMetres = 25f;
        [Tooltip("After the shrink it goes to the centre when the scan is this many seconds away.")]
        [SerializeField, Min(0f)] private float centreBeforeScanSeconds = 15f;

        [Header("Dominion")]
        [Tooltip("3v3v3: it goes to the centre in the last this-many seconds before its payout.")]
        [SerializeField, Min(0f)] private float centreBeforePayoutSeconds = 10f;
        [Tooltip("Sudden death: how far inside the circle's edge it stays, in metres (the circle keeps shrinking).")]
        [SerializeField, Min(0f)] private float circleMarginMetres = 2f;

        [Header("Navigation")]
        [Tooltip("How wide a bot thinks it is when it plans a route, in metres (the body is 0.7 wide each side; more keeps it off walls).")]
        [SerializeField, Min(0.1f)] private float agentRadius = 1f;
        [Tooltip("How tall a bot thinks it is when it plans a route, in metres.")]
        [SerializeField, Min(0.5f)] private float agentHeight = 2.6f;
        [Tooltip("The highest step a route may climb, in metres.")]
        [SerializeField, Min(0f)] private float agentClimb = 0.4f;
        [Tooltip("How near it must come to a bend in its route before it heads for the next one, in metres. A little more than its step keeps it from circling a bend.")]
        [SerializeField, Min(0.1f)] private float cornerReachMetres = 0.8f;
        [Tooltip("How often it plans its route again while walking, in seconds.")]
        [SerializeField, Min(0.1f)] private float repathSeconds = 0.5f;
        [Tooltip("If it moved less than half a metre in this many seconds while walking, it steps aside and plans a new way round...")]
        [SerializeField, Min(0.2f)] private float stuckSeconds = 1f;
        [Tooltip("...but never within this many metres of where it is going (there it just stops).")]
        [SerializeField, Min(0f)] private float noSidestepNearGoalMetres = 1.5f;

        public string[] Names => names;
        public Build[] Builds => builds;
        public Build BuildAt(int index) => builds != null && index >= 0 && index < builds.Length ? builds[index] : null;

        public float ReactionSeconds => reactionSeconds;
        public float MemorySeconds => memorySeconds;
        public float TargetLostSeconds => targetLostSeconds;
        public float TargetSwitchMetres => targetSwitchMetres;
        public float ThinkSeconds => thinkSeconds;

        public float AimErrorStartMetres => aimErrorStartMetres;
        public float AimErrorMinMetres => aimErrorMinMetres;
        public float AimSettleSeconds => aimSettleSeconds;
        public float AimWobbleSeconds => aimWobbleSeconds;
        public float TurnDegreesPerSecond => turnDegreesPerSecond;
        public float LeadFraction => leadFraction;

        public float EngageExtraMetres => engageExtraMetres;
        public float ObjectiveGuardMetres => objectiveGuardMetres;
        public float HitMemorySeconds => hitMemorySeconds;
        public float GoalHoldSeconds => goalHoldSeconds;

        public float StrafeSwitchMinSeconds => strafeSwitchMinSeconds;
        public float StrafeSwitchMaxSeconds => strafeSwitchMaxSeconds;
        public float FightStandStillChance => fightStandStillChance;
        public float RangeDeadBandMetres => rangeDeadBandMetres;

        public float IdlePauseEveryMinSeconds => idlePauseEveryMinSeconds;
        public float IdlePauseEveryMaxSeconds => idlePauseEveryMaxSeconds;
        public float IdlePauseChance => idlePauseChance;
        public float PauseMinSeconds => pauseMinSeconds;
        public float PauseMaxSeconds => pauseMaxSeconds;

        public float OutnumberedRadiusMetres => outnumberedRadiusMetres;
        public int OutnumberedMargin => outnumberedMargin;
        public float EnemyNearZonePenaltyMetres => enemyNearZonePenaltyMetres;
        public float ZoneIntelSeconds => zoneIntelSeconds;
        public float HelpTeammateMetres => helpTeammateMetres;
        public float TeammateFightMetres => teammateFightMetres;

        public float StopFiringHeat => stopFiringHeat;
        public float FinishTargetHealth => finishTargetHealth;
        public float VentSuccessChance => ventSuccessChance;

        public float RetreatHealth => retreatHealth;
        public float ReturnHealth => returnHealth;
        public float CorneredMetres => corneredMetres;
        public float HealthPackHealth => healthPackHealth;
        public float HealthPackMetres => healthPackMetres;
        public float UltimatePackMetres => ultimatePackMetres;

        public float FlameMetres => flameMetres;
        public float ZipMinMetres => zipMinMetres;
        public float ZipMaxMetres => zipMaxMetres;
        public float ZipMinHealth => zipMinHealth;
        public float ZipEscapeHealth => zipEscapeHealth;
        public float ZipEscapeEnemyMetres => zipEscapeEnemyMetres;
        public float FenceMetres => fenceMetres;
        public float FenceTwoEnemiesMetres => fenceTwoEnemiesMetres;

        public float CoverHealth => coverHealth;
        public float CoverMinMetres => coverMinMetres;
        public float SprintQuietSeconds => sprintQuietSeconds;
        public float SprintMaxHeat => sprintMaxHeat;
        public float HealZoneHealth => healZoneHealth;
        public float HealZoneTeammateMetres => healZoneTeammateMetres;

        public float ScopeMinMetres => scopeMinMetres;
        public float ScoutMinMetres => scoutMinMetres;
        public float ScoutMaxMetres => scoutMaxMetres;
        public float ScoutSeconds => scoutSeconds;
        public float BlinkHealth => blinkHealth;
        public float BlinkEnemyMetres => blinkEnemyMetres;
        public float RaybeamMetres => raybeamMetres;
        public float RaybeamSeenSeconds => raybeamSeenSeconds;
        public float PressRetrySeconds => pressRetrySeconds;

        public float TierThreeBonusMetres => tierThreeBonusMetres;
        public float BountyBonusMetres => bountyBonusMetres;
        public float HumanTeammateMetres => humanTeammateMetres;
        public int PushCapitalTeammates => pushCapitalTeammates;
        public float PushCapitalMetres => pushCapitalMetres;
        public float DefendZoneMetres => defendZoneMetres;
        public float CentreBeforeScanSeconds => centreBeforeScanSeconds;

        public float CentreBeforePayoutSeconds => centreBeforePayoutSeconds;
        public float CircleMarginMetres => circleMarginMetres;

        public float AgentRadius => agentRadius;
        public float AgentHeight => agentHeight;
        public float AgentClimb => agentClimb;
        public float CornerReachMetres => cornerReachMetres;
        public float RepathSeconds => repathSeconds;
        public float StuckSeconds => stuckSeconds;
        public float NoSidestepNearGoalMetres => noSidestepNearGoalMetres;
    }
}
