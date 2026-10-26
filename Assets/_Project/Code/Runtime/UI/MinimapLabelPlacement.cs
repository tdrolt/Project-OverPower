using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The spot and the look shared by every line under the corner minimap: the scan countdown, Dominion's centre payout and the sudden-death circle
    /// countdown. They hang from the lowest point of the DRAWN frame (the triangle's base, a rectangle's lower edge on the lane map), not from the
    /// square box the frame sits in: the triangle's base is about a quarter of the box above the box's bottom. Gap and font: UiTheme > Under The Minimap.
    /// Positions are in canvas units from the canvas's top right corner (HudScreenLayout.CornerMinimapRect).
    /// </summary>
    public static class MinimapLabelPlacement
    {
        /// <summary>How far below its centre the drawn frame reaches. The triangle is apex up at rotation 0 with its corners triangleRadius from the
        /// centre; the rectangle is frameSize. rotationDegrees is the frame's UI rotation (counter-clockwise), which follows the camera's yaw.</summary>
        public static float ShapeBottomBelowCentre(bool rectangular, Vector2 frameSize, float triangleRadius, float rotationDegrees)
        {
            float turn = rotationDegrees * Mathf.Deg2Rad;
            if (rectangular)
                return (Mathf.Abs(frameSize.x * Mathf.Sin(turn)) + Mathf.Abs(frameSize.y * Mathf.Cos(turn))) / 2f;
            float lowest = float.MinValue;
            for (int corner = 0; corner < 3; corner++)
            {
                float angle = turn + (90f + 120f * corner) * Mathf.Deg2Rad;
                lowest = Mathf.Max(lowest, -Mathf.Sin(angle) * triangleRadius);
            }
            return lowest;
        }

        /// <summary>The middle of the label's top edge: centred on the corner box, gap below the frame's lowest point.</summary>
        public static Vector2 LabelTop(Rect cornerBox, float shapeBottomBelowCentre, float gap) =>
            new Vector2(cornerBox.center.x, cornerBox.center.y - shapeBottomBelowCentre - gap);

        /// <summary>Hangs rect (width x height, its text growing downward) under the corner minimap. Cheap to call every frame: the frame turns with the
        /// camera after the label is built, so callers place again on each refresh.</summary>
        public static void PlaceUnder(RectTransform rect, UiTheme theme, float width, float height)
        {
            Rect box = HudScreenLayout.CornerMinimapRect(theme.minimapCornerMargin, theme.minimapFrameWidth, theme.minimapCornerSize);
            Vector2 top = LabelTop(box, MinimapView.CornerShapeBottomBelowCentre(theme), theme.minimapLinesGap);
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            var size = new Vector2(width, height);
            if (rect.sizeDelta != size) rect.sizeDelta = size;
            if (rect.anchoredPosition != top) rect.anchoredPosition = top;
        }

        /// <summary>The HUD's own text look (the minimap's and the ability bar's): Lines Font or else the HUD font, bold, the HUD outline and shadow.
        /// Returns the label's own material; the caller destroys it with the label.</summary>
        public static Material ApplyLook(TextMeshProUGUI text, UiTheme theme)
        {
            // An empty HUD font means TextMeshPro's default, and Dominion's lines are built in the display font: name the default or they keep theirs.
            TMP_FontAsset font = theme.minimapLinesFont != null ? theme.minimapLinesFont : theme.font != null ? theme.font : TMP_Settings.defaultFontAsset;
            if (font != null)
                text.font = font; // before the material: assigning a font swaps the material
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            var material = new Material(text.fontSharedMaterial);
            theme.ApplyHudTextStyle(material);
            text.fontSharedMaterial = material;
            return material;
        }
    }
}
