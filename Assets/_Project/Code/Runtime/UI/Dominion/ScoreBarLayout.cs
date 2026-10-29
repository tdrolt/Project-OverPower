using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where Dominion's score bars go: beside the corner minimap on the timer's side (left of a top-right map, right of a top-left one), one row
    /// per team in team order, each row the points box, the bar, then the team's round-win dots. Rects are in canvas units from the minimap's
    /// corner (HudScreenLayout.CornerMinimapRect's convention), so the block keeps its distance to the minimap at every 16:9 resolution. Beside
    /// the map rather than under it: the lines under the map move with the frame's turn, the space beside it does not.
    /// </summary>
    public static class ScoreBarLayout
    {
        public static float DotsWidth(int dotCount, float dotSize, float dotGap) =>
            dotCount <= 0 ? 0f : dotCount * dotSize + (dotCount - 1) * dotGap;

        /// <summary>Where a row's first dot starts, from the row's left edge: after the points box, the bar and the gap.</summary>
        public static float DotsLeft(float numberWidth, float barWidth, float dotsGap) => numberWidth + barWidth + dotsGap;

        public static float RowWidth(float numberWidth, float barWidth, float dotsGap, float dotsWidth) =>
            dotsWidth > 0f ? DotsLeft(numberWidth, barWidth, dotsGap) + dotsWidth : numberWidth + barWidth;

        public static float StackHeight(int rows, float rowHeight, float rowGap) =>
            rows <= 0 ? 0f : rows * rowHeight + (rows - 1) * rowGap;

        /// <summary>The bars' block: gapToMinimap beside the minimap's box toward the screen's middle, its top edge top below the screen's top.
        /// width and height are the block's size on the canvas (already scaled by the HUD scale).</summary>
        public static Rect Block(Rect minimapBox, float gapToMinimap, float top, float width, float height, bool minimapOnLeft = false) =>
            new Rect(minimapOnLeft ? minimapBox.xMax + gapToMinimap : minimapBox.xMin - gapToMinimap - width, -top - height, width, height);
    }
}
