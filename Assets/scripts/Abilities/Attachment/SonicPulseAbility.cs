using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// Knocks every enemy in a short forward cone straight away from the caster. An enemy whose flight ends against a
    /// wall or another player is stunned, and so is the player it landed on, but only if that second player is ALSO an
    /// enemy of the caster (the brief wins over the plan's "wall or enemy" wording; a plain wall has no "both"). Zero
    /// damage: pure crowd control, like the stun gun and the zip gun.
    /// RUNS ON EVERY CLIENT, THE CASTER'S INCLUDED, once per cast (Phase 0); instantaneous, so one OverlapSphere/
    /// cone-filter/push pass and no coroutine.
    /// ORIGIN IS THE BODY, NOT THE MUZZLE: both the cone and the occlusion check use ctx.Origin, unlike the flamethrower's
    /// root/muzzle split, because the spec gives the pulse one point ("the cone's apex, not the muzzle").
    /// THE PUSH IS OWNER-AUTHORITATIVE, LIKE EVERY DISPLACEMENT: this calls IDisplaceable.Displace on every eligible target
    /// on every client, and PlayerDisplacement/DummyTarget's guards (IsMine, or "always my own authority" for a dummy)
    /// make only the victim's machine move. So the push direction is safe to compute identically everywhere:
    /// KnockbackResolver.ComputePushDirection reads the candidate's position as seen by this client, which matters only on
    /// the victim's owner; other clients compute from a slightly stale position and discard it when Displace no-ops.
    /// THE COLLISION DECISION RUNS ONLY ON THE VICTIM'S OWNER, because onEnd fires only on the machine that started the
    /// move. Self-stun is free (ApplyStatus is owner-guarded, unconditional for a dummy); stunning a PLAYER blocker on
    /// ANOTHER machine needs the peer RPC PlayerStatusEffects.RequestOnOwner; a dummy blocker is always local
    /// (IDamageable.HasLocalAuthority).
    /// </summary>
    public sealed class SonicPulseAbility : AbilityModule
    {
        [Header("Knockback")]
        [SerializeField, Tooltip("How far an enemy caught in the pulse flies, in metres, before " +
                 "stopping on its own (a wall or another player stops it sooner). Tudor's spec: 5.")]
        private float knockbackDistance = 5f;

        [SerializeField, Tooltip("How fast the knockback travels, in metres per second - at the " +
                 "default 5m this is a 0.36s push. Controller's call: fast enough to feel like an " +
                 "impact, not a slow drift.")]
        private float knockbackSpeed = 14f;

        [SerializeField, Tooltip("How far the pulse throws YOU backwards, away from where you aim, in " +
                 "metres, at the same speed as the enemy push. Hitting a wall on the way never stuns " +
                 "you. 0 = off.")]
        private float selfPushDistance = 4f;

        [SerializeField, Tooltip("How many seconds a collision stuns for - the victim always, and " +
                 "whatever it collided with too if that is an enemy of the caster.")]
        private float collisionStunSeconds = 2f;

        [Header("Cone")]
        [SerializeField, Tooltip("How far the pulse reaches, in metres, measured on the ground plane " +
                 "from the caster's own body. Tudor's spec: 4.")]
        private float coneRange = 4f;

        [SerializeField, Tooltip("Full angle of the pulse's cone, in degrees, measured on the ground " +
                 "plane - 90 means 45 degrees either side of wherever the caster is currently " +
                 "facing. Controller's call.")]
        private float coneAngle = 90f;

        [SerializeField, Tooltip("Which layers count as a target. Default is where a living player or " +
                 "a practice dummy sits; widening this only costs performance, since anything without " +
                 "an IDamageable is skipped anyway.")]
        private LayerMask detectionMask = ~0;

        // Not a design tunable: how many overlapping colliders one cone check considers - matches
        // FlamethrowerAbility.MaxOverlapColliders' identical reasoning.
        private const int MaxOverlapColliders = 16;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Reused every cast rather than allocated fresh - FlamethrowerAbility's own reasoning for
        // seenThisTick/candidateBuffer. alreadyHit (ConeFilter.SelectCandidates' third HashSet) is
        // NOT reused: a pulse is a one-shot cast with nothing to deduplicate against a later tick, so
        // it is built fresh - and empty - every ExecuteCast instead.
        private readonly HashSet<IDamageable> collidersSeenThisCast = new HashSet<IDamageable>();
        private readonly List<ConeCandidate> candidateBuffer = new List<ConeCandidate>();

        // Not a design tunable: walls and cover occlude a pulse whatever this module's own Detection
        // Mask is set to catch - same reasoning as FlamethrowerAbility.buildingMask. Computed once
        // since NameToLayer never changes at runtime.
        private int buildingMask;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer("Building");
            buildingMask = layer >= 0 ? 1 << layer : 0;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            knockbackDistance = Mathf.Max(0f, knockbackDistance);
            knockbackSpeed = Mathf.Max(0.01f, knockbackSpeed);
            selfPushDistance = Mathf.Max(0f, selfPushDistance);
            collisionStunSeconds = Mathf.Max(0f, collisionStunSeconds);
            coneRange = Mathf.Max(0f, coneRange);
            coneAngle = Mathf.Clamp(coneAngle, 0f, 360f);
        }

        // ---- owner only ---------------------------------------------------------------------------

        /// <summary>Origin is the caster's BODY (ctx.Origin), not the muzzle - see the class comment.
        /// Direction is the flat aim direction, already normalised by PlayerAim.</summary>
        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Origin = ctx.Origin, Direction = ctx.AimDirection };
            return true;
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0)
                return;

            Vector3 origin = cast.Payload.Origin;
            Vector3 forward = cast.Payload.Direction;
            int casterActor = cast.CasterActor;
            int casterTeam = cast.CasterTeam;

            PushCasterBack(forward, cast.IsCasterClient);

            int count = Physics.OverlapSphereNonAlloc(origin, coneRange, overlapBuffer, detectionMask,
                                                       QueryTriggerInteraction.Ignore);

            collidersSeenThisCast.Clear();
            candidateBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                // A body is several colliders to Physics - collidersSeenThisCast keeps each target to
                // one ConeCandidate, the same reasoning as FlamethrowerAbility's own seenThisTick.
                if (candidate == null || !collidersSeenThisCast.Add(candidate))
                    continue;

                candidateBuffer.Add(new ConeCandidate(candidate, collider.transform.position));
            }

            // Fresh and empty every cast - see the field comment above for why this is never reused.
            List<ConeCandidate> eligible = ConeFilter.SelectCandidates(candidateBuffer, origin, forward,
                coneRange, coneAngle, casterActor, casterTeam, new HashSet<IDamageable>());

            foreach (ConeCandidate candidate in eligible)
            {
                if (IsOccludedByWall(origin, candidate.Position))
                    continue; // Behind a wall or cover.

                IDisplaceable displaceable = (candidate.Target as Component)?.GetComponentInParent<IDisplaceable>();
                if (displaceable == null)
                    continue; // Has health but nothing to push - should not happen for a player or a dummy.

                Vector3 pushDirection = KnockbackResolver.ComputePushDirection(origin, candidate.Position, forward);
                IDamageable victim = candidate.Target;

                // The victim is captured per iteration (C#'s per-iteration foreach variable), so no local copy is needed.
                // A player knows who pushed them (the Dominion respawn bubble refuses an enemy's push, and the pusher's own bubble ends on a push
                // that lands); a dummy has neither.
                System.Action<DisplaceEnd> onEnd = end => HandlePushEnd(end, victim, casterActor, casterTeam);
                if (displaceable is PlayerDisplacement player)
                    player.Displace(pushDirection, knockbackDistance, knockbackSpeed, onEnd, casterActor);
                else
                    displaceable.Displace(pushDirection, knockbackDistance, knockbackSpeed, onEnd);
            }
        }

        /// <summary>
        /// The pulse throws its own caster backwards too, so it doubles as an escape. Only on the
        /// caster's own machine (cast.IsCasterClient): PlayerDisplacement is owner-authoritative and the
        /// move then replicates as the owner's ordinary movement, so no client but this one starts
        /// it and no network traffic is added. Forced priority, because the push must happen even
        /// mid-dash (a Voluntary request would be refused, or would lose to a running Forced move).
        /// The end callback is deliberately empty - unlike an enemy, the caster is never stunned
        /// for hitting a wall on the way (HandlePushEnd is not used).
        /// </summary>
        private void PushCasterBack(Vector3 aimDirection, bool isCasterClient)
        {
            if (!KnockbackResolver.ShouldSelfPush(isCasterClient, aimDirection, selfPushDistance) ||
                Owner == null || Owner.Displacement == null)
                return;

            Vector3 direction = KnockbackResolver.ComputeSelfPushDirection(aimDirection);
            Owner.Displacement.Displace(direction, selfPushDistance, knockbackSpeed, SelfPushEnded);
        }

        // The user is never stunned by their own push (D9), so unlike HandlePushEnd this does nothing.
        private static void SelfPushEnded(DisplaceEnd end) { }

        /// <summary>True when a Building-layer collider (a wall, or cover) stands between origin and
        /// the target - same idea as FlamethrowerAbility.IsOccludedByWall, but from the caster's body rather than the
        /// muzzle (this ability has the one point).</summary>
        private bool IsOccludedByWall(Vector3 origin, Vector3 targetPosition)
        {
            Vector3 delta = targetPosition - origin;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
                return false;

            return Physics.Raycast(origin, delta / distance, distance, buildingMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Runs only on the machine where the push actually happened - the victim's own owner (see
        /// the class comment). end.Blocker is null for a push that ran its full distance (or was
        /// cancelled by a second pulse) - nothing collided, so nobody is stunned.
        /// </summary>
        private void HandlePushEnd(DisplaceEnd end, IDamageable victim, int casterActor, int casterTeam)
        {
            if (end.Blocker == null)
                return;

            var stun = new StatusEffectSpec { kind = StatusKind.Stun, duration = collisionStunSeconds, abilityId = Definition.Id };

            // The victim is stunned by ANY blocker - a wall, cover, or a player, teammate or not.
            // Only the SECOND stun (on whatever the victim hit) depends on who that is.
            IStatusReceiver victimReceiver = (victim as Component)?.GetComponentInParent<IStatusReceiver>();
            victimReceiver?.ApplyStatus(stun, casterActor);

            IDamageable blockerDamageable = end.Blocker.GetComponentInParent<IDamageable>();
            IStatusReceiver blockerReceiver = end.Blocker.GetComponentInParent<IStatusReceiver>();

            if (!KnockbackResolver.ShouldStunBlocker(blockerDamageable, blockerReceiver != null, casterActor, casterTeam))
                return; // A wall, cover, the caster, or one of their teammates - never stunned.

            if (blockerDamageable.HasLocalAuthority)
                blockerReceiver.ApplyStatus(stun, casterActor); // A local dummy - no network trip needed.
            else if (blockerReceiver is PlayerStatusEffects peerStatus)
                peerStatus.RequestOnOwner(stun, casterActor); // A remote player - the one S4 peer RPC.
            else
                blockerReceiver.ApplyStatus(stun, casterActor); // Should not happen: every non-local
                                                                  // IStatusReceiver today is a player.
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"Knockback {ShopNumberFormat.Compact(knockbackDistance)}m",
                                   $"Range {ShopNumberFormat.Compact(coneRange)}m · {ShopNumberFormat.Compact(coneAngle)}° cone",
                                   $"Wall or ally hit stuns {ShopNumberFormat.Compact(collisionStunSeconds)}s");
    }
}
