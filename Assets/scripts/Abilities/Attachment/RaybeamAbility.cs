using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Weapons;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// Three beams, the outer two converging at beamConvergenceRange straight down the shooter's aim direction, each
    /// applying Vulnerability to every enemy it crosses. Zero damage: a debuff, not a weapon.
    /// WHY A FIXED CONVERGENCE POINT, NOT THE CURSOR: converging on the cursor's depth lands all three at any range for a
    /// pixel-perfect click (RaybeamGeometryTests), but a ground-plane cursor cannot be clicked that precisely at range, so
    /// the bonus only worked up close. Now catching two or three beams is about closing in and firing along the target.
    /// THE THIRD BEAM IS REDUNDANCY AGAINST A PARTIAL MISS, NOT EXTRA DAMAGE, and PIERCE IS ON: each beam debuffs every
    /// enemy along its whole line (BeamResolver.Unlimited), convergence point or not, but still stops at the first
    /// Building or IStructure and applies once per target.
    /// VULNERABILITY'S CAP IS GLOBAL (GameplayConfig > Vulnerability Cap, read by StatusEffectState's StackToCap), never a
    /// field here: one beam is the per-beam magnitude, two or three reach the cap.
    /// Ultimate slot: readiness comes entirely from Owner.UltimateCharge (IsReady/TryBuildCast, like the other ultimates),
    /// not the base class's 1-charge/0 s pool, which recovers instantly and so never gates anything.
    /// beamWidth is the ONE home for the hit diameter (SphereCast radius) and the drawn diameter (DrawBeam), so what a
    /// player sees is what hits them; beamOriginSpacing must leave a visible gap between beam edges.
    /// RUNS ON EVERY CLIENT, once per cast; STATUS IS VICTIM-SIDE like Hitscan: every client resolves the same three rays
    /// from the synced Origin/Direction/Point and calls IStatusReceiver.ApplyStatus, and the receiver's IsMine guard
    /// means only the victim's machine applies it. The maths lives in RaybeamGeometry (Combat/), tested pure.
    /// </summary>
    public sealed class RaybeamAbility : AbilityModule
    {
        [Header("Beams")]
        [SerializeField, Tooltip("Extra damage taken from ONE beam, as a fraction - 0.30 means " +
                 "+30%. The real cap on stacking several is NOT set here: it lives on GameplayConfig " +
                 "as Vulnerability Cap (0.60), shared by every debuff in the game that applies this " +
                 "status, so two or three beams landing together read +60%, never +90%.")]
        private float vulnerabilityPerBeam = 0.30f;

        [SerializeField, Tooltip("How many seconds the +damage debuff lasts from ONE beam. A second " +
                 "raybeam landing before this runs out adds a fresh stack of its own rather than " +
                 "extending this one, but the reported time-left reads as a refresh either way, " +
                 "since it always shows whichever stack has the most time remaining.")]
        private float vulnerabilitySeconds = 4f;

        [SerializeField, Tooltip("How far apart the two outer beams start either side of the muzzle, " +
                 "in metres. Controller's call: wide enough that the three beams read as separate " +
                 "shots at close range, narrow enough that all three still converge on one target at " +
                 "the tuned range below. 2026-09-20: nudged 0.75 -> 0.9 to keep the same visible gap " +
                 "between beam edges now that Beam Width is 30% fatter (see its own tooltip) - purely " +
                 "a readability correction, not an attempt to change the spread.")]
        private float beamOriginSpacing = 0.9f;

        [SerializeField, Tooltip("How far each beam TRAVELS AND DEBUFFS from its OWN origin, in " +
                 "metres, pierce included. Two things at once: the CAMERA sets how far out you can " +
                 "place the cursor on screen (roughly 19m out at default zoom, up to 38m fully " +
                 "zoomed out) - but TryBuildCast pulls the convergence point back to THIS number " +
                 "whenever the cursor sits further away than it, so the beams meet at this distance " +
                 "instead of at the cursor. That makes this both how far a beam keeps hitting things " +
                 "past its target AND the furthest point the three beams can ever converge, whatever " +
                 "the camera would otherwise let you reach. Tudor, 2026-09-18: \"i want for the " +
                 "beams to traverse\" - doubled from 12 to 24. Tudor, 2026-09-20: \"give them 20% " +
                 "more range\" - 24 -> 28.8.")]
        private float beamRange = 28.8f;

        [SerializeField, Tooltip("Diameter of each beam, in metres - BOTH what it hits (the " +
                 "SphereCast radius below) AND what you see (DrawBeam sets the drawn line's width " +
                 "from this same number, on the instantiated clone only - never on the shared Laser " +
                 "Beam VFX prefab, which the two laser weapons also use). One number, one home: " +
                 "before rework step 6 the drawn line was a flat 0.08m regardless of this value. " +
                 "Tudor, 2026-09-18: \"thickness of the beam 1.5 times\" - 0.35 -> 0.525. Tudor, " +
                 "2026-09-20: \"make the beams a bit thicker (30%)\" - 0.525 -> 0.6825.")]
        private float beamWidth = 0.6825f;

        [SerializeField, Tooltip("How far downrange, in metres, the two OUTER beams are aimed to " +
                 "cross the centre beam's line - a fixed distance along the shooter's own aim " +
                 "direction, not wherever the cursor happens to sit. 2026-09-20: this is the fix for " +
                 "\"the raybeam is supposed to be a long range ultimate\" - converging on the cursor's " +
                 "exact depth already works at any range for a pixel-perfect click, but a ground " +
                 "cursor cannot deliver pixel-perfect depth at range, which is what actually limited " +
                 "the old stacked-vulnerability bonus to close quarters. Fixing the crossing point " +
                 "here instead means catching two or three beams is about closing to roughly this " +
                 "distance and firing along the target, not about clicking one exact metre of ground. " +
                 "Default 15 sits in the middle of the 10-20m band Tudor's ultimate is meant to reward; " +
                 "clamped to Beam Range so it can never ask a beam to converge past where it stops " +
                 "existing.")]
        private float beamConvergenceRange = 15f;

        [SerializeField, Tooltip("Which layers a beam can hit. Default is where living players and " +
                 "dummies sit; Building is the walls a beam must stop at. Deployable cover is found " +
                 "as an IStructure on whatever collider it actually sits on, whichever layer that is.")]
        private LayerMask hitMask = ~0;

        [Header("Visuals")]
        [SerializeField, Tooltip("The visible beam. A prefab with a Line Renderer on it - its two " +
                 "points are set to run from that beam's own origin to wherever it ended. Leave " +
                 "empty for invisible beams (useful while first wiring the ability up).")]
        private GameObject beamVfx;

        [SerializeField, Tooltip("Seconds each beam line stays on screen. Purely visual - the status " +
                 "application already happened the instant the cast was received.")]
        private float beamVisualSeconds = 0.15f;

        // Not a tuning value, matching Hitscan's MaxContacts: nine players with a couple of colliders each plus the
        // walls along a beam fit comfortably.
        private const int MaxContacts = 32;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxContacts];
        private readonly List<BeamContact> contactBuffer = new List<BeamContact>(MaxContacts);

        // Vulnerability's magnitude and duration never change per cast, so the spec is built once
        // rather than re-boxed every ExecuteCast.
        private StatusEffectSpec vulnerabilitySpec;

        protected override void OnValidate()
        {
            base.OnValidate();
            vulnerabilityPerBeam = Mathf.Max(0f, vulnerabilityPerBeam);
            vulnerabilitySeconds = Mathf.Max(0f, vulnerabilitySeconds);
            beamOriginSpacing = Mathf.Max(0f, beamOriginSpacing);
            beamRange = Mathf.Max(0.1f, beamRange);
            beamWidth = Mathf.Max(0.01f, beamWidth);
            beamConvergenceRange = Mathf.Clamp(beamConvergenceRange, 0.1f, beamRange);
            beamVisualSeconds = Mathf.Max(0f, beamVisualSeconds);
            RebuildSpec();
        }

        private void Awake() => RebuildSpec();

        /// <summary>Rebuilt once more here: Awake/OnValidate run before Bind assigns Definition (AbilityModule: "do not
        /// read it in Awake"), so the Awake spec always has abilityId -1. OnEquip is the first point Definition is safe.</summary>
        public override void OnEquip() => RebuildSpec();

        private void RebuildSpec()
        {
            vulnerabilitySpec = new StatusEffectSpec
            {
                kind = StatusKind.Vulnerability,
                duration = vulnerabilitySeconds,
                magnitude = vulnerabilityPerBeam,
                // Definition is null here when OnValidate runs on the raw prefab asset (before Bind
                // ever assigns it) - see AbilityModule's own OnValidate comment.
                abilityId = Definition != null ? Definition.Id : -1
            };
        }

        // ---- owner only ---------------------------------------------------------------------------

        /// <summary>Full meter only, like the other ultimates: the base class's 1 charge and 0 s cooldown recover
        /// instantly, so without this override that pool would never refuse a cast and Raybeam would be spammable.</summary>
        public override bool IsReady => Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull;

        /// <summary>
        /// Origin is the MUZZLE (ctx.Muzzle, the wall-safe one), not the body: three beams from the gun read better than
        /// from the chest, and SafeMuzzlePosition keeps it off the inside of a wall the caster is pressed against.
        /// Point is the cursor's ground point with Y set to muzzle height (a ground point would angle every beam into the
        /// floor), clamped to Beam Range from the muzzle.
        /// Refuses unless UltimateCharge agrees to spend, so the base-class SpendCharge never runs on a meter that was not full.
        /// </summary>
        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;
            if (Owner.UltimateCharge == null || !Owner.UltimateCharge.Spend())
                return false;

            // Past this point the meter is already spent - no early return may follow, or a later
            // `return false` would eat a full ultimate meter with no cast to show for it.
            Vector3 origin = ctx.Muzzle;

            Vector3 rawPoint = ctx.TargetPoint;
            rawPoint.y = origin.y; // A ground point sends beams into the floor.

            Vector3 toRaw = rawPoint - origin;
            float rawDistance = toRaw.magnitude;
            Vector3 point = (rawDistance > beamRange && rawDistance > 0.0001f)
                ? origin + toRaw / rawDistance * beamRange
                : rawPoint;

            payload = new CastPayload { Origin = origin, Direction = ctx.AimDirection, Point = point };
            return true;
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0)
                return;

            Vector3 origin = cast.Payload.Origin;
            Vector3 direction = cast.Payload.Direction;
            int casterActor = cast.CasterActor;
            int casterTeam = cast.CasterTeam;

            RaybeamGeometry.BeamOrigins(origin, direction, beamOriginSpacing,
                out Vector3 left, out Vector3 centre, out Vector3 right);

            // The outer beams converge on a FIXED point down the shooter's aim direction (beamConvergenceRange), never
            // on cast.Payload.Point (still sent, but it does not decide where beams cross). The centre beam's origin sits on
            // the aim line, so the same point is identical to firing straight down direction and keeps one formula.
            // The formula lives in RaybeamGeometry so it has its own test.
            Vector3 convergePoint = RaybeamGeometry.ConvergencePoint(origin, direction, beamConvergenceRange, beamRange);

            FireOneBeam(left, convergePoint, direction, casterActor, casterTeam);
            FireOneBeam(centre, convergePoint, direction, casterActor, casterTeam);
            FireOneBeam(right, convergePoint, direction, casterActor, casterTeam);
        }

        /// <summary>
        /// One beam: resolve who it crosses, apply the debuff to each once, draw the line. No ApplyDamage at all (zero
        /// damage; SonicPulseAbility likewise carries no dead "damage" field).
        /// </summary>
        private void FireOneBeam(Vector3 beamOrigin, Vector3 point, Vector3 fallbackDirection,
                                 int casterActor, int casterTeam)
        {
            Vector3 aimDirection = RaybeamGeometry.AimFromOriginToPoint(beamOrigin, point, fallbackDirection);

            int count = Physics.SphereCastNonAlloc(beamOrigin, beamWidth * 0.5f, aimDirection, hitBuffer,
                                                   beamRange, BuildMask(), QueryTriggerInteraction.Ignore);

            contactBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null)
                    continue;

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                contactBuffer.Add(new BeamContact(hit.distance, target, hit.point, target is IStructure));
            }

            // Unlimited: this ONE beam applies to every enemy it crosses before the cut (pierce); BeamResolver still
            // keeps each target to a single hit and stops dead at the first wall or structure.
            BeamResult beam = BeamResolver.Resolve(contactBuffer, beamRange, BeamResolver.Unlimited,
                                                    casterActor, casterTeam);

            for (int i = 0; i < beam.Struck.Count; i++)
            {
                IStatusReceiver receiver = (beam.Struck[i].Target as Component)?.GetComponentInParent<IStatusReceiver>();
                receiver?.ApplyStatus(vulnerabilitySpec, casterActor);
            }

            Vector3 end = beamOrigin + aimDirection * beam.Length;
            DrawBeam(beamOrigin, end, casterTeam);
        }

        /// <summary>Hitscan.BuildMask's identical reasoning: the designer's layers minus the three
        /// invariants (Bullet, DeadPlayer, Barrier) that are never negotiable for any hit-detecting
        /// shot - Raybeam always passes a jersey barrier (GDD p.29), whatever its own hitMask ticks.</summary>
        private int BuildMask() => HitMasks.StripNonNegotiableLayers(hitMask);

        /// <summary>A local, throwaway effect on each client, matching Hitscan.DrawBeam - never a
        /// networked object, since every client draws its own copy from the same cast event.</summary>
        private void DrawBeam(Vector3 from, Vector3 to, int casterTeam)
        {
            if (beamVfx == null)
                return;

            // D2: an enemy's Raybeam is drawn only while this beam's line crosses my team's sight (own team's always).
            if (!TeamSight.ShotShownAlong(casterTeam, from, to))
                return;

            GameObject beam = Instantiate(beamVfx, from, Quaternion.identity);
            LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
            if (line != null)
            {
                line.useWorldSpace = true;

                // Exactly as wide as the beam that hits (beamWidth is the one home). Set on the INSTANTIATED CLONE, never
                // the prefab asset: Laser Beam VFX is shared with both laser weapons, and widening it would widen those too.
                line.startWidth = line.endWidth = beamWidth;

                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }

            Destroy(beam, beamVisualSeconds);
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"+{ShopNumberFormat.Compact(vulnerabilityPerBeam * 100f)}% damage taken per beam, for {ShopNumberFormat.Compact(vulnerabilitySeconds)}s",
                                   $"Range {ShopNumberFormat.Compact(beamRange)}m");
    }
}
