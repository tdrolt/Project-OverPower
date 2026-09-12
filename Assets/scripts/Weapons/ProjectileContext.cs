using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Weapons
{
    /// <summary>
    /// Everything one projectile needs to know about the shot that produced it: which weapon fired
    /// it, who fired it, which way it is going and how hard it hits. Rebuilt from the fire RPC on
    /// every client, so all nine machines simulate the identical projectile.
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
    /// pass their setup through instantiationData instead, which is what the deployables in a later
    /// task do.
    /// </summary>
    public sealed class ProjectileContext
    {
        /// <summary>The stat block this shot came from. Everything a projectile needs that is not
        /// per-shot - speed, radius, range, impact VFX - is read from here rather than duplicated,
        /// so retuning the weapon asset retunes shots already in the air on the next trigger pull.</summary>
        public WeaponDefinition Weapon { get; }

        /// <summary>ActorNumber of whoever pulled the trigger. Inside the fire RPC this comes from
        /// PhotonMessageInfo.Sender, never PhotonNetwork.LocalPlayer - see DamageInfo's own note on
        /// why getting this wrong hands kill credit to the victim.</summary>
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

        /// <summary>Damage this one projectile deals before armor, vulnerability or reduction -
        /// resolved once at fire time rather than read off the weapon at impact, so a shot in
        /// flight cannot be retuned mid-air by a weapon swap.</summary>
        public float Damage { get; }

        public ProjectileContext(WeaponDefinition weapon, int shooterActorNumber, int shooterTeamId,
                                 Vector3 direction, float chargeFraction, float damage)
        {
            Weapon = weapon;
            ShooterActorNumber = shooterActorNumber;
            ShooterTeamId = shooterTeamId;
            Direction = direction;
            ChargeFraction = chargeFraction;
            Damage = damage;
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
