using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// A pointy-top hexagon outline drawn as its own mesh with mitred corners, so it can grow and shrink every frame while its line
    /// keeps the same thickness (a sprite scaled to size would thicken the line with it). Sized by the apothem: the distance from
    /// the centre to the inside of each side.
    /// </summary>
    // Graphic's own RequireComponent is not inherited: without this a ring built in code has no CanvasRenderer and never draws.
    [RequireComponent(typeof(CanvasRenderer))]
    public class HexRingGraphic : MaskableGraphic
    {
        private const float Cos30 = 0.8660254f;
        // Below this a change is invisible; skipping it spares a mesh rebuild every frame while an eased radius settles.
        private const float Epsilon = 0.01f;

        private float apothem;
        private float lineWidth;

        public float Apothem => apothem;
        public float LineWidth => lineWidth;

        public void SetShape(float innerApothem, float width)
        {
            innerApothem = Mathf.Max(0f, innerApothem);
            width = Mathf.Max(0f, width);
            if (Mathf.Abs(innerApothem - apothem) < Epsilon && Mathf.Abs(width - lineWidth) < Epsilon)
                return;
            apothem = innerApothem;
            lineWidth = width;
            SetVerticesDirty();
        }

        /// <summary>Corner 0 is straight up, the rest follow clockwise.</summary>
        public static Vector2 Corner(int corner, float apothem)
        {
            float radius = apothem / Cos30;
            float radians = corner * Mathf.PI / 3f;
            return new Vector2(Mathf.Sin(radians) * radius, Mathf.Cos(radians) * radius);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (lineWidth <= 0f)
                return;
            Color32 c = color;
            for (int i = 0; i < 6; i++)
            {
                vh.AddVert(Corner(i, apothem), c, Vector2.zero);
                vh.AddVert(Corner(i, apothem + lineWidth), c, Vector2.zero);
            }
            for (int i = 0; i < 6; i++)
            {
                int inner = i * 2, nextInner = (i + 1) % 6 * 2;
                vh.AddTriangle(inner, inner + 1, nextInner + 1);
                vh.AddTriangle(inner, nextInner + 1, nextInner);
            }
        }
    }
}
