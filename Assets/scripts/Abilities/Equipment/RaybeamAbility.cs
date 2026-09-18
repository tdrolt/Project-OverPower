using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// Three beams converging on the cursor. Tudor's spec: each beam that crosses an enemy applies
    /// +30% damage taken for 4 seconds, stacking to a cap of +60% - one beam is +30%, two or three
    /// is +60%. THE THIRD BEAM IS REDUNDANCY AGAINST A PARTIAL MISS, NOT EXTRA DAMAGE: all three
    /// land only at the convergence point, so the skill is placing the cursor at the right
    /// distance, not spamming the key. Zero damage - this is a debuff, not a weapon (addendum,
    /// [C, plan]).
    ///
    /// REWORK STEP 5 (Tudor, 2026-09-18): moved from Equipment to the Ultimate slot. Readiness comes
    /// entirely from Owner.UltimateCharge - see IsReady/TryBuildCast below, the identical pattern
    /// InvulnerabilityAbility, ElectricFenceAbility and AoeZoneAbility already use - NOT the base
    /// class's own charge/cooldown pool (1 charge, 0s cooldown on this prefab; that recovers the
    /// instant it is spent, so it is never itself a gate). No cooldown of its own any more.
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
                 "the tuned range below.")]
        private float beamOriginSpacing = 0.75f;

        [SerializeField, Tooltip("How far each beam reaches from its OWN origin, in metres, not from " +
                 "the cursor - the cursor point itself is also clamped to this distance from the " +
                 "muzzle. Controller's call.")]
        private float beamRange = 12f;

        [SerializeField, Tooltip("Diameter of each beam's hit-check, in metres - a thin line would " +
                 "miss a target standing a few centimetres off the exact convergence point. " +
                 "Controller's call.")]
        private float beamWidth = 0.35f;

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
            Vector3 point = cast.Payload.Point;
            int casterActor = cast.CasterActor;
            int casterTeam = cast.CasterTeam;

            RaybeamGeometry.BeamOrigins(origin, direction, beamOriginSpacing,
                out Vector3 left, out Vector3 centre, out Vector3 right);

            FireOneBeam(left, point, direction, casterActor, casterTeam);
            FireOneBeam(centre, point, direction, casterActor, casterTeam);
            FireOneBeam(right, point, direction, casterActor, casterTeam);
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

        /// <summary>Hitscan.BuildMask's identical reasoning: the designer's layers minus the two
        /// invariants (Bullet, DeadPlayer) that are never negotiable for any hit-detecting shot.</summary>
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
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }

            Destroy(beam, beamVisualSeconds);
        }
    }
}
