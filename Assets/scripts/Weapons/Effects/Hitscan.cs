using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.UI;

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

        [SerializeField, Tooltip("Where the beam's team colour, width, glow and linger time come " +
                 "from - Assets/Gameplay/Config/UiTheme.asset, shared with the HUD, the aim cone " +
                 "and every shot's trail (Task 11a/11b). Presentation only; nothing here is a " +
                 "gameplay value. Serialized here rather than passed in from WeaponFiring because " +
                 "this component ALREADY holds its other presentation fields (Beam Vfx above) as " +
                 "plain serialized fields on this same never-spawned prefab asset - see the class " +
                 "comment - so this is one more of the same, not a new pattern.")]
        private UiTheme theme;

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

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                // IStructure (CoverWall) is the fact BeamResolver needs to stop a piercing beam at
                // it like a wall rather than carrying on through it as just another target - see
                // BeamResolver's own class comment (Task 1.8b review fix). Resolved here, once, since
                // this is the one place that actually has the IDamageable to check.
                ContactBuffer.Add(new BeamContact(hit.distance, target, hit.point, target is IStructure));
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

            DrawBeam(origin, end, shot.ShooterTeamId);
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

        /// <summary>
        /// Where a beam built from `shot` would visibly stop if it fired RIGHT NOW - the wall or
        /// structure within its (charged) range, or the full range if nothing stops it first.
        ///
        /// Task 11b: WeaponFiring's wind-up warning line calls this once, the instant the trigger is
        /// pulled, to draw its telegraph along the exact path the real beam will travel. Walls and
        /// structures do not move, so resolving this at the START of the wind-up predicts exactly
        /// where the beam lands once it actually fires at the END of it. Delegates to Resolve rather
        /// than re-deriving the mask/range/pierce rules a second time, so the warning can never
        /// disagree with what the beam actually does - including that a laser with unlimited pierce
        /// (every laser today) never stops early just because a player is standing in the line; only
        /// a wall or a structure shortens it.
        /// </summary>
        public float PredictBeamLength(Vector3 origin, ProjectileContext shot) => Resolve(origin, shot).Length;

        /// <summary>The designer's layers, minus the two invariants HitMasks enforces for every
        /// shot - see HitMasks.StripNonNegotiableLayers, shared with ProjectileMotor - minus walls
        /// if this is the through-walls leaf.</summary>
        private int BuildMask()
        {
            int mask = HitMasks.StripNonNegotiableLayers(hitMask);

            IgnoreWalls ignoreWalls = GetComponent<IgnoreWalls>();
            return ignoreWalls != null ? ignoreWalls.RemoveWallsFrom(mask) : mask;
        }

        // Fallbacks for a Hitscan whose Theme slot was left empty (ResolveBeamColor below logs once
        // and the beam still fires and still draws, just with these numbers instead) - roughly the
        // old hardcoded look this file drew before Task 11b gave every laser prefab a shared theme.
        private const float FallbackBeamWidth = 0.08f;
        private const float FallbackLingerSeconds = 0.12f;
        private static readonly Color FallbackBeamColor = new Color(0.35f, 0.95f, 1f, 1f);

        private static bool warnedMissingTheme;

        /// <summary>A local, throwaway effect on each client - never a networked object, since
        /// every client draws its own copy from the same RPC. Task 11b: coloured by the shooter's
        /// team (UiTheme.ShotColorFor), widened and boosted toward an emissive look, and faded to
        /// transparent over its linger time rather than popping out of existence - see
        /// UiTheme.laserBeamWidth/laserBeamEmission/laserBeamLingerSeconds.</summary>
        private void DrawBeam(Vector3 from, Vector3 to, int shooterTeamId)
        {
            if (beamVfx == null)
                return;

            GameObject beam = Instantiate(beamVfx, from, Quaternion.identity);
            LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
            if (line == null)
            {
                Destroy(beam, theme != null ? theme.laserBeamLingerSeconds : FallbackLingerSeconds);
                return;
            }

            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);

            float width = theme != null ? theme.laserBeamWidth : FallbackBeamWidth;
            line.startWidth = width;
            line.endWidth = width;

            Color color = ResolveBeamColor(shooterTeamId);
            line.startColor = color;
            line.endColor = color;

            float linger = theme != null ? theme.laserBeamLingerSeconds : FallbackLingerSeconds;
            beam.AddComponent<BeamFade>().Begin(line, color, linger);
        }

        /// <summary>The shooter's team colour, boosted by Laser Beam Emission for a brighter, more
        /// glowing line than a flat team tint - see UiTheme.laserBeamEmission. Falls back to the old
        /// hardcoded cyan (FallbackBeamColor) and logs once, the same ShotTeamVisuals/AimConeView
        /// pattern for a component whose Theme slot was never assigned - the beam still fires and
        /// still draws either way, just untinted.</summary>
        private Color ResolveBeamColor(int shooterTeamId)
        {
            if (theme == null)
            {
                if (!warnedMissingTheme)
                {
                    warnedMissingTheme = true;
                    Debug.LogWarning($"[Hitscan] {name}: no UiTheme assigned - the beam draws with a " +
                                      "fallback colour instead of its shooter's team colour.");
                }
                return FallbackBeamColor;
            }

            Color baseColor = theme.ShotColorFor(shooterTeamId);
            float glow = theme.laserBeamEmission;
            return new Color(baseColor.r * glow, baseColor.g * glow, baseColor.b * glow, baseColor.a);
        }

        private static void PlayImpact(WeaponDefinition weapon, Vector3 at)
        {
            if (weapon.ImpactVfx != null && VFXManager.Instance != null)
                VFXManager.Instance.PlayVFX(weapon.ImpactVfx, at);
        }

        /// <summary>Fades the fired beam's own LineRenderer to transparent over its linger time, then
        /// destroys it. Added to the INSTANTIATED beam VFX clone, never to Hitscan itself - Hitscan
        /// lives on its weapon's projectile prefab ASSET and is never spawned (see the class
        /// comment), so it has no scene GameObject of its own to run a coroutine or Update on. The
        /// clone this attaches to is a real, spawned scene object, so it can.</summary>
        private sealed class BeamFade : MonoBehaviour
        {
            private LineRenderer line;
            private Color from;
            private float duration;
            private float startTime;

            public void Begin(LineRenderer target, Color baseColor, float lingerSeconds)
            {
                line = target;
                from = baseColor;
                duration = Mathf.Max(0.0001f, lingerSeconds);
                startTime = Time.time;
            }

            private void Update()
            {
                float t = Mathf.Clamp01((Time.time - startTime) / duration);
                Color faded = new Color(from.r, from.g, from.b, Mathf.Lerp(from.a, 0f, t));
                line.startColor = faded;
                line.endColor = faded;

                if (t >= 1f)
                    Destroy(gameObject);
            }
        }
    }
}
