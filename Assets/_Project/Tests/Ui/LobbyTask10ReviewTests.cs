using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Lobby;
using Overpower.Match;
using Overpower.UI;
using Photon.Realtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>The review of lobby Task 10 (room screen, warm-up bar) and the Task 9 fixes' leftovers: the bar on its own canvas below the shop and
    /// scoreboard, the pages closing when a match starts and on Escape, the missing-theme fallbacks, and the numbers moved to the theme.</summary>
    public class LobbyTask10ReviewTests
    {
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private UiTheme theme;
        private readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp() => theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private T Made<T>(T o) where T : Object { made.Add(o); return o; }

        // ---- the warm-up bar sits under the shop and the scoreboard ----

        [Test]
        public void TheWarmupBarHasItsOwnCanvasBelowTheShopAndTheScoreboard()
        {
            var kit = new LobbyUiKit(theme);
            var root = Made(new GameObject("lobby canvas", typeof(RectTransform), typeof(Canvas))); // the lobby screens' canvas: the bar's own canvas nests in it
            WarmupBar bar = WarmupBar.Create(root.transform, kit);
            Canvas own = bar.GetComponent<Canvas>();
            Assert.NotNull(own, "the bar draws on a canvas of its own: on the lobby canvas (sort order 100) it covered the shop (-5) and the scoreboard (30)");
            Assert.IsTrue(own.overrideSorting);
            Assert.AreEqual(-10, own.sortingOrder);
            Assert.NotNull(bar.GetComponent<GraphicRaycaster>(), "End warm-up is clicked: its canvas needs a raycaster");
            kit.Dispose();
        }

        // ---- the pages never stay up into a match ----

        [TestCase(false, 0, true)]   // the list
        [TestCase(false, 2, true)]   // not in a room: whatever the last room said does not matter
        [TestCase(true, 0, true)]    // the lobby room before Start
        [TestCase(true, 1, false)]   // the warm-up
        [TestCase(true, 2, false)]   // live
        public void ThePagesMayBeShownOnlyOutsideAMatch(bool inRoom, int stage, bool expected) =>
            Assert.AreEqual(expected, LobbyScreenRules.OverlayMayBeShown(inRoom, stage));

        [Test]
        public void AnOpenPageClosesTheMomentTheMatchStartsAndStaysOpenInTheLobby()
        {
            var kit = new LobbyUiKit(theme);
            var root = Made(new GameObject("canvas", typeof(RectTransform)));
            ModeInfoPanel panel = ModeInfoPanel.Create(root.transform, kit);
            panel.Show("Conquest 3v3v3");
            panel.ApplyMatchState(true, 0);
            Assert.IsTrue(panel.IsShowing, "a lobby room that has not started keeps its page");
            panel.ApplyMatchState(true, 1);
            Assert.IsFalse(panel.IsShowing, "Start landed: the page is gone, its shade would block every shot");
            kit.Dispose();
        }

        [Test]
        public void EscapeClosesAnOpenPageAndDoesNothingWithNoneOpen()
        {
            var kit = new LobbyUiKit(theme);
            var root = Made(new GameObject("canvas", typeof(RectTransform)));
            ModeInfoPanel panel = ModeInfoPanel.Create(root.transform, kit);
            Assert.IsFalse(panel.CloseOnEscape(true), "nothing open: Escape is not ours");
            panel.Show("Conquest");
            Assert.IsFalse(panel.CloseOnEscape(false));
            Assert.IsTrue(panel.IsShowing);
            Assert.IsTrue(panel.CloseOnEscape(true));
            Assert.IsFalse(panel.IsShowing);
            kit.Dispose();
        }

        [Test]
        public void AnOpenPageOwnsEscapeSoTheQuitQuestionDoesNotOpenOnTopOfIt()
        {
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, false, pageOpenThisFrame: true));
            Assert.IsTrue(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, false, pageOpenLastFrame: true),
                "the page closed itself on this very key press");
            Assert.IsFalse(EscapeOwnershipRule.BelongsToShopOrChat(false, false, false, false, false, false));
        }

        // ---- a missing theme is an error in the log and the built-in words, never a silent return or a throw ----

        [Test]
        public void AJoinRefusedWithNoThemeLogsAnErrorAndStillSaysWhy()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no UiTheme"));
            Assert.AreEqual(LobbyDirectory.FallbackFullText, LobbyDirectory.JoinFailureText(ErrorCode.GameFull, null));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no UiTheme"));
            Assert.AreEqual(LobbyDirectory.FallbackFailedText, LobbyDirectory.JoinFailureText(ErrorCode.InternalServerError, null));
        }

        [Test]
        public void TheBuiltInJoinWordsAreTheWordsTheThemeStartsWith()
        {
            var fresh = Made(ScriptableObject.CreateInstance<UiTheme>());
            Assert.AreEqual(fresh.lobbyJoinFullText, LobbyDirectory.FallbackFullText);
            Assert.AreEqual(fresh.lobbyJoinClosedText, LobbyDirectory.FallbackClosedText);
            Assert.AreEqual(fresh.lobbyJoinGoneText, LobbyDirectory.FallbackGoneText);
            Assert.AreEqual(fresh.lobbyJoinFailedText, LobbyDirectory.FallbackFailedText);
            Assert.AreEqual(fresh.lobbyLateJoinFullText, LobbyStart.FallbackLateJoinFullText);
            Assert.AreEqual(fresh.lobbyLateJoinNoSeatText, LobbyStart.FallbackLateJoinNoSeatText);
        }

        [Test]
        public void ACustomLateJoinTextFromTheThemeReachesTheGiveUpPath()
        {
            var custom = Made(ScriptableObject.CreateInstance<UiTheme>());
            custom.lobbyLateJoinFullText = "NO ROOM";
            custom.lobbyLateJoinNoSeatText = "NO ANSWER";
            Assert.AreEqual("NO ROOM", LobbyStart.LateJoinText(custom, noSeatInTime: false));
            Assert.AreEqual("NO ANSWER", LobbyStart.LateJoinText(custom, noSeatInTime: true));
        }

        [Test]
        public void ALateJoinWithNoThemeLogsAnErrorAndUsesTheBuiltInWords()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no UiTheme"));
            Assert.AreEqual(LobbyStart.FallbackLateJoinFullText, LobbyStart.LateJoinText(null, false));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no UiTheme"));
            Assert.AreEqual(LobbyStart.FallbackLateJoinNoSeatText, LobbyStart.LateJoinText(null, true));
        }

        // ---- layout numbers that were in the code now live in the theme ----

        [Test]
        public void TheLayoutNumbersThatWereInTheCodeAreInTheTheme()
        {
            Assert.AreEqual(3f, theme.lobbyRoomHeaderGap);
            Assert.AreEqual(15f, theme.lobbyRoomModeRowGap);
            Assert.AreEqual(0.62f, theme.lobbyRoomInfoIconTextFactor);
            Assert.AreEqual(0.4f, theme.lobbyRoomModeIconGapFactor);
            Assert.AreEqual(4f, theme.lobbyRoomModeButtonSlack);
            Assert.AreEqual(1.4f, theme.lobbyRoomSideNameLine);
            Assert.AreEqual(20.88f, theme.warmupBarHostRightPadding);
            Assert.AreEqual(23.04f, theme.nameScreenFieldPadding);
            Assert.AreEqual(3f, theme.createModeRadiusExtra);
        }
    }
}
