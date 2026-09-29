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
