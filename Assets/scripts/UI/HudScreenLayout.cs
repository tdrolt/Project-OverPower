using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where the F1 debug log may draw, in real screen pixels. Its own file, and pure, because the debug log is
    /// the one thing on screen that is NOT a Canvas: it is IMGUI, so it has no CanvasScaler and no reference
    /// resolution. Everything it has to stay clear of - the corner minimap above all - is laid out in UiTheme's
    /// canvas units, and the only honest way to compare the two is to convert with the same formula UGUI uses.
    ///
    /// Tudor, 2026-09-17: F1 stays one key. The test range panel keeps the top-left corner it has always had, and
    /// the log moves to the right-hand side, under the map, where the two can no longer sit on top of each other.
    ///
    /// The band is worked out from the theme's CORNER minimap numbers, never from the map's live rectangle: M
    /// moves and scales that rectangle, and a log that jumped down the screen every time someone opened the map
    /// would be worse than one sitting a few pixels lower than it strictly has to.
    /// </summary>
    public static class HudScreenLayout
    {
        /// <summary>How many screen pixels one canvas unit is worth, for a CanvasScaler in Scale With Screen Size
        /// mode - Unity's own documented formula, a blend of the width and height ratios in log space, which is
        /// why it is a Pow/Lerp/Log rather than a plain Lerp of the two ratios.</summary>
        public static float CanvasScaleFactor(Vector2 referenceResolution, float match,
                                              float screenWidth, float screenHeight)
        {
            float referenceWidth = Mathf.Max(1f, referenceResolution.x);
            float referenceHeight = Mathf.Max(1f, referenceResolution.y);
            float logWidth = Mathf.Log(Mathf.Max(1f, screenWidth) / referenceWidth, 2f);
            float logHeight = Mathf.Log(Mathf.Max(1f, screenHeight) / referenceHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(match)));
        }

        /// <summary>How far down from the top of the screen, in pixels, the corner minimap's reserved band ends:
        /// its margin from the screen edge, plus its frame width, plus its own bounding size - all in canvas
        /// units, scaled. Frame width is in the sum because it doubles as part of the map's own inset from the
        /// screen edge (MinimapView.cs: "inset = cornerMargin + frameWidth"), not just as a cosmetic border
        /// drawn inward - the map's real bottom edge sits a whole frame width further down than margin+size
        /// alone would say. Omitting it (HUD review fix, 2026-09-18) under-measured the band by frameWidth *
        /// scaleFactor: invisible at today's numbers up to about scale 1.2, but the log started overlapping
        /// the minimap's own frame on screens larger than 1920x1080 (about 1 px at 2560x1440, about 4 px at
        /// 3840x2160).</summary>
        public static float MinimapBandBottomPixels(float cornerMargin, float frameWidth, float cornerSize,
                                                     float scaleFactor) =>
            Mathf.Max(0f, (cornerMargin + frameWidth + cornerSize) * Mathf.Max(0f, scaleFactor));

        /// <summary>The debug log's rectangle in GUI space (y grows DOWNWARD, which is what GUI.Box wants): along
        /// the right edge inside margin, starting gap pixels below the minimap band, never taller than
        /// maxHeightFraction of the screen and never running off the bottom of it.</summary>
        public static Rect DebugLogRect(float screenWidth, float screenHeight, float minimapBandBottom,
                                        float width, float maxHeightFraction, float margin, float gap)
        {
            float w = Mathf.Min(Mathf.Max(1f, width), Mathf.Max(1f, screenWidth - 2f * margin));
            float top = Mathf.Max(margin, minimapBandBottom + gap);
            float available = Mathf.Max(0f, screenHeight - margin - top);
            float h = Mathf.Min(Mathf.Max(0f, screenHeight) * Mathf.Clamp01(maxHeightFraction), available);
            float x = Mathf.Max(margin, screenWidth - margin - w);
            return new Rect(x, top, w, h);
        }
    }
}
