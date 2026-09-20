using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// Three beams converging on the cursor. Tudor's spec: each beam that crosses an enemy applies
    /// +30% damage taken for 4 seconds, stacking to a cap of +60% - one beam is +30%, two or three
    /// is +60%. THE THIRD BEAM IS REDUNDANCY AGAINST A PARTIAL MISS, NOT EXTRA DAMAGE: aiming at the
    /// convergence point is what lands all three, so the skill is placing the cursor at the right
    /// distance, not spamming the key - but PIERCE IS ON (see FireOneBeam below), so this is NOT the
    /// only place a beam can land. With `beamRange` doubled to 24m (rework step 6) each beam debuffs
    /// every enemy anywhere along its own 24m line, convergence point or not; three separate targets
    /// spread along one beam's path all take that beam's debuff, not just whichever one sits at the
    /// cursor. Zero damage - this is a debuff, not a weapon (addendum, [C, plan]).
    ///
    /// REWORK STEP 5 (Tudor, 2026-09-18): moved from Equipment to the Ultimate slot. Readiness comes
    /// entirely from Owner.UltimateCharge - see IsReady/TryBuildCast below, the identical pattern
    /// InvulnerabilityAbility, ElectricFenceAbility and AoeZoneAbility already use - NOT the base
    /// class's own charge/cooldown pool (1 charge, 0s cooldown on this prefab; that recovers the
    /// instant it is spent, so it is never itself a gate). No cooldown of its own any more.
    ///
    /// REWORK STEP 6 (Tudor, 2026-09-18): "increase its range 2 times and thickness of the beam 1.5
    /// times" - beamRange 12 -> 24, beamWidth 0.35 -> 0.525. beamWidth is now the ONE home for both
    /// the hit diameter (the SphereCast radius below, unchanged) and the drawn diameter (DrawBeam
    /// now sets the instantiated clone's LineRenderer width from it) - before this the drawn line was
    /// a flat 0.08m from the shared Laser Beam VFX prefab regardless of beamWidth, a 4.4x mismatch
    /// between what a player saw and what actually hit them.
    ///
    /// REWORK 2026-09-20 (Tudor: "the raybeam is supposed to be a long range ultimate... make the
    /// beams a bit thicker (30%) and give them 20% more range"): beamRange 24 -> 28.8 (+20%),
    /// beamWidth 0.525 -> 0.6825 (+30%). The 30% thickness alone leaves only ~6.75cm of gap between
    /// adjacent beam edges at the muzzle (spacing 0.75 - beamWidth 0.6825) - close to fusing into one
    /// fat beam - so beamOriginSpacing also moves 0.75 -> 0.9, which puts the edge gap back to almost
    /// exactly what it was before this rework (0.225 -> 0.2175m), i.e. the three beams read exactly
    /// as distinguishable as they always did, just each one fatter.
    ///
    /// THE SPREAD (this rework's second half, a controller judgement call, not Tudor's literal
    /// words): converging the outer beams on the CURSOR's exact depth (the pre-existing mechanic,
    /// AimFromOriginToPoint) already lands all three beams exactly on target at ANY range for a
    /// pixel-perfect click - RaybeamGeometryTests proves this holds unchanged from muzzle to the far
    /// end of the range. The catch is that a real player cannot click a ground-plane cursor
    /// pixel-perfectly on a distant target: the further out the cursor sits, the more a small
    /// screen-space slip becomes a large world-space depth error, which is what actually produced
    /// "only works within ~3m" in practice, not a flaw in the convergence maths itself. Making the
    /// bonus reliable at REAL FIGHTING RANGE (10-20m) therefore means no longer trusting the cursor's
    /// exact depth for the two outer beams: they now always converge at beamConvergenceRange metres
    /// straight down the shooter's own aim direction, regardless of exactly where the cursor sits.
    /// The centre beam is unaffected (its origin already sits on the aim line, so aiming it at that
    /// same fixed point is identical to firing it straight down Direction). Tudor's own "lining up"
    /// skill becomes about closing to roughly the right distance and firing along the target, not
    /// about clicking a specific metre of ground - which fits an ultimate Tudor now calls "long
    /// range" better than a mechanic that quietly punished exactly the players fighting at range.
    ///
    /// RUNS ON EVERY CLIENT, INCLUDING THE CASTER'S OWN, exactly once per cast - the beams are
    /// instant, so there is no coroutine the way the flamethrower's spray needs one. DAMAGE (well,
    /// STATUS) IS VICTIM-SIDE, EXACTLY LIKE HITSCAN: every client resolves the same three rays from
    /// the same synced Origin/Direction/Point and calls IStatusReceiver.ApplyStatus on everything
    /// they cross; the receiver's own guard (PlayerStatusEffects checks IsMine, a dummy is always
    /// its own authority) makes sure only the victim's own machine actually applies it.
    ///
    /// GEOMETRY LIVES IN RaybeamGeometry (Combat/), NOT HERE - beam origin placement and "does this
    /// beam's ray cross that point" are pure maths with their own edit-mode tests (see
    /// RaybeamGeometryTests), the identical split BeamResolver keeps from Hitscan for the same
    /// reason: eyeballing three converging rays in Play mode is not how "the beams converge
    /// correctly" gets verified.
    ///
    /// PIERCE, NOT STOP-AT-FIRST: each beam calls BeamResolver.Resolve with Unlimited targets, so a
    /// beam crossing three enemies in a line applies its debuff to all three, not just the nearest -
    /// the addendum's own "[C: pierces]" note. A single beam still never double-applies to one
    /// target with two colliders (BeamResolver's own "each target once" rule) and still stops dead
    /// at the first Building or IStructure hit (deployable cover), exactly like the base laser.
    ///
    /// VULNERABILITY'S CAP IS GLOBAL, NOT A FIELD HERE - the addendum is explicit that the plan's
    /// per-ability vulnerabilityCap field must not exist: GameplayConfig.VulnerabilityCap (0.60) is
    /// what StatusEffectState's StackToCap rule actually reads, already true for every vulnerability
    /// application in the game (DummyTarget, PlayerStatusEffects). This module only ever sends
    /// magnitude 0.30 - the cap plays out on its own the moment two beams land.
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

        // Not a tuning value, matching Hitscan's own MaxContacts reasoning: nine players with a
        // couple of colliders each plus the walls in a 12m line fit comfortably.
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

        /// <summary>Rebuilt once more here (Task T3): Awake/OnValidate both run before Bind ever
        /// assigns Definition (AbilityModule's own class comment - "do not read it in Awake"), so
        /// the spec built at Awake always has abilityId -1. OnEquip is the first point Definition is
        /// safe to read, and this module has nothing else to do on equip.</summary>
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

        /// <summary>Full meter only (rework step 5) - same reasoning and same call as the other three
        /// ultimates (InvulnerabilityAbility, ElectricFenceAbility, AoeZoneAbility): 1 charge and a
        /// 0s cooldown on the base class recover instantly, so without this override that pool would
        /// never actually refuse a cast and Raybeam would be spammable regardless of the ultimate
        /// meter, ultimate slot or not.</summary>
        public override bool IsReady => Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull;

        /// <summary>
        /// Origin is the MUZZLE (ctx.Muzzle, the wall-safe one - addendum, overriding the plan's
        /// plain ctx.Origin), not the body: three beams starting from the gun read better than three
        /// starting from the chest, and SafeMuzzlePosition already keeps this off the inside of a
        /// wall the caster is pressed against.
        ///
        /// Point is the cursor's ground point with its Y overridden to muzzle height - a ground
        /// point would send every beam angling down into the floor the instant the cursor is more
        /// than a few metres away - then clamped to Beam Range from the muzzle so a cursor far
        /// across the map cannot make the beams reach further than the tuned distance.
        ///
        /// Refuses the cast (rework step 5) unless UltimateCharge itself agrees to spend - the same
        /// "proceed only if true" the other three ultimates use, so the runner's own base-class
        /// SpendCharge (the 1-charge/0s pool below) never runs on a meter that was not actually full.
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
            rawPoint.y = origin.y; // A ground point sends beams into the floor - addendum's own note.

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

            // 2026-09-20 rework (see beamConvergenceRange's own tooltip): the outer beams converge
            // on a FIXED point straight down the shooter's own aim direction, never on the cursor's
            // cast.Payload.Point - that field is still computed and sent (other systems may still
            // want it), it just no longer decides where these beams cross. The centre beam's origin
            // already sits on the aim line, so aiming it at the same fixed point is identical to
            // firing it straight down direction - passing the same point to all three keeps this one
            // formula instead of special-casing the centre beam.
            Vector3 convergePoint = origin + direction * Mathf.Min(beamConvergenceRange, beamRange);

            FireOneBeam(left, convergePoint, direction, casterActor, casterTeam);
            FireOneBeam(centre, convergePoint, direction, casterActor, casterTeam);
            FireOneBeam(right, convergePoint, direction, casterActor, casterTeam);
        }

        /// <summary>
        /// One beam, start to finish: resolve who it crosses, apply the debuff to each of them once,
        /// and draw the line. Damage is never applied - Tudor's spec is 0 damage for this ability, so
        /// there is no ApplyDamage call here at all (matching SonicPulseAbility's identical choice
        /// to simply not carry a dead "damage" field for a 0).
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

            // Unlimited: this ONE beam must apply to every enemy it crosses before the cut, not
            // stop at the nearest - the addendum's "[C: pierces]" note. BeamResolver still keeps
            // each target to a single hit even with two colliders, and still stops dead at the
            // first wall or structure - see this class's own comment.
            BeamResult beam = BeamResolver.Resolve(contactBuffer, beamRange, BeamResolver.Unlimited,
                                                    casterActor, casterTeam);

            for (int i = 0; i < beam.Struck.Count; i++)
            {
                IStatusReceiver receiver = (beam.Struck[i].Target as Component)?.GetComponentInParent<IStatusReceiver>();
                receiver?.ApplyStatus(vulnerabilitySpec, casterActor);
            }

            Vector3 end = beamOrigin + aimDirection * beam.Length;
            DrawBeam(beamOrigin, end);
        }

        /// <summary>Hitscan.BuildMask's identical reasoning: the designer's layers minus the three
        /// invariants (Bullet, DeadPlayer, Barrier) that are never negotiable for any hit-detecting
        /// shot - Raybeam always passes a jersey barrier (GDD p.29), whatever its own hitMask ticks.</summary>
        private int BuildMask() => HitMasks.StripNonNegotiableLayers(hitMask);

        /// <summary>A local, throwaway effect on each client, matching Hitscan.DrawBeam - never a
        /// networked object, since every client draws its own copy from the same cast event.</summary>
        private void DrawBeam(Vector3 from, Vector3 to)
        {
            if (beamVfx == null)
                return;

            GameObject beam = Instantiate(beamVfx, from, Quaternion.identity);
            LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
            if (line != null)
            {
                line.useWorldSpace = true;

                // The drawn beam is exactly as wide as the beam that actually hits (rework step 6).
                // Before this, beamWidth was a pure hit diameter and the visible line was whatever
                // width the shared VFX prefab happened to carry - 0.08m against a 0.35m hitbox, so
                // the thing the player aimed by was four times thinner than the thing that struck.
                // Set on the INSTANTIATED CLONE, never on the prefab asset: Laser Beam VFX is shared
                // with both laser weapons, and widening it there would widen those too.
                line.startWidth = line.endWidth = beamWidth;

                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }

            Destroy(beam, beamVisualSeconds);
        }
    }
}
