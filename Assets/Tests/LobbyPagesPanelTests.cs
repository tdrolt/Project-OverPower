using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>How to play (page list, picture, text, previous and next, counter, cross) and the game mode info page (cards, heading, cross),
    /// built from the real theme and assets. They read what the panels draw, never the words themselves.</summary>
    public class LobbyPagesPanelTests
    {
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string ConquestPath = "Assets/Gameplay/Config/Modes/Conquest 3v3v3.asset";
        private const string Conquest3v3Path = "Assets/Gameplay/Config/Modes/Conquest 3v3.asset";

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

        private HowToPlayPanel NewHowToPlay() => HowToPlayPanel.Create(canvas.transform, kit);
        private ModeInfoPanel NewModeInfo() => ModeInfoPanel.Create(canvas.transform, kit);

        // ---- How to play ----

        [Test]
        public void HowToPlayOpensOnTheFirstPageWithItsTitleTextPictureAndCounter()
        {
            HowToPlayPages pages = theme.howToPlayPages;
            HowToPlayPanel panel = NewHowToPlay();
            Assert.IsTrue(panel.Show());
            Assert.IsTrue(panel.IsShowing);
            Assert.AreEqual(6, panel.PageCount);
            Assert.AreEqual(0, panel.PageIndex);
            Assert.AreEqual(pages.Pages[0].title, panel.TitleText);
            Assert.AreEqual(pages.Pages[0].text, panel.BodyText);
            Assert.AreSame(pages.Pages[0].picture, panel.ShownPicture);
            Assert.AreEqual("1 / 6", panel.CounterText);
        }

        [Test]
        public void ThePageListHasOneEntryPerPageInOrderAndMarksTheOpenOne()
        {
            HowToPlayPages pages = theme.howToPlayPages;
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            CollectionAssert.AreEqual(pages.Titles(), panel.EntryTitles);
            Assert.AreEqual(0, panel.SelectedEntry);
            panel.SelectPage(3);
            Assert.AreEqual(3, panel.SelectedEntry);
        }

        [Test]
        public void ClickingAPageListEntryShowsThatPage()
        {
            HowToPlayPages pages = theme.howToPlayPages;
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            Assert.IsTrue(panel.SelectPage(3));
            Assert.AreEqual(3, panel.PageIndex);
            Assert.AreEqual(pages.Pages[3].title, panel.TitleText);
            Assert.AreEqual(pages.Pages[3].text, panel.BodyText);
            Assert.AreSame(pages.Pages[3].picture, panel.ShownPicture);
            Assert.AreEqual("4 / 6", panel.CounterText);
            Assert.IsFalse(panel.SelectPage(6), "there is no seventh page");
            Assert.IsFalse(panel.SelectPage(-1));
            Assert.AreEqual(3, panel.PageIndex);
        }

        [Test]
        public void TheButtonsNameTheNeighbouringPagesAndStopAtTheEnds()
        {
            HowToPlayPages pages = theme.howToPlayPages;
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            Assert.IsFalse(panel.PreviousEnabled, "the first page has no page before it");
            Assert.IsTrue(panel.NextEnabled);
            Assert.IsFalse(panel.PressPrevious(), "pressing a disabled previous does nothing");
            Assert.AreEqual(0, panel.PageIndex);
            StringAssert.Contains(pages.Pages[1].title, panel.NextText);

            Assert.IsTrue(panel.PressNext());
            Assert.AreEqual(1, panel.PageIndex);
            StringAssert.Contains(pages.Pages[0].title, panel.PreviousText);
            StringAssert.Contains(pages.Pages[2].title, panel.NextText);

            for (int i = 0; i < 4; i++) Assert.IsTrue(panel.PressNext());
            Assert.AreEqual(5, panel.PageIndex);
            Assert.IsFalse(panel.NextEnabled, "the last page has no page after it");
            Assert.IsFalse(panel.PressNext(), "it does not wrap round to the first page");
            Assert.AreEqual(5, panel.PageIndex);
            StringAssert.Contains(pages.Pages[4].title, panel.PreviousText);
            Assert.AreEqual("6 / 6", panel.CounterText);
        }

        [Test]
        public void ThePreviousButtonGoesBackOnePage()
        {
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            panel.SelectPage(4);
            Assert.IsTrue(panel.PressPrevious());
            Assert.AreEqual(3, panel.PageIndex);
        }

        [Test]
        public void TheCrossClosesHowToPlayOnAnyPage()
        {
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            panel.SelectPage(5);
            Assert.IsTrue(panel.CloseButton.Root.activeInHierarchy, "the cross is on every page");
            panel.CloseButton.Press();
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void TheCrossStaysAtTheTopRightOfTheCard()
        {
            HowToPlayPanel panel = NewHowToPlay();
            RectTransform rect = panel.CloseButton.Rect;
            Assert.AreEqual(new Vector2(1f, 1f), rect.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), rect.anchorMax);
            Assert.Less(rect.anchoredPosition.x, 0f);
            Assert.Less(rect.anchoredPosition.y, 0f);
        }

        [Test]
        public void HowToPlayRemembersTheLastPageItWasLeftOn()
        {
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            panel.SelectPage(2);
            panel.CloseButton.Press();
            panel.Show();
            Assert.AreEqual(2, panel.PageIndex);
        }

        [Test]
        public void HowToPlayClosesWhenAMatchStarts()
        {
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            panel.ApplyMatchState(true, 0);
            Assert.IsTrue(panel.IsShowing);
            panel.ApplyMatchState(true, 1);
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void EscapeClosesHowToPlayAndAnOpenPageOwnsEscape()
        {
            HowToPlayPanel panel = NewHowToPlay();
            panel.Show();
            Assert.IsTrue(LobbyOverlayPanel.AnyPageOpen);
            Assert.IsTrue(panel.CloseOnEscape(true));
            Assert.IsFalse(panel.IsShowing);
            Assert.IsFalse(LobbyOverlayPanel.AnyPageOpen);
        }

        [Test]
        public void EveryPagesTextFitsItsBoxAtTheSizeTheSamePageSizeIsChosenFor()
        {
            HowToPlayPanel panel = NewHowToPlay();
            Assert.LessOrEqual(panel.TextSizeUsed, theme.howToPlayTextSize);
            Assert.GreaterOrEqual(panel.TextSizeUsed, theme.howToPlayTextMinSize);
            Assert.IsTrue(panel.AllPagesFit, "the longest page does not fit its box even at the smallest size: shorten it or enlarge the box");
        }

        // ---- the game mode info page ----

        [Test]
        public void TheModeInfoPageShowsTheModesNameAsItsHeadingAndOneCardPerInfoCard()
        {
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ConquestPath);
            Assert.NotNull(mode);
            ModeInfoPanel panel = NewModeInfo();
            Assert.IsTrue(panel.Show(mode));
            Assert.AreEqual(mode.DisplayName, panel.TitleText);
            Assert.AreEqual(mode.InfoCards.Count, panel.CardCount);
            Assert.Greater(panel.CardCount, 0);
            for (int i = 0; i < panel.CardCount; i++)
            {
                Assert.AreEqual(mode.InfoCards[i].title, panel.CardTitle(i));
                Assert.AreEqual(mode.InfoCards[i].text, panel.CardText(i));
                Assert.AreEqual(mode.InfoCards[i].accent, panel.CardAccent(i));
            }
            Assert.IsNull(panel.EmptyText, "a mode with cards has no empty line");
            Assert.IsFalse(string.IsNullOrEmpty(panel.KickerText), "the small GAME MODE heading is there");
        }

        [Test]
        public void ThreeCardsStandInARowAndTheInfoPageFitsInsideItsCard()
        {
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ConquestPath);
            ModeInfoPanel panel = NewModeInfo();
            panel.Show(mode);
            Assert.AreEqual(theme.modeInfoColumns, panel.Columns);
            Assert.AreEqual(Mathf.CeilToInt(mode.InfoCards.Count / (float)theme.modeInfoColumns), panel.Rows);
            Assert.LessOrEqual(panel.CardTextSizeUsed, theme.modeInfoCardTextSize);
            Assert.GreaterOrEqual(panel.CardTextSizeUsed, theme.modeInfoCardTextMinSize);
            Assert.IsTrue(panel.AllCardsFit, "a card's text does not fit even at the smallest size");
        }

        [Test]
        public void TheModeInfoPageShowsTheCardsOfTheModeItIsOpenedFor()
        {
            var conquest = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ConquestPath);
            var small = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(Conquest3v3Path);
            ModeInfoPanel panel = NewModeInfo();
            panel.Show(conquest);
            Assert.AreEqual(conquest.DisplayName, panel.TitleText);
            panel.Hide();
            panel.Show(small);
            Assert.AreEqual(small.DisplayName, panel.TitleText);
            Assert.AreEqual(small.InfoCards.Count, panel.CardCount);
        }

        [Test]
        public void AModeWithNoInfoCardsSaysSoInsteadOfShowingNothing()
        {
            // Every shipped mode has cards now (Dominion's came in Task 13), so a mode made here stands for "a mode nobody wrote cards for".
            var mode = ScriptableObject.CreateInstance<GameModeDefinition>();
            try
            {
                Assert.AreEqual(0, mode.InfoCards.Count, "this test needs a mode without cards");
                ModeInfoPanel panel = NewModeInfo();
                panel.Show(mode);
                Assert.AreEqual(0, panel.CardCount);
                Assert.AreEqual(theme.modeInfoNoCardsText, panel.EmptyText);
            }
            finally { Object.DestroyImmediate(mode); }
        }

        [Test]
        public void TheModeInfoPageHandlesNoModeAtAll()
        {
            ModeInfoPanel panel = NewModeInfo();
            Assert.DoesNotThrow(() => panel.Show((GameModeDefinition)null));
            Assert.AreEqual(0, panel.CardCount);
        }

        [Test]
        public void TheCrossClosesTheModeInfoPageAndItClosesWhenAMatchStarts()
        {
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ConquestPath);
            ModeInfoPanel panel = NewModeInfo();
            panel.Show(mode);
            panel.CloseButton.Press();
            Assert.IsFalse(panel.IsShowing);
            panel.Show(mode);
            panel.ApplyMatchState(true, 2);
            Assert.IsFalse(panel.IsShowing);
        }

        [Test]
        public void TheConquestCardsKeepTheirAccentColoursAsTheTopEdge()
        {
            var mode = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(ConquestPath);
            ModeInfoPanel panel = NewModeInfo();
            panel.Show(mode);
            var accents = new HashSet<Color>();
            for (int i = 0; i < panel.CardCount; i++) accents.Add(panel.CardEdgeColour(i));
            var expected = new HashSet<Color>();
            foreach (GameModeDefinition.InfoCard card in mode.InfoCards) expected.Add(card.accent);
            Assert.IsTrue(accents.SetEquals(expected), "the coloured edge on each card is the card's accent");
        }
    }
}
