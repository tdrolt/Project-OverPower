using System.Reflection;
using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The corner minimap moves to the top left (Tudor, 29 Oct) and everything hung on it mirrors: the lines under it, Dominion's score bars
    /// (now right of the map, between it and the timer) and the bounty labels' keep-clear box. One theme switch, Minimap On Left.
    /// </summary>
    public class MinimapOnLeftTests
    {
        [Test]
        public void TheCornerIsTheTopLeftOrTheTopRight()
        {
            Assert.AreEqual(new Vector2(0f, 1f), HudScreenLayout.CornerAnchor(true));
            Assert.AreEqual(Vector2.one, HudScreenLayout.CornerAnchor(false));
        }

        [Test]
        public void TheMinimapBoxMirrorsAcrossTheScreen()
        {
            Rect right = HudScreenLayout.CornerMinimapRect(20f, 4f, 300f);
            Rect left = HudScreenLayout.CornerMinimapRect(20f, 4f, 300f, true);
            Assert.AreEqual(new Rect(-324f, -324f, 300f, 300f), right, "measured from the top right, as before");
            Assert.AreEqual(new Rect(24f, -324f, 300f, 300f), left, "measured from the top left");
        }

        [Test]
        public void TheMapSitsTheInsetInFromItsCorner()
        {
            Assert.AreEqual(new Vector2(24f, -24f), HudScreenLayout.CornerInset(24f, true));
            Assert.AreEqual(new Vector2(-24f, -24f), HudScreenLayout.CornerInset(24f, false));
        }

        [Test]
        public void ALaneMapsFrameIsPinnedToTheBoxsTopLeftOnTheLeft()
        {
            var box = new Rect(24f, -324f, 300f, 300f);
            Vector2 top = MinimapLabelPlacement.LabelTop(box, new Vector2(300f, 100f), 50f, 8f, true);
            Assert.AreEqual(174f, top.x, 1e-4f, "centred under the frame, which starts at the box's left");
            Vector2 tall = MinimapLabelPlacement.LabelTop(box, new Vector2(100f, 300f), 150f, 8f, true);
            Assert.AreEqual(74f, tall.x, 1e-4f, "a tall frame hugs the left edge");
        }

        [Test]
        public void TheScoreBarsStartTheGapRightOfTheMinimapOnTheLeft()
        {
            var minimap = new Rect(24f, -324f, 300f, 300f);
            Rect block = ScoreBarLayout.Block(minimap, 16f, 20f, 200f, 60f, true);
            Assert.AreEqual(340f, block.xMin, 1e-4f, "the gap right of the map");
            Assert.AreEqual(-20f, block.yMax, 1e-4f);
            Assert.AreEqual(200f, block.width, 1e-4f);
            Rect rightSide = ScoreBarLayout.Block(new Rect(-324f, -324f, 300f, 300f), 16f, 20f, 200f, 60f);
            Assert.AreEqual(-340f, rightSide.xMax, 1e-4f, "unchanged on the right");
        }

        [Test]
        public void TheBountyKeepClearBoxIsMeasuredFromItsCorner()
        {
            var canvas = new Rect(-960f, -540f, 1920f, 1080f);
            Rect left = BountyLabelPlacement.CornerRectInCanvas(canvas, new Rect(24f, -324f, 300f, 300f), true);
            Assert.AreEqual(new Rect(-936f, 216f, 300f, 300f), left);
            Rect right = BountyLabelPlacement.CornerRectInCanvas(canvas, new Rect(-324f, -324f, 300f, 300f));
            Assert.AreEqual(new Rect(636f, 216f, 300f, 300f), right);
        }

        [Test]
        public void TheDebugLogOnlyMakesRoomForAMinimapOnItsOwnSide()
        {
            Assert.AreEqual(0f, HudScreenLayout.MinimapBandBottomPixels(24f, 4f, 340f, 1f, true), 1e-4f);
            Assert.AreEqual(368f, HudScreenLayout.MinimapBandBottomPixels(24f, 4f, 340f, 1f), 1e-4f);
        }

        [Test]
        public void ALineUnderTheMapHangsFromTheLeftCorner()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var go = new GameObject("Line", typeof(RectTransform));
            try
            {
                theme.minimapOnLeft = true;
                var rect = (RectTransform)go.transform;
                MinimapLabelPlacement.PlaceUnder(rect, theme, 250f, 40f);
                Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMin);
                Assert.Greater(rect.anchoredPosition.x, 0f, "inside the screen from the left edge");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }

        private static readonly FieldInfo OnLeft = typeof(UiTheme).GetField(nameof(UiTheme.minimapOnLeft));

        [TestCase(typeof(MinimapView), "SetLarge")]
        [TestCase(typeof(MinimapLabelPlacement), "PlaceUnder")]
        [TestCase(typeof(ScoreBars), "Build")]
        [TestCase(typeof(TowerBountyView), "AvoidRect")]
        [TestCase(typeof(DebugOverlay), "LogRect")]
        public void EveryCornerPieceReadsTheSwitch(System.Type owner, string method) =>
            Assert.IsTrue(IlWiring.Uses(owner, method, OnLeft), owner.Name + "." + method);
    }
}
