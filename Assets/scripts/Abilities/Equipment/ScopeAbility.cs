using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Hold right click (the Equipment slot) to pull the camera back further than the scroll zoom allows, so a
    /// sniper-style loadout can actually see what it is shooting at - Tudor, 2026-09-18: "Laser, Charge Laser,
    /// Baseline and Rockets can already hit targets the shooter can't see on screen." Let go and it eases back.
    /// No toggle, no cooldown, no heat cost (charges 0 on this module, Sprint's own convention for "self-limiting
    /// through something other than a cooldown" - here there is no limiter at all, by design), no movement slow,
    /// and nothing shown to other players: carrying Scope in the Equipment slot means giving up mines, cover,
    /// raybeam and so on, and that trade-off alone is the cost.
    ///
    /// OWNER ONLY, NO NETWORK MESSAGE AT ALL. The camera this ability moves exists only on the owner's own
    /// machine (CameraTracking.Instance is that machine's local player's camera - there is nothing for any other
    /// client to move, or even to know about). TryBuildCast always refuses below, which is the one path through
    /// AbilityRunner.TryCast that spends no charge and sends no RPC - a refused press is simply consumed by the
    /// buffer and forgotten, with nothing shown to the player either (see AbilityRunner.Update/TryCast). Every
    /// bit of real behaviour lives in OwnerTick, which AbilityRunner already only ever calls on the owner.
    ///
    /// EASES, DOES NOT SNAP, WHILE HELD. CameraTracking.AddZoomMultiplier/RemoveZoomMultiplier apply a multiplier
    /// instantly and have no notion of time (CameraZoomStack, scope step 1) - all of the smoothing is this
    /// class's own job, done by easing appliedFactor toward its target every frame (ScopeEase) and writing it to
    /// the camera each frame it sits above 1.
    ///
    /// SNAPS BACK AT ONCE FOR EVERY INTERRUPT REASON, AND ON RESPAWN. A silence, stun, death or unequip is not a
    /// moment worth a smooth transition, and easing the multiplier out past any of them risks a stale entry
    /// surviving into the next life - the exact shape of bug 2.10, PlayerMotor's own respawn speed bug, just for
    /// the camera instead of movement speed. Interrupt runs for every InterruptReason on purpose: there is no
    /// case here where continuing to ease out is better than an instant snap.
    /// </summary>
    public sealed class ScopeAbility : AbilityModule
    {
        [Header("Zoom")]
        [SerializeField, Tooltip("How much further than normal the camera pulls back while scoped, as a percent " +
                 "of the normal distance. 20 means a scoped player sees 20% further than an unscoped one at the " +
                 "SAME scroll zoom - including at the scroll wheel's own zoomed-out limit, because this applies " +
                 "AFTER that limit rather than being added to it. Tudor's number.")]
        private float extraZoomOutPercent = 20f;

        [SerializeField, Tooltip("Seconds to ease fully in when the key is pressed, or fully out when it is " +
                 "released - a snap reads as a camera glitch rather than a deliberate zoom. Every interrupt " +
                 "(death, stun, silence, unequip) skips this and snaps back in one frame instead - see the class " +
                 "comment.")]
        private float easeSeconds = 0.2f;

        // Owner only: this frame's applied multiplier, eased toward 1 (not scoped) or 1 + extraZoomOutPercent/100
        // (fully scoped). Exactly 1 means "nothing active" - see ApplyOrRemove.
        private float appliedFactor = 1f;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            // Nothing to send: the camera this ability moves exists only on this machine, and the spec is
            // explicit that other players see nothing at all. Refusing spends no charge and sends no RPC - see
            // the class comment for why this is the "sends nothing, shows nothing odd" path through
            // AbilityRunner.TryCast, unlike Sprint (which returns true purely to reserve a future cosmetic hook).
            payload = default;
            return false;
        }

        public override void ExecuteCast(in CastEvent cast)
        {
            // Never runs - TryBuildCast always refuses above, so no client (this one included) ever receives a
            // cast to execute.
        }

        public override void OwnerTick(float deltaTime, bool held, bool canAct)
        {
            float target = held && canAct ? 1f + extraZoomOutPercent / 100f : 1f;
            appliedFactor = ScopeEase.Advance(appliedFactor, target, extraZoomOutPercent, easeSeconds, deltaTime);

            ApplyOrRemove();
        }

        public override void Interrupt(InterruptReason reason) => SnapBack();

        public override void OnRespawned() => SnapBack();

        /// <summary>A player root destroyed directly (leaving the room while holding RMB, say) skips
        /// AbilityRunner.Equip's own Interrupt(Unequipped) entirely - neither Interrupt nor OnRespawned ever
        /// runs. CameraTracking outlives this module and its stack is keyed by object reference, so without this
        /// the 1.2 entry would stay forever and the local camera would sit ~20% further out for the rest of the
        /// session. Same fix as InvulnerabilityAbility.OnDestroy/ClearShield for the same class of bug. Safe to
        /// run twice - Interrupt(Unequipped) already calls SnapBack before this module is destroyed, and removing
        /// an absent key is a no-op (CameraTracking.RemoveZoomMultiplier's own contract). Real Play Mode/build
        /// destruction (leaving the room) DOES call this normally; only this project's edit-mode test harness has
        /// to invoke it explicitly (see ScopeAbilityTests' own comment on why).</summary>
        private void OnDestroy() => SnapBack();

        /// <summary>Removes the multiplier at once rather than easing it out - see the class comment on why every
        /// interrupt reason and a respawn both skip the ease entirely.</summary>
        private void SnapBack()
        {
            appliedFactor = 1f;
            CameraTracking.Instance?.RemoveZoomMultiplier(this);
        }

        /// <summary>Written every frame appliedFactor sits above 1 (still easing in, or fully scoped); removed
        /// the moment it lands back on exactly 1, so the stack never carries a no-op entry. Null-checked every
        /// call - CameraTracking.Instance is null before the local camera exists and in every edit-mode test,
        /// which has no camera at all.</summary>
        private void ApplyOrRemove()
        {
            CameraTracking camera = CameraTracking.Instance;
            if (camera == null)
                return;

            if (appliedFactor > 1f)
                camera.AddZoomMultiplier(this, appliedFactor);
            else
                camera.RemoveZoomMultiplier(this);
        }
    }
}
