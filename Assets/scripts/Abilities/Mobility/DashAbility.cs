using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A short burst of movement toward the cursor - Tudor's decision, superseding the brief's
    /// "along your held movement key": a dash always aims at the ground point under the cursor, WASD
    /// held or not, and only falls back to the way you are facing when the cursor sits on (or right
    /// next to) you, where "toward the cursor" would not mean anything. The first real ability built
    /// on the framework (Task 1.6a) - Blink and Sprint (the rest of Task 1.6) come later on the same
    /// base.
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
        // dash falls back to the aim direction instead of normalizing a near-zero vector.
        private const float CursorOnSelfThreshold = 0.1f;

        // Owner only: which move is currently in flight, so a Stunned/Died/Unequipped interrupt
        // cancels THIS ability's own displacement and never someone else's Forced move that
        // happens to be running at the same moment - see PlayerDisplacement's priority rules.
        private bool travelling;
        private Vector3 travelStart;

        // Every client: counts down the remote trail so it turns off on its own without a second
        // network message - see PlayResolveTrail below.
        private float remoteTrailSecondsLeft;

        public override bool IsActive => travelling;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            // No PlayerDisplacement on this player (should not happen once Task 1.6a is on the
            // prefab, but a module must never assume another component exists) - refuse rather
            // than cast into nothing.
            if (Owner.Displacement == null)
                return false;

            // Tudor's decision [T]: dash always goes toward the cursor, never along whatever WASD
            // key happens to be held - ctx.MoveDirection is not read at all. Flattened because
            // TargetPoint is a ground point and a dash never gains or loses height. Falls back to
            // the aim direction only when the cursor is on top of the player (or too close for
            // "toward it" to mean anything) - CursorOnSelfThreshold is that radius, not a design
            // tunable, so it stays a constant rather than a Dash-specific Inspector field.
            Vector3 towardCursor = ctx.TargetPoint - ctx.Origin;
            towardCursor.y = 0f;
            Vector3 dir = towardCursor.sqrMagnitude > CursorOnSelfThreshold * CursorOnSelfThreshold
                ? towardCursor
                : ctx.AimDirection;
            payload = new CastPayload { Origin = ctx.Origin, Direction = dir.normalized };
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
    }
}
