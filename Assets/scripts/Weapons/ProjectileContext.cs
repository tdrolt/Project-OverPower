using System;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Weapons
{
    /// <summary>
    /// Everything one projectile needs to know about the shot that produced it: which weapon (or
    /// ability) fired it, who fired it, which way it is going and how hard it hits. Rebuilt from the
    /// fire RPC (a weapon) or the cast RPC (an ability) on every client, so every machine simulates
    /// the identical projectile.
    ///
    /// A plain class rather than fields on ProjectileMotor, so that anything bolted onto a
    /// projectile later - an explosion, a trail, a debug gizmo - reads the same one description of
    /// the shot instead of each holding its own copy that can drift.
    ///
    /// WeaponFiring hands this over by calling ProjectileMotor.Initialize immediately after a local
    /// Instantiate. That is correct HERE and only here, because a local Instantiate returns the one
    /// and only object it created, so writing to it configures the thing that exists.
    ///
    /// Do NOT copy this pattern to PhotonNetwork.Instantiate. That returns only the CALLER's copy;
    /// every other client builds its own from the prefab and never sees fields you assigned
    /// afterwards, so the object silently behaves differently on each machine. Networked spawns
    /// pass their setup through instantiationData instead, which is what the deployables use.
    ///
    /// TWO WAYS A SHOT IS BUILT (Task 1.7b). A weapon shot carries a WeaponDefinition and reads its
    /// speed/radius/range/impact VFX from it, so retuning the weapon asset retunes shots already in
    /// the air on the next trigger pull. An ABILITY shot (the zip gun, later the stun gun) has no
    /// weapon at all - its own module's Inspector fields ARE the stat block - so it hands the motor
    /// speed/radius/range/damage directly instead. ProjectileMotor reads ONLY the properties below
    /// (ProjectileSpeed/ProjectileRadius/MaxRange/Damage/SourceId), never context.Weapon itself, so
    /// it never needs to know which kind of shot it is simulating. Weapon-only extras - ImpactVfx,
    /// beam range-charging, cursor detonation - are read straight off context.Weapon by the effect
    /// components that use them, and those components only ever ride on a WEAPON'S projectile prefab,
    /// so Weapon being null for an ability shot never reaches them.
    /// </summary>
    public sealed class ProjectileContext
    {
        /// <summary>The stat block this shot came from, or null for an ability shot - see the class
        /// comment. Everything a projectile needs that is not per-shot - speed, radius, range,
        /// impact VFX - is read from here rather than duplicated, so retuning the weapon asset
        /// retunes shots already in the air on the next trigger pull.</summary>
        public WeaponDefinition Weapon { get; }

        /// <summary>The ability that fired this shot, or -1 for a weapon shot. Set once by the
        /// ability constructor below; never used when Weapon is not null.</summary>
        public int AbilityId { get; } = -1;

        /// <summary>What DamageInfo.WeaponId should read for this shot: the weapon's own id for a
        /// weapon shot, the ability's id otherwise. One place to ask "who gets credit" instead of
        /// every caller null-checking Weapon itself.</summary>
        public int SourceId => Weapon != null ? Weapon.Id : AbilityId;

        /// <summary>Metres per second. From the weapon for a weapon shot; from the ability module's
        /// own field otherwise - see the class comment.</summary>
        public float ProjectileSpeed { get; }

        /// <summary>Metres. From the weapon for a weapon shot; from the ability module's own field
        /// otherwise.</summary>
        public float ProjectileRadius { get; }

        /// <summary>Metres this shot may travel before it expires. From the weapon for a weapon
        /// shot (already scaled by RangeMultiplier below), or from the ability module's own field
        /// otherwise (never scaled - no ability reads OverPower's buff).</summary>
        public float MaxRange { get; }

        /// <summary>
        /// What MaxRange above was multiplied by at fire time - 1 for a shot nothing has boosted.
        /// Task 2.6 (GDD p.20): OverPower's +10% range needs to reach every place range is measured
        /// for THIS shot, not just the travel-distance cap MaxRange already covers - a beam weapon's
        /// Hitscan.ChargedRange recomputes its reach from the weapon asset directly rather than
        /// reading MaxRange, so it takes this multiplier as its own parameter instead of a second,
        /// independently-tuned formula. Always 1 for an ability shot.
        /// </summary>
        public float RangeMultiplier { get; }

        /// <summary>
        /// Set only when this exact context was built for the CASTER's own local copy of an ability
        /// projectile - see ZipGunAbility.ExecuteCast. Every client spawns the same local projectile
        /// (so a remote player's shot is still visible on your screen), but only the shooter's own
        /// machine should ACT on the hit (a zip pull moves the shooter's own body). Null for a
        /// weapon shot and for every other client's copy of an ability shot; AbilityHitRelay is the
        /// IProjectileBehaviour that calls it.
        /// </summary>
        public Action<ProjectileHitInfo> OnAbilityHit { get; }

        /// <summary>ActorNumber of whoever pulled the trigger (or cast the ability). Inside the fire
        /// RPC this comes from PhotonMessageInfo.Sender, never PhotonNetwork.LocalPlayer - see
        /// DamageInfo's own note on why getting this wrong hands kill credit to the victim.</summary>
        public int ShooterActorNumber { get; }

        /// <summary>The shooter's team, so the sweep can fly straight through teammates instead of
        /// stopping dead on them. -1 means the team was not known, which deliberately behaves as
        /// "not a teammate" - the same fail-open rule Teams.AreSameTeam uses.</summary>
        public int ShooterTeamId { get; }

        /// <summary>Normalised, already including this projectile's share of the weapon's spread
        /// and its roll from the shared aim cone.</summary>
        public Vector3 Direction { get; }

        /// <summary>0..1 for a charge weapon, 0 for everything else. Plumbed end to end so charge
        /// weapons need no new RPC later; nothing scales off it yet.</summary>
        public float ChargeFraction { get; }

        /// <summary>
        /// Where the shooter's cursor was resting on the ground when the trigger went down.
        ///
        /// It travels with the shot because Camera.main and Input.mousePosition inside the fire
        /// RPC would resolve on the RECEIVER - the same class of bug WeaponFiring was written to
        /// fix. Only a weapon that aims at a point rather than a direction reads it (the cursor
        /// rocket); for everything else it is carried and ignored.
        /// </summary>
        public Vector3 TargetPoint { get; }

        /// <summary>
        /// What this shot's damage is currently multiplied by, 1 for a shot nothing has modified.
        ///
        /// A behaviour that changes how hard the shot lands sets this ONE number rather than
        /// rewriting a damage figure, so everything derived from the shot - the direct hit the
        /// motor deals and the splash an explosion deals - scales together automatically. A
        /// distance-scaling rocket whose direct damage grew while its splash did not would be a
        /// nasty surprise to a designer reading "+50% damage at range".
        /// </summary>
        public float DamageMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Damage this one projectile deals before armor, vulnerability or reduction - the figure
        /// resolved at fire time, scaled by whatever DamageMultiplier currently is.
        ///
        /// The base figure is resolved once at fire time rather than read off the weapon at
        /// impact, so a shot in flight cannot be retuned mid-air by a weapon swap. Only an
        /// IProjectileBehaviour riding along on this same projectile may move the multiplier.
        /// </summary>
        public float Damage => baseDamage * DamageMultiplier;

        private readonly float baseDamage;

        /// <summary>
        /// Rescale this shot while it is in the air - the seam ScaleDamageWithDistance uses.
        ///
        /// Set the multiplier rather than adding to it, so a behaviour ticking every frame
        /// recomputes the same answer instead of compounding it into orbit. Negative multipliers
        /// are clamped away: a projectile that heals what it hits is never what anyone meant.
        /// </summary>
        public void SetDamageMultiplier(float multiplier)
        {
            DamageMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>A weapon shot - Weapon is never null afterwards. ProjectileSpeed/Radius/MaxRange
        /// are read from it once here rather than by the motor at every use, so a weapon shot and an
        /// ability shot look identical to ProjectileMotor from this point on.</summary>
        public ProjectileContext(WeaponDefinition weapon, int shooterActorNumber, int shooterTeamId,
                                 Vector3 direction, Vector3 targetPoint, float chargeFraction,
                                 float damage, float rangeMultiplier = 1f)
        {
            Weapon = weapon;
            ProjectileSpeed = weapon.ProjectileSpeed;
            ProjectileRadius = weapon.ProjectileRadius;
            RangeMultiplier = rangeMultiplier;
            MaxRange = weapon.MaxRange * rangeMultiplier;
            ShooterActorNumber = shooterActorNumber;
            ShooterTeamId = shooterTeamId;
            Direction = direction;
            TargetPoint = targetPoint;
            ChargeFraction = chargeFraction;
            baseDamage = damage;
        }

        /// <summary>
        /// An ability shot (Task 1.7b) - the zip gun today, the stun gun later. Weapon stays null;
        /// speed/radius/range/damage come from the ability module's own Inspector fields instead of
        /// a WeaponDefinition, and AbilityId stands in for Weapon.Id wherever a weapon shot would use
        /// it (see SourceId). ChargeFraction is always 0 - no ability charges a projectile today.
        ///
        /// onHit is how the CASTER's own local copy of the projectile reacts to a hit (a zip pull) -
        /// pass it only when building the context for the caster's own client; every other client's
        /// copy of the same shot passes null, so AbilityHitRelay quietly does nothing there. This is
        /// what makes "only the caster's copy acts on a hit" true without ProjectileMotor or
        /// AbilityHitRelay ever asking "am I the caster" themselves.
        /// </summary>
        public ProjectileContext(int abilityId, float projectileSpeed, float projectileRadius,
                                 float maxRange, float damage, int shooterActorNumber,
                                 int shooterTeamId, Vector3 direction, Vector3 targetPoint,
                                 Action<ProjectileHitInfo> onHit = null)
        {
            Weapon = null;
            AbilityId = abilityId;
            ProjectileSpeed = projectileSpeed;
            ProjectileRadius = projectileRadius;
            RangeMultiplier = 1f; // No ability reads OverPower's buff - see the property's own comment.
            MaxRange = maxRange;
            ShooterActorNumber = shooterActorNumber;
            ShooterTeamId = shooterTeamId;
            Direction = direction;
            TargetPoint = targetPoint;
            ChargeFraction = 0f;
            baseDamage = damage;
            OnAbilityHit = onHit;
        }
    }

    /// <summary>What a behaviour wants the projectile to do after a hit has been dealt with.</summary>
    public enum ProjectileHitResponse
    {
        /// <summary>The shot is over. This is what happens with no behaviours attached at all.</summary>
        Despawn,

        /// <summary>Carry on flying. A pierce behaviour returns this after telling the motor to
        /// ignore what it just hit; a bounce behaviour returns it after redirecting the motor.</summary>
        KeepFlying
    }

    /// <summary>
    /// What one ability projectile hit, handed to ProjectileContext.OnAbilityHit - see its own
    /// comment for why that delegate is only ever set on the caster's own copy of the shot.
    /// </summary>
    public readonly struct ProjectileHitInfo
    {
        /// <summary>Where the sweep actually touched - a wall's surface or a player's hitbox.</summary>
        public readonly Vector3 Point;

        /// <summary>The surface normal at Point. Not read by the zip gun (it pulls toward Point, not
        /// away from the normal), kept for a future ability that does.</summary>
        public readonly Vector3 Normal;

        /// <summary>The thing that took the hit, or null when it was level geometry (a wall).</summary>
        public readonly IDamageable Victim;

        public ProjectileHitInfo(Vector3 point, Vector3 normal, IDamageable victim)
        {
            Point = point;
            Normal = normal;
            Victim = victim;
        }
    }

    /// <summary>
    /// The extension seam the whole weapon plan rests on. Pierce, explode-on-impact and bounce are
    /// three more weapons in the upgrade tree, and none of them may require an edit to
    /// ProjectileMotor - they are components dropped onto their own projectile prefab, which the
    /// motor finds with GetComponents and consults at each decision point.
    ///
    /// Composition rather than subclassing on purpose: a weapon that both pierces AND explodes is
    /// two components on one prefab, where a subclass hierarchy would need a fourth class for the
    /// combination.
    ///
    /// Nothing implements this yet, and nothing should until those weapons are actually built.
    /// </summary>
    public interface IProjectileBehaviour
    {
        /// <summary>Called once, right after the motor is initialised and before its first step.</summary>
        void OnSpawned(ProjectileMotor motor, ProjectileContext context);

        /// <summary>
        /// Called for every hit the sweep stops on, after damage has already been applied. Use
        /// motor.Ignore(hit.collider) then return KeepFlying to pierce; motor.Redirect(...) then
        /// KeepFlying to bounce; spawn an explosion and return Despawn to detonate. When several
        /// behaviours are attached, any single KeepFlying wins.
        /// </summary>
        /// <param name="victim">The thing that took the damage, or null when the shot hit level
        /// geometry.</param>
        ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context,
                                    RaycastHit hit, IDamageable victim);

        /// <summary>Called once as the projectile goes away, for any reason - impact, range or the
        /// lifetime backstop. The last chance to spawn something at its final position.</summary>
        void OnExpired(ProjectileMotor motor, ProjectileContext context);
    }
}
