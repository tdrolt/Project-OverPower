using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureProgressTests
    {
        [Test]
        public void IdleIsZero()
        {
            Assert.AreEqual(0f, CaptureProgress.Idle.Evaluate(123456), 1e-5f);
        }

        [Test]
        public void ProgressMovesAtItsRateFromTheStamp()
        {
            var p = new CaptureProgress(team: 1, progress01: 0.2f, ratePerSecond01: 0.1f, stampMs: 1000);
            Assert.AreEqual(0.5f, p.Evaluate(4000), 1e-4f);
        }

        [Test]
        public void ProgressClampsBetweenZeroAndOne()
        {
            var up = new CaptureProgress(1, 0.9f, 0.5f, 0);
            var down = new CaptureProgress(1, 0.1f, -0.5f, 0);
            Assert.AreEqual(1f, up.Evaluate(10000), 1e-5f);
            Assert.AreEqual(0f, down.Evaluate(10000), 1e-5f);
        }

        [Test]
        public void EvaluationSurvivesTimestampWrap()
        {
            var p = new CaptureProgress(1, 0f, 0.1f, int.MaxValue - 500);
            Assert.AreEqual(0.1f, p.Evaluate(unchecked(int.MaxValue + 501)), 1e-3f);
        }

        [Test]
        public void EncodingRoundTripsThroughInts()
        {
            var p = new CaptureProgress(2, 0.3337f, -0.2f, 77);
            CaptureProgress back = CaptureProgress.Decode(p.EncodeTeam(), p.EncodeProgress(), p.EncodeRate(), p.StampMs);
            Assert.AreEqual(2, back.Team);
            Assert.AreEqual(0.3337f, back.Progress01, 1e-4f);
            Assert.AreEqual(-0.2f, back.RatePerSecond01, 1e-4f);
            Assert.AreEqual(77, back.StampMs);
            Assert.IsFalse(back.Fading, "the 4-int Decode overload (old clients' wire shape) must default to not-fading");
        }

        [Test]
        public void FadingEncodingRoundTripsThroughInts()
        {
            // captureFadeSpeed (2026-09-24): a fifth wire value (cFade) so a fade/refill can be told apart from a
            // live capture/drain's own two-update echo-race Idle guards (CaptureRingState.From) and from a real
            // player action (CaptureTransitionClassifier) - see both files' own comments.
            var p = new CaptureProgress(1, 0.6f, -0.05f, 500, fading: true);
            Assert.IsTrue(p.Fading);
            CaptureProgress back = CaptureProgress.Decode(p.EncodeTeam(), p.EncodeProgress(), p.EncodeRate(), p.StampMs, p.EncodeFading());
            Assert.IsTrue(back.Fading);
            Assert.AreEqual(1, back.Team);
            Assert.AreEqual(0.6f, back.Progress01, 1e-4f);
            Assert.AreEqual(-0.05f, back.RatePerSecond01, 1e-4f);
        }

        [Test]
        public void NotFadingIsTheDefault()
        {
            Assert.IsFalse(new CaptureProgress(1, 0.5f, 0.1f, 0).Fading);
            Assert.IsFalse(CaptureProgress.Idle.Fading);
            Assert.IsFalse(CaptureProgress.Held(1, 0.4f, 500).Fading);
        }

        [Test]
        public void FadingFlipAlwaysNeedsRepublish()
        {
            // Same team, same rate MAGNITUDE, only Fading differs - still a real change: a remote client's
            // CaptureRingState.From (and CaptureTransitionClassifier) each read Fading, not just the rate, so a
            // silent flip would leave every client drawing the wrong picture.
            var moving = new CaptureProgress(1, 0.2f, -0.1f, 0, fading: false);
            var fading = new CaptureProgress(1, 0.2f, -0.1f, 0, fading: true);
            Assert.IsTrue(moving.NeedsRepublishComparedTo(fading));
            Assert.IsTrue(fading.NeedsRepublishComparedTo(moving));
        }

        [Test]
        public void SameRateAndTeamNeedsNoRepublish()
        {
            var a = new CaptureProgress(1, 0.2f, 0.1f, 0);
            var b = new CaptureProgress(1, 0.5f, 0.1f, 3000);
            Assert.IsFalse(a.NeedsRepublishComparedTo(b));
            Assert.IsTrue(a.NeedsRepublishComparedTo(new CaptureProgress(1, 0.5f, 0.2f, 3000)));
            Assert.IsTrue(a.NeedsRepublishComparedTo(new CaptureProgress(2, 0.5f, 0.1f, 3000)));
        }

        [Test]
        public void HeldKeepsTeamAndProgressAtRateZeroAndDoesNotMove()
        {
            CaptureProgress held = CaptureProgress.Held(1, 0.4f, 500);
            Assert.AreEqual(1, held.Team);
            Assert.AreEqual(0.4f, held.Progress01, 1e-5f);
            Assert.AreEqual(0f, held.RatePerSecond01);
            Assert.IsTrue(held.IsHeld);
            Assert.AreEqual(0.4f, held.Evaluate(99999), 1e-5f);
        }

        [Test]
        public void HeldWithNothingBankedOrNoTeamIsIdle()
        {
            Assert.AreEqual(-1, CaptureProgress.Held(1, 0f, 500).Team);
            Assert.AreEqual(-1, CaptureProgress.Held(-1, 0.5f, 500).Team);
            Assert.IsFalse(CaptureProgress.Held(1, 0f, 500).IsHeld);
        }

        [Test]
        public void MovingIntoOrOutOfAHoldNeedsARepublish()
        {
            var moving = new CaptureProgress(1, 0.2f, 0.1f, 0);
            CaptureProgress held = CaptureProgress.Held(1, 0.3f, 1000);
            Assert.IsTrue(moving.NeedsRepublishComparedTo(held));
            Assert.IsTrue(held.NeedsRepublishComparedTo(moving));
            Assert.IsTrue(held.NeedsRepublishComparedTo(CaptureProgress.Idle), "a hold ending in Idle changes the team");
        }

        [Test]
        public void IdleAndMovingProgressAreNotHeld()
        {
            Assert.IsFalse(CaptureProgress.Idle.IsHeld);
            Assert.IsFalse(new CaptureProgress(0, 0.5f, -0.2f, 0).IsHeld);
        }

        [Test]
        public void ATeamWithZeroProgressAtRateZeroIsNotHeld()
        {
            // Decoded from the room: a hold under 1/10000 of a capture rounds to 0 on the wire
            // (review fix, 2026-09-17) - IsHeld must agree with CaptureRingState, which already
            // reads team>=0, progress 0, rate 0 as Idle, not Paused.
            var roundedAwayHold = new CaptureProgress(1, 0f, 0f, 1000);
            Assert.IsFalse(roundedAwayHold.IsHeld);
        }
    }
}
