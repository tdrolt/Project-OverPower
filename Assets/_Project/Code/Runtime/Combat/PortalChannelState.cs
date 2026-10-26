using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure timer logic for a teleport portal's channel: standing inside a portal for channelSeconds
    /// triggers a travel. Time is injected (Tick, not Time.deltaTime) so "leaving cancels", "a stun
    /// cancels" and "the arrival portal needs you to step out before it channels again" are provable
    /// without a scene, a Rigidbody or a frame budget.
    ///
    /// Knows nothing about Vector3, Portal or Physics: which portal you stand in and whether a charge
    /// is available are decided by the caller and handed in each tick as an opaque identity token and
    /// a bool (TeleportAbility passes the Portal component; tests pass a boxed int or a string).
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

        /// <summary>Seconds into the current channel, 0 when not channelling. Waits on a HUD progress
        /// ring; TeleportAbility does not read it.</summary>
        public float Elapsed => elapsed;

        public PortalChannelState(float channelSeconds)
        {
            this.channelSeconds = Mathf.Max(0.01f, channelSeconds);
        }

        /// <summary>What happened THIS tick, so the caller knows which phase (if any) to send.
        /// Started/Cancelled/Completed each fire exactly once, on the tick the state changes;
        /// Progressing (and None, when nothing is or was running) fire on every other tick, so a caller
        /// only reacts to the first three.</summary>
        public enum Result { None, Started, Progressing, Cancelled, Completed }

        /// <summary>
        /// One tick. standingIn: the portal (by identity) the player physically occupies, or null if
        /// they are on no portal. canChannel: every OTHER gate at once (a charge is available, there is
        /// a paired portal, the player can act: not dead, stunned or silenced), collapsed into one bool
        /// because any one being false cancels or refuses a channel exactly the same way.
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
            if (justStarted)
                elapsed = 0f; // stepping straight from one portal onto another must not carry channel time over
            elapsed += deltaTime;

            if (elapsed >= channelSeconds)
            {
                channelingPortal = null;
                elapsed = 0f;
                return Result.Completed;
            }

            return justStarted ? Result.Started : Result.Progressing;
        }

        /// <summary>Call once a travel actually happens, so the DESTINATION portal starts latched:
        /// standing inside the portal you just arrived in must not start channelling you straight
        /// back.</summary>
        public void LatchArrival(object arrivalPortal) => arrivalLatch = arrivalPortal;

        /// <summary>True while this portal is the arrival portal the body has not yet stepped out of.</summary>
        public bool IsLatchedOn(object portal) => portal != null && Equals(arrivalLatch, portal);

        /// <summary>Retunes how long a channel takes, keeping any channel already in progress running
        /// rather than resetting it (the same courtesy as ChargePool.SetRechargeSeconds): a designer
        /// changing this mid-match should not punish whoever is mid-channel.</summary>
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
