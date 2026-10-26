using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>What the warm-up bar shows for each message: the End warm-up button shown / greyed, and the reason line (lobby Task 10 review).</summary>
    public class WarmupBarRulesTests
    {
        [Test]
        public void ThereIsNothingToShowOnceLiveOrWithNoMessage()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.None, -1, 3);
            Assert.IsFalse(view.ButtonShown);
            Assert.IsFalse(view.ReasonShown);
            Assert.IsFalse(view.Countdown);
        }

        [Test]
        public void TheCountdownHasNoButtonAndNoReason()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.Countdown, 1, 4);
            Assert.IsTrue(view.Countdown);
            Assert.IsFalse(view.ButtonShown);
            Assert.IsFalse(view.ReasonShown);
        }

        [Test]
        public void TheHostSeesEndWarmupPressableWhenItIsAllowed()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.HostMayEnd, -1, 4);
            Assert.IsTrue(view.ButtonShown);
            Assert.IsTrue(view.ButtonEnabled);
            Assert.IsFalse(view.ReasonShown);
            Assert.IsFalse(view.Countdown);
        }

        [Test]
        public void ABlockedHostSeesEndWarmupGreyedWithTheTeamThatHasNobody()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.HostBlocked, 2, 3);
            Assert.IsTrue(view.ButtonShown);
            Assert.IsFalse(view.ButtonEnabled);
            Assert.IsTrue(view.ReasonShown);
        }

        [Test]
        public void TheReasonWaitsUntilSomeoneIsPresentSoTheHostsOwnTeamIsNotNamedEmptyRightAfterStart()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.HostBlocked, 0, 0);
            Assert.IsTrue(view.ButtonShown, "the button is still there, greyed");
            Assert.IsFalse(view.ButtonEnabled);
            Assert.IsFalse(view.ReasonShown, "no player counted yet: the team properties have not landed");
        }

        [Test]
        public void NoTeamToNameMeansNoReasonLine()
        {
            Assert.IsFalse(WarmupBarRules.ViewFor(WarmupMessage.HostBlocked, -1, 3).ReasonShown);
        }

        [Test]
        public void EveryoneElseSeesNoButtonAndNoReason()
        {
            WarmupBarView view = WarmupBarRules.ViewFor(WarmupMessage.WaitingForHost, 1, 3);
            Assert.IsFalse(view.ButtonShown);
            Assert.IsFalse(view.ReasonShown);
        }
    }
}
