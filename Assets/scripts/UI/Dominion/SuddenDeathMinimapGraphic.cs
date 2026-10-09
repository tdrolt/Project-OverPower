using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The sudden-death circle on the minimap. One UI graphic that draws, in the map's own space, a red band all the way from the
    /// circle's edge out beyond the map (everything OUTSIDE the circle is tinted) and a solid red ring on the edge. A mesh instead of 64 line
    /// segments plus a picture, so the tint hugs the circle exactly while it shrinks; it sits under the Viewport mask like the rest of the map, so
    /// the triangle's window clips it. Never takes clicks (raycastTarget off), so it can never swallow a shot.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SuddenDeathMinimapGraphic : MaskableGraphic
    {
        private const int Segments = 64;

        private Vector2 centre;
        private float radius;
        private float ringWidth;
        private float reach;
        private Color ringColour;
        private Color tintColour;

        public override Texture mainTexture => Texture2D.whiteTexture;

        /// <summary>Where the circle is in this graphic's own space (the map's canvas units), how wide its edge line is, how far the tint reaches
        /// past the edge, and the two colours.</summary>
        public void Set(Vector2 circleCentre, float circleRadius, float edgeWidth, float tintReach, Color edgeColour, Color outsideColour)
        {
            if (centre == circleCentre && Mathf.Approximately(radius, circleRadius) && Mathf.Approximately(ringWidth, edgeWidth)
                && Mathf.Approximately(reach, tintReach) && ringColour == edgeColour && tintColour == outsideColour)
                return;
            centre = circleCentre; radius = circleRadius; ringWidth = edgeWidth; reach = tintReach;
            ringColour = edgeColour; tintColour = outsideColour;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (radius <= 0f) return;
            AddBand(vh, radius, radius + reach, tintColour);
            AddBand(vh, Mathf.Max(0f, radius - ringWidth * 0.5f), radius + ringWidth * 0.5f, ringColour);
        }

        private void AddBand(VertexHelper vh, float inner, float outer, Color colour)
        {
            int first = vh.currentVertCount;
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * 2f * Mathf.PI / Segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(centre + direction * inner, colour, Vector2.zero);
                vh.AddVert(centre + direction * outer, colour, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int a = first + i * 2;
                vh.AddTriangle(a, a + 1, a + 2);
                vh.AddTriangle(a + 2, a + 1, a + 3);
            }
        }
    }
}
