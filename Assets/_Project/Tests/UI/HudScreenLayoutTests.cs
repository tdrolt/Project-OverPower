using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class HudScreenLayoutTests
    {
        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        [Test]
        public void AtTheReferenceResolutionOneCanvasUnitIsOnePixel()
        {
            Assert.AreEqual(1f, HudScreenLayout.CanvasScaleFactor(Reference, 0.5f, 1920f, 1080f), 1e-4f);
        }

        [Test]
        public void MatchZeroFollowsTheWidthAndMatchOneFollowsTheHeight()
        {
            Assert.AreEqual(0.5f, HudScreenLayout.CanvasScaleFactor(Reference, 0f, 960f, 1080f), 1e-4f);
            Assert.AreEqual(0.5f, HudScreenLayout.CanvasScaleFactor(Reference, 1f, 1920f, 540f), 1e-4f);
        }

        [Test]
        public void ANonReferenceAspectRatioBlendsBothRatiosGeometrically()
        {
            // 616x576 is not this project's own capture size (that is 1280x720, exactly the reference's 16:9
            // aspect, where the width and height ratios are always equal and the blend below is trivial) - it is
            // deliberately a DIFFERENT aspect ratio, chosen so this test actually exercises the Pow/Lerp/Log blend
            // instead of two equal numbers agreeing by construction.
            float expected = Mathf.Sqrt((616f / 1920f) * (576f / 1080f));
            Assert.AreEqual(expected, HudScreenLayout.CanvasScaleFactor(Reference, 0.5f, 616f, 576f), 1e-4f);
        }

        [Test]
        public void TheMinimapBandIsItsMarginPlusFrameWidthPlusItsSizeInPixels()
        {
            Assert.AreEqual(184.5f, HudScreenLayout.MinimapBandBottomPixels(24f, 5f, 340f, 0.5f), 1e-3f);
        }

        [Test]
        public void TheFrameWidthWidensTheBandEnoughToMatterAtALargeScale()
        {
            // HUD review fix, 2026-09-18: frame width used to be left out of the band entirely, which under-
            // measured it by frameWidth * scaleFactor - invisible up to about scale 1.2 (5 * 1.2 = 6, hidden
            // by the 6px debugLogGapBelowMinimapPixels default), but on a screen twice the reference size
            // (e.g. 3840x2160 against the 1920x1080 reference) the shortfall is a full 10px, eating the whole
            // gap and overlapping the minimap's own frame by about 4px. Scale is passed directly here (not
            // derived through CanvasScaleFactor) so this test isolates the band formula itself.
            Assert.AreEqual((24f + 5f + 340f) * 2f, HudScreenLayout.MinimapBandBottomPixels(24f, 5f, 340f, 2f), 1e-3f);
        }

        [Test]
        public void TheLogSitsAgainstTheRightEdgeBelowTheBand()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 576f, 150f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(616f - 8f - 420f, r.x, 1e-3f);
            Assert.AreEqual(156f, r.y, 1e-3f);
            Assert.AreEqual(420f, r.width, 1e-3f);
            Assert.AreEqual(576f * 0.45f, r.height, 1e-3f);
        }

        [Test]
        public void ANarrowScreenShrinksTheLogInsteadOfPushingItOffTheLeftEdge()
        {
            Rect r = HudScreenLayout.DebugLogRect(300f, 576f, 0f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(8f, r.x, 1e-3f);
            Assert.AreEqual(284f, r.width, 1e-3f);
        }

        [Test]
        public void ATallMinimapBandShortensTheLogRatherThanRunningOffTheBottom()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 400f, 360f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(366f, r.y, 1e-3f);
            Assert.AreEqual(400f - 8f - 366f, r.height, 1e-3f);
            Assert.LessOrEqual(r.yMax, 400f - 8f + 1e-3f);
        }

        [Test]
        public void TheLogNeverGetsANegativeHeight()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 200f, 400f, 420f, 0.45f, 8f, 6f);
            Assert.GreaterOrEqual(r.height, 0f);
        }

        [Test]
        public void WithNoMinimapBandTheLogStillClearsTheScreenMargin()
        {
            Rect r = HudScreenLayout.DebugLogRect(1920f, 1080f, 0f, 420f, 0.45f, 8f, 0f);
            Assert.AreEqual(8f, r.y, 1e-3f);
        }
    }
}
