using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where the F1 debug log may draw, in real screen pixels. Pure, because the log is IMGUI (no CanvasScaler, no
    /// reference resolution) while everything it must stay clear of, the corner minimap above all, is laid out in
    /// UiTheme canvas units; the two compare only by converting with UGUI's own formula. The band comes from the
    /// theme's CORNER minimap numbers, never the map's live rectangle: M moves and scales that, and a log jumping
    /// down the screen whenever the map opens is worse than one sitting a few pixels low.
    /// </summary>
    public static class HudScreenLayout
    {
        /// <summary>Screen pixels per canvas unit for a CanvasScaler in Scale With Screen Size mode: Unity's own
        /// formula, a blend of the width and height ratios in log space, hence Pow/Lerp/Log and not a plain Lerp.</summary>
        public static float CanvasScaleFactor(Vector2 referenceResolution, float match,
                                              float screenWidth, float screenHeight)
        {
            float referenceWidth = Mathf.Max(1f, referenceResolution.x);
            float referenceHeight = Mathf.Max(1f, referenceResolution.y);
            float logWidth = Mathf.Log(Mathf.Max(1f, screenWidth) / referenceWidth, 2f);
            float logHeight = Mathf.Log(Mathf.Max(1f, screenHeight) / referenceHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(match)));
        }

        /// <summary>Pixels down from the screen top where the corner minimap's reserved band ends. Frame width is in
        /// the sum because it is part of the map's inset from the screen edge (MinimapView: "inset = cornerMargin +
        /// frameWidth"), not just an inward border; without it the log overlaps the frame on screens above 1920x1080.</summary>
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
