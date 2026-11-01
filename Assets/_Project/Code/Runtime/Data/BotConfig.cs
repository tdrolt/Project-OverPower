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
            "Mira", "Jonas", "Sanne", "Bram", "Noor", "Lucas", "Fenna", "Daan", "Tess", "Milan", "Lotte", "Ruben", "Isa", "Sem",
            "Jade", "Thijs", "Evi", "Luuk", "Roos", "Finn", "Yara", "Stijn", "Lina", "Joris", "Nina", "Koen", "Maud", "Olaf",
            "Vera", "Timo", "pixelfox", "NightOwl", "Kestrel", "mossy", "Tidebreaker", "Juno", "Quinn", "Rikkert", "Ilse", "Wouter",
        };
        [Tooltip("The three builds, in this order: Shotgun, Burst, Laser. A bot draws one at random when it is added.")]
        [SerializeField] private Build[] builds = new Build[3];

        [Header("Seeing and reacting")]
        [Tooltip("How long a bot waits after an enemy appears before it shoots, in seconds. 0.5 = an easy, human-like delay.")]
        [SerializeField, Min(0f)] private float reactionSeconds = 0.5f;
        [Tooltip("How long a bot remembers where it last saw an enemy, in seconds; it goes to look there in that time.")]
        [SerializeField, Min(0f)] private float memorySeconds = 3f;
        [Tooltip("A bot keeps its target unless another enemy is this many metres closer.")]
        [SerializeField, Min(0f)] private float targetSwitchMetres = 3f;
        [Tooltip("How often a bot re-decides what to do, in seconds. Smaller = sharper, larger = slower to change its mind.")]
        [SerializeField, Range(0.05f, 1f)] private float thinkSeconds = 0.1f;

        [Header("Aim")]
        [Tooltip("How far its shots miss at first, in metres to the side of the target.")]
        [SerializeField, Min(0f)] private float aimErrorStartMetres = 1.5f;
        [Tooltip("How far its shots still miss once settled, in metres.")]
        [SerializeField, Min(0f)] private float aimErrorMinMetres = 0.5f;
        [Tooltip("How many seconds of shooting the same target it takes to settle from the first error to the settled one.")]
        [SerializeField, Min(0.01f)] private float aimSettleSeconds = 2f;
        [Tooltip("How often the miss changes direction, in seconds (a new random offset each time).")]
        [SerializeField, Min(0.05f)] private float aimWobbleSeconds = 0.3f;
        [Tooltip("How fast it can turn its aim, in degrees per second. No instant flicks.")]
        [SerializeField, Min(10f)] private float turnDegreesPerSecond = 360f;
        [Tooltip("How much it leads a moving target with a slow shot: 0 = aims at where the target is, 1 = exactly where it will be.")]
        [SerializeField, Range(0f, 1f)] private float leadFraction = 0.5f;

        [Header("Moving in a fight")]
        [Tooltip("It strafes left and right while it fights and switches side after a random time between these two, in seconds.")]
        [SerializeField, Min(0.1f)] private float strafeSwitchMinSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float strafeSwitchMaxSeconds = 2.5f;

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
        [SerializeField, Min(0f)] private float zipMaxMetres = 16f;
        [Tooltip("...and only above this much health.")]
        [SerializeField, Range(0f, 1f)] private float zipMinHealth = 0.5f;
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
        [Tooltip("Blink away when below this much health...")]
        [SerializeField, Range(0f, 1f)] private float blinkHealth = 0.35f;
        [Tooltip("...with an enemy within this many metres.")]
        [SerializeField, Min(0f)] private float blinkEnemyMetres = 10f;
        [Tooltip("Raybeam when its target is within this many metres...")]
        [SerializeField, Min(0f)] private float raybeamMetres = 20f;
        [Tooltip("...and has been in sight this many seconds.")]
        [SerializeField, Min(0f)] private float raybeamSeenSeconds = 1f;

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
        [Tooltip("After the shrink it goes to the centre when the scan is this many seconds away.")]
        [SerializeField, Min(0f)] private float centreBeforeScanSeconds = 15f;

        [Header("Dominion")]
        [Tooltip("3v3v3: it goes to the centre in the last this-many seconds before its payout.")]
        [SerializeField, Min(0f)] private float centreBeforePayoutSeconds = 10f;
        [Tooltip("Sudden death: how far inside the circle's edge it stays, in metres (the circle keeps shrinking).")]
        [SerializeField, Min(0f)] private float circleMarginMetres = 2f;

        [Header("Navigation")]
        [Tooltip("How wide a bot thinks it is when it plans a route, in metres (the body is 0.7 wide each side; a little more keeps it off walls).")]
        [SerializeField, Min(0.1f)] private float agentRadius = 0.75f;
        [Tooltip("How tall a bot thinks it is when it plans a route, in metres.")]
        [SerializeField, Min(0.5f)] private float agentHeight = 2.6f;
        [Tooltip("The highest step a route may climb, in metres.")]
        [SerializeField, Min(0f)] private float agentClimb = 0.4f;
        [Tooltip("How often it plans its route again while walking, in seconds.")]
        [SerializeField, Min(0.1f)] private float repathSeconds = 0.5f;
        [Tooltip("If it moved less than half a metre in this many seconds while walking, it plans a new way round.")]
        [SerializeField, Min(0.2f)] private float stuckSeconds = 1f;

        public string[] Names => names;
        public Build[] Builds => builds;
        public Build BuildAt(int index) => builds != null && index >= 0 && index < builds.Length ? builds[index] : null;

        public float ReactionSeconds => reactionSeconds;
        public float MemorySeconds => memorySeconds;
        public float TargetSwitchMetres => targetSwitchMetres;
        public float ThinkSeconds => thinkSeconds;

        public float AimErrorStartMetres => aimErrorStartMetres;
        public float AimErrorMinMetres => aimErrorMinMetres;
        public float AimSettleSeconds => aimSettleSeconds;
        public float AimWobbleSeconds => aimWobbleSeconds;
        public float TurnDegreesPerSecond => turnDegreesPerSecond;
        public float LeadFraction => leadFraction;

        public float StrafeSwitchMinSeconds => strafeSwitchMinSeconds;
        public float StrafeSwitchMaxSeconds => strafeSwitchMaxSeconds;

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
        public float FenceMetres => fenceMetres;
        public float FenceTwoEnemiesMetres => fenceTwoEnemiesMetres;

        public float CoverHealth => coverHealth;
        public float CoverMinMetres => coverMinMetres;
        public float SprintQuietSeconds => sprintQuietSeconds;
        public float SprintMaxHeat => sprintMaxHeat;
        public float HealZoneHealth => healZoneHealth;
        public float HealZoneTeammateMetres => healZoneTeammateMetres;

        public float ScopeMinMetres => scopeMinMetres;
        public float BlinkHealth => blinkHealth;
        public float BlinkEnemyMetres => blinkEnemyMetres;
        public float RaybeamMetres => raybeamMetres;
        public float RaybeamSeenSeconds => raybeamSeenSeconds;

        public float TierThreeBonusMetres => tierThreeBonusMetres;
        public float BountyBonusMetres => bountyBonusMetres;
        public float HumanTeammateMetres => humanTeammateMetres;
        public int PushCapitalTeammates => pushCapitalTeammates;
        public float PushCapitalMetres => pushCapitalMetres;
        public float CentreBeforeScanSeconds => centreBeforeScanSeconds;

        public float CentreBeforePayoutSeconds => centreBeforePayoutSeconds;
        public float CircleMarginMetres => circleMarginMetres;

        public float AgentRadius => agentRadius;
        public float AgentHeight => agentHeight;
        public float AgentClimb => agentClimb;
        public float RepathSeconds => repathSeconds;
        public float StuckSeconds => stuckSeconds;
    }
}
