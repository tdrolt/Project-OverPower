using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// Pushing an enemy capital beyond "enough of the team near it": an exposed capital, going straight on from a Tier II next to it, and
    /// trading capitals (finishing a capture far enough along before turning back). Literal numbers made up for the test; the real ones
    /// live in BotConfig.
    /// </summary>
    public class BotPushRulesTests
    {
        [Test]
        public void ACapitalWithNoMoreDefendersKnownThanAllowedIsExposed()
        {
            Assert.IsTrue(BotPushRules.Exposed(0, 2));
            Assert.IsTrue(BotPushRules.Exposed(2, 2));
            Assert.IsFalse(BotPushRules.Exposed(3, 2));
        }

        [Test]
        public void AnExposedCapitalIsPushedOnceEnoughOfTheTeamIsWithinTheWiderReach()
        {
            Assert.IsTrue(BotPushRules.PushAnyway(true, 3, 3, false));
            Assert.IsFalse(BotPushRules.PushAnyway(true, 2, 3, false), "not enough of the team within reach");
            Assert.IsFalse(BotPushRules.PushAnyway(false, 4, 3, false), "too well defended");
        }

        [Test]
        public void AForcedPushNeedsNobodyElse()
        {
            Assert.IsTrue(BotPushRules.PushAnyway(false, 1, 3, true));
        }

        [Test]
        public void ABotNearATierTwoOfItsTeamGoesOnToTheCapitalOnlyWhenNoDefenderIsKnown()
        {
            Assert.IsTrue(BotPushRules.Forced(true, 0));
            Assert.IsFalse(BotPushRules.Forced(true, 1), "an enemy is known near the capital");
            Assert.IsFalse(BotPushRules.Forced(false, 0), "not near such a Tier II");
        }

        [Test]
        public void DrainingAZoneAnotherTeamHeldIsTheFirstHalfOfTakingIt()
        {
            Assert.AreEqual(0f, BotPushRules.CaptureDone01(true, true, true, 1f), 1e-4f, "nothing drained yet");
            Assert.AreEqual(0.3f, BotPushRules.CaptureDone01(true, true, true, 0.4f), 1e-4f, "0.6 of the hold drained");
            Assert.AreEqual(0.5f, BotPushRules.CaptureDone01(true, false, true, 0f), 1e-4f, "drained to neutral");
            Assert.AreEqual(0.8f, BotPushRules.CaptureDone01(true, false, true, 0.6f), 1e-4f, "0.6 captured after the drain");
        }

        [Test]
        public void TakingAZoneThatWasNeutralIsTheCaptureAlone() =>
            Assert.AreEqual(0.35f, BotPushRules.CaptureDone01(false, false, true, 0.35f), 1e-4f);

        [Test]
        public void AZoneItsTeamIsNotTakingCountsAsNotStarted() =>
            Assert.AreEqual(0f, BotPushRules.CaptureDone01(true, true, false, 0.2f), 1e-4f);

        [Test]
        public void ACaptureFarEnoughAlongIsFinishedBeforeGoingHome()
        {
            Assert.IsTrue(BotPushRules.KeepTaking(true, 0.7f, 0.65f));
            Assert.IsTrue(BotPushRules.KeepTaking(true, 0.65f, 0.65f), "exactly far enough");
            Assert.IsFalse(BotPushRules.KeepTaking(true, 0.6f, 0.65f), "not far enough: it goes back to defend");
            Assert.IsFalse(BotPushRules.KeepTaking(false, 0.9f, 0.65f), "not taking an enemy capital or Tier II");
        }
    }
}
