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
            Assert.AreEqual(new Vector2(100f, 50f), BountyLabelPlacement.Place(new Vector2(100f, 50f), Size, Canvas, Margin, false, NoAvoid));
        }

        [Test]
        public void ALabelPastTheRightEdgeIsPulledBackByItsHalfWidthAndTheMargin()
        {
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(2000f, 0f), Size, Canvas, Margin, false, NoAvoid);
            Assert.AreEqual(960f - Margin - 100f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelPastTheLeftEdgeIsPulledBack()
        {
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(-5000f, 0f), Size, Canvas, Margin, false, NoAvoid);
            Assert.AreEqual(-960f + Margin + 100f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelAboveOrBelowTheCanvasIsPulledBackByItsHalfHeightAndTheMargin()
        {
            Assert.AreEqual(540f - Margin - 20f, BountyLabelPlacement.Place(new Vector2(0f, 9000f), Size, Canvas, Margin, false, NoAvoid).y, 1e-3f);
            Assert.AreEqual(-540f + Margin + 20f, BountyLabelPlacement.Place(new Vector2(0f, -9000f), Size, Canvas, Margin, false, NoAvoid).y, 1e-3f);
        }

        [Test]
        public void ALabelWiderThanTheCanvasIsCentred()
        {
            Assert.AreEqual(0f, BountyLabelPlacement.Place(new Vector2(300f, 0f), new Vector2(5000f, 40f), Canvas, Margin, false, NoAvoid).x, 1e-3f);
        }

        [Test]
        public void ALabelOverTheMinimapIsPushedJustBelowIt()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(750f, 400f), Size, Canvas, Margin, true, minimap);
            Assert.AreEqual(minimap.yMin, placed.y + Size.y / 2f, 1e-3f);
            Assert.AreEqual(750f, placed.x, 1e-3f);
        }

        [Test]
        public void ALabelClearOfTheMinimapIsNotMoved()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Assert.AreEqual(new Vector2(0f, 0f), BountyLabelPlacement.Place(Vector2.zero, Size, Canvas, Margin, true, minimap));
        }

        [Test]
        public void WithoutAMinimapToAvoidTheLabelStaysOverIt()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Assert.AreEqual(new Vector2(750f, 400f), BountyLabelPlacement.Place(new Vector2(750f, 400f), Size, Canvas, Margin, false, minimap));
        }

        [Test]
        public void ALabelPastTheCornerEndsUpOnScreenAndOffTheMinimap()
        {
            Rect minimap = new Rect(600f, 300f, 300f, 300f);
            Vector2 placed = BountyLabelPlacement.Place(new Vector2(3000f, 3000f), Size, Canvas, Margin, true, minimap);
            Rect label = new Rect(placed - Size / 2f, Size);
            Assert.IsFalse(label.Overlaps(minimap));
            Assert.LessOrEqual(label.xMax, Canvas.xMax - Margin + 1e-3f);
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
