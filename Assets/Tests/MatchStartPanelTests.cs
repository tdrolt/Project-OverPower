using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Two-team lobby, Task 3: the pure pieces of the panel's own decision - which mode the switch
    /// button names, whether it is clickable, and which warm-up line wins - extracted onto internal static
    /// methods (see MatchStartPanel's own comments on each) precisely so they are testable with no MonoBehaviour,
    /// no Photon room and no UiTheme asset. Everything else (the actual click, the actual label/line text, the
    /// button's on-screen position) still needs the two-client Play Mode check the brief asks for.</summary>
    public class MatchStartPanelTests
    {
        [Test]
        public void TheSwitchButtonAlwaysNamesTheOtherMode()
        {
            Assert.AreEqual(MatchStartRules.TwoTeams, MatchStartPanel.OtherLobbyMode(MatchStartRules.ThreeTeams));
            Assert.AreEqual(MatchStartRules.ThreeTeams, MatchStartPanel.OtherLobbyMode(MatchStartRules.TwoTeams));
        }

        [Test]
        public void SwitchingToTwoTeamsNeedsMaySwitchToTwoTeams()
        {
            Assert.IsTrue(MatchStartPanel.SwitchButtonInteractable(MatchStartRules.ThreeTeams, maySwitchToTwoTeams: true, maySwitchToThreeTeams: false));
            Assert.IsFalse(MatchStartPanel.SwitchButtonInteractable(MatchStartRules.ThreeTeams, maySwitchToTwoTeams: false, maySwitchToThreeTeams: true),
                "7+ already in the room: the switch to two teams greys out");
        }

        [Test]
        public void SwitchingBackToThreeTeamsNeedsMaySwitchToThreeTeams()
        {
            Assert.IsTrue(MatchStartPanel.SwitchButtonInteractable(MatchStartRules.TwoTeams, maySwitchToTwoTeams: false, maySwitchToThreeTeams: true));
            Assert.IsFalse(MatchStartPanel.SwitchButtonInteractable(MatchStartRules.TwoTeams, maySwitchToTwoTeams: true, maySwitchToThreeTeams: false),
                "the teams got fixed on a stale frame - the belt-and-braces read wins, not an assumption");
        }

        [Test]
        public void TheTooManyReasonShowsOnlyToTheHostInThreeTeamModeInTheWarmUpWhenTheSwitchIsRefused()
        {
            Assert.IsTrue(MatchStartPanel.ShowTooManyForHost(isHost: true, StartState.Warmup, MatchStartRules.ThreeTeams, maySwitchToTwoTeams: false));

            Assert.IsFalse(MatchStartPanel.ShowTooManyForHost(false, StartState.Warmup, MatchStartRules.ThreeTeams, false), "not the host");
            Assert.IsFalse(MatchStartPanel.ShowTooManyForHost(true, StartState.CountingDown, MatchStartRules.ThreeTeams, false), "not the warm-up");
            Assert.IsFalse(MatchStartPanel.ShowTooManyForHost(true, StartState.Live, MatchStartRules.ThreeTeams, false), "not the warm-up");
            Assert.IsFalse(MatchStartPanel.ShowTooManyForHost(true, StartState.Warmup, MatchStartRules.TwoTeams, false), "already two teams - nothing to refuse switching back");
            Assert.IsFalse(MatchStartPanel.ShowTooManyForHost(true, StartState.Warmup, MatchStartRules.ThreeTeams, true), "the switch is actually allowed");
        }

        [Test]
        public void TheTooManyReasonOverridesEveryOtherWarmUpLine()
        {
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TooManyForHost,
                MatchStartPanel.WarmupLineFor(WarmupMessage.WaitingForTeams, tooManyForHost: true));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TooManyForHost,
                MatchStartPanel.WarmupLineFor(WarmupMessage.HostMayStart, tooManyForHost: true));
        }

        [Test]
        public void EachWarmupMessageMapsToItsOwnLineWhenNotTooMany()
        {
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Waiting, MatchStartPanel.WarmupLineFor(WarmupMessage.WaitingForTeams, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Host, MatchStartPanel.WarmupLineFor(WarmupMessage.HostMayStart, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Guest, MatchStartPanel.WarmupLineFor(WarmupMessage.WaitingForHost, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Countdown, MatchStartPanel.WarmupLineFor(WarmupMessage.Countdown, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsWaiting, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsWaitingForPlayers, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsHost, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsHostMayStart, false));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsGuest, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsWaitingForHost, false));
        }

        [Test]
        public void TheStartButtonNamesTheTeamsTheMatchWillStartWith()
        {
            Assert.AreEqual(2, MatchStartPanel.StartButtonTeams(2));
            Assert.AreEqual(3, MatchStartPanel.StartButtonTeams(3));
        }

        [Test]
        public void TheStartButtonNeverNamesFewerThanTwoOrMoreThanThree()
        {
            Assert.AreEqual(2, MatchStartPanel.StartButtonTeams(0), "the button is hidden then, but the text must stay sane");
            Assert.AreEqual(2, MatchStartPanel.StartButtonTeams(1));
            Assert.AreEqual(3, MatchStartPanel.StartButtonTeams(4));
        }

        [Test]
        public void TheStartButtonTextFillsInTheTeamCount()
        {
            Assert.AreEqual("Start match (3 teams)", MatchStartPanel.StartButtonText("Start match ({0} teams)", 3));
            Assert.AreEqual("Start match (2 teams)", MatchStartPanel.StartButtonText("Start match ({0} teams)", 2));
        }

        [Test]
        public void ABrokenStartButtonFormatShowsTheRawTextInsteadOfThrowing()
        {
            Assert.AreEqual("Start {match", MatchStartPanel.StartButtonText("Start {match", 3));
        }
    }
}
