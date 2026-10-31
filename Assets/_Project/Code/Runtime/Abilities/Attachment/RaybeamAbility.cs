using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Weapons;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// Three beams from the gun, the outer two converging at Beam Convergence Range straight down the aim (a ground cursor cannot be clicked
    /// precisely enough at range to converge on it), each applying Vulnerability once to every enemy it crosses (pierce) up to the first wall or
    /// IStructure. Zero damage; the third beam is redundancy against a partial miss, and the stacking cap is GameplayConfig's Vulnerability Cap.
    /// Beam Width is the one home for what hits and what is drawn (one merged beam, RaybeamBeamView). The press only starts the wind-up
    /// (RaybeamWindup); the caster's machine then fires with the aim of that moment or cancels, and only firing spends the meter, which alone
    /// gates the press. The fire phase runs on every client from the synced rays; status is victim-side (the receiver's IsMine guard).
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

        [SerializeField, Tooltip("How far apart the two outer beams start either side of the muzzle, in metres. The three lines are " +
                 "drawn as one beam, so this is not a visible gap: with Beam Width it sets how wide the beam is at the gun, and with " +
                 "Beam Convergence Range where the lines cross. Narrow enough that all three still converge on one target at the " +
                 "tuned range below.")]
        private float beamOriginSpacing = 0.9f;

        [SerializeField, Tooltip("How far each beam TRAVELS AND DEBUFFS from its OWN origin, in metres, pierce included. Two things " +
                 "at once: the CAMERA sets how far out you can place the cursor on screen (about 38m at the zoom a match " +
                 "starts at, the farthest; about 19m with the wheel zoomed all the way in) - but when the beams fire, the cursor's point is pulled back to THIS " +
                 "number whenever the cursor sits further away than it, so the beams meet at this distance instead of " +
                 "at the cursor. That makes this both how far a beam keeps hitting things past its target AND the " +
                 "furthest point the three beams can ever converge, whatever the camera would otherwise let you reach.")]
        private float beamRange = 28.8f;

        [SerializeField, Tooltip("Diameter of each of the three hit lines, in metres - BOTH what it hits (the SphereCast radius is half " +
                 "of it) AND what you see: the drawn beam spans the outer lines' outer edges, each exactly this wide, and its white " +
                 "core is a share of this width. One number, one home.")]
        private float beamWidth = 0.6825f;

        [SerializeField, Tooltip("How far downrange, in metres, the two OUTER beams are aimed to cross the centre beam's line - a " +
                 "fixed distance along the shooter's own aim direction, not wherever the cursor happens to sit. The " +
                 "raybeam is a long-range ultimate, so the crossing point is fixed here. Converging on the cursor's " +
                 "exact depth already works at any range for a pixel-perfect click, but a ground cursor cannot " +
                 "deliver pixel-perfect depth at range, which is what actually limited the old stacked-vulnerability " +
                 "bonus to close quarters. Fixing the crossing point here instead means catching two or three beams " +
                 "is about closing to roughly this distance and firing along the target, not about clicking one exact " +
                 "metre of ground. Default 15 sits in the middle of the 10-20m band the ultimate is meant to reward; " +
                 "clamped to Beam Range so it can never ask a beam to converge past where it stops existing.")]
        private float beamConvergenceRange = 15f;

        [SerializeField, Tooltip("Which layers a beam can hit. Default is where living players and " +
                 "dummies sit; Building is the walls a beam must stop at. Deployable cover is found " +
                 "as an IStructure on whatever collider it actually sits on, whichever layer that is.")]
        private LayerMask hitMask = ~0;

        [Header("Wind-up")]
        [SerializeField, Tooltip("Seconds from pressing the Raybeam to the beams firing. Meanwhile a red hexagon shrinks at your gun " +
                 "for everyone who can see you, and you can still move and aim: the beams fire from wherever your gun is and points " +
                 "when it ends. Being stunned or dying during it cancels the Raybeam, and your ultimate meter stays full; " +
                 "overheating does not, the beams still fire. 0 fires at the press.")]
        private float windupSeconds = 0.2f;

        [Header("Visuals")]
        [SerializeField, Tooltip("The visible beam: the three hit lines drawn as ONE beam spanning the outer lines' outer edges (each " +
                 "line exactly Beam Width wide): Raybeam Beam (one red sheath, one white-hot core, one team-colour outline). Any other " +
                 "prefab with a Line Renderer draws the centre line as a plain line. Leave empty for invisible beams (useful while " +
                 "first wiring the ability up).")]
        private GameObject beamVfx;

        [SerializeField, Tooltip("Seconds the beam stays on screen. Purely visual - the status " +
                 "application already happened the instant the beams fired.")]
        private float beamVisualSeconds = 0.15f;

        [SerializeField, Tooltip("The scorch that flashes where a beam stops on a wall or a placed cover. Shown where your team " +
                 "can see that spot, like a gun's impact. Leave empty for no scorch.")]
        private GameObject scorchVfx;

        // Not a tuning value, matching Hitscan's MaxContacts: nine players with a couple of colliders each plus the
        // walls along a beam fit comfortably.
        private const int MaxContacts = 32;
        private const byte PhaseWindup = 0, PhaseFire = 1, PhaseCancel = 2; // 0 is always the press (CastEvent.Phase)
        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxContacts];
        private readonly List<BeamContact> contactBuffer = new List<BeamContact>(MaxContacts);
        private readonly List<Vector3> normalBuffer = new List<Vector3>(MaxContacts); // alongside contactBuffer: where a scorch faces

        // Vulnerability's magnitude and duration never change per cast, so the spec is built once
        // rather than re-boxed every ExecuteCast.
        private StatusEffectSpec vulnerabilitySpec;

        private readonly Vector3[] lineFrom = new Vector3[3], lineTo = new Vector3[3];
        private bool windingUp; // owner only
        private float windupStartedAt;
        private RaybeamChargeView charge;
        private bool chargeBuilt;

        protected override void OnValidate()
        {
            base.OnValidate();
            vulnerabilityPerBeam = Mathf.Max(0f, vulnerabilityPerBeam);
            vulnerabilitySeconds = Mathf.Max(0f, vulnerabilitySeconds);
            beamOriginSpacing = Mathf.Max(0f, beamOriginSpacing);
            beamRange = Mathf.Max(0.1f, beamRange);
            beamWidth = Mathf.Max(0.01f, beamWidth);
            beamConvergenceRange = Mathf.Clamp(beamConvergenceRange, 0.1f, beamRange);
            windupSeconds = Mathf.Max(0f, windupSeconds);
            beamVisualSeconds = Mathf.Max(0f, beamVisualSeconds);
            RebuildSpec();
        }

        private void Awake() => RebuildSpec();

        // The meter alone gates the press (IsReady): the runner must never refuse a press after TryBuildCast has started the wind-up.
        protected override bool SpendsChargeOnCast => false;

        private void OnDestroy()
        {
            if (charge != null)
                Destroy(charge.gameObject);
        }

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

        /// <summary>Full meter only, like the other ultimates, and not while a wind-up is already charging: the base class's 1 charge and
        /// 0 s cooldown recover instantly, so without this override that pool would never refuse a cast and Raybeam would be spammable.</summary>
        public override bool IsReady => RaybeamWindup.IsReady(Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull, windingUp);

        /// <summary>The press only starts the wind-up: the payload tells every client where the gun was, for the hexagon. The meter is
        /// not spent here but when the beams fire (EndWindup), so a cancelled wind-up leaves it full.</summary>
        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;
            if (!IsReady)
                return false;

            windingUp = true;
            windupStartedAt = Time.time;
            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        public override void OwnerTick(float deltaTime, bool held, CastBlock actorBlock)
        {
            if (!windingUp)
                return;
            RaybeamWindupStep step = RaybeamWindup.Step(Time.time - windupStartedAt, windupSeconds, actorBlock);
            if (step != RaybeamWindupStep.Waiting)
                EndWindup(step);
        }

        /// <summary>A death, a stun or an unequip ends the wind-up; a silence does not (RaybeamWindup.InterruptCancels). Died and Unequipped
        /// reach every client, Stunned only the owner, whose cancel phase hides the hexagon everywhere else.</summary>
        public override void Interrupt(InterruptReason reason)
        {
            if (!RaybeamWindup.InterruptCancels(reason))
                return;
            if (windingUp)
                EndWindup(RaybeamWindupStep.Cancel);
            if (charge != null)
                charge.End();
        }

        private void EndWindup(RaybeamWindupStep end)
        {
            windingUp = false;
            if (RaybeamWindup.SpendsUltimate(end) && Owner.UltimateCharge != null && Owner.UltimateCharge.Spend())
                SendPhase(PhaseFire, BeamPayload(CurrentContext()));
            else
                SendPhase(PhaseCancel, default);
        }

        /// <summary>
        /// Origin is the MUZZLE (ctx.Muzzle, the wall-safe one), not the body: three beams from the gun read better than
        /// from the chest, and SafeMuzzlePosition keeps it off the inside of a wall the caster is pressed against.
        /// Point is the cursor's ground point with Y set to muzzle height (a ground point would angle every beam into the
        /// floor), clamped to Beam Range from the muzzle.
        /// </summary>
        private CastPayload BeamPayload(in CastContext ctx)
        {
            Vector3 origin = ctx.Muzzle;

            Vector3 rawPoint = ctx.TargetPoint;
            rawPoint.y = origin.y; // A ground point sends beams into the floor.

            Vector3 toRaw = rawPoint - origin;
            float rawDistance = toRaw.magnitude;
            Vector3 point = (rawDistance > beamRange && rawDistance > 0.0001f)
                ? origin + toRaw / rawDistance * beamRange
                : rawPoint;

            return new CastPayload { Origin = origin, Direction = ctx.AimDirection, Point = point };
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            switch (cast.Phase)
            {
                case PhaseWindup:
                    ShowCharge(cast);
                    return;
                case PhaseFire:
                    if (charge != null)
                        charge.End();
                    FireBeams(cast);
                    return;
                case PhaseCancel:
                    if (charge != null)
                        charge.End();
                    return;
            }
        }

        private void ShowCharge(in CastEvent cast)
        {
            if (!chargeBuilt)
            {
                chargeBuilt = true; // one try: a theme without its look warns once, not on every press
                UiTheme theme = Owner != null && Owner.Health != null ? Owner.Health.Theme : null;
                charge = RaybeamChargeView.TryCreate(theme, Definition.Id, Owner?.PhotonView, Owner?.Weapon, Owner?.Root.transform);
            }
            if (charge != null)
                charge.Begin(Time.time - cast.SecondsLate, windupSeconds, cast.CasterTeam);
        }

        private void FireBeams(in CastEvent cast)
        {
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

            lineFrom[0] = left;
            lineFrom[1] = centre;
            lineFrom[2] = right;
            for (int i = 0; i < lineFrom.Length; i++)
                lineTo[i] = FireOneBeam(lineFrom[i], convergePoint, direction, casterActor, casterTeam);
            DrawBeam(origin, direction, casterTeam);
        }

        /// <summary>
        /// One beam: resolve who it crosses, apply the debuff to each once, scorch where it stops; returns where it ended. No
        /// ApplyDamage at all (zero damage; SonicPulseAbility likewise carries no dead "damage" field).
        /// </summary>
        private Vector3 FireOneBeam(Vector3 beamOrigin, Vector3 point, Vector3 fallbackDirection,
                                 int casterActor, int casterTeam)
        {
            Vector3 aimDirection = RaybeamGeometry.AimFromOriginToPoint(beamOrigin, point, fallbackDirection);

            int count = Physics.SphereCastNonAlloc(beamOrigin, beamWidth * 0.5f, aimDirection, hitBuffer,
                                                   beamRange, BuildMask(), QueryTriggerInteraction.Ignore);

            contactBuffer.Clear();
            normalBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null)
                    continue;

                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                contactBuffer.Add(new BeamContact(hit.distance, target, hit.point, target is IStructure));
                normalBuffer.Add(hit.normal);
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

            RaybeamLook.ScorchSpot(contactBuffer, normalBuffer, beam, end, aimDirection, out Vector3 scorchAt, out Quaternion scorchFacing);
            if (RaybeamLook.ShowsScorch(RaybeamLook.EndsOnSurface(beam), TeamSight.ShotShownAt(casterTeam, scorchAt)) &&
                scorchVfx != null && VfxManager.Instance != null)
                VfxManager.Instance.PlayVFX(scorchVfx, scorchAt, scorchFacing);
            return end;
        }

        /// <summary>Hitscan.BuildMask's identical reasoning: the designer's layers minus the three
        /// invariants (Bullet, DeadPlayer, Barrier) that are never negotiable for any hit-detecting
        /// shot - Raybeam always passes a jersey barrier (GDD p.29), whatever its own hitMask ticks.</summary>
        private int BuildMask() => HitMasks.StripNonNegotiableLayers(hitMask);

        /// <summary>A local, throwaway effect on each client, matching Hitscan.DrawBeam - never a
        /// networked object, since every client draws its own copy from the same cast event.</summary>
        private void DrawBeam(Vector3 origin, Vector3 aim, int casterTeam)
        {
            if (beamVfx == null)
                return;

            if (!RaybeamLook.BeamShown(lineFrom, lineTo, (a, b) => TeamSight.ShotShownAlong(casterTeam, a, b)))
                return;

            GameObject beam = Instantiate(beamVfx, origin, Quaternion.identity);
            ShowBeam(beam, origin, aim, lineFrom, lineTo, casterTeam);
            Destroy(beam, beamVisualSeconds);
        }

        /// <summary>Lays the drawn beam (an instantiated clone, never the prefab asset) over the three lines, each exactly as wide as the
        /// line that hits (beamWidth is the one home). A beam prefab without the Raybeam's own view gets a plain centre line.</summary>
        internal void ShowBeam(GameObject beam, Vector3 origin, Vector3 aim, Vector3[] from, Vector3[] to, int casterTeam)
        {
            if (beam.TryGetComponent(out RaybeamBeamView view))
            {
                view.Draw(origin, aim, from, to, beamWidth, casterTeam);
                return;
            }

            LineRenderer line = beam.GetComponentInChildren<LineRenderer>();
            if (line == null)
                return;
            int middle = from.Length / 2;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = beamWidth;
            line.positionCount = 2;
            line.SetPosition(0, from[middle]);
            line.SetPosition(1, to[middle]);
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"+{ShopNumberFormat.Compact(vulnerabilityPerBeam * 100f)}% damage taken per beam, for {ShopNumberFormat.Compact(vulnerabilitySeconds)}s",
                                   $"Range {ShopNumberFormat.Compact(beamRange)}m");
    }
}
