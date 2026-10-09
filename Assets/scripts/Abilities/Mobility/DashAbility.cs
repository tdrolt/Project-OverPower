using UnityEngine;
using Overpower.Vision;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// A short burst of movement along the held WASD direction (DashDirectionRule), else toward the cursor. The travel
    /// belongs to PlayerDisplacement: a dash is a Voluntary request to the one shared mover, so it loses to a knockback in
    /// flight and cancels on death like every displacement. This module owns what makes it a DASH: the numbers, the
    /// damage-reduction buff while travelling and the remote trail. Owner.Displacement is typed IDisplaceable, so DisplaceVoluntary
    /// and Cancel need the cast down to PlayerDisplacement - safe because a dash only ever moves its own caster.
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

        [SerializeField, Tooltip("After you use up every Dash charge, Dash stays locked until this many " +
                 "charges have come back. Using fewer than all of them never locks it. 0 or 1 means no " +
                 "lock-out at all.")]
        [Min(0)]
        private int chargesNeededAfterRunningDry = 2;

        [Header("Remote trail (cosmetic only)")]
        [SerializeField, Tooltip("TrailRenderer shown on every OTHER client's screen while this " +
                 "dash travels, so a fast dash still reads clearly at a distance. The caster's own " +
                 "screen already shows the real movement and draws no trail of its own.")]
        private TrailRenderer remoteTrail;

        // Not a design tunable: the radius, in metres, inside which the cursor is essentially on the player, so the dash
        // falls back to the aim direction instead of normalizing a near-zero vector. Only used while standing still.
        private const float CursorOnSelfThreshold = 0.1f;

        // Not a design tunable: how big ctx.MoveDirection must be (0..1 units, as ClampMagnitude of the motor's
        // MovementInput) before a held movement key is trusted over the cursor. Below it, a tiny stick drift or a key
        // released this frame reads as standing still instead of sending the dash in a near-arbitrary direction.
        private const float MovementThreshold = 0.1f;

        // Owner only: whether THIS ability's move is in flight, so a Stunned/Died/Unequipped interrupt never cancels
        // someone else's Forced move running at the same moment - see PlayerDisplacement's priority rules.
        private bool travelling;
        private Vector3 travelStart;

        // Every client: counts down the remote trail so it turns off on its own without a second network message.
        private float remoteTrailSecondsLeft;

        protected override int ChargesNeededAfterRunningDry => chargesNeededAfterRunningDry;

        public override bool IsActive => travelling;

        /// <summary>A dash refused for the wall you're touching, or a knockback that's running, is worth re-asking about
        /// for the rest of the press-buffer window: unlike most refusals, the reason can clear a few frames later (you
        /// turn to face open ground, or the knockback ends) without a fresh key press.</summary>
        internal override bool RetriesRefusalWithinBuffer => true;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            // A module must never assume another component exists: with no PlayerDisplacement, refuse rather than cast
            // into nothing.
            if (Owner.Displacement == null)
                return false;

            // Direction [T]: a held movement key (past MovementThreshold) wins, else toward the cursor, else the aim
            // direction. DashDirectionRule.Choose decides; this only builds the raw Origin->TargetPoint vector for it
            // (MoveDirection and AimDirection already arrive flat).
            Vector3 towardCursor = ctx.TargetPoint - ctx.Origin;
            Vector3 dir = DashDirectionRule.Choose(ctx.MoveDirection, towardCursor, ctx.AimDirection,
                                                    MovementThreshold, CursorOnSelfThreshold);
            payload = new CastPayload { Origin = ctx.Origin, Direction = dir };

            // A dash that cannot move - the player is touching or pressed into a wall that way, or a knockback owns the
            // body - is refused HERE, before AbilityRunner spends the charge (the same reason BlinkAbility checks
            // CanTeleport up front). Otherwise, from against a thin wall, it would carry the player through it.
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

            // Guarded on travelling: if a Forced move (knockback) had already pre-empted this dash it is false already,
            // which keeps a stun from ever cancelling someone else's knockback (PlayerDisplacement's priority rules).
            if (!travelling)
                return;

            (Owner.Displacement as PlayerDisplacement)?.Cancel();
        }

        private void Update()
        {
            // Every client, including the caster: the trail switches off on its own, no second network message.
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
                // A Forced move (knockback) beat this dash to the mover between TryBuildCast and here: very unlikely,
                // but the buff must not be left stuck on.
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

            // Vision: the trail betrays the dasher, so it is shown only while the dasher is (my team's always).
            VisibleWhenSeen.AttachToCaster(remoteTrail.gameObject, Owner.PhotonView);
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

        public override string ShopStatsText() => $"Distance {ShopNumberFormat.Compact(distance)}m";
    }
}
