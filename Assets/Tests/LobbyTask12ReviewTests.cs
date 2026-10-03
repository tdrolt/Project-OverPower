using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Lobby;
using Overpower.Match;
using Overpower.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Task 12 review fixes: the fonts hold the letters of the languages the players write in, one Escape closes one thing, the page
    /// and the warm-up bar are driven by a testable Tick, and every mode's info page fits.</summary>
    public class LobbyTask12ReviewTests
    {
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";

        private UiTheme theme;
        private LobbyUiKit kit;
        private GameObject canvas;

        [SetUp]
        public void SetUp()
        {
            theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            kit = new LobbyUiKit(theme);
            canvas = new GameObject("lobby canvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvas);
            kit.Dispose();
        }

        // ---- fonts ----

        private static IEnumerable<char> LettersEveryAtlasNeeds()
        {
            for (char c = ' '; c <= 'ſ'; c++) yield return c;
            for (char c = 'Ș'; c <= 'ț'; c++) yield return c;
            yield return '‹';
            yield return '›';
        }

        // Public Sans has no IJ / ij ligature letters, no n-with-preceding-apostrophe and no long s: these are not letters a player types.
        private static readonly HashSet<char> NotInPublicSans = new HashSet<char> { 'Ĳ', 'ĳ', 'ŉ', 'ſ' };

        [TestCase("lobbyDisplayFont")]
        [TestCase("lobbyBodyFont")]
        [TestCase("lobbyBoldFont")]
        public void EachLobbyFontHoldsLatin1LatinExtendedARomanianCommaLettersAndTheArrows(string field)
        {
            var font = (TMP_FontAsset)typeof(UiTheme).GetField(field).GetValue(theme);
            Assert.NotNull(font);
            Assert.AreEqual(AtlasPopulationMode.Static, font.atlasPopulationMode, "a static atlas needs every letter in it already");
            var missing = new List<string>();
            foreach (char c in LettersEveryAtlasNeeds())
            {
                if (field != "lobbyDisplayFont" && NotInPublicSans.Contains(c)) continue;
                if (!font.HasCharacter(c, false, false)) missing.Add(((int)c).ToString("X4"));
            }
            Assert.IsEmpty(missing, font.name + " lacks: " + string.Join(" ", missing));
        }

        [Test]
        public void NoLobbyFontNeedsAFallbackForRomanianOrPolishNames()
        {
            foreach (var font in new[] { theme.lobbyDisplayFont, theme.lobbyBodyFont, theme.lobbyBoldFont })
                foreach (char c in "Ștefan ăîâ ț ß é ł ő ű ż")
                    Assert.IsTrue(c == ' ' || font.HasCharacter(c, false, false), font.name + " lacks " + c);
        }

        // ---- Escape ----

        private HowToPlayPanel NewPage()
        {
            HowToPlayPanel panel = HowToPlayPanel.Create(canvas.transform, kit);
            Assert.IsTrue(panel.TryShow(null, false, 0));
            return panel;
        }

        [Test]
        public void EscapeClosesAnOpenPage()
        {
            HowToPlayPanel panel = NewPage();
            panel.Tick(false, 0, true, false);
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void NoEscapeLeavesThePageOpen()
        {
            HowToPlayPanel panel = NewPage();
            panel.Tick(false, 0, false, false);
            Assert.IsTrue(panel.IsShowing);
        }

        [Test]
        public void EscapeThatClosesTheChatDoesNotAlsoCloseThePage()
        {
            HowToPlayPanel panel = NewPage();
            panel.Tick(false, 0, false, false); // the chat was NOT open last frame
            panel.Tick(false, 0, true, true);   // the Escape: the chat is open this frame (and only this frame)
            Assert.IsTrue(panel.IsShowing, "the chat is open: Escape closes it first");
        }

        [Test]
        public void EscapeThatClosesTheChatEarlierInTheSameFrameDoesNotCloseThePage()
        {
            HowToPlayPanel panel = NewPage();
            panel.Tick(false, 0, false, true);  // chat open last frame
            panel.Tick(false, 0, true, false);  // the chat already closed itself this frame, then Escape reaches the page
            Assert.IsTrue(panel.IsShowing, "the chat was open last frame: that Escape belonged to the chat");
            panel.Tick(false, 0, true, false);  // the next Escape is the page's
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void ThePageHidesWhenTheMatchStartsInTheRoomAndNotBefore()
        {
            HowToPlayPanel panel = NewPage();
            panel.Tick(true, LobbySeatRules.LobbyBeforeStart, false, false);
            Assert.IsTrue(panel.IsShowing);
            panel.Tick(true, LobbySeatRules.LobbyWarmup, false, false);
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void ThePageCannotBeOpenedDuringAMatch()
        {
            HowToPlayPanel panel = HowToPlayPanel.Create(canvas.transform, kit);
            Assert.IsFalse(panel.TryShow(null, true, LobbySeatRules.LobbyWarmup));
            Assert.IsFalse(panel.IsShowing);
            Assert.IsTrue(panel.TryShow(null, true, LobbySeatRules.LobbyBeforeStart));
            Assert.IsTrue(panel.IsShowing);
        }

        // ---- the warm-up bar ----

        [Test]
        public void TheWarmupBarShowsTheHostsEndButtonAndHidesOutsideTheWarmup()
        {
            WarmupBar bar = WarmupBar.Create(canvas.transform, kit);
            Assert.IsFalse(bar.IsShowing);
            bar.Tick(true, WarmupMessage.HostMayEnd, 4, "Host", -1, 4);
            Assert.IsTrue(bar.IsShowing);
            Assert.IsTrue(bar.EndInteractable);
            bar.Tick(false, WarmupMessage.None, 0, "", -1, 0);
            Assert.IsFalse(bar.IsShowing);
        }

        [Test]
        public void TheWarmupBarGreysEndAndNamesTheTeamThatHasNobody()
        {
            WarmupBar bar = WarmupBar.Create(canvas.transform, kit);
            bar.Tick(true, WarmupMessage.HostBlocked, 2, "Host", 1, 2);
            Assert.IsTrue(bar.EndVisible);
            Assert.IsFalse(bar.EndInteractable);
            Assert.IsNotEmpty(bar.ReasonText);
            bar.Tick(true, WarmupMessage.HostBlocked, 0, "Host", 1, 0);
            Assert.IsEmpty(bar.ReasonText, "nobody is counted yet: no team is named");
        }

        [Test]
        public void TheWarmupBarGuestSeesNoButtonAndTheCountdownReplacesTheTitle()
        {
            WarmupBar bar = WarmupBar.Create(canvas.transform, kit);
            bar.Tick(true, WarmupMessage.WaitingForHost, 0, "Host", -1, 3);
            Assert.IsFalse(bar.EndVisible);
            StringAssert.Contains("Host", bar.InfoText);
            string warmupTitle = bar.TitleText;
            bar.Tick(true, WarmupMessage.Countdown, 3, "Host", -1, 3);
            Assert.AreNotEqual(warmupTitle, bar.TitleText);
            StringAssert.Contains("3", bar.TitleText);
            Assert.IsFalse(bar.EndVisible);
        }

        // ---- every mode's info page fits ----

        [Test]
        public void EveryModesInfoPageFitsItsCard()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameModeDefinition", new[] { "Assets/Gameplay/Config/Modes" });
            int checkedModes = 0;
            foreach (string guid in guids)
            {
                var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (mode == null || mode.InfoCards.Count == 0) continue;
                ModeInfoPanel panel = ModeInfoPanel.Create(canvas.transform, kit);
                Assert.IsTrue(panel.Show(mode));
                Assert.AreEqual(mode.InfoCards.Count, panel.CardCount, mode.name);
                Assert.IsTrue(panel.AllCardsFit, mode.name + ": a card's text does not fit even at the smallest size");
                Assert.GreaterOrEqual(panel.CardTextSizeUsed, theme.modeInfoCardTextMinSize, mode.name);
                checkedModes++;
                Object.DestroyImmediate(panel.gameObject);
            }
            Assert.GreaterOrEqual(checkedModes, 2, "both Conquest modes have cards (Dominion has none yet)");
        }
    }
}
