using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A short burst of movement - Tudor, 2026-09-21: "make the dash go towards the player movement
    /// direction (like using wasd and where the player is currently moving to dash that way) and
    /// keep the blink at cursor location", reversing his own 2026-09-13 call (below) that a dash
    /// always aims at the cursor regardless of WASD. While a movement key is held past
    /// MovementThreshold the dash goes exactly that way - camera-relative, the direction the player
    /// is ALREADY walking (AbilityRunner.BuildContext, off PlayerMotor.MovementInput()), not a fresh
    /// read of the keys. Standing still falls back to 2026-09-13's original rule, unchanged: toward
    /// the ground point under the cursor, or the way you're facing once the cursor sits on (or right
    /// next to) you, where "toward it" would not mean anything. See DashDirectionRule for the actual
    /// choice, pulled into its own pure class so it is tested without a scene. Blink
    /// (BlinkAbility) is unaffected by any of this - it still always lands at the cursor. The first
    /// real ability built on the framework (Task 1.6a) - Blink and Sprint (the rest of Task 1.6) come
    /// later on the same base.
    ///
    /// The travel itself belongs to PlayerDisplacement, not this class: a dash is just a Voluntary
    /// request to the one shared mover, so it automatically loses to a knockback in flight and
    /// automatically cancels on death the same way every other displacement does. This module only
    /// owns what makes it a DASH rather than a generic shove - the numbers, the damage-reduction
    /// buff while travelling, and the remote trail.
    ///
    /// Owner.Displacement is typed IDisplaceable (Overpower.Abilities.AbilityOwner) so that any
    /// future ability can shove ANY IDamageable generically, the caster's own body included -
    /// DisplaceVoluntary and Cancel are PlayerDisplacement-specific (a dash can only ever move ITS
    /// OWN caster, never an arbitrary target), so this module casts down to the concrete type once
    /// it already knows Owner.Displacement is the caster's own player.
    /// </summary>
    public sealed class DashAbility : AbilityModule
    {
        [Header("Movement")]
        [SerializeField, Tooltip("How far one dash covers, in metres.")]
        private float distance = 3f;

        [SerializeField, Tooltip("How fast the dash covers that distance, in metres per second - " +
                 "higher finishes the same distance sooner. This is the dash's own travel speed, " +
                 "not PlayerMotor's walking speed, which a dash never touches.")]
        private float travelSpeed = 18f;

        [SerializeField, Tooltip("Fraction of incoming damage removed while the dash is travelling, " +
                 "0..1. Set to 0 by Tudor's combat revamp - a dash-in grants no extra survivability " +
                 "today - but the field stays so turning it back on is a number, not a code change.")]
        [Range(0f, 1f)]
        private float damageReduction = 0f;

        [Header("Remote trail (cosmetic only)")]
        [SerializeField, Tooltip("TrailRenderer shown on every OTHER client's screen while this " +
                 "dash travels, so a fast dash still reads clearly at a distance. The caster's own " +
                 "screen already shows the real movement and draws no trail of its own.")]
        private TrailRenderer remoteTrail;

        // Not a design tunable: the radius, in metres, inside which "toward the cursor" stops
        // meaning anything because the cursor is essentially standing on the player. Below this the
        // dash falls back to the aim direction instead of normalizing a near-zero vector. Only
        // consulted while standing still - see MovementThreshold below.
        private const float CursorOnSelfThreshold = 0.1f;

        // Not a design tunable: how big ctx.MoveDirection must be - in the same 0..1 units
        // Vector3.ClampMagnitude(owner.Motor.MovementInput(), 1f) produces - before a held movement
        // key is trusted over the cursor. Below this, a tiny stick drift (or a key released this
        // same frame) reads as standing still instead of sending the dash off in a near-arbitrary
        // direction. Tudor, 2026-09-21 (the WASD-follows-movement change).
        private const float MovementThreshold = 0.1f;

        // Owner only: which move is currently in flight, so a Stunned/Died/Unequipped interrupt
        // cancels THIS ability's own displacement and never someone else's Forced move that
        // happens to be running at the same moment - see PlayerDisplacement's priority rules.
        private bool travelling;
        private Vector3 travelStart;

        // Every client: counts down the remote trail so it turns off on its own without a second
        // network message - see PlayResolveTrail below.
        private float remoteTrailSecondsLeft;

        public override bool IsActive => travelling;

        /// <summary>Review fix: a dash refused for the wall you're touching, or a knockback that's running, is
        /// worth re-asking about for the rest of the press-buffer window - unlike most refusals, the reason can
        /// clear a few frames later (you turn to face open ground, or the knockback ends) without a fresh key
        /// press. Silent otherwise in a build (LogDashRefused is Editor-only): this is what actually makes mashing
        /// dash against a wall, then turning away, still dash.</summary>
        internal override bool RetriesRefusalWithinBuffer => true;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            // No PlayerDisplacement on this player (should not happen once Task 1.6a is on the
            // prefab, but a module must never assume another component exists) - refuse rather
            // than cast into nothing.
            if (Owner.Displacement == null)
                return false;

            // Tudor's decision [T], reversed 2026-09-21 (was: always toward the cursor, ctx.MoveDirection
            // never read at all - 2026-09-13). While a movement key is held past MovementThreshold the
            // dash goes exactly that way; standing still keeps the original rule - toward the ground
            // point under the cursor, falling back to the aim direction only when the cursor is on top
            // of the player (or too close for "toward it" to mean anything) - CursorOnSelfThreshold is
            // that radius, not a design tunable, so it stays a constant rather than a Dash-specific
            // Inspector field. DashDirectionRule.Choose is the actual decision, pulled out pure so it is
            // tested without a scene; TryBuildCast only builds the raw Origin->TargetPoint vector before
            // handing it over (MoveDirection and AimDirection already arrive flat).
            Vector3 towardCursor = ctx.TargetPoint - ctx.Origin;
            Vector3 dir = DashDirectionRule.Choose(ctx.MoveDirection, towardCursor, ctx.AimDirection,
                                                    MovementThreshold, CursorOnSelfThreshold);
            payload = new CastPayload { Origin = ctx.Origin, Direction = dir };

            // Movement step 2: a dash that cannot move - the player is touching or pressed into a wall that way, or a
            // knockback owns the body - is refused HERE, before AbilityRunner spends the charge (the same reason
            // BlinkAbility checks CanTeleport up front). It used to spend a charge and, from against a thin wall,
            // carry the player through it.
            if (Owner.Displacement is PlayerDisplacement self && !self.CanStartVoluntary(payload.Direction))
            {
                LogDashRefused(payload.Direction);
                payload = default;
                return false;
            }

            return true;
        }

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.IsCasterClient)
                StartTravel(cast.Payload.Direction);
            else
                PlayRemoteTrail();
        }

        public override void Interrupt(InterruptReason reason)
        {
            if (reason != InterruptReason.Stunned && reason != InterruptReason.Died && reason != InterruptReason.Unequipped)
                return; // Silenced does not stop a dash - it is not a weapon and spends no heat.

            // Guarded on travelling rather than cancelling unconditionally: if a Forced move
            // (knockback) had already pre-empted this dash, travelling would already be false -
            // see PlayerDisplacement's priority rules. This is what keeps a stun from ever
            // cancelling someone else's knockback.
            if (!travelling)
                return;

            (Owner.Displacement as PlayerDisplacement)?.Cancel();
        }

        private void Update()
        {
            // Every client, including the caster: counts the remote trail down so it switches off
            // on its own instead of needing a second network message when the dash ends.
            if (remoteTrailSecondsLeft <= 0f)
                return;

            remoteTrailSecondsLeft -= Time.deltaTime;
            if (remoteTrailSecondsLeft <= 0f && remoteTrail != null)
                remoteTrail.emitting = false;
        }

        private void StartTravel(Vector3 direction)
        {
            var displacement = Owner.Displacement as PlayerDisplacement;
            if (displacement == null)
                return; // TryBuildCast already refused this on the owner; a stale build elsewhere should not throw.

            if (damageReduction > 0f)
                Owner.Status?.AddDamageReduction(this, damageReduction);

            travelling = true;
            travelStart = Owner.Root.transform.position;

            bool started = displacement.DisplaceVoluntary(direction, distance, travelSpeed, HandleDisplaceEnd);
            if (!started)
            {
                // A Forced move (knockback) beat this dash to the mover between TryBuildCast and
                // here - extremely unlikely given both run on the same frame, but the buff must not
                // be left stuck on if it happens.
                travelling = false;
                if (damageReduction > 0f)
                    Owner.Status?.RemoveDamageReduction(this);
            }
        }

        private void HandleDisplaceEnd(DisplaceEnd end)
        {
            travelling = false;
            if (damageReduction > 0f)
                Owner.Status?.RemoveDamageReduction(this);

            LogDash(Vector3.Distance(travelStart, end.EndPosition), end.Outcome);
        }

        private void PlayRemoteTrail()
        {
            if (remoteTrail == null)
                return;

            remoteTrail.Clear();
            remoteTrail.emitting = true;
            remoteTrailSecondsLeft = travelSpeed > 0f ? distance / travelSpeed : 0f;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDash(float travelled, DisplaceOutcome outcome)
        {
            Debug.Log($"[DASH] dist={travelled:F2} outcome={outcome}");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDashRefused(Vector3 direction)
        {
            Debug.Log($"[DASH] refused dir={direction:F2} (a wall at the start, or a knockback is running)");
        }
    }
}
