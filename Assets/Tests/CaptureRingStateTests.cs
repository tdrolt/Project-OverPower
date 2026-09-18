using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureRingStateTests
    {
        [Test]
        public void NothingInProgressOnANeutralZoneShowsNoBandAndANeutralEdge()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Idle, -1, false, 1000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.IsFalse(s.ShowsArc);
            Assert.AreEqual(-1, s.OutlineTeam);
            Assert.IsFalse(s.UnderAttack);
        }

        [Test]
        public void NothingInProgressOnAnOwnedZoneEdgesInTheOwnersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Idle, 2, false, 1000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.AreEqual(2, s.OutlineTeam);
        }

        [Test]
        public void ACaptureGrowsInTheCapturersColourFromTheServerClock()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.2f, 1f / 15f, 1000), -1, false, 4000);
            Assert.AreEqual(CaptureRingPhase.Capturing, s.Phase);
            Assert.AreEqual(0.4f, s.Fill01, 1e-4f);
            Assert.AreEqual(1, s.ArcTeam);
            Assert.AreEqual(-1, s.OutlineTeam);
            Assert.IsTrue(s.ShowsArc);
        }

        [Test]
        public void ADrainShowsTheOwnersRemainingHoldAndNamesTheDrainer()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(2, 0.8f, -0.2f, 1000), 0, true, 2000);
            Assert.AreEqual(CaptureRingPhase.Draining, s.Phase);
            Assert.AreEqual(0.6f, s.Fill01, 1e-4f);
            Assert.AreEqual(0, s.ArcTeam, "the band is the owner's remaining hold, in the owner's colour");
            Assert.AreEqual(0, s.OutlineTeam);
            Assert.AreEqual(2, s.DrainerTeam);
            Assert.IsTrue(s.UnderAttack);
        }

        [Test]
        public void AHeldCaptureIsPausedAtItsValueInTheCapturersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Held(1, 0.35f, 1000), -1, false, 99999);
            Assert.AreEqual(CaptureRingPhase.Paused, s.Phase);
            Assert.AreEqual(0.35f, s.Fill01, 1e-5f);
            Assert.AreEqual(1, s.ArcTeam);
            Assert.AreEqual(-1, s.DrainerTeam);
        }

        [Test]
        public void AHeldDrainIsPausedInTheOwnersColour()
        {
            CaptureRingState s = CaptureRingState.From(CaptureProgress.Held(2, 0.6f, 1000), 0, true, 5000);
            Assert.AreEqual(CaptureRingPhase.Paused, s.Phase);
            Assert.AreEqual(0.6f, s.Fill01, 1e-5f);
            Assert.AreEqual(0, s.ArcTeam);
            Assert.AreEqual(-1, s.DrainerTeam, "only a moving drain pulses in the drainer's colour");
            Assert.IsTrue(s.UnderAttack);
        }

        [Test]
        public void AHoldWithNothingBankedIsIdle()
        {
            // Decoded from the room: a team with 0 progress and rate 0 (a hold below 1/10000 rounds to 0 on the wire).
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0f, 0f, 1000), -1, false, 2000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
        }

        [Test]
        public void UnderAttackOnlyCountsForAnOwnedZoneAndShowsWithNothingDraining()
        {
            Assert.IsFalse(CaptureRingState.From(CaptureProgress.Idle, -1, true, 1000).UnderAttack);
            CaptureRingState owned = CaptureRingState.From(CaptureProgress.Idle, 1, true, 1000);
            Assert.IsTrue(owned.UnderAttack);
            Assert.AreEqual(CaptureRingPhase.Idle, owned.Phase);
        }

        [Test]
        public void ADrainOnAZoneThatAlreadyWentNeutralIsIdle()
        {
            // The owner and the progress are two separate room updates; the neutral owner can arrive first.
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(2, 0.1f, -0.2f, 1000), -1, false, 1400);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
        }

        [Test]
        public void ACaptureByTheZonesOwnOwnerIsIdle()
        {
            // The same gap after a capture completes: the new owner arrives before the progress goes Idle.
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.95f, 1f / 15f, 1000), 1, false, 2000);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.AreEqual(1, s.OutlineTeam);
        }

        [Test]
        public void WithoutAServerClockTheBandShowsThePublishedValue()
        {
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(1, 0.25f, 1f / 15f, 1000), -1, false, 0);
            Assert.AreEqual(0.25f, s.Fill01, 1e-5f);
        }

        [Test]
        public void AnOutOfPlayZoneShowsNoArcWhateverTheRoomSays()
        {
            // A capturing progress for team 0, an owner and an under-attack flag that would otherwise draw
            // something - out of play must win over all three.
            CaptureRingState s = CaptureRingState.From(new CaptureProgress(0, 0.5f, 1f / 15f, 1000), 1, true, 4000, outOfPlay: true);
            Assert.AreEqual(CaptureRingPhase.Idle, s.Phase);
            Assert.IsFalse(s.ShowsArc);
            Assert.IsTrue(s.OutOfPlay);
            Assert.AreEqual(-1, s.OutlineTeam);
            Assert.IsFalse(s.UnderAttack);
        }
    }
}
