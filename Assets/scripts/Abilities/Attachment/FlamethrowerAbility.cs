using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Vision;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// Sprays a short forward cone that ignites every enemy it touches. Contact starts the burn; the burn then ticks on
    /// its own through StatusEffectState/PlayerStatusEffects (Refresh stack rule) and does NOT need the target to stay in
    /// the cone, the difference between a usable ability and an unusable one in a top-down game.
    /// RUNS ON EVERY CLIENT, THE CASTER'S INCLUDED: ExecuteCast starts a Spray Seconds coroutine on every machine; each
    /// FixedUpdate it re-reads the CASTER's CURRENT position and facing from Owner.Root.transform, never the payload's
    /// Origin/Direction (a snapshot of the press), so the spray tracks the aim. Owner already IS the caster on every
    /// client (this module was Equipped onto the caster's Abilities child, AbilityRunner.Equip); facing is kept by
    /// PlayerAim and replicated by PlayerNetSync.
    /// RANGE/ANGLE USE THE ROOT; OCCLUSION USES THE MUZZLE: the muzzle sits ~2 m forward of the root, so measuring range
    /// from it let a target outside the cone still read as in range and keep burning (found by measurement).
    /// ONCE PER TARGET PER CAST, VIA A HASHSET: without it, re-applying every physics step would re-Refresh the burn on a
    /// standing target for the whole spray, so it would burn well past Burn Seconds. The set lives on the coroutine's
    /// stack, fresh per cast. ConeFilter.SelectCandidates only READS it; this class marks a target hit AFTER the occlusion
    /// check, so a target behind cover can still catch fire the moment it steps into the open.
    /// WALLS AND COVER BOTH BLOCK IT: one raycast against the Building layer, which every arena wall and Deployable Cover's
    /// collider sit on. IStructure candidates (cover) are excluded before that check (ConeFilter, like MineTargeting): its
    /// fails-open -1/-1 identity would read as "an enemy with no known team", and it has no IStatusReceiver to burn.
    /// INTERRUPT(DIED) STOPS THE SPRAY ON EVERY CLIENT (AbilityRunner.HandleAliveChanged calls Interrupt on all of them).
    /// Stunned/Silenced are deliberately NOT handled: AbilityModule delivers those on the OWNER only, and stopping the
    /// spray there alone would make a stun look different on the caster's screen than on everyone else's.
    /// The caster can still fire their weapon while spraying: only the Attachment slot and its own cooldown are used.
    /// </summary>
    public sealed class FlamethrowerAbility : AbilityModule
    {
        [Header("Burn")]
        [SerializeField, Tooltip("Damage per second the burn deals, routed through the normal damage " +
                 "funnel so armor absorbs it like any other hit. Tudor's spec: 5.")]
        private float burnDamagePerSecond = 5f;

        [SerializeField, Tooltip("How many seconds the burn lasts once applied - it keeps ticking even " +
                 "after the target leaves the cone. Tudor's spec: 5. Landing it again before it expires " +
                 "REFRESHES the full duration rather than stacking a second one (StatusEffectState's " +
                 "Refresh rule for Burn), so two overlapping sprays still deal 5 damage per second, " +
                 "never 10.")]
        private float burnSeconds = 5f;

        [Header("Cone")]
        [SerializeField, Tooltip("Full angle of the spray cone, in degrees, measured on the ground " +
                 "plane - 45 means 22.5 degrees either side of wherever the caster is currently " +
                 "facing. Controller's call.")]
        private float coneAngle = 45f;

        [SerializeField, Tooltip("How far the spray reaches, in metres, measured on the ground plane " +
                 "from the caster's own position (their root, not the muzzle - see TickCone). Controller's call.")]
        private float coneRange = 7f;

        [SerializeField, Tooltip("How long the spray stays live once cast, in seconds - the cone is " +
                 "re-checked every physics step for this long and then stops on its own. The burn " +
                 "already applied keeps ticking for its own Burn Seconds well past this. Controller's call.")]
        private float spraySeconds = 1f;

        [SerializeField, Tooltip("Which layers count as a target. Default is where a living player or " +
                 "a practice dummy sits; widening this only costs performance, since anything without " +
                 "an IDamageable is skipped anyway.")]
        private LayerMask detectionMask = ~0;

        [Header("VFX (cheap, cosmetic only)")]
        [SerializeField, Tooltip("Colour of the flame cone drawn on the floor on every client for the " +
                 "duration of Spray Seconds (its per-vertex opacity is on the cone prefab). Nothing " +
                 "about the burn itself depends on it.")]
        private Color vfxColor = new Color(1f, 0.45f, 0.1f, 1f);

        [SerializeField, Tooltip("The soft flame cone - Assets/Gameplay/Abilities/Flamethrower Cone.prefab. Drawn from Cone Angle and " +
                 "Cone Range above, with its tip under the caster, so it always shows exactly the area the burn " +
                 "checks (a soft tip-to-edge gradient, not a hard outline). Visual only.")]
        private FlameConeVisual sprayVfxPrefab;

        // Not a design tunable: how many overlapping colliders one cone check considers - matches
        // Mine.MaxOverlapColliders' reasoning, comfortably every player plus every practice dummy within Cone Range.
        private const int MaxOverlapColliders = 16;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Reused every tick rather than allocated fresh, the same reason Mine.cs keeps its own
        // overlapBuffer/caught as fields instead of locals.
        private readonly HashSet<IDamageable> seenThisTick = new HashSet<IDamageable>();
        private readonly List<ConeCandidate> candidateBuffer = new List<ConeCandidate>();

        // Not a design tunable: walls and cover occlude a spray whatever this module's own Detection
        // Mask is set to catch - see the class comment. Computed once since NameToLayer never changes
        // at runtime, the same trick ExplodeOnImpact.buildingMask uses.
        private int buildingMask;

        private Coroutine sprayCoroutine;
        private FlameConeVisual vfx;

        public override bool IsActive => sprayCoroutine != null;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer("Building");
            buildingMask = layer >= 0 ? 1 << layer : 0;
        }

        private void OnDestroy()
        {
            // The module prefab has no Collider (AbilityDefinition.Validate forbids one), so the VFX
            // is a separate, unparented GameObject (see ShowVfx) - it must be cleaned up explicitly
            // here, or unequipping/re-equipping the flamethrower would leak one every time.
            if (vfx != null)
                Destroy(vfx.gameObject);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            burnDamagePerSecond = Mathf.Max(0f, burnDamagePerSecond);
            burnSeconds = Mathf.Max(0f, burnSeconds);
            coneAngle = Mathf.Clamp(coneAngle, 0f, 360f);
            coneRange = Mathf.Max(0f, coneRange);
            spraySeconds = Mathf.Max(0f, spraySeconds);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        public override void Interrupt(InterruptReason reason)
        {
            if (reason != InterruptReason.Died)
                return; // See the class comment for why only Died is handled here.

            StopSpray();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0)
                return;

            if (sprayCoroutine != null)
                StopCoroutine(sprayCoroutine); // A re-press restarts the spray rather than running two at once.

            sprayCoroutine = StartCoroutine(SprayRoutine(cast.CasterActor, cast.CasterTeam));
        }

        private IEnumerator SprayRoutine(int casterActor, int casterTeam)
        {
            // Fresh per cast, on purpose - see the class comment on "once per target per cast".
            var alreadyHit = new HashSet<IDamageable>();
            var burn = new StatusEffectSpec { kind = StatusKind.Burn, duration = burnSeconds, magnitude = burnDamagePerSecond, abilityId = Definition.Id };

            ShowVfx();

            float elapsed = 0f;
            while (elapsed < spraySeconds)
            {
                TickCone(casterActor, casterTeam, burn, alreadyHit);
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
            }

            HideVfx();
            sprayCoroutine = null;
        }

        private void TickCone(int casterActor, int casterTeam, in StatusEffectSpec burn, HashSet<IDamageable> alreadyHit)
        {
            // Range and angle are measured from the caster's ROOT; only the occlusion check uses the muzzle. Using the
            // muzzle for both measured wrong: it sits ~2 m forward of the root, so a target outside the cone read in range.
            //
            // On a NON-OWNER client this reads the caster's network-lerped Transform (PlayerAim rotates, PlayerNetSync
            // replicates), a little behind the caster's own view: the accepted victim-favouring latency tradeoff every
            // projectile lives with (ProjectileMotor). The VICTIM's client still decides whether the burn lands
            // (IStatusReceiver is owner-guarded), so a laggy copy of the caster cannot burn someone their own client missed.
            Vector3 apex = Owner.Root.transform.position;
            Vector3 forward = Owner.Root.transform.forward;
            // SafeMuzzlePosition, not MuzzlePosition: hugging a wall pushes the raw muzzle inside it, and a Raycast started
            // inside a collider never reports it, so the occlusion check would read the wall as clear (it leaked through a
            // wall and a Deployable Cover). The clearance check pulls the point back to the near side of the wall.
            Vector3 muzzle = Owner.Weapon != null ? Owner.Weapon.SafeMuzzlePosition : apex;

            PositionVfx(apex, forward);

            int count = Physics.OverlapSphereNonAlloc(apex, coneRange, overlapBuffer, detectionMask,
                                                       QueryTriggerInteraction.Ignore);

            seenThisTick.Clear();
            candidateBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                // A body is several colliders to Physics - seenThisTick keeps each target to one
                // ConeCandidate per tick, the same reasoning as Mine.cs's own caught HashSet.
                if (candidate == null || !seenThisTick.Add(candidate))
                    continue;

                candidateBuffer.Add(new ConeCandidate(candidate, collider.transform.position));
            }

            List<ConeCandidate> eligible = ConeFilter.SelectCandidates(candidateBuffer, apex, forward, coneRange,
                                                                        coneAngle, casterActor, casterTeam, alreadyHit);

            foreach (ConeCandidate candidate in eligible)
            {
                if (IsOccludedByWall(muzzle, candidate.Position))
                    continue; // Behind a wall or cover - not marked hit, so it can still catch fire once it steps clear.

                IStatusReceiver receiver = (candidate.Target as Component)?.GetComponentInParent<IStatusReceiver>();
                if (receiver == null)
                    continue; // Has health but nothing to burn - should not happen once IStructure is excluded.

                receiver.ApplyStatus(burn, casterActor);
                alreadyHit.Add(candidate.Target); // From here on, this cast never re-applies to this target.
            }
        }

        /// <summary>True when a Building-layer collider (a wall, or cover) stands between the muzzle and the target. Like
        /// ExplodeOnImpact.IsOccludedByAWall, minus the struck-collider exclusion: a cone check has no impact point.</summary>
        private bool IsOccludedByWall(Vector3 muzzle, Vector3 targetPosition)
        {
            Vector3 delta = targetPosition - muzzle;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
                return false;

            return Physics.Raycast(muzzle, delta / distance, distance, buildingMask, QueryTriggerInteraction.Ignore);
        }

        private void StopSpray()
        {
            if (sprayCoroutine == null)
                return;

            StopCoroutine(sprayCoroutine);
            sprayCoroutine = null;
            HideVfx();
        }

        // ---- VFX (cheap, cosmetic only - see the Inspector tooltip) --------------------------------

        private void ShowVfx()
        {
            if (vfx == null)
            {
                if (sprayVfxPrefab == null)
                    return; // Nothing to draw; the burn works the same without it.

                // Unparented: the module sits under the player, whose hierarchy moves to the
                // DeadPlayer layer on death. OnDestroy removes it.
                vfx = Instantiate(sprayVfxPrefab);
                vfx.name = "Flamethrower Cone VFX (cheap, cosmetic only)";
            }

            // Re-read on every spray, so an Inspector change to Cone Angle/Range shows on the next cast.
            // Owner.IsMine (A3): the faint aiming outline is only ever drawn on the caster's OWN screen -
            // everyone else sees just the soft fan, never a hard edge.
            vfx.Configure(coneRange, coneAngle, vfxColor, Owner.IsMine);
            vfx.gameObject.SetActive(true);
            // Vision: the cone is hidden with the enemy who sprays it, checked every frame (friendly: always).
            // It is also shown while the cone reaches any of my team (the fire hurts them, so they must see it).
            VisibleWhenSeen gate = VisibleWhenSeen.AttachToCaster(vfx.gameObject, Owner.PhotonView);
            if (gate != null)
                gate.SetCone(coneRange, coneAngle);
        }

        private void PositionVfx(Vector3 apex, Vector3 forward)
        {
            if (vfx != null)
                vfx.Place(apex, forward);
        }

        private void HideVfx()
        {
            if (vfx != null)
                vfx.gameObject.SetActive(false);
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"Burn {ShopNumberFormat.Compact(burnDamagePerSecond)} damage/s for {ShopNumberFormat.Compact(burnSeconds)}s",
                                   $"Range {ShopNumberFormat.Compact(coneRange)}m · {ShopNumberFormat.Compact(coneAngle)}° cone · sprays {ShopNumberFormat.Compact(spraySeconds)}s");
    }
}
