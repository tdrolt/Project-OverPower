using System;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Weapons
{
    /// <summary>
    /// Everything one projectile needs to know about its shot: which weapon or ability fired it, who, which way, how hard. Rebuilt from the fire RPC
    /// (weapon) or cast RPC (ability) on every client so every machine simulates the identical projectile; a plain class so anything bolted on later
    /// (explosion, trail, gizmo) reads one description of the shot instead of holding a copy that can drift.
    /// A weapon shot reads speed/radius/range/impact VFX from its WeaponDefinition, so retuning the asset retunes shots already in the air. An ability
    /// shot has no weapon (Weapon is null): its module's Inspector fields are the stat block. ProjectileMotor reads ONLY the properties below, never
    /// context.Weapon; weapon-only extras (ImpactVfx, beam range-charging, cursor detonation) are read off context.Weapon by effect components that
    /// only ride on a weapon's projectile prefab.
    /// WeaponFiring hands this over via ProjectileMotor.Initialize right after a LOCAL Instantiate, which is correct only because a local Instantiate
    /// returns the one object it created. Do NOT copy that to PhotonNetwork.Instantiate: it returns only the CALLER's copy, other clients build their
    /// own from the prefab and never see fields assigned afterwards, so the object silently differs per machine. Networked spawns pass setup through
    /// instantiationData (the deployables do).
    /// </summary>
    public sealed class ProjectileContext
    {
        /// <summary>The stat block this shot came from, or null for an ability shot.</summary>
        public WeaponDefinition Weapon { get; }

        /// <summary>The ability that fired this shot, or -1 for a weapon shot.</summary>
        public int AbilityId { get; } = -1;

        /// <summary>The weapon's own id for a weapon shot, the ability's id otherwise - one place to ask "who gets credit" instead of every caller
        /// null-checking Weapon.</summary>
        public int SourceId => Weapon != null ? Weapon.Id : AbilityId;

        /// <summary>Metres per second.</summary>
        public float ProjectileSpeed { get; }

        /// <summary>Metres.</summary>
        public float ProjectileRadius { get; }

        /// <summary>Metres this shot may travel before it expires. From the weapon (already scaled by RangeMultiplier) or the ability module's own
        /// field (never scaled - no ability reads OverPower's buff).</summary>
        public float MaxRange { get; }

        /// <summary>
        /// What MaxRange was multiplied by at fire time (OverPower's range buff, GDD p.20) - 1 for a shot nothing has boosted, always 1 for an ability
        /// shot. A beam's Hitscan.ChargedRange recomputes its reach from the weapon asset instead of reading MaxRange, so it takes this as a parameter
        /// rather than a second, independently-tuned formula.
        /// </summary>
        public float RangeMultiplier { get; }

        /// <summary>
        /// Set only on the CASTER's own local copy of an ability projectile (ZipGunAbility.ExecuteCast). Every client spawns the same local projectile so
        /// a remote player's shot is visible on your screen, but only the shooter's machine should ACT on the hit (a zip pull moves the shooter's own body).
        /// Null for a weapon shot and for every other client's copy; AbilityHitRelay is the IProjectileBehaviour that calls it.
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

        /// <summary>0..1 for a charge weapon, 0 for everything else.</summary>
        public float ChargeFraction { get; }

        /// <summary>
        /// Where the shooter's cursor was resting on the ground when the trigger went down. It travels with the shot because Camera.main and
        /// Input.mousePosition inside the fire RPC would resolve on the RECEIVER (the bug WeaponFiring exists to avoid). Only a weapon that aims at a
        /// point rather than a direction reads it (the cursor rocket); for everything else it is carried and ignored.
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
        /// A SEPARATE damage multiplier, decided once at fire time and never touched again, unlike DamageMultiplier, which BounceOffWalls and
        /// ScaleDamageWithDistance overwrite mid-flight. OverPower's damage buff lives here: folded into baseDamage it would miss
        /// ExplodeOnImpact.SplashDamageAt (splash has its own damage figure that never passes through baseDamage), and stored in DamageMultiplier a
        /// bounce would overwrite it rather than compose with it. Damage and SplashDamageAt each apply it exactly once.
        /// 1 = unchanged; always 1 for an ability shot.
        /// </summary>
        public float FireTimeDamageMultiplier { get; }

        /// <summary>
        /// Damage this one projectile deals before armor, vulnerability or reduction: the base figure resolved at fire time, scaled by DamageMultiplier
        /// and FireTimeDamageMultiplier. Resolved once rather than read off the weapon at impact, so a shot in flight cannot be retuned mid-air by a
        /// weapon swap. Only an IProjectileBehaviour riding along on this same projectile may move DamageMultiplier.
        /// </summary>
        public float Damage => baseDamage * DamageMultiplier * FireTimeDamageMultiplier;

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
                                 float damage, float rangeMultiplier = 1f,
                                 float fireTimeDamageMultiplier = 1f)
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
            FireTimeDamageMultiplier = fireTimeDamageMultiplier;
        }

        /// <summary>
        /// An ability shot: Weapon stays null; speed/radius/range/damage come from the ability module's own Inspector fields, and AbilityId stands in
        /// for Weapon.Id wherever a weapon shot would use it (see SourceId). ChargeFraction is always 0.
        /// onHit is how the CASTER's own copy reacts to a hit (a zip pull): pass it only when building the context for the caster's client; every other
        /// client passes null, so AbilityHitRelay quietly does nothing there and neither it nor ProjectileMotor ever asks "am I the caster".
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
            RangeMultiplier = 1f; // No ability reads OverPower's buff.
            FireTimeDamageMultiplier = 1f; // Same reason.
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
    /// The extension seam the weapon plan rests on: pierce, explode-on-impact and bounce are components dropped onto their own projectile prefab,
    /// which ProjectileMotor finds with GetComponents and consults at each decision point, so none of them requires an edit to the motor.
    /// Composition rather than subclassing: a weapon that both pierces AND explodes is two components on one prefab, not a fourth class.
    /// </summary>
    public interface IProjectileBehaviour
    {
        /// <summary>Called once, right after the motor is initialised and before its first step.</summary>
        void OnSpawned(ProjectileMotor motor, ProjectileContext context);

        /// <summary>
        /// Called for every hit the sweep stops on, after damage has been applied. motor.Ignore(hit.collider) then KeepFlying to pierce;
        /// motor.Redirect(newDirection, hitNormal, hitCollider) then KeepFlying to bounce (Redirect also lifts the projectile off the surface and arms
        /// the resting-overlap guard, or it rattles on the wall); spawn an explosion and return Despawn to detonate.
        /// When several behaviours are attached, any single KeepFlying wins.
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
