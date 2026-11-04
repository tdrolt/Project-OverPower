using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>T6 review fix (item 6): the world-to-pixel mapping (min/span/pixel-size arithmetic,
    /// including the Y-FLIP - "image up is +Z", per the arena-symmetry design's own ArenaRender
    /// comment) tested as pure arithmetic, independent of whether Game Scene happens to be open in
    /// the Editor right now. Render() itself is checked to never return a null Result.</summary>
    public class ArenaReportRenderTests
    {
        [Test]
        public void RenderNeverReturnsNull()
        {
            // Available may be true or false depending on whether Game Scene happens to be open in
            // this Editor session right now - only the "never null" contract is deterministic here.
            Assert.IsNotNull(ArenaReportRender.Render());
        }

        [Test]
        public void WorldToPixelMapsTheArenasMinCornerToTheImagesBottomLeft()
        {
            // MinX/MinZ (-7,-7) is the arena's own minimum corner: x maps straight to the left edge
            // (px=0); z maps to the BOTTOM of the image (py=PixelSize), not the top, because "image
            // up is +Z" means the smallest z sits at the bottom.
            (float px, float py) = ArenaReportRender.WorldToPixel(-7f, -7f);
            Assert.AreEqual(0f, px, 0.01f);
            Assert.AreEqual(1024f, py, 0.01f);
        }

        [Test]
        public void WorldToPixelMapsTheArenasMaxCornerToTheImagesTopRight()
        {
            (float px, float py) = ArenaReportRender.WorldToPixel(137f, 137f);
            Assert.AreEqual(1024f, px, 0.01f);
            Assert.AreEqual(0f, py, 0.01f);
        }

        [Test]
        public void EachMapsPictureIsItsOwnWhicheverSceneIsOpen()
        {
            ArenaReportRender.Result lane = ArenaReportRender.Render(ReportMap.Lane);
            ArenaReportRender.Result triangle = ArenaReportRender.Render(ReportMap.Triangle);
            Assert.IsTrue(lane.Available, lane.Note);
            Assert.IsTrue(triangle.Available, triangle.Note);
            Assert.AreNotEqual(lane.Base64Png, triangle.Base64Png);
            Assert.AreEqual("the lane", lane.MapName);
        }

        [Test]
        public void TheLaneIsFramedToItselfNotToTheTriangleArena()
        {
            ArenaReportRender.Result lane = ArenaReportRender.Render(ReportMap.Lane);
            Assert.AreEqual(lane.ImageWidthMetres, lane.FrameWidthMetres, 0.01f, "the lane's picture is already framed to the lane");
            Assert.Less(lane.FrameWidthMetres, 144f);
            Assert.Less(lane.PixelsUp, lane.PixelsAcross, "the lane is long and narrow");
            ArenaReportRender.Result triangle = ArenaReportRender.Render(ReportMap.Triangle);
            Assert.AreEqual(144f, triangle.FrameWidthMetres, 0.01f);
            Assert.AreEqual(-7f, triangle.FrameMinX, 0.01f);
            Assert.AreEqual(triangle.PixelsAcross, triangle.PixelsUp);
        }

        [Test]
        public void WorldToPixelMatchesAHandComputedInteriorPoint()
        {
            // The same point used to manually verify the fixture's one death lands inside the
            // canvas during the T6 polish pass: world (5, 5) -> pixel (85.333, 938.667).
            (float px, float py) = ArenaReportRender.WorldToPixel(5f, 5f);
            Assert.AreEqual(85.333f, px, 0.01f);
            Assert.AreEqual(938.667f, py, 0.01f);
        }
    }
}
