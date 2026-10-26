using Photon.Pun;
using UnityEngine;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// Hold right click (the Attachment slot) to pull the camera back further than the scroll zoom allows, so a
    /// sniper-style loadout can see what it shoots at. Let go and it eases back. No toggle, cooldown or heat cost
    /// (charges 0: no limiter at all, by design), no movement slow, nothing shown to other players: giving up mines,
    /// cover and raybeam in the Attachment slot is the whole cost.
    /// OWNER ONLY, NO NETWORK MESSAGE. The camera exists only on the owner's machine. TryBuildCast always refuses,
    /// the one path through AbilityRunner.TryCast that spends no charge and sends no RPC; all behaviour is in
    /// OwnerTick, which AbilityRunner calls on the owner only.
    /// EASES, DOES NOT SNAP, WHILE HELD: CameraTracking's zoom multiplier has no notion of time (CameraZoomStack), so
    /// this class eases appliedFactor toward its target (ScopeEase) and writes it each frame it sits above 1.
    /// SNAPS BACK AT ONCE FOR EVERY INTERRUPT REASON AND ON RESPAWN: easing out past a silence, stun, death or
    /// unequip risks a stale entry surviving into the next life (the shape of PlayerMotor's respawn speed bug).
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

        [Header("Sight while held")]
        [SerializeField, Tooltip("While you hold the Scope, your sight cone is this wide (full angle in degrees). " +
                 "Your teammates' screens use it too. Letting go, dying or respawning brings the normal cone back.")]
        private float sightConeAngleDegrees = 30f;

        [SerializeField, Tooltip("While you hold the Scope, your sight cone reaches this far (metres).")]
        private float sightConeLength = 26f;

        [SerializeField, Tooltip("While you hold the Scope, the circle around you grows by this many metres. " +
                 "Negative shrinks it (-3 turns a 7 m circle into 4 m); it never goes below 0.")]
        private float sightCircleChange = -3f;

        /// <summary>The cone angle (degrees) of the sight while the Scope is held.</summary>
        public float SightConeAngleDegrees => sightConeAngleDegrees;

        /// <summary>The cone length (metres) of the sight while the Scope is held.</summary>
        public float SightConeLength => sightConeLength;

        /// <summary>Metres added to the sight circle while the Scope is held (negative shrinks it).</summary>
        public float SightCircleChange => sightCircleChange;

        // Owner only: whether the sight is the scoped one right now, and the last value written to the Player Property.
        private bool holdingSight;
        private bool publishedScoped;
        private bool seededFromRoom;

        /// <summary>True while the Scope is held and the player can act. The owner mirrors it to every client as the Player
        /// Property vScp (ScopeSightProperty); a remote copy of this module never changes it.</summary>
        public bool IsHoldingSight => holdingSight;

        // Owner only: this frame's applied multiplier, eased toward 1 (not scoped) or 1 + extraZoomOutPercent/100
        // (fully scoped). Exactly 1 means "nothing active" - see ApplyOrRemove.
        private float appliedFactor = 1f;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            // Nothing to send: the camera exists only on this machine and other players see nothing. Refusing spends
            // no charge and sends no RPC (unlike Sprint, which returns true to reserve a future cosmetic hook).
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
            SetHoldingSight(held && canAct);

            ApplyOrRemove();
        }

        public override void Interrupt(InterruptReason reason) => SnapBack();

        public override void OnRespawned() => SnapBack();

        /// <summary>A player root destroyed directly (leaving the room while holding RMB) skips AbilityRunner.Equip's
        /// Interrupt(Unequipped), so neither Interrupt nor OnRespawned runs. CameraTracking outlives this module and its
        /// stack is keyed by object reference, so without this the entry would keep the local camera zoomed out for the
        /// session (same fix as InvulnerabilityAbility.OnDestroy). Safe to run twice: removing an absent key is a no-op.
        /// Only the edit-mode test harness has to invoke it explicitly (see ScopeAbilityTests).</summary>
        private void OnDestroy() => SnapBack();

        /// <summary>Removes the multiplier at once rather than easing it out - see the class comment on why every
        /// interrupt reason and a respawn both skip the ease entirely.</summary>
        private void SnapBack()
        {
            appliedFactor = 1f;
            CameraTracking.Instance?.RemoveZoomMultiplier(this);
            SetHoldingSight(false);
        }

        // Written only by the owner, only on change, no RPC: every client reads the player's vScp property.
        private void SetHoldingSight(bool value)
        {
            holdingSight = value;
            if (!seededFromRoom && Owner != null && Owner.PhotonView != null && Owner.IsMine && PhotonNetwork.InRoom)
            {
                // First tick in a room: a stale vScp=true left by a drop while scoped counts as published, so it is overwritten.
                seededFromRoom = true;
                PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(ScopeSightProperty.Key, out object roomValue);
                publishedScoped = ScopeSightProperty.SeedPublished(roomValue);
            }
            if (!ScopeSightProperty.ShouldPublish(publishedScoped, value))
                return;
            if (Owner == null || Owner.PhotonView == null || !Owner.IsMine || !PhotonNetwork.InRoom)
                return;
            publishedScoped = value;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { ScopeSightProperty.Key, value } });
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

        public override string ShopStatsText() => $"Camera pulls back {ShopNumberFormat.Compact(extraZoomOutPercent)}% further";
    }
}
