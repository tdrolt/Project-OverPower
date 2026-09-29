using NUnit.Framework;
using Overpower.Net;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Tudor D18: the STUNNED / SLOWED label rule (stun beats slow, the bar's fill, when it hides)
    /// and the Player Property that carries it to the other clients.</summary>
    public class StatusLabelRuleTests
    {
        [Test]
        public void StunAndSlowTogetherShowStunned() =>
            Assert.AreEqual(StatusLabel.Stunned, StatusLabelRule.Choose(1.5f, 3f));

        [Test]
        public void StunAloneShowsStunned() =>
            Assert.AreEqual(StatusLabel.Stunned, StatusLabelRule.Choose(0.2f, 0f));

        [Test]
        public void SlowAloneShowsSlowed() =>
            Assert.AreEqual(StatusLabel.Slowed, StatusLabelRule.Choose(0f, 2f));

        [Test]
        public void NeitherShowsNothing() =>
            Assert.AreEqual(StatusLabel.None, StatusLabelRule.Choose(0f, 0f));

        [Test]
        public void FillAtHalfTimeIsHalf() =>
            Assert.AreEqual(0.5f, StatusLabelRule.Fill(1f, 2f), 1e-5f);

        [Test]
        public void FillPastTheEndIsZeroAndTheLabelIsHidden()
        {
            Assert.AreEqual(0f, StatusLabelRule.Fill(-0.3f, 2f), 1e-5f);
            Assert.AreEqual(StatusLabel.None, StatusLabelRule.Choose(-0.3f, -0.1f));

            // A remote copy hides the same way: its seconds left (server time) are at or below zero.
            float remoteLeft = StatusLabelRule.SecondsLeft(10000, 10400);
            Assert.LessOrEqual(remoteLeft, 0f);
            Assert.AreEqual(StatusLabel.None, StatusLabelRule.Choose(remoteLeft, remoteLeft));
        }

        [Test]
        public void PublishesTheFirstTimeAndOnEveryLabelChangeIncludingToNone()
        {
            Assert.IsTrue(StatusLabelRule.ShouldPublish(false, StatusLabel.None, 0, 0, StatusLabel.None, 0, 0, 120), "first ever");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.None, 0, 0, StatusLabel.Stunned, 5000, 2000, 120), "none to stunned");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Slowed, 7000, 4000, 120), "stunned to slowed");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.None, 0, 0, 120), "expiry or death to none");
        }

        [Test]
        public void DoesNotRepublishForJitterOrWhileNothingShows()
        {
            Assert.IsFalse(StatusLabelRule.ShouldPublish(true, StatusLabel.None, 0, 0, StatusLabel.None, 0, 0, 120), "still none");
            Assert.IsFalse(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Stunned, 5120, 2000, 120), "exactly at tolerance");
            Assert.IsFalse(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Stunned, 4900, 2000, 120), "jitter earlier");
        }

        [Test]
        public void RepublishesWhenARefreshMovesTheEndOrTheWindowChanges()
        {
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Stunned, 5121, 2000, 120), "end later");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Stunned, 4879, 2000, 120), "end earlier");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, 5000, 2000, StatusLabel.Stunned, 5000, 3000, 120), "window changed");
            Assert.IsTrue(StatusLabelRule.ShouldPublish(true, StatusLabel.Stunned, int.MaxValue - 50, 2000, StatusLabel.Stunned, int.MinValue + 200, 2000, 120), "wrap-safe");
        }

        [Test]
        public void OnlySlowedAndStunnedDecodeAsALabel()
        {
            Assert.IsTrue(StatusLabelProperty.TryDecode(new int[] { 1, 5000, 1500 }, out _, out _, out _));
            Assert.IsTrue(StatusLabelProperty.TryDecode(new int[] { 2, 5000, 1500 }, out _, out _, out _));
            Assert.IsFalse(StatusLabelProperty.TryDecode(new int[] { 3, 5000, 1500 }, out _, out _, out _));
            Assert.IsFalse(StatusLabelProperty.TryDecode(new int[] { -1, 5000, 1500 }, out _, out _, out _));
        }

        [Test]
        public void FillNeverPassesFullOrDividesByZero()
        {
            Assert.AreEqual(1f, StatusLabelRule.Fill(3f, 2f), 1e-5f);
            Assert.AreEqual(0f, StatusLabelRule.Fill(1f, 0f), 1e-5f);
        }

        [Test]
        public void TrackedTotalRisesOnAFreshStatusHoldsWhileCountingDownAndResetsAtTheEnd()
        {
            Assert.AreEqual(2f, StatusLabelRule.TrackedTotal(0f, 2f), 1e-5f, "fresh status");
            Assert.AreEqual(2f, StatusLabelRule.TrackedTotal(2f, 1.2f), 1e-5f, "counting down keeps the window");
            Assert.AreEqual(3f, StatusLabelRule.TrackedTotal(2f, 3f), 1e-5f, "a longer refresh raises it");
            Assert.AreEqual(0f, StatusLabelRule.TrackedTotal(3f, 0f), 1e-5f, "ended");
        }

        [Test]
        public void SecondsLeftReadsServerTimeAndSurvivesTheClockWrapping()
        {
            Assert.AreEqual(1.5f, StatusLabelRule.SecondsLeft(11500, 10000), 1e-4f);
            Assert.Less(StatusLabelRule.SecondsLeft(10000, 11000), 0f, "past the end");
            Assert.AreEqual(2f, StatusLabelRule.SecondsLeft(int.MinValue + 1000, int.MaxValue - 999), 1e-3f, "wrapped past 2^31");
        }

        [Test]
        public void PropertyRoundTripsAndBadValuesReadAsNoLabel()
        {
            Assert.IsTrue(StatusLabelProperty.TryDecode(StatusLabelProperty.Encode(2, 5000, 1500), out int label, out int end, out int dur));
            Assert.AreEqual(2, label);
            Assert.AreEqual(5000, end);
            Assert.AreEqual(1500, dur);

            Assert.IsFalse(StatusLabelProperty.TryDecode(StatusLabelProperty.None(), out _, out _, out _));
            Assert.IsFalse(StatusLabelProperty.TryDecode(null, out _, out _, out _));
            Assert.IsFalse(StatusLabelProperty.TryDecode("junk", out _, out _, out _));
            Assert.IsFalse(StatusLabelProperty.TryDecode(new int[] { 2 }, out _, out _, out _));
        }
    }
}
