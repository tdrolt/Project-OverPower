using NUnit.Framework;
using Overpower.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The round bar at the top centre is the round heading and the clock alone: the points and the round-win dots are on the score bars, top right.</summary>
    public class RoundBarSlimTests
    {
        private const string ThemePath = "Assets/_Project/Shared/Config/UiTheme.asset";
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

        private RoundHud hud;

        private Transform Draw(int teamCount)
        {
            hud = new RoundHud(kit, canvas.transform);
            int[] teams = teamCount == 3 ? new[] { 0, 1, 2 } : new[] { 0, 1 };
            hud.Refresh(teams, 1, 3, 90, false, false);
            return canvas.transform.Find("Round Bar");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void TheRoundBarHoldsOnlyTheRoundHeadingAndTheClock(int teamCount)
        {
            Transform bar = Draw(teamCount);
            Assert.IsNotNull(bar);
            foreach (Transform t in bar.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsFalse(t.name.StartsWith("Team "), t.name + ": the team blocks moved to the score bars");
                Assert.IsFalse(t.name.StartsWith("Dot "), t.name + ": the round-win dots moved to the score bars");
            }
            var names = new System.Collections.Generic.List<string>();
            foreach (TextMeshProUGUI text in bar.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.name != "Centre Flash") names.Add(text.name);
            names.Sort();
            CollectionAssert.AreEqual(new[] { "Clock", "Round" }, names, "besides the flash the bar holds only the round heading and the clock");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void TheRoundBarIsAsWideAsItsClock(int teamCount)
        {
            Transform bar = Draw(teamCount);
            float clock = teamCount == 3 ? theme.dominionBar3ClockWidth : theme.dominionBarClockWidth;
            Assert.AreEqual(clock, ((RectTransform)bar).sizeDelta.x, 0.01f);
        }

        [Test] public void TheCentreFlashShowsCentredUnderTheClock()
        {
            Transform bar = Draw(2);
            hud.Flash(1, 200, new[] { "A", "B" });
            var flash = (RectTransform)bar.Find("Centre Flash");
            Assert.IsTrue(flash.gameObject.activeSelf);
            float width = ((RectTransform)bar).sizeDelta.x;
            Assert.AreEqual(width / 2f, flash.anchorMin.x * width + flash.anchoredPosition.x, 0.01f, "centred on the clock whichever team was paid");
            Assert.Less(flash.anchoredPosition.y, -((RectTransform)bar).sizeDelta.y + 0.01f, "under the bar");
        }
    }
}
