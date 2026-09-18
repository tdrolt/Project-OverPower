using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// One weapon's complete stat block. Thirteen of these assets exist, one per weapon state in
    /// the upgrade tree, and every one of them is driven by this same list of numbers - there is
    /// no per-weapon code anywhere. A shotgun is "simultaneous, with spread". A rocket is "big max
    /// cone, slow recovery", which reads in play as "accurate only from a standstill". A charge
    /// rifle is "canCharge, with steps". If some weapon ever needs behaviour that cannot be
    /// expressed here, that is a sign this stat block is missing a field, not that the weapon
    /// needs a script of its own.
    ///
    /// Fields are [SerializeField] private with read-only properties on purpose. A
    /// ScriptableObject is a single shared instance for the whole process, so writing to one at
    /// runtime quietly edits the asset in the Editor and does nothing at all in a build - it
    /// appears to work for exactly as long as you are testing in the Editor. Read-only access
    /// makes that mistake impossible to make by accident; anything that needs to vary per player,
    /// such as a charged-up damage value, copies what it needs into a per-player runtime struct
    /// on spawn.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The number this weapon is known by across the network. It must be unique within " +
                 "the Weapon Catalogue, and it must never change once a build is out, because " +
                 "clients look weapons up by this number - never by the weapon's position in the " +
                 "catalogue list. A new asset starts at 0, which means you have not set it yet.")]
        [SerializeField] private int id;
        public int Id => id;

        [Tooltip("The weapon's name as players read it in the shop and on the HUD.")]
        [SerializeField] private string displayName = "";
        public string DisplayName => displayName;

        [Tooltip("One line shown when a player hovers this in the loadout screen. Say what it " +
                 "does, not its numbers - numbers are shown next to it automatically.")]
        [SerializeField, TextArea(1, 3)] private string description = "";
        public string Description => description;

        [Tooltip("The weapon this one upgrades from. Leave it empty if this is a starting weapon. " +
                 "These links are the entire upgrade tree - there is no separate tree asset that " +
                 "could drift out of sync with them.")]
        [SerializeField] private WeaponDefinition parent;
        public WeaponDefinition Parent => parent;

        [Tooltip("The image shown for this weapon in the shop and on the HUD.")]
        [SerializeField] private Sprite icon;
        public Sprite Icon => icon;

        [Tooltip("Gold a player spends to upgrade from the parent weapon to this one. Ignored on " +
                 "a starting weapon, since there is nothing to upgrade from.")]
        [SerializeField] private int goldCost;
        public int GoldCost => goldCost;

        [Header("Firing")]
        [Tooltip("Damage one projectile deals, before armor soaks any of it and before any " +
                 "vulnerability debuff increases it. A shot that sends out three projectiles " +
                 "deals this three times if all three connect.")]
        [SerializeField] private float damage = 11f;
        public float Damage => damage;

        [Tooltip("Seconds between shots while the trigger is held down. Lower is faster: 0.32 is " +
                 "roughly three shots per second.")]
        [SerializeField] private float fireInterval = 0.32f;
        public float FireInterval => fireInterval;

        [Tooltip("How many projectiles one trigger pull sends out. Heat and the fire interval are " +
                 "charged once per trigger pull, not once per projectile, so raising this makes " +
                 "the weapon strictly stronger unless you also raise Overheat Per Shot.")]
        [SerializeField] private int projectilesPerShot = 1;
        public int ProjectilesPerShot => projectilesPerShot;

        [Tooltip("Off means the projectiles leave the barrel one after another, spaced by " +
                 "Sequential Delay - a burst. On means they all leave at once, fanned out by " +
                 "Spread Degrees - a shotgun. This switch decides which of those two fields " +
                 "actually does anything.")]
        [SerializeField] private bool simultaneous = false;
        public bool Simultaneous => simultaneous;

        [Tooltip("Seconds between the projectiles inside one burst. Only used when Simultaneous " +
                 "is off; ignored completely when it is on.")]
        [SerializeField] private float sequentialDelay = 0.07f;
        public float SequentialDelay => sequentialDelay;

        [Tooltip("How wide the fan of projectiles is, in degrees, for a shotgun-style shot. Only " +
                 "used when Simultaneous is on; ignored when it is off. This is a fixed pattern " +
                 "and is separate from the aim cone below, which is accuracy and changes as the " +
                 "player fires.")]
        [SerializeField] private float spreadDegrees = 0f;
        public float SpreadDegrees => spreadDegrees;

        [Tooltip("Seconds between pulling the trigger and the shot actually firing. For a BEAM " +
                 "weapon (a Hitscan component on the Projectile Prefab) every player also sees a " +
                 "warning line along the beam's exact path during the wait, so the target can step " +
                 "out of it before it lands. A projectile weapon given a wind-up just fires later - " +
                 "there is no fixed path to draw a warning line along until the shot actually " +
                 "leaves the barrel, so it gets no line. 0 = fires instantly.")]
        [SerializeField] private float windupSeconds = 0f;
        public float WindupSeconds => windupSeconds;

        [Header("Projectile")]
        [Tooltip("The projectile prefab this weapon spawns. The prefab carries the look of the " +
                 "shot and its hit detection; how fast, how fat and how far it flies come from " +
                 "the three fields below. A prefab with a Hitscan component makes this a beam " +
                 "weapon instead: nothing is spawned, the shot lands instantly, and of the three " +
                 "fields below only Max Range still applies.")]
        [SerializeField] private GameObject projectilePrefab;
        public GameObject ProjectilePrefab => projectilePrefab;

        [Tooltip("How fast the projectile travels, in metres per second. At 55 it crosses a " +
                 "30 metre lane in just over half a second, so players barely need to lead a " +
                 "moving target; lower it to make aiming ahead matter.")]
        [SerializeField] private float projectileSpeed = 55f;
        public float ProjectileSpeed => projectileSpeed;

        [Tooltip("How fat the projectile is for hit detection, in metres. Bigger is more " +
                 "forgiving to aim with. 0.1 is a thin bullet that rewards precision.")]
        [SerializeField] private float projectileRadius = 0.1f;
        public float ProjectileRadius => projectileRadius;

        [Tooltip("How far the projectile flies, in metres, before it disappears. Shots simply " +
                 "stop at this distance - damage does not taper off as range increases.")]
        [SerializeField] private float maxRange = 30f;
        public float MaxRange => maxRange;

        [Header("Accuracy")]
        [Tooltip("The tightest the aim cone ever gets, in degrees - the weapon's accuracy when " +
                 "the player has not been firing. 0 would make it pinpoint.")]
        [SerializeField] private float minConeAngle = 1.5f;
        public float MinConeAngle => minConeAngle;

        [Tooltip("The widest the aim cone's BLOOM can ever open, in degrees, no matter how long the " +
                 "trigger is held - the ceiling on how inaccurate sustained fire alone gets. Moving " +
                 "Spread Degrees below adds ON TOP of this while the player is moving (see " +
                 "AimConeState.EffectiveAngle), so the actual spread while moving can exceed this " +
                 "number - this is a ceiling on bloom, not on the weapon's effective spread.")]
        [SerializeField] private float maxConeAngle = 7f;
        public float MaxConeAngle => maxConeAngle;

        [Tooltip("Degrees added to the aim cone by every trigger pull. This is the whole reason " +
                 "holding the trigger costs you accuracy.")]
        [SerializeField] private float bloomPerShot = 0.8f;
        public float BloomPerShot => bloomPerShot;

        [Tooltip("Degrees the aim cone closes back up per second. Keep this BELOW Bloom Per Shot " +
                 "divided by Fire Interval, or sustained fire can never widen the cone at all " +
                 "and accuracy becomes a decorative number. The Console warns you by name if " +
                 "this weapon crosses that line.")]
        [SerializeField] private float recoveryPerSecond = 2.0f;
        public float RecoveryPerSecond => recoveryPerSecond;

        [Tooltip("How much tighter the cone gets the moment the player stops moving. 1.5 means " +
                 "standing still is one and a half times more accurate than strafing, which is " +
                 "what makes planting your feet a real decision rather than a hidden bonus.")]
        [SerializeField] private float standingStillMultiplier = 1.5f;
        public float StandingStillMultiplier => standingStillMultiplier;

        [Tooltip("Degrees added to this weapon's spread the moment the player starts moving, " +
                 "removed the moment they stop. Makes shooting on the move visibly less accurate.")]
        [SerializeField] private float movingSpreadDegrees = 4f;
        public float MovingSpreadDegrees => movingSpreadDegrees;

        [Tooltip("While the player keeps moving, the spread widens by this many degrees per second " +
                 "toward Max Cone Angle. Recovery Per Second still pulls it back, so this only " +
                 "does anything when it is larger than Recovery Per Second.")]
        [SerializeField] private float movingBloomPerSecond = 3f;
        public float MovingBloomPerSecond => movingBloomPerSecond;

        [Header("Overheat")]
        [Tooltip("Heat added per trigger pull - NOT per projectile. A shot that fires five " +
                 "projectiles still adds this amount once. Divide Overheat Max on the Gameplay " +
                 "Config by this to work out how many shots the player gets before being silenced.")]
        [SerializeField] private float overheatPerShot = 7f;
        public float OverheatPerShot => overheatPerShot;

        [Tooltip("Heat given back when a shot connects with a player, so accurate players get to " +
                 "hold the trigger longer. Only BEAM weapons (a Hitscan component on the Projectile " +
                 "Prefab) use this today: once per beam that strikes at least one target, however " +
                 "many it pierces - hitting a wall does not count. Projectile weapons ignore it. " +
                 "0 means no refund at all.")]
        [SerializeField] private float overheatRefundOnHit = 0f;
        public float OverheatRefundOnHit => overheatRefundOnHit;

        [Header("Charge")]
        [Tooltip("Turn this on for weapons that do something extra when the trigger is held down " +
                 "before firing. The five fields below are ignored entirely while this is off.")]
        [SerializeField] private bool canCharge = false;
        public bool CanCharge => canCharge;

        [Tooltip("Seconds of holding the trigger to reach a full charge. The clock only starts once " +
                 "the weapon is off cooldown (see WeaponFiring.ChargeFraction), so this is the price " +
                 "on top of Fire Interval, not inside it.")]
        [SerializeField] private float maxChargeSeconds = 0f;
        public float MaxChargeSeconds => maxChargeSeconds;

        [Tooltip("How many distinct charge levels the hold passes through on its way to full. " +
                 "Steps give the player readable stages to react to instead of a smooth ramp " +
                 "they have to guess the timing of. Keep this equal to Charge Max Projectiles minus " +
                 "Projectiles Per Shot on a weapon that stacks rounds, so every step is exactly one " +
                 "more round. A step is reached by COMPLETING it: at 2 steps, a hold under half fires " +
                 "the base count, half or more fires one extra, and only a full hold fires them all.")]
        [SerializeField] private int chargeSteps = 0;
        public int ChargeSteps => chargeSteps;

        [Tooltip("Damage at a full charge, as a multiple of the Damage field above. 1 means " +
                 "charging adds no damage; 2 means a full charge hits twice as hard.")]
        [SerializeField] private float chargeDamageMultiplier = 1f;
        public float ChargeDamageMultiplier => chargeDamageMultiplier;

        [Tooltip("Range at a full charge, as a multiple of the Max Range field above. 1 means " +
                 "charging does not change how far the shot travels. Only beam weapons (Hitscan) " +
                 "read this today; projectiles always fly exactly Max Range.")]
        [SerializeField] private float chargeRangeMultiplier = 1f;
        public float ChargeRangeMultiplier => chargeRangeMultiplier;

        [Tooltip("How many projectiles a full charge releases, for weapons that stack shots up " +
                 "while the trigger is held. 0 means charging does not add projectiles.")]
        [SerializeField] private int chargeMaxProjectiles = 0;
        public int ChargeMaxProjectiles => chargeMaxProjectiles;

        [Header("Feedback")]
        [Tooltip("Effect spawned at the barrel each time the weapon fires. Leave it empty for no " +
                 "muzzle effect.")]
        [SerializeField] private GameObject muzzleVfx;
        public GameObject MuzzleVfx => muzzleVfx;

        [Tooltip("Sound played each time the weapon fires. Leave it empty for a silent weapon.")]
        [SerializeField] private AudioClip fireSfx;
        public AudioClip FireSfx => fireSfx;

        [Tooltip("Effect spawned where a projectile lands. Leave it empty for no impact effect.")]
        [SerializeField] private GameObject impactVfx;
        public GameObject ImpactVfx => impactVfx;

#if UNITY_EDITOR
        /// <summary>
        /// Catches the one accuracy misconfiguration that is invisible in play. If the cone closes
        /// faster than sustained fire can open it, the cone never leaves Min Cone Angle, so every
        /// other accuracy number on the weapon stops doing anything - and the weapon simply feels
        /// fine, which is why nobody notices.
        ///
        /// Five of the first seven weapons in the design were wrong in exactly this way, and it
        /// only came to light when the aim cone got unit tested. A warning in the Console is how
        /// the designer catches the sixth one without writing a test for it.
        /// </summary>
        private void OnValidate()
        {
            if (fireInterval > 0f && bloomPerShot > 0f)
            {
                float bloomPerSecond = bloomPerShot / fireInterval;
                if (recoveryPerSecond >= bloomPerSecond)
                {
                    Debug.LogWarning(
                        $"Weapon '{name}': accuracy currently does nothing. Holding the trigger adds " +
                        $"{bloomPerSecond:0.##} degrees of spread per second ({bloomPerShot} Bloom Per " +
                        $"Shot every {fireInterval}s), but Recovery Per Second removes {recoveryPerSecond} " +
                        $"- so the cone never leaves Min Cone Angle and sustained fire is as accurate as " +
                        $"a single shot. Fix it by lowering Recovery Per Second below {bloomPerSecond:0.##}, " +
                        $"or by raising Bloom Per Shot above {recoveryPerSecond * fireInterval:0.##}.",
                        this);
                }
            }

            // Same shape of trap as above, but for the moving bloom added in the playtest polish
            // pass: if it doesn't outpace recovery, moving never widens the cone past the flat
            // moving spread, and Moving Bloom Per Second is a decorative number.
            if (movingBloomPerSecond > 0f && movingBloomPerSecond <= recoveryPerSecond)
            {
                Debug.LogWarning(
                    $"{name}: Moving Bloom Per Second ({movingBloomPerSecond}) is not larger than Recovery Per Second " +
                    $"({recoveryPerSecond}), so moving never widens the cone - the bloom is decorative. Raise Moving " +
                    $"Bloom Per Second above {recoveryPerSecond}, or set it to 0 on purpose.", this);
            }

            // Task 11b (playtest polish, designer request [T]): a wind-up that lasts as long as - or
            // longer than - the weapon's own cooldown lets a second trigger pull land its RPC, and
            // therefore start showing ITS OWN warning line, before the first beam has fired. Two
            // overlapping warning lines from the same shooter read as a glitch, not a weapon.
            if (windupSeconds > 0f && windupSeconds >= fireInterval)
            {
                Debug.LogWarning(
                    $"{name}: Windup Seconds ({windupSeconds}) is not less than Fire Interval " +
                    $"({fireInterval}), so a second trigger pull can start a new warning line before " +
                    "this one's beam has fired - the warnings would overlap. Lower Windup Seconds " +
                    $"below {fireInterval}, or raise Fire Interval above {windupSeconds}.", this);
            }
        }
#endif
    }
}
