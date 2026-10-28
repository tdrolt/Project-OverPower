using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using Photon.Pun;

namespace Overpower.Tests
{
    /// <summary>
    /// Leave match (Tudor, 28 Oct): during a match every exit takes the player back to the lobby list, like a normal leave, and the
    /// match carries on for the others. Closing the game is offered only outside a match, and never in the browser (a web page cannot
    /// close itself).
    /// </summary>
    public class LeaveMatchRulesTests
    {
        // ---- the result, waiting and death-card button

        [TestCase(MatchPhase.Warmup)]
        [TestCase(MatchPhase.ThreeTeams)]
        [TestCase(MatchPhase.TwoTeams)]
        public void WhileTheMatchRunsThePanelButtonLeavesTheMatch(MatchPhase phase) =>
            Assert.AreEqual(ResultButtonAction.LeaveMatch, BackToNameScreenRules.ButtonAction(phase));

        [Test]
        public void OnceTheMatchIsOverThePanelButtonGoesBackToTheList() =>
            Assert.AreEqual(ResultButtonAction.BackToLobbyList, BackToNameScreenRules.ButtonAction(MatchPhase.Over));

        [TestCase(ResultButtonAction.LeaveMatch, true)]
        [TestCase(ResultButtonAction.BackToLobbyList, true)]
        [TestCase(ResultButtonAction.CloseGame, false)]
        public void LeavingAndGoingBackBothLeadToTheLobbyList(ResultButtonAction action, bool expected) =>
            Assert.AreEqual(expected, BackToNameScreenRules.LeadsToLobbyList(action));

        [Test]
        public void ThePanelLabelFollowsTheAction()
        {
            Assert.AreEqual("List", QuitButton.LabelFor(true, MatchPhase.Over, "List", "Leave"));
            Assert.AreEqual("Leave", QuitButton.LabelFor(true, MatchPhase.TwoTeams, "List", "Leave"), "a knocked-out player's panel mid-match");
            Assert.AreEqual("Leave", QuitButton.LabelFor(false, MatchPhase.Over, "List", "Leave"), "the waiting panel");
        }

        // ---- the Escape pop-up

        [TestCase(false, 0, false)]
        [TestCase(true, 0, false)]
        [TestCase(true, 1, true)]
        [TestCase(true, 2, true)]
        [TestCase(false, 2, false)]
        public void AMatchRunsFromTheWarmUpOn(bool inRoom, int lobbyStage, bool expected) =>
            Assert.AreEqual(expected, BackToNameScreenRules.IsMatchRunning(inRoom, lobbyStage));

        [Test]
        public void EscapeDuringAMatchLeavesIt() => Assert.IsTrue(BackToNameScreenRules.EscapeLeavesTheMatch(matchRunning: true));

        [Test]
        public void EscapeOutsideAMatchDoesNotLeaveOne() => Assert.IsFalse(BackToNameScreenRules.EscapeLeavesTheMatch(matchRunning: false));

        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        public void CloseTheGameOnlyOutsideAMatchOnDesktop(bool matchRunning, bool webPlayer, bool expected) =>
            Assert.AreEqual(expected, BackToNameScreenRules.OffersQuitGame(matchRunning, webPlayer));

        // ---- the game calls the rules

        [Test]
        public void ThePanelButtonAsksWhereItLeads() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(QuitButton), nameof(QuitButton.Quit),
                typeof(BackToNameScreenRules).GetMethod(nameof(BackToNameScreenRules.LeadsToLobbyList))));

        [Test]
        public void EscapesYesAsksWhetherItLeavesTheMatch() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(QuitConfirmPanel), "OnYesClicked",
                typeof(BackToNameScreenRules).GetMethod(nameof(BackToNameScreenRules.EscapeLeavesTheMatch))));

        [Test]
        public void EscapeShowsCloseTheGameOnlyWhenTheRuleSaysSo() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(QuitConfirmPanel), "Open",
                typeof(BackToNameScreenRules).GetMethod(nameof(BackToNameScreenRules.OffersQuitGame))));

        [Test]
        public void TheNewWordsAreInTheThemeWithATooltip()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/UiTheme.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "leaveMatchPromptText", "escapeMenuTitleText", "escapeMenuCloseText", "resultButtonLeaveMatch" })
            {
                var field = typeof(UiTheme).GetField(name);
                Assert.NotNull(field, name);
                var tip = (UnityEngine.TooltipAttribute)System.Attribute.GetCustomAttribute(field, typeof(UnityEngine.TooltipAttribute));
                Assert.NotNull(tip, name + " has no tooltip");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not in UiTheme.asset");
            }
        }

        [Test]
        public void LeavingFreesTheSeat() =>
            Assert.IsTrue(IlWiring.CallsEndingWithBool(typeof(RoomManager), nameof(RoomManager.ReturnToLobbyList),
                typeof(PhotonNetwork).GetMethod(nameof(PhotonNetwork.LeaveRoom)), false),
                "LeaveRoom() without false keeps the seat for the rejoin window");
    }
}
