using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// Knocks every enemy standing in a short forward cone straight away from the caster - Tudor's
    /// Equipment spec: 5m of knockback in a 4m cone, 8s cooldown. An enemy whose flight ends against
    /// a wall or another player is stunned for 2s, and so is whichever player it landed on - but
    /// only if that second player is ALSO an enemy of the caster (Task 1.10 addendum's own "Holes"
    /// section: the brief wins over the plan's "wall or enemy" wording, and there is no "both" for a
    /// plain wall). Zero damage - this is pure crowd control, the identical "utility, not a weapon"
    /// shape as the stun gun and the zip gun.
    ///
    /// RUNS ON EVERY CLIENT, INCLUDING THE CASTER'S OWN, exactly once per cast (Phase 0) - unlike the
    /// flamethrower's spray, a pulse is instantaneous, so there is no coroutine ticking a cone over
    /// several frames, just one OverlapSphere/cone-filter/push pass.
    ///
    /// ORIGIN IS THE BODY, NOT THE MUZZLE (the addendum's own wording: "the cone's apex, not the
    /// muzzle"). Both the cone check and the occlusion check below use ctx.Origin - unlike the
    /// flamethrower, which deliberately splits root (range/angle) from muzzle (occlusion), the
    /// addendum gives sonic pulse only one point to work from, so both use it.
    ///
    /// THE PUSH ITSELF IS OWNER-AUTHORITATIVE, LIKE EVERY DISPLACEMENT. This module never moves
    /// anyone directly - it calls IDisplaceable.Displace on every eligible target, on every client,
    /// and PlayerDisplacement/DummyTarget's own guards (IsMine, or "always my own authority" for a
    /// dummy) make sure only the victim's own machine actually starts moving. That is also why the
    /// push direction below is safe to compute identically everywhere: KnockbackResolver.
    /// ComputePushDirection reads the CANDIDATE'S OWN POSITION AS SEEN BY THIS CLIENT, which only
    /// matters on the one client where Displace is not a no-op - the victim's owner, where "as seen
    /// by this client" and "the victim's true position" are the same thing. Every other client
    /// computes a direction from a synced (and therefore slightly stale) position too, but throws it
    /// away the moment Displace no-ops, so that staleness never reaches anyone.
    ///
    /// THE COLLISION DECISION RUNS ONLY ON THE VICTIM'S OWNER (the addendum's own wording) - because
    /// onEnd only ever fires on the machine that actually started the move, for the identical reason
    /// the push itself only actually happens there. Self-stun is free (IStatusReceiver.ApplyStatus is
    /// itself owner-guarded, or unconditional for a dummy); stunning a PLAYER blocker who is on a
    /// DIFFERENT machine needs the one peer RPC Task 1.0's S4 built for exactly this
    /// (PlayerStatusEffects.RequestOnOwner) - a dummy blocker is always "this machine's own", per
    /// IDamageable.HasLocalAuthority, so it never needs that trip.
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

        [SerializeField, Tooltip("How many seconds a collision stuns for - the victim always, and " +
                 "whatever it collided with too if that is an enemy of the caster. Tudor's spec: 2.")]
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
                    continue; // Behind a wall or cover - the addendum's own occlusion rule.

                IDisplaceable displaceable = (candidate.Target as Component)?.GetComponentInParent<IDisplaceable>();
                if (displaceable == null)
                    continue; // Has health but nothing to push - should not happen for a player or a dummy.

                Vector3 pushDirection = KnockbackResolver.ComputePushDirection(origin, candidate.Position, forward);
                IDamageable victim = candidate.Target;

                // The victim reference is captured by this specific closure, not shared across
                // iterations - C#'s per-iteration foreach variable makes that safe without an extra
                // local copy.
                displaceable.Displace(pushDirection, knockbackDistance, knockbackSpeed,
                    end => HandlePushEnd(end, victim, casterActor, casterTeam));
            }
        }

        /// <summary>True when a Building-layer collider (a wall, or cover) stands between origin and
        /// the target - same idea as FlamethrowerAbility.IsOccludedByWall, but from the caster's body
        /// rather than the muzzle, per the addendum's single-point wording for this ability.</summary>
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
    }
}
