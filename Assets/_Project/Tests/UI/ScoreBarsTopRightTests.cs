using System.Reflection;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>Dominion's score bars sit top right, left of the corner minimap, each team's bar followed by its round-win dots; the round clock stays
    /// alone at the top centre. Layout numbers in the overlap cases are literals in the theme's range, never read from the asset.</summary>
    public class ScoreBarsTopRightTests
    {
        // ---------------------------------------------------------------- the layout rule

        [Test] public void ARowIsThePointsBoxTheBarAGapThenTheDots()
        {
            Assert.AreEqual(45f, ScoreBarLayout.DotsWidth(2, 18f, 9f), 1e-4f, "two dots and one gap");
            Assert.AreEqual(18f, ScoreBarLayout.DotsWidth(1, 18f, 9f), 1e-4f);
            Assert.AreEqual(0f, ScoreBarLayout.DotsWidth(0, 18f, 9f), 1e-4f, "no dots, no width");
            Assert.AreEqual(339f, ScoreBarLayout.DotsLeft(75f, 255f, 9f), 1e-4f, "the dots start after the box, the bar and the gap");
            Assert.AreEqual(384f, ScoreBarLayout.RowWidth(75f, 255f, 9f, 45f), 1e-4f);
            Assert.AreEqual(330f, ScoreBarLayout.RowWidth(75f, 255f, 9f, 0f), 1e-4f, "no dots, no gap after the bar");
        }

        [Test] public void TheRowsStackWithAGapBetweenThem()
        {
            Assert.AreEqual(75f, ScoreBarLayout.StackHeight(2, 33f, 9f), 1e-4f);
            Assert.AreEqual(117f, ScoreBarLayout.StackHeight(3, 33f, 9f), 1e-4f);
            Assert.AreEqual(0f, ScoreBarLayout.StackHeight(0, 33f, 9f), 1e-4f);
        }

        [Test] public void TheBlockEndsTheGapLeftOfTheMinimapAndStartsTheTopMarginUnderTheScreenTop()
        {
            Rect minimap = HudScreenLayout.CornerMinimapRect(24f, 5f, 340f); // x and y -369 .. -29
            Rect block = ScoreBarLayout.Block(minimap, 20f, 30f, 300f, 90f);
            Assert.AreEqual(-389f, block.xMax, 1e-3f);
            Assert.AreEqual(-30f, block.yMax, 1e-3f);
            Assert.AreEqual(300f, block.width, 1e-3f);
            Assert.AreEqual(90f, block.height, 1e-3f);
        }

        [TestCase(1920, 1080, 2)]
        [TestCase(1920, 1080, 3)]
        [TestCase(1280, 720, 2)]
        [TestCase(1280, 720, 3)]
        public void TheBarsClearTheMinimapItsLinesAndTheRoundClock(int screenWidth, int screenHeight, int teams)
        {
            const float hudScale = 0.8f;
            float factor = HudScreenLayout.CanvasScaleFactor(new Vector2(1920f, 1080f), 0.5f, screenWidth, screenHeight);
            var canvas = new Vector2(screenWidth / factor, screenHeight / factor);

            Rect minimap = HudScreenLayout.CornerMinimapRect(24f, 5f, 340f);
            // Lines under the map: hung from the lowest a turned triangle can reach (its circumradius, at most half the box), two lines of text.
            Vector2 linesTop = MinimapLabelPlacement.LabelTop(minimap, new Vector2(340f, 340f), 170f, 6f);
            float linesHeight = (20f + 14f) * 1.5f;
            var lines = new Rect(linesTop.x - 170f, linesTop.y - linesHeight, 340f, linesHeight);

            // The round clock at the top centre and the payout flash under it (the flash is up to one and a half clocks wide).
            float clockWidth = teams == 3 ? 200f : 190f;
            float flashWidth = clockWidth * 1.5f;
            float clockBottom = 30f + 80f + 6f + 36f * 1.5f;
            var clockBand = new Rect(-canvas.x / 2f - flashWidth / 2f, -clockBottom, flashWidth, clockBottom - 30f);

            float width = ScoreBarLayout.RowWidth(75f, 255f, 9f, ScoreBarLayout.DotsWidth(2, 18f, 9f)) * hudScale;
            float height = ScoreBarLayout.StackHeight(teams, 33f, 9f) * hudScale;
            Rect block = ScoreBarLayout.Block(minimap, 24f, 30f, width, height);

            Assert.Greater(block.width, 0f);
            Assert.IsFalse(block.Overlaps(minimap), "clear of the minimap");
            Assert.IsFalse(block.Overlaps(lines), "clear of the lines under it");
            Assert.IsFalse(block.Overlaps(clockBand), "clear of the round clock and its flash");
            Assert.GreaterOrEqual(block.xMin, -canvas.x, "on screen on the left");
            Assert.GreaterOrEqual(block.yMin, -canvas.y, "on screen at the bottom");
            Assert.LessOrEqual(block.yMax, 0f, "on screen at the top");
        }

        // ---------------------------------------------------------------- the stage rule

        [Test] public void TheDotsShowInARoundItsOvertimeAndSuddenDeath()
        {
            Assert.IsTrue(DominionScoreBarRules.DotsShownIn(DominionStage.Round));
            Assert.IsTrue(DominionScoreBarRules.DotsShownIn(DominionStage.Overtime));
            Assert.IsTrue(DominionScoreBarRules.DotsShownIn(DominionStage.SuddenDeath), "the round wins stay on screen while the circle decides the round");
            Assert.IsFalse(DominionScoreBarRules.DotsShownIn(DominionStage.Break), "the break card shows them");
            Assert.IsFalse(DominionScoreBarRules.DotsShownIn(DominionStage.Over));
            Assert.IsFalse(DominionScoreBarRules.DotsShownIn(DominionStage.None));
        }

        // ---------------------------------------------------------------- the drawn bars (the theme asset gives structure only)

        private const string ThemePath = "Assets/_Project/Design/Settings/UiTheme.asset";
        private UiTheme theme;
        private LobbyUiKit kit;
        private GameObject canvasGo;

        [SetUp] public void SetUp()
        {
            theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            kit = new LobbyUiKit(theme);
            canvasGo = new GameObject("score bars canvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(canvasGo);
            kit.Dispose();
        }

        private Transform Draw(int[] teams, int[] wins, bool pointsShown)
        {
            var bars = new ScoreBars(kit, canvasGo.transform);
            bars.Refresh(teams, new[] { 100, 50, 20 }, wins, 2, pointsShown);
            return canvasGo.transform.Find("Score Bars");
        }

        private static RectTransform Child(Transform root, string name)
        {
            Transform t = root.Find(name);
            Assert.IsNotNull(t, name);
            return (RectTransform)t;
        }

        [Test] public void EachTeamsBarIsFollowedByItsTwoDotsFilledForTheRoundsItWon()
        {
            Transform root = Draw(new[] { 0, 1 }, new[] { 1, 0, 0 }, true);
            Assert.IsNotNull(root);
            foreach (int team in new[] { 0, 1 })
            {
                RectTransform bar = Child(root, "Team " + team + " Bar");
                float barRight = bar.anchoredPosition.x + bar.sizeDelta.x;
                float barMiddle = bar.anchoredPosition.y - bar.sizeDelta.y / 2f;
                for (int i = 1; i <= 2; i++)
                {
                    RectTransform dot = Child(root, "Team " + team + " Dot " + i);
                    Assert.Greater(dot.anchoredPosition.x, barRight, "the dot comes after the bar");
                    Assert.AreEqual(barMiddle, dot.anchoredPosition.y - dot.sizeDelta.y / 2f, 0.01f, "level with its bar");
                    bool filled = dot.GetComponent<Image>().sprite == GeneratedSprites.Disc;
                    Assert.AreEqual(team == 0 && i == 1, filled, "team " + team + " dot " + i);
                }
                Assert.IsNull(root.Find("Team " + team + " Dot 3"), "two dots for two rounds to win");
            }
        }

        [Test] public void TheRowsStackInTeamOrderFromTheTop()
        {
            Transform root = Draw(new[] { 0, 1, 2 }, new[] { 0, 0, 0 }, true);
            float y0 = Child(root, "Team 0 Bar").anchoredPosition.y;
            float y1 = Child(root, "Team 1 Bar").anchoredPosition.y;
            float y2 = Child(root, "Team 2 Bar").anchoredPosition.y;
            Assert.Greater(y0, y1);
            Assert.Greater(y1, y2);
        }

        [Test] public void TheBarsHangFromTheTopRightCorner()
        {
            var root = (RectTransform)Draw(new[] { 0, 1 }, new[] { 0, 0, 0 }, true);
            Assert.AreEqual(Vector2.one, root.anchorMin);
            Assert.AreEqual(Vector2.one, root.anchorMax);
            Assert.AreEqual(Vector2.one, root.pivot);
        }

        [Test] public void WithoutPointsOnlyTheDotsShowAndThePointsComeBack()
        {
            var bars = new ScoreBars(kit, canvasGo.transform);
            int[] teams = { 0, 1 };
            bars.Refresh(teams, new[] { 100, 50, 0 }, new[] { 1, 0, 0 }, 2, false);
            Transform root = canvasGo.transform.Find("Score Bars");
            Assert.IsFalse(Child(root, "Team 0 Bar").gameObject.activeSelf);
            Assert.IsFalse(Child(root, "Team 0 Number Box").gameObject.activeSelf);
            Assert.IsTrue(Child(root, "Team 0 Dot 1").gameObject.activeSelf);
            bars.Refresh(teams, new[] { 100, 50, 0 }, new[] { 1, 0, 0 }, 2, true);
            Assert.IsTrue(Child(root, "Team 0 Bar").gameObject.activeSelf);
            Assert.IsTrue(Child(root, "Team 0 Number Box").gameObject.activeSelf);
        }

        [Test] public void AWonDotIsTheTeamsColourAndAnEmptyOneItsTextColourFaded()
        {
            var team = new Color(0.4f, 0.2f, 0.8f, 1f);
            var text = new Color(0.6f, 0.5f, 0.9f, 1f);
            Assert.AreEqual(team, DominionScoreBarRules.DotColour(true, team, text, 0.5f));
            Assert.AreEqual(new Color(0.6f, 0.5f, 0.9f, 0.5f), DominionScoreBarRules.DotColour(false, team, text, 0.5f));
            Assert.AreEqual(new Color(0.6f, 0.5f, 0.9f, 0.4f), DominionScoreBarRules.DotColour(false, team, new Color(0.6f, 0.5f, 0.9f, 0.8f), 0.5f),
                "the fade multiplies the text colour's own alpha");
            Assert.AreEqual(0f, DominionScoreBarRules.DotColour(false, team, text, -1f).a, "never below invisible");
            Assert.AreEqual(1f, DominionScoreBarRules.DotColour(false, team, text, 3f).a, "never above the text colour");
        }

        [Test] public void InSuddenDeathEachRowsEmptyDotsSayWhoseRowItIs()
        {
            int[] teams = { 0, 1, 2 };
            Transform root = Draw(teams, new[] { 0, 0, 0 }, false);
            Color first = Child(root, "Team 0 Dot 1").GetComponent<Image>().color;
            foreach (int team in teams)
            {
                Color dot = Child(root, "Team " + team + " Dot 1").GetComponent<Image>().color;
                Color text = theme.dominionTeamTextColors[team];
                Assert.AreEqual(new Vector3(text.r, text.g, text.b), new Vector3(dot.r, dot.g, dot.b), "team " + team + "'s empty ring is its own colour");
                Assert.AreEqual(text.a * theme.dominionEmptyDotTeamAlpha, dot.a, 1e-4f, "team " + team + "'s ring is faded by the theme's amount");
                if (team != 0) Assert.AreNotEqual(first, dot, "two rows' rings look alike");
            }
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "PaintDots", typeof(UiTheme).GetField(nameof(UiTheme.dominionEmptyDotTeamAlpha))));
        }

        // ---------------------------------------------------------------- the wiring

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test] public void TheBarsArePlacedByTheTestedLayoutLeftOfTheMinimap()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Build", typeof(ScoreBarLayout).GetMethod(nameof(ScoreBarLayout.Block))));
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Build", typeof(HudScreenLayout).GetMethod(nameof(HudScreenLayout.CornerMinimapRect))));
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "Build", typeof(ScoreBarLayout).GetMethod(nameof(ScoreBarLayout.RowWidth))));
            Assert.IsTrue(IlWiring.Uses(typeof(ScoreBars), "BuildRow", typeof(ScoreBarLayout).GetMethod(nameof(ScoreBarLayout.DotsLeft))));
        }

        [Test] public void TheHudShowsTheRowsWhenTheStageRuleSays() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionHud), "Update", typeof(DominionScoreBarRules).GetMethod(nameof(DominionScoreBarRules.DotsShownIn))));

        [Test] public void TheRowsAreHandedTheRoomsWinsAndThePointsRulesAnswer()
        {
            MethodInfo update = typeof(DominionHud).GetMethod("Update", Any);
            MethodInfo refresh = typeof(ScoreBars).GetMethod(nameof(ScoreBars.Refresh));
            MethodInfo shownIn = typeof(DominionScoreBarRules).GetMethod(nameof(DominionScoreBarRules.ShownIn));
            FieldInfo bars = typeof(DominionHud).GetField("scoreBars", Any);
            FieldInfo wins = typeof(DominionRoomState).GetField(nameof(DominionRoomState.Wins));
            byte[] il = update.GetMethodBody().GetILAsByteArray();
            var calls = IlWiring.CallOffsets(update, refresh);
            Assert.AreEqual(1, calls.Count, "one place hands the rows their numbers");
            int call = calls[0];

            int receiver = -1;
            foreach (int at in IlWiring.FieldLoadOffsets(update, bars)) if (at < call) receiver = System.Math.Max(receiver, at);
            Assert.GreaterOrEqual(receiver, 0);
            bool winsHanded = false;
            foreach (int at in IlWiring.FieldLoadOffsets(update, wins)) if (at > receiver && at < call) winsHanded = true;
            Assert.IsTrue(winsHanded, "the room's round wins go to the rows");

            byte[] token = System.BitConverter.GetBytes(shownIn.MetadataToken);
            int before = call - 5;
            Assert.IsTrue(il[before] == 0x28 && il[before + 1] == token[0] && il[before + 2] == token[1] && il[before + 3] == token[2] && il[before + 4] == token[3],
                "the last argument, whether the points show, is the tested stage rule's answer");
        }
    }
}
