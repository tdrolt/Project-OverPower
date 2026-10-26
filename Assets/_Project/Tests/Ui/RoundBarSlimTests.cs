using NUnit.Framework;
using Overpower.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The round bar at the top centre shows no points (the score bars do); each team block keeps its colour line and round-win dots, centred.</summary>
    public class RoundBarSlimTests
    {
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private UiTheme theme;
        private LobbyUiKit kit;
        private GameObject canvas;

        [SetUp] public void SetUp()
        {
            theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            kit = new LobbyUiKit(theme);
            canvas = new GameObject("round bar canvas", typeof(RectTransform), typeof(Canvas));
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(canvas);
            kit.Dispose();
        }

        private Transform Draw(int teamCount, int winsToWin)
        {
            var hud = new RoundHud(kit, canvas.transform);
            int[] teams = teamCount == 3 ? new[] { 0, 1, 2 } : new[] { 0, 1 };
            hud.Refresh(teams, 1, 3, 90, false, false, new[] { 1, 0, 0 }, winsToWin, new[] { "A", "B", "C" });
            return canvas.transform.Find("Round Bar");
        }

        [TestCase(2, 2)]
        [TestCase(3, 2)]
        public void NoTeamBlockHoldsAScoreText(int teamCount, int winsToWin)
        {
            Transform bar = Draw(teamCount, winsToWin);
            Assert.IsNotNull(bar);
            int blocks = 0;
            foreach (Transform t in bar)
            {
                if (!t.name.StartsWith("Team ")) continue;
                blocks++;
                Assert.AreEqual(0, t.GetComponentsInChildren<TextMeshProUGUI>(true).Length, t.name + " draws no text");
            }
            Assert.AreEqual(teamCount, blocks);
            var names = new System.Collections.Generic.List<string>();
            foreach (TextMeshProUGUI text in bar.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.name != "Centre Flash") names.Add(text.name);
            names.Sort();
            CollectionAssert.AreEqual(new[] { "Clock", "Round" }, names, "besides the flash the bar holds only the round heading and the clock");
        }

        [Test] public void ABlockIsNeverNarrowerThanItsDotsPlusMargins()
        {
            Transform bar = Draw(2, 5);
            float needed = 5 * theme.dominionDotSize + 4 * theme.dominionDotGap + 2f * theme.dominionBarDotsMargin;
            foreach (Transform t in bar)
                if (t.name.StartsWith("Team ")) Assert.GreaterOrEqual(((RectTransform)t).sizeDelta.x, needed - 0.01f, t.name);
        }

        [TestCase(2, 2)]
        [TestCase(3, 3)]
        public void EveryTeamBlockKeepsItsDotsCentredAndItsColourLine(int teamCount, int winsToWin)
        {
            Transform bar = Draw(teamCount, winsToWin);
            foreach (Transform t in bar)
            {
                if (!t.name.StartsWith("Team ")) continue;
                Assert.IsNotNull(t.Find("Edge"), t.name + " keeps its team-colour line");
                float blockWidth = ((RectTransform)t).sizeDelta.x;
                float min = float.MaxValue, max = float.MinValue;
                int dots = 0;
                foreach (Transform d in t)
                {
                    if (!d.name.StartsWith("Dot ")) continue;
                    dots++;
                    var r = (RectTransform)d;
                    min = Mathf.Min(min, r.anchoredPosition.x);
                    max = Mathf.Max(max, r.anchoredPosition.x + r.sizeDelta.x);
                }
                Assert.AreEqual(winsToWin, dots, t.name);
                Assert.AreEqual(blockWidth * 0.5f, (min + max) * 0.5f, 0.01f, t.name + " dots sit centred in the block");
            }
        }
    }
}
