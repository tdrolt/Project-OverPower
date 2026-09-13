using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure timer logic for a teleport portal's channel: standing inside your own portal for
    /// channelSeconds triggers a travel. Time is injected (Tick, not Time.deltaTime) so "leaving
    /// cancels", "a stun cancels" and "the arrival portal needs you to step out before it channels
    /// again" are all provable without a scene, a Rigidbody or a frame budget - the same reason
    /// DisplacementPriority and BlinkDestinationSearch stay free of MonoBehaviour.
    ///
    /// Knows nothing about Vector3, Portal or Physics: "which portal, if any, are you standing in
    /// right now" and "is a charge available to spend" are both decided by the caller and handed in
    /// each tick as an opaque identity token and a bool - TeleportAbility passes the actual Portal
    /// component; tests pass a boxed int or a string, whichever is convenient.
    /// </summary>
    public sealed class PortalChannelState
    {
        private float channelSeconds;

        // The portal (by identity) currently being channelled, or null when nothing is running.
        private object channelingPortal;

        // The arrival portal you must physically step out of before it can channel you again - set
        // by LatchArrival right when a travel completes. Cleared the moment you are found standing
        // somewhere other than it (including nowhere at all), never by a timer.
        private object arrivalLatch;

        private float elapsed;

        public bool IsChanneling => channelingPortal != null;

        /// <summary>Seconds into the current channel, 0 when not channelling. For a future HUD
        /// progress ring; not read by TeleportAbility today.</summary>
        public float Elapsed => elapsed;

        public PortalChannelState(float channelSeconds)
        {
            this.channelSeconds = Mathf.Max(0.01f, channelSeconds);
        }

        /// <summary>What happened THIS tick, so the caller knows which phase (if any) to send.
        /// Started/Cancelled/Completed each fire exactly once, on the tick the state actually
        /// changes - Progressing (and None, when nothing is or was running) fire every other tick a
        /// channel is simply continuing or simply absent, so a caller only reacts to the first four.</summary>
        public enum Result { None, Started, Progressing, Cancelled, Completed }

        /// <summary>
        /// One tick. standingIn: the portal (by identity) the player currently physically occupies,
        /// or null if they are in neither of their own portals. canChannel: every OTHER gate at once
        /// - a charge is available, there is a paired portal to travel to, the player can act (not
        /// dead, stunned or silenced) - collapsed into one bool because none of them behave any
        /// differently from this class's point of view: any one of them being false cancels or
        /// refuses a channel exactly the same way.
        /// </summary>
        public Result Tick(float deltaTime, object standingIn, bool canChannel)
        {
            // Stepping out of the arrival portal - however briefly - lifts the latch for good; it
            // does not need to STAY out, only to have left once. Checked first so a step-out-and-
            // back-in on the very same tick already counts, and so the check below sees the latch's
            // up-to-date state rather than last tick's.
            if (arrivalLatch != null && !Equals(standingIn, arrivalLatch))
                arrivalLatch = null;

            bool blockedByLatch = arrivalLatch != null && Equals(standingIn, arrivalLatch);
            bool canChannelHere = standingIn != null && canChannel && !blockedByLatch;

            if (!canChannelHere)
            {
                if (channelingPortal == null)
                    return Result.None;

                channelingPortal = null;
                elapsed = 0f;
                return Result.Cancelled;
            }

            bool justStarted = !Equals(channelingPortal, standingIn);
            channelingPortal = standingIn;
            elapsed += deltaTime;

            if (elapsed >= channelSeconds)
            {
                channelingPortal = null;
                elapsed = 0f;
                return Result.Completed;
            }

            return justStarted ? Result.Started : Result.Progressing;
        }

        /// <summary>Call once a travel actually happens, so the DESTINATION portal starts latched -
        /// Tudor's decision: standing inside the portal you just arrived in must not immediately
        /// start channelling you straight back.</summary>
        public void LatchArrival(object arrivalPortal) => arrivalLatch = arrivalPortal;

        /// <summary>Retunes how long a channel takes, keeping any channel already in progress
        /// running rather than resetting it - a designer changing this mid-match should not punish
        /// whoever happens to be mid-channel, the same courtesy ChargePool.SetRechargeSeconds gives
        /// a recharge already timing.</summary>
        public void SetChannelSeconds(float seconds) => channelSeconds = Mathf.Max(0.01f, seconds);

        /// <summary>Forces the channel off with no Cancelled result - for a caller that is about to
        /// be destroyed outright (Interrupt(Unequipped)) and has nobody left to send a cancel phase
        /// to. Does not touch the arrival latch: unequipping does not "step out" of anything.</summary>
        public void Reset()
        {
            channelingPortal = null;
            elapsed = 0f;
        }
    }
}
