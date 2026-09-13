using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Weapons
{
    /// <summary>
    /// Turns a weapon into an instant beam: no travel time, the shot lands the moment the trigger
    /// is pulled, anywhere along its range. The laser path's defining trait.
    ///
    /// Put it on the prefab in a weapon's Projectile Prefab slot. WeaponFiring checks that prefab
    /// for this component and, if it is there, casts a ray instead of spawning a projectile. That is
    /// the whole switch between "bullet" and "beam" - ProjectileMotor was not touched, and a beam
    /// travels in the same fire RPC a bullet does, carrying the same origin, direction, seed and
    /// charge. No new network message exists for lasers.
    ///
    /// THIS COMPONENT IS NEVER SPAWNED. WeaponFiring calls it straight off the prefab asset, so it
    /// keeps no memory between shots: anything written to one of its fields would be written into
    /// the asset itself. Everything a shot needs arrives as a parameter.
    ///
    /// DAMAGE IS VICTIM-SIDE, EXACTLY LIKE A BULLET. Every client runs the same ray from the same
    /// transmitted origin and direction and calls ApplyDamage on everything it strikes; only the
    /// victim's own client acts on it (PlayerHealth.ApplyDamage returns unless photonView.IsMine).
    /// Do not "optimise" this into the shooter applying damage - see ProjectileMotor's class
    /// comment for why the victim is the authority.
    ///
    /// Which targets a ray actually strikes - nearest first, each once, stopping at walls or the
    /// pierce limit - is decided in BeamResolver, which is unit tested. This file only turns the
    /// physics query into input for it and acts on its answer.
    /// </summary>
    [DisallowMultipleComponent]
    public class Hitscan : MonoBehaviour
    {
        [SerializeField, Tooltip("Which layers the beam can hit. Default is where living players " +
                 "and dummies are; Building is the walls. Bullet and DeadPlayer are always removed " +
                 "whatever you tick here. To let the beam pass through walls, add an Ignore Walls " +
                 "component rather than unticking Building, so the leaf reads as a leaf.")]
        private LayerMask hitMask = ~0;

        [SerializeField, Tooltip("The visible beam. A prefab with a Line Renderer on it - its two " +
                 "points are set to run from the muzzle to wherever the beam ended. Leave empty " +
                 "for an invisible beam.")]
        private GameObject beamVfx;

        [SerializeField, Tooltip("Seconds the beam stays on screen after a shot. Purely visual: " +
                 "the damage has already happened the instant the trigger was pulled. Longer reads " +
                 "better at a glance but clutters a busy fight.")]
        private float beamDuration = 0.12f;

        // Not a tuning value: how many colliders one ray considers. Nine players with a couple of
        // colliders each plus the walls in a 41m line fit comfortably. If this were ever exceeded
        // Physics would drop hits arbitrarily - possibly the wall - so it is generous on purpose.
        private const int MaxContacts = 32;
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxContacts];
        private static readonly List<BeamContact> ContactBuffer = new List<BeamContact>(MaxContacts);

        /// <summary>
        /// Works out what this beam strikes WITHOUT dealing damage or drawing anything. The
        /// shooter's own client calls this the instant it fires, to decide the laser's heat refund
        /// - see WeaponFiring.RefundHeatIfBeamConnects.
        /// </summary>
        public BeamResult Resolve(Vector3 origin, ProjectileContext shot)
        {
            float range = ChargedRange(shot.Weapon, shot.ChargeFraction);
            Vector3 direction = shot.Direction.normalized;

            int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, range, BuildMask(),
                                                QueryTriggerInteraction.Ignore);

            ContactBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.collider == null)
                    continue;

                ContactBuffer.Add(new BeamContact(hit.distance,
                                                  hit.collider.GetComponentInParent<IDamageable>(),
                                                  hit.point));
            }

            Pierce pierce = GetComponent<Pierce>();
            int maxTargets = pierce != null ? pierce.MaxTargets : 1;

            return BeamResolver.Resolve(ContactBuffer, range, maxTargets,
                                        shot.ShooterActorNumber, shot.ShooterTeamId);
        }

        /// <summary>The whole shot, on one client: resolve the ray, damage what it struck, and draw
        /// it. Called from the fire RPC on EVERY client, the shooter's included.</summary>
        public void Fire(Vector3 origin, ProjectileContext shot)
        {
            BeamResult beam = Resolve(origin, shot);

            for (int i = 0; i < beam.Struck.Count; i++)
            {
                BeamContact contact = beam.Struck[i];
                contact.Target.ApplyDamage(new DamageInfo(shot.Damage, shot.ShooterActorNumber,
                                                          shot.ShooterTeamId, shot.Weapon.Id,
                                                          DamageSource.Projectile, false, contact.Point));
                PlayImpact(shot.Weapon, contact.Point);
            }

            Vector3 end = origin + shot.Direction.normalized * beam.Length;
            if (beam.StoppedOnGeometry)
                PlayImpact(shot.Weapon, end);

            DrawBeam(origin, end);
        }

        /// <summary>
        /// How far a beam reaches. A charging laser reaches further the longer the trigger was
        /// held, up to Max Range x Charge Range Multiplier at full charge - ramping smoothly, the
        /// same way WeaponFiring ramps charged damage, so both halves of the charge payoff grow
        /// together. A weapon that cannot charge always reaches exactly Max Range.
        ///
        /// The charge fraction is the one that crossed the wire, so every client draws the same
        /// length and damages the same targets.
        /// </summary>
        public static float ChargedRange(WeaponDefinition weapon, float chargeFraction)
        {
            if (!weapon.CanCharge)
                return weapon.MaxRange;

            return weapon.MaxRange * Mathf.Lerp(1f, weapon.ChargeRangeMultiplier, Mathf.Clamp01(chargeFraction));
        }

        /// <summary>The designer's layers, minus the two invariants HitMasks enforces for every
        /// shot - see HitMasks.StripNonNegotiableLayers, shared with ProjectileMotor - minus walls
        /// if this is the through-walls leaf.</summary>
        private int BuildMask()
        {
            int mask = HitMasks.StripNonNegotiableLayers(hitMask);

            IgnoreWalls ignoreWalls = GetComponent<IgnoreWalls>();
            return ignoreWalls != null ? ignoreWalls.RemoveWallsFrom(mask) : mask;
        }

        /// <summary>A local, throwaway effect on each client - never a networked object, since
        /// every client draws its own copy from the same RPC.</summary>
        private void DrawBeam(Vector3 from, Vector3 to)
        {
            if (beamVfx == null)
                return;

            GameObject beam = Instantiate(beamVfx, from, Quaternion.identity);
            LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
            if (line != null)
            {
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }

            Destroy(beam, beamDuration);
        }

        private static void PlayImpact(WeaponDefinition weapon, Vector3 at)
        {
            if (weapon.ImpactVfx != null && VFXManager.Instance != null)
                VFXManager.Instance.PlayVFX(weapon.ImpactVfx, at);
        }
    }
}
