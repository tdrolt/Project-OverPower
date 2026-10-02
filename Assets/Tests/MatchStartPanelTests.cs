using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The pure pieces of the panel's own decision - which warm-up line shows and what the Start button says -
    /// extracted onto internal static methods (see MatchStartPanel's own comments on each) precisely so they are testable
    /// with no MonoBehaviour, no Photon room and no UiTheme asset. Everything else (the actual click, the actual
    /// label/line text, the button's on-screen position) still needs the Play Mode check.</summary>
    public class MatchStartPanelTests
    {
        [Test]
        public void EachWarmupMessageMapsToItsOwnLine()
        {
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Waiting, MatchStartPanel.WarmupLineFor(WarmupMessage.WaitingForTeams));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Host, MatchStartPanel.WarmupLineFor(WarmupMessage.HostMayStart));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Guest, MatchStartPanel.WarmupLineFor(WarmupMessage.WaitingForHost));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.Countdown, MatchStartPanel.WarmupLineFor(WarmupMessage.Countdown));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsWaiting, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsWaitingForPlayers));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsHost, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsHostMayStart));
            Assert.AreEqual(MatchStartPanel.WarmupLineKey.TwoTeamsGuest, MatchStartPanel.WarmupLineFor(WarmupMessage.TwoTeamsWaitingForHost));
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
