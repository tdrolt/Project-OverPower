using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class PortalChannelStateTests
    {
        private const string A = "portal-a";
        private const string B = "portal-b";

        private static PortalChannelState NewState() => new PortalChannelState(channelSeconds: 3f);

        [Test]
        public void NotStandingInAnyPortalReportsNone()
        {
            var state = NewState();

            var result = state.Tick(0.1f, standingIn: null, canChannel: true);

            Assert.AreEqual(PortalChannelState.Result.None, result);
            Assert.IsFalse(state.IsChanneling);
        }

        [Test]
        public void EnteringAPortalStartsTheChannelOnce()
        {
            var state = NewState();

            var first = state.Tick(0.1f, A, canChannel: true);
            var second = state.Tick(0.1f, A, canChannel: true);

            Assert.AreEqual(PortalChannelState.Result.Started, first);
            Assert.AreEqual(PortalChannelState.Result.Progressing, second);
            Assert.IsTrue(state.IsChanneling);
        }

        [Test]
        public void ThreeSecondsOfStandingStillCompletesTheChannel()
        {
            var state = NewState();

            state.Tick(1f, A, true);
            state.Tick(1f, A, true);
            var result = state.Tick(1f, A, true);

            Assert.AreEqual(PortalChannelState.Result.Completed, result);
            Assert.IsFalse(state.IsChanneling);
        }

        [Test]
        public void LeavingThePortalCancelsTheChannel()
        {
            var state = NewState();
            state.Tick(1f, A, true);

            var result = state.Tick(0.1f, null, true);

            Assert.AreEqual(PortalChannelState.Result.Cancelled, result);
            Assert.IsFalse(state.IsChanneling);
        }

        [Test]
        public void CannotActCancelsTheChannelEvenWhileStillStandingInIt()
        {
            var state = NewState();
            state.Tick(1f, A, true);

            // Still physically standing in A, but canChannel (stunned) is false - a stun must stop
            // the channel exactly like leaving the circle would.
            var result = state.Tick(0.1f, A, false);

            Assert.AreEqual(PortalChannelState.Result.Cancelled, result);
        }

        [Test]
        public void CancellingTwiceInARowReportsNoneTheSecondTime()
        {
            var state = NewState();
            state.Tick(1f, A, true);
            state.Tick(0.1f, null, true); // cancels

            var result = state.Tick(0.1f, null, true);

            Assert.AreEqual(PortalChannelState.Result.None, result);
        }

        [Test]
        public void NoChargeAvailableRefusesToStartAChannel()
        {
            var state = NewState();

            var result = state.Tick(0.1f, A, canChannel: false);

            Assert.AreEqual(PortalChannelState.Result.None, result);
            Assert.IsFalse(state.IsChanneling);
        }

        [Test]
        public void SwitchingWhichPortalYouStandInRestartsTheChannelAsANewStart()
        {
            var state = NewState();
            state.Tick(1f, A, true);

            // Only relevant if a player could somehow straddle both circles in one tick - the state
            // must not silently keep the OLD portal's progress under the NEW portal's identity.
            var result = state.Tick(1f, B, true);

            Assert.AreEqual(PortalChannelState.Result.Started, result);
        }

        [Test]
        public void ArrivalLatchBlocksChannellingInTheDestinationPortal()
        {
            var state = NewState();
            state.LatchArrival(B);

            var result = state.Tick(1f, B, true);

            Assert.AreEqual(PortalChannelState.Result.None, result);
            Assert.IsFalse(state.IsChanneling);
        }

        [Test]
        public void SteppingOutOfTheArrivalPortalLiftsTheLatch()
        {
            var state = NewState();
            state.LatchArrival(B);

            state.Tick(1f, null, true);   // stepped out, even briefly
            var result = state.Tick(1f, B, true); // stepped back in

            Assert.AreEqual(PortalChannelState.Result.Started, result);
        }

        [Test]
        public void TheLatchDoesNotAffectTheOtherPortal()
        {
            var state = NewState();
            state.LatchArrival(B);

            // Never left B's arena in this test, but standing in A (the OTHER portal) is unrelated
            // to the arrival latch entirely.
            var result = state.Tick(1f, A, true);

            Assert.AreEqual(PortalChannelState.Result.Started, result);
        }

        [Test]
        public void ResetStopsAnInProgressChannelWithNoCancelledResult()
        {
            var state = NewState();
            state.Tick(1f, A, true);

            state.Reset();
            var result = state.Tick(0.1f, null, true);

            Assert.AreEqual(PortalChannelState.Result.None, result);
        }

        [Test]
        public void SetChannelSecondsLetsAnInProgressChannelCompleteSooner()
        {
            var state = NewState();
            state.Tick(1f, A, true); // 1s in, out of 3

            state.SetChannelSeconds(1.5f);
            var result = state.Tick(1f, A, true); // 2s in, out of the new 1.5

            Assert.AreEqual(PortalChannelState.Result.Completed, result);
        }
    }
}
