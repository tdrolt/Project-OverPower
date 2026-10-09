using UnityEngine;

namespace Overpower.Match
{
    /// <summary>Where a bounty label or pop is drawn on its canvas: kept fully on screen, and off the corner minimap. Canvas units, origin at the canvas centre.</summary>
    public static class BountyLabelPlacement
    {
        public const float HeightPerFontSize = 0.6f;

        public static float LabelHeight(float fontSize) => fontSize * HeightPerFontSize;

        /// <summary>The corner minimap's rectangle in canvas coordinates, from its rectangle measured from the canvas's top right corner.</summary>
        public static Rect CornerRectInCanvas(Rect canvas, Rect fromTopRight) =>
            new Rect(canvas.xMax + fromTopRight.x, canvas.yMax + fromTopRight.y, fromTopRight.width, fromTopRight.height);

        /// <summary>The label's centre after keeping it inside the canvas by <paramref name="margin"/>; a label over the minimap rectangle is first pushed
        /// to just below it (then kept inside again). A label wider or taller than the room left is centred on that axis.</summary>
        public static Vector2 Place(Vector2 desired, Vector2 size, Rect canvas, float margin, bool hasAvoid, Rect avoid)
        {
            Vector2 centre = Clamp(desired, size, canvas, margin);
            if (hasAvoid && new Rect(centre - size / 2f, size).Overlaps(avoid))
            {
                centre.y = avoid.yMin - size.y / 2f;
                centre = Clamp(centre, size, canvas, margin);
            }
            return centre;
        }

        private static Vector2 Clamp(Vector2 centre, Vector2 size, Rect canvas, float margin)
        {
            return new Vector2(Clamp1(centre.x, size.x / 2f, canvas.xMin + margin, canvas.xMax - margin),
                               Clamp1(centre.y, size.y / 2f, canvas.yMin + margin, canvas.yMax - margin));
        }

        private static float Clamp1(float value, float half, float low, float high)
        {
            float min = low + half, max = high - half;
            return min > max ? (low + high) / 2f : Mathf.Clamp(value, min, max);
        }
    }
}
