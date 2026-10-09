using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.UI;
using Overpower.Vision;

namespace Overpower.Weapons
{
    /// <summary>
    /// Turns a weapon into an instant beam: no travel time - once it actually fires (immediately,
    /// or after its weapon's Windup Seconds delay; see WeaponFiring.FireAfterWindup) the shot
    /// lands that same instant, anywhere along its range. The laser path's defining trait.
    ///
    /// Put it on the prefab in a weapon's Projectile Prefab slot. WeaponFiring checks that prefab for this component and, if it is there, casts a
    /// ray instead of spawning a projectile - that is the whole switch between "bullet" and "beam". A beam travels in the same fire RPC a bullet
    /// does, carrying the same origin, direction, seed and charge; no new network message exists for lasers.
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
                 "and dummies are; Building is the walls. Bullet, DeadPlayer and Barrier are always " +
                 "removed whatever you tick here - a beam always passes a jersey barrier (GDD p.29). " +
                 "To let the beam pass through walls, add an Ignore Walls component rather than " +
                 "unticking Building, so the leaf reads as a leaf.")]
        private LayerMask hitMask = ~0;

        [SerializeField, Tooltip("The visible beam. A prefab with a Line Renderer on it - its two " +
                 "points are set to run from the muzzle to wherever the beam ended. Leave empty " +
                 "for an invisible beam. Its own Line Renderer width/colour (e.g. 0.08 on Laser " +
                 "Beam VFX.prefab) are cosmetic placeholders only - DrawBeam overwrites both every " +
                 "beam from UiTheme's Laser Beam Width/team colour, so set the real numbers there.")]
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
        private static readonly Collider[] OverlapBuffer = new Collider[MaxContacts];
        private static readonly List<BeamContact> ContactBuffer = new List<BeamContact>(MaxContacts);

        // Not a tuning value either: a physics epsilon for AddPointBlankContacts below, not a
        // gameplay range. Physics.OverlapSphere needs a non-zero radius to reliably report a
        // collider containing its centre; this is deliberately tiny so it can never itself reach a
        // target the origin is merely NEAR rather than actually inside/touching.
        private const float PointBlankRadius = 0.02f;

        // Reused across every beam this never-spawned asset draws rather than `new`-ed per shot: SetPropertyBlock copies its contents into the
        // renderer, so one instance is safe to reuse for the next beam (see ApplyBeamGlow).
        // NOT a `= new MaterialPropertyBlock()` field initializer: a static initializer runs the first time ANYTHING touches this type, which in the
        // editor can be while Unity is still deserializing a Hitscan off a prefab (e.g. loading the weapon catalogue for a test), and Unity throws
        // "CreateImpl is not allowed to be called from a MonoBehaviour constructor (or instance field initializer)". Constructed lazily in
        // ApplyBeamGlow instead, the same rule ShotTeamVisuals follows by building its own in Awake.
        private static MaterialPropertyBlock beamPropertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Works out what this beam strikes WITHOUT dealing damage or drawing anything. The
        /// shooter's own client calls this the instant it fires, to decide the laser's heat refund
        /// - see WeaponFiring.RefundHeatIfBeamConnects.
        /// </summary>
        public BeamResult Resolve(Vector3 origin, ProjectileContext shot)
        {
            float range = ChargedRange(shot.Weapon, shot.ChargeFraction, shot.RangeMultiplier);
            Vector3 direction = shot.Direction.normalized;
            int mask = BuildMask();

            int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, range, mask,
                                                QueryTriggerInteraction.Ignore);

            ContactBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.collider == null)
                    continue;

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();

                // IStructure (CoverWall) is the fact BeamResolver needs to stop a piercing beam at it like a wall rather than carrying on through it as just
                // another target - see BeamResolver's class comment. Resolved here, once, since this is the one place that has the IDamageable to check.
                ContactBuffer.Add(new BeamContact(hit.distance, target, hit.point, target is IStructure));
            }

            AddPointBlankContacts(origin, mask);

            Pierce pierce = GetComponent<Pierce>();
            int maxTargets = pierce != null ? pierce.MaxTargets : 1;

            return BeamResolver.Resolve(ContactBuffer, range, maxTargets,
                                        shot.ShooterActorNumber, shot.ShooterTeamId);
        }

        /// <summary>
        /// Catches a target whose collider already CONTAINS the origin - the point-blank case the raycast above structurally cannot see. Unity never
        /// reports a collider a ray starts inside of, and the muzzle (SafeMuzzlePosition, just inside the shooter's own body capsule) can sit inside a
        /// target's capsule when the two players are close.
        /// Deliberately an OverlapSphere AT THE ORIGIN, not a second ray cast from farther back: a volume test at a single point can only find something
        /// that point is ALREADY touching, so it can never see past a wall the raycast did not see - SafeMuzzlePosition still guarantees that point sits
        /// on the shooter's own side of anything it was hugging, which keeps the old wall exploit closed. Uses the same mask as the raycast (so the
        /// through-walls leaf still ignores Building here too), and only turns an overlap into a contact when it is IDamageable: plain geometry
        /// overlapping the origin would mean SafeMuzzlePosition itself failed to clear it, which is that property's job to prevent.
        /// Distance is recorded as 0: nothing is closer to the muzzle than something the muzzle is already inside of, and BeamResolver only uses
        /// Distance to ORDER and CAP contacts (BeamResolverTests.AContactAtZeroDistanceIsStruckFirstAndEndsTheBeamThere), so 0 sorting first is
        /// correct. A target also found by the raycast is deduplicated by BeamResolver's alreadyStruck set, which keeps whichever contact it meets
        /// first in distance order - here, always this one. Point is the origin itself (Collider.ClosestPoint returns the query point unchanged when it
        /// is already inside), used only for damage/impact-VFX placement.
        /// </summary>
        private void AddPointBlankContacts(Vector3 origin, int mask)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, PointBlankRadius, OverlapBuffer, mask,
                                                       QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null)
                    continue;

                ContactBuffer.Add(new BeamContact(0f, target, origin, target is IStructure));
            }
        }

        /// <summary>The whole shot, on one client: resolve the ray, damage what it struck, and draw
        /// it. Called from the fire RPC on EVERY client, the shooter's included.</summary>
        public void Fire(Vector3 origin, ProjectileContext shot)
        {
            BeamResult beam = Resolve(origin, shot);

            for (int i = 0; i < beam.Struck.Count; i++)
            {
                BeamContact contact = beam.Struck[i];
                // abilityId -1: a beam is always a weapon's own (shot.Weapon read directly) - see DamageInfo.AbilityId. The mark fields come straight off the
                // same weapon stat block, read here on the VICTIM's own copy of the asset - the identical build every client runs, so every client's Hitscan
                // agrees on whether and how much this shot marks, with no separate message for it.
                contact.Target.ApplyDamage(new DamageInfo(shot.Damage, shot.ShooterActorNumber,
                                                          shot.ShooterTeamId, shot.Weapon.Id,
                                                          DamageSource.Projectile, false, contact.Point, -1,
                                                          shot.Weapon.MarkWindowSeconds, shot.Weapon.MarkedDamageMultiplier));
                // Every client runs this, so each can show my team the enemy it just hit (X-Ray blind hit); damage is untouched.
                TeamSight.RevealOnHit(shot.Weapon, contact.Target, shot.ShooterTeamId);
                PlayImpact(shot.Weapon, contact.Point, shot.ShooterTeamId);
            }

            Vector3 end = origin + shot.Direction.normalized * beam.Length;
            if (beam.StoppedOnGeometry)
                PlayImpact(shot.Weapon, end, shot.ShooterTeamId);

            DrawBeam(origin, end, shot.ShooterTeamId);
        }

        /// <summary>
        /// How far a beam reaches. A charging beam weapon reaches further the longer the trigger was
        /// held, up to Max Range x Charge Range Multiplier at full charge - ramping smoothly, the
        /// same way WeaponFiring ramps charged damage, so both halves of the charge payoff grow
        /// together. A weapon that cannot charge always reaches exactly Max Range.
        ///
        /// The charge fraction is the one that crossed the wire, so every client draws the same
        /// length and damages the same targets.
        ///
        /// rangeMultiplier defaults to 1 (unchanged); OverPower's buff is the one caller that passes anything else, via the shot's own
        /// ProjectileContext.RangeMultiplier (see its comment for why a beam takes this as a parameter instead of reading a second range field).
        /// </summary>
        public static float ChargedRange(WeaponDefinition weapon, float chargeFraction, float rangeMultiplier = 1f)
        {
            float range = weapon.CanCharge
                ? weapon.MaxRange * Mathf.Lerp(1f, weapon.ChargeRangeMultiplier, Mathf.Clamp01(chargeFraction))
                : weapon.MaxRange;

            return range * rangeMultiplier;
        }

        /// <summary>
        /// Where a beam built from `shot` would visibly stop if it fired RIGHT NOW - the wall or
        /// structure within its (charged) range, or the full range if nothing stops it first.
        ///
        /// WeaponFiring's wind-up warning line calls this once, the instant the trigger is pulled, to draw its telegraph along the exact path the real
        /// beam will travel. Walls and structures do not move, so resolving at the START of the wind-up predicts exactly where the beam lands at the END.
        /// Delegates to Resolve rather than re-deriving the mask/range/pierce rules, so the warning can never disagree with what the beam does -
        /// including that a laser with unlimited pierce (every laser today) never stops early just because a player is standing in the line; only a wall
        /// or a structure shortens it.
        /// </summary>
        public float PredictBeamLength(Vector3 origin, ProjectileContext shot) => Resolve(origin, shot).Length;

        /// <summary>The designer's layers, minus the three invariants HitMasks enforces for every
        /// shot - see HitMasks.StripNonNegotiableLayers, shared with ProjectileMotor - minus walls
        /// if this is the through-walls leaf.</summary>
        private int BuildMask()
        {
            int mask = HitMasks.StripNonNegotiableLayers(hitMask);

            IgnoreWalls ignoreWalls = GetComponent<IgnoreWalls>();
            return ignoreWalls != null ? ignoreWalls.RemoveWallsFrom(mask) : mask;
        }

        // Fallbacks for a Hitscan whose Theme slot was left empty (ResolveBeamColor below logs once, and the beam still fires and draws with these).
        private const float FallbackBeamWidth = 0.08f;
        private const float FallbackLingerSeconds = 0.12f;
        private static readonly Color FallbackBeamColor = new Color(0.35f, 0.95f, 1f, 1f);

        private static bool warnedMissingTheme;

        /// <summary>A local, throwaway effect on each client - never a networked object, since every client draws its own copy from the same RPC.
        /// Coloured by the shooter's team (UiTheme.ShotColorFor), widened and boosted toward an emissive look, and faded to transparent over its
        /// linger time rather than popping out of existence - see UiTheme.laserBeamWidth/laserBeamEmission/laserBeamLingerSeconds.</summary>
        private void DrawBeam(Vector3 from, Vector3 to, int shooterTeamId)
        {
            if (beamVfx == null)
                return;

            // A beam from the fog is drawn only while the line crosses my team's sight (own team's always). (D2)
            if (!TeamSight.ShotShownAlong(shooterTeamId, from, to))
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

            // Overwrites the start/end width the beam VFX prefab's own Line Renderer was baked with (see the Beam Vfx tooltip) - Laser Beam Width on
            // UiTheme is the one real home for this number.
            float width = theme != null ? theme.laserBeamWidth : FallbackBeamWidth;
            line.startWidth = width;
            line.endWidth = width;

            // The TRUE team colour, never boosted - see ApplyBeamGlow for why the brightness multiply lives elsewhere.
            Color color = ResolveBeamColor(shooterTeamId);
            line.startColor = color;
            line.endColor = color;
            ApplyBeamGlow(line, theme != null ? theme.laserBeamEmission : 1f);

            float linger = theme != null ? theme.laserBeamLingerSeconds : FallbackLingerSeconds;
            beam.AddComponent<BeamFade>().Begin(line, color, linger);
        }

        /// <summary>The shooter's team colour, unmodified - see ApplyBeamGlow for where the brightness boost happens instead. Falls back to
        /// FallbackBeamColor and logs once (the ShotTeamVisuals/AimConeView pattern) for a component whose Theme slot was never assigned - the beam
        /// still fires and draws either way, just untinted.</summary>
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

            return theme.ShotColorFor(shooterTeamId);
        }

        /// <summary>
        /// The brightness boost lives here, not in the vertex colour: a LineRenderer's start/end colour is written into an 8-bit-per-channel vertex
        /// buffer, so a channel that crosses 1.0 clamps instead of brightening (team 1's violet (0.68, 0.32, 1) x 1.6 rendered (1, 0.51, 1), visibly
        /// pinker rather than a brighter violet).
        /// So the vertex colour (set by the caller) stays the plain team colour and the multiply happens in the shader, via a MaterialPropertyBlock on
        /// _BaseColor - a full-precision float4 uniform that is never quantised, so (glow, glow, glow, 1) scales every channel by the identical factor
        /// with no clamp and no hue shift.
        /// This works only because Laser Beam.mat's shader (Universal Render Pipeline/Particles/Unlit, _ColorMode 0 = Multiply; checked with
        /// get_material_properties/get_shader_properties rather than assumed) already multiplies its vertex colour by _BaseColor. Its _EmissionColor
        /// IS HDR-tagged and looks like the obvious property, but the material never enables the shader's _EMISSION keyword, so writing it would do
        /// nothing without flipping a keyword on the shared asset - _BaseColor needs no such toggle.
        /// </summary>
        private static void ApplyBeamGlow(LineRenderer line, float glow)
        {
            if (beamPropertyBlock == null)
                beamPropertyBlock = new MaterialPropertyBlock();

            beamPropertyBlock.Clear();
            beamPropertyBlock.SetColor(BaseColorId, new Color(glow, glow, glow, 1f));
            line.SetPropertyBlock(beamPropertyBlock);
        }

        private static void PlayImpact(WeaponDefinition weapon, Vector3 at, int shooterTeamId)
        {
            // An enemy beam's impact flash in the fog is not shown (own team's always). (D2)
            if (weapon.ImpactVfx != null && VFXManager.Instance != null && TeamSight.ShotShownAt(shooterTeamId, at))
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
