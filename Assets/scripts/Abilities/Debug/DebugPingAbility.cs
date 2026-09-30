using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Test content, not a real ability: the smallest module that exercises every part of the
    /// ability framework, so the framework can be proven before any real ability depends on it.
    /// It drops a sphere where the cursor was when the key went down, on every client.
    ///
    /// With Channel Seconds above 0 it becomes a HOLD: the sphere stays up while the key is held and
    /// completes after that long; letting go early, dying, being stunned or overheating cancels it.
    /// That variant (the "Debug Ping Channel" prefab) exists to prove holds and interrupts reach
    /// every client, which a real channel (the teleport) will rely on.
    ///
    /// Read it as the template for a real module: TryBuildCast (owner) only packs what the caster
    /// knew; ExecuteCast (every client) does the visible part from the payload alone; OwnerTick and
    /// Interrupt (owner) decide when a hold ends and announce it with SendPhase.
    /// </summary>
    public sealed class DebugPingAbility : AbilityModule
    {
        // Phase numbers this module defines. 0 is always the cast itself (see CastEvent.Phase).
        private const byte PhaseCast = 0;
        private const byte PhaseComplete = 1;
        private const byte PhaseCancel = 2;

        [Header("Marker")]
        [SerializeField, Tooltip("Radius in metres of the sphere dropped at the cursor.")]
        private float markerRadius = 0.5f;

        [SerializeField, Tooltip("Seconds the sphere stays up after an instant ping, or after a " +
                 "channel completes.")]
        private float markerSeconds = 1.5f;

        [Header("Channel")]
        [SerializeField, Tooltip("0 = an instant ping. Above 0 = hold the key this many seconds to " +
                 "complete; releasing early (or dying, being stunned or overheating) cancels it. The " +
                 "charge is spent when the hold starts and is not refunded on a cancel.")]
        private float channelSeconds = 0f;

        // Owner only: the hold in progress. The payload is kept so the complete/cancel phases name the
        // same point as the cast that started them.
        private bool channelling;
        private float channelElapsed;
        private CastPayload channelPayload;

        // Every client: the sphere of a channel still in progress on this machine's screen.
        private GameObject channelMarker;

        public override bool IsActive => channelling;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Origin = ctx.Origin, Direction = ctx.AimDirection, Point = ctx.TargetPoint };
            return true;
        }

        public override void ExecuteCast(in CastEvent cast)
        {
            Debug.Log($"[PING] actor={cast.CasterActor} slot={Definition.Slot} phase={cast.Phase} " +
                      $"point={cast.Payload.Point.ToString("F2")} caster={cast.IsCasterClient}");

            switch (cast.Phase)
            {
                case PhaseCast:
                    ClearChannelMarker();
                    GameObject marker = SpawnMarker(cast.Payload.Point);
                    if (channelSeconds <= 0f)
                    {
                        Destroy(marker, markerSeconds);
                        return;
                    }

                    channelMarker = marker;
                    if (cast.IsCasterClient)
                    {
                        // Timing lives on the caster only; everyone else waits to be told the outcome.
                        channelling = true;
                        channelElapsed = 0f;
                        channelPayload = cast.Payload;
                    }
                    return;

                case PhaseComplete:
                    if (channelMarker != null)
                        Destroy(channelMarker, markerSeconds);
                    channelMarker = null;
                    return;

                case PhaseCancel:
                    ClearChannelMarker();
                    return;
            }
        }

        public override void OwnerTick(float deltaTime, bool held, bool canAct)
        {
            if (!channelling)
                return;

            if (!held)
            {
                EndChannel(PhaseCancel);
                return;
            }

            channelElapsed += deltaTime;
            if (channelElapsed >= channelSeconds)
                EndChannel(PhaseComplete);
        }

        public override void Interrupt(InterruptReason reason)
        {
            // The owner announces the cancel so other screens stop drawing it - they cannot see a
            // stun or an overheat on their own. Death is also handled locally on every client below,
            // so the sphere vanishes there without waiting for the message.
            if (channelling)
                EndChannel(PhaseCancel);

            ClearChannelMarker();
        }

        private void EndChannel(byte phase)
        {
            channelling = false;
            SendPhase(phase, channelPayload);
        }

        private GameObject SpawnMarker(Vector3 point)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Debug Ping Marker";

            // Removed immediately rather than with Destroy, which waits for the end of the frame: for
            // that one frame the sphere would be a solid object that stops bullets.
            DestroyImmediate(marker.GetComponent<Collider>());

            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * markerRadius * 2f;
            return marker;
        }

        private void ClearChannelMarker()
        {
            if (channelMarker != null)
                Destroy(channelMarker);
            channelMarker = null;
        }

        /// <summary>A player leaving mid-channel destroys this module with it; the sphere, which
        /// lives in the world rather than under the player, must not outlive it.</summary>
        private void OnDestroy() => ClearChannelMarker();
    }
}
