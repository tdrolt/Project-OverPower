using NUnit.Framework;
using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class BountyLabelPlacementTests
    {
        private static readonly Rect Canvas = new Rect(-960f, -540f, 1920f, 1080f);
        private static readonly Vector2 Size = new Vector2(200f, 40f);
        private static readonly Rect NoAvoid = default;
        private const float Margin = 16f;

        [Test]
        public void ALabelInsideTheCanvasStaysWhereItWas()
        {
            Assert.AreEqual(new Vector2(100f, 50f), BountyLabelPlacement.Place(new Vector2(100f, 50f), Size, Canvas, Margin, false, NoAvoid, false, NoAvoid));
        }

        [Test]
        public void ALabelPastTheRightEdgeIsPulledBackByItsHalfWidthAndTheMargin()
        {
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(2000f, 0f), Size, Canvas, Margin, false, NoAvoid, false, NoAvoid);
            Assert.AreEqual(960f - Margin - 100f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelPastTheLeftEdgeIsPulledBack()
        {
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(-5000f, 0f), Size, Canvas, Margin, false, NoAvoid, false, NoAvoid);
            Assert.AreEqual(-960f + Margin + 100f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelAboveOrBelowTheCanvasIsPulledBackByItsHalfHeightAndTheMargin()
        {
            Assert.AreEqual(540f - Margin - 20f, BountyLabelPlacement.Place(new Vector2(0f, 9000f), Size, Canvas, Margin, false, NoAvoid, false, NoAvoid).y, 1e-3f);
            Assert.AreEqual(-540f + Margin + 20f, BountyLabelPlacement.Place(new Vector2(0f, -9000f), Size, Canvas, Margin, false, NoAvoid, false, NoAvoid).y, 1e-3f);
        }

        [Test]
        public void ALabelWiderThanTheCanvasIsCentred()
        {
            Assert.AreEqual(0f, BountyLabelPlacement.Place(new Vector2(300f, 0f), new Vector2(5000f, 40f), Canvas, Margin, false, NoAvoid, false, NoAvoid).x, 1e-3f);
        }

        [Test]
        public void ALabelOverTheMinimapIsPushedJustBelowIt()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(750f, 400f), Size, Canvas, Margin, true, minimap, false, NoAvoid);
            Assert.AreEqual(minimap.yMin, placed.y + Size.y / 2f, 1e-3f);
            Assert.AreEqual(750f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelClearOfTheMinimapIsNotMoved()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Assert.AreEqual(new Vector2(0f, 0f), BountyLabelPlacement.Place(Vector2.zero, Size, Canvas, Margin, true, minimap, false, NoAvoid));
        }

        [Test]
        public void WithoutAMinimapToAvoidTheLabelStaysOverIt()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Assert.AreEqual(new Vector2(750f, 400f), BountyLabelPlacement.Place(new Vector2(750f, 400f), Size, Canvas, Margin, false, minimap, false, NoAvoid));
        }

        [Test]
        public void ALabelPastTheCornerEndsUpOnScreenAndOffTheMinimap()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(3000f, 3000f), Size, Canvas, Margin, true, minimap, false, NoAvoid);
            Rect label = new Rect(placed - Size / 2f, Size);
            Assert.IsFalse(label.Overlaps(minimap));
            Assert.LessOrEqual(label.xMax, Canvas.xMax - Margin + 1e-3f);
        }

        [Test]
        public void ALabelOverTheAbilityBarIsPushedJustAboveIt()
        {
            Rect bar = new Rect(-300f, -540f, 600f, 150f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(0f, -450f), Size, Canvas, Margin, false, NoAvoid, true, bar);
            Assert.AreEqual(bar.yMax, placed.y - Size.y / 2f, 1e-3f);
            Assert.AreEqual(0f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelClearOfTheAbilityBarIsNotMoved()
        {
            Rect bar = new Rect(-300f, -540f, 600f, 150f);
            Assert.AreEqual(new Vector2(0f, 0f), BountyLabelPlacement.Place(Vector2.zero, Size, Canvas, Margin, false, NoAvoid, true, bar));
        }

        [Test]
        public void WithoutAnAbilityBarToAvoidTheLabelStaysOverIt()
        {
            Rect bar = new Rect(-300f, -540f, 600f, 150f);
            Assert.AreEqual(new Vector2(0f, -450f), BountyLabelPlacement.Place(new Vector2(0f, -450f), Size, Canvas, Margin, false, NoAvoid, false, bar));
        }

        [Test]
        public void ALabelBelowTheScreenEndsUpOnScreenAndOffTheAbilityBar()
        {
            Rect bar = new Rect(-300f, -540f, 600f, 150f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(0f, -9000f), Size, Canvas, Margin, false, NoAvoid, true, bar);
            Assert.IsFalse(new Rect(placed - Size / 2f, Size).Overlaps(bar));
            Assert.GreaterOrEqual(placed.y - Size.y / 2f, Canvas.yMin);
        }

        [Test]
        public void ALabelOverBothTheMinimapAndTheBarEndsUpClearOfBoth()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Rect bar = new Rect(-300f, -540f, 600f, 150f);
            foreach (Vector2 desired in new[] { new Vector2(750f, 400f), new Vector2(0f, -450f), new Vector2(3000f, 3000f), new Vector2(-3000f, -3000f) })
            {
                Rect label = new Rect(BountyLabelPlacement.Place(desired, Size, Canvas, Margin, true, minimap, true, bar) - Size / 2f, Size);
                Assert.IsFalse(label.Overlaps(minimap), desired.ToString());
                Assert.IsFalse(label.Overlaps(bar), desired.ToString());
            }
        }

        [Test]
        public void ALabelIsRedoneWhenItIsNewWhenTheAmountChangesOrWhenTheThemeSizeChanges()
        {
            Assert.IsTrue(BountyLabelPlacement.NeedsRedo(false, 0, 0f, 500, 30f));
            Assert.IsTrue(BountyLabelPlacement.NeedsRedo(true, 500, 30f, 400, 30f));
            Assert.IsTrue(BountyLabelPlacement.NeedsRedo(true, 500, 30f, 500, 36f));
            Assert.IsFalse(BountyLabelPlacement.NeedsRedo(true, 500, 30f, 500, 30f));
        }

        [Test]
        public void TheHeightIsAboutSixTenthsOfTheFontSize()
        {
            Assert.AreEqual(30f, BountyLabelPlacement.LabelHeight(50f), 1e-3f);
        }

        [Test]
        public void TheCornerRectInCanvasHangsFromTheTopRightCorner()
        {
            Rect fromTopRight = HudScreenLayout.CornerMinimapRect(20f, 4f, 300f);
            Rect inCanvas = BountyLabelPlacement.CornerRectInCanvas(Canvas, fromTopRight);
            Assert.AreEqual(960f - 24f, inCanvas.xMax, 1e-3f);
            Assert.AreEqual(540f - 24f, inCanvas.yMax, 1e-3f);
            Assert.AreEqual(300f, inCanvas.width, 1e-3f);
            Assert.AreEqual(300f, inCanvas.height, 1e-3f);
        }
    }
}
