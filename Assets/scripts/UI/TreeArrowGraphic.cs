using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Task 5b-2 (D6): draws the arrows of the shop's weapon tree - one curved line with an arrowhead at the upgrade
    /// end per (weapon -> upgrade) pair. It only reads where the node rectangles currently are, so it follows the
    /// layout groups and any new weapon. Not a raycast target: the nodes under it keep their clicks and hovers.
    ///
    /// Shapes: to an upgrade in another column, an S-curve from the weapon's bottom edge to the upgrade's top edge.
    /// To the first upgrade stacked straight below, a short straight drop. To a later upgrade further down the same
    /// column (a node sits between), a C-curve down the gap beside the column, entering the upgrade from the side.
    /// </summary>
    public sealed class TreeArrowGraphic : MaskableGraphic
    {
        private struct Arrow
        {
            public RectTransform from;
            public RectTransform to;
            public int indexAmongSiblings;
        }

        private readonly List<Arrow> arrows = new List<Arrow>();
        private readonly List<Vector2> path = new List<Vector2>(32);
        private readonly Vector3[] corners = new Vector3[4];
        private Vector2 headTip;
        private Vector2 headDir;

        public float LineWidth = 2f;
        public float HeadSize = 10f;
        /// <summary>How far left of the column the side arrow bows out.</summary>
        public float SideLaneWidth = 24f;

        public int ArrowCount => arrows.Count;

        public void Add(RectTransform from, RectTransform to, int indexAmongSiblings)
        {
            arrows.Add(new Arrow { from = from, to = to, indexAmongSiblings = indexAmongSiblings });
            SetVerticesDirty();
        }

        // Node positions come from layout groups that settle after this graphic exists, so repaint each frame while shown (a handful of arrows).
        private void LateUpdate() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (Arrow arrow in arrows)
            {
                if (arrow.from == null || arrow.to == null) continue;
                Rect from = LocalRect(arrow.from);
                Rect to = LocalRect(arrow.to);
                if (from.width <= 0f || to.width <= 0f) continue; // Layout has not run yet.
                BuildPath(from, to, arrow.indexAmongSiblings);
                AddStroke(vh);
            }
        }

        private Rect LocalRect(RectTransform node)
        {
            node.GetWorldCorners(corners);
            Vector2 min = transform.InverseTransformPoint(corners[0]);
            Vector2 max = transform.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void BuildPath(Rect from, Rect to, int indexAmongSiblings)
        {
            path.Clear();
            bool sameColumn = Mathf.Abs(to.center.x - from.center.x) < from.width * 0.25f;
            Vector2 p0, p1, p2, p3;
            if (!sameColumn)
            {
                // S-curve: leaves the weapon's bottom edge straight down, arrives at the upgrade's top edge straight down.
                p0 = new Vector2(from.center.x, from.yMin);
                p3 = new Vector2(to.center.x, to.yMax);
                float midY = (p0.y + p3.y) * 0.5f;
                p1 = new Vector2(p0.x, midY);
                p2 = new Vector2(p3.x, midY);
            }
            else if (indexAmongSiblings == 0)
            {
                p0 = new Vector2(from.center.x, from.yMin);
                p3 = new Vector2(to.center.x, to.yMax);
                p1 = Vector2.Lerp(p0, p3, 0.33f);
                p2 = Vector2.Lerp(p0, p3, 0.66f);
            }
            else
            {
                // C-curve down the gap beside the column, into the upgrade's left edge.
                p0 = new Vector2(from.xMin, from.center.y);
                p3 = new Vector2(to.xMin, to.center.y);
                p1 = new Vector2(p0.x - SideLaneWidth, p0.y);
                p2 = new Vector2(p3.x - SideLaneWidth, p3.y);
            }

            // The line stops where the arrowhead starts; the head's tip sits on the upgrade's edge.
            Vector2 tangent = (p3 - p2).sqrMagnitude > 0.0001f ? (p3 - p2).normalized : (p3 - p0).normalized;
            Vector2 shift = -tangent * HeadSize;
            Vector2 q3 = p3 + shift;
            Vector2 q2 = p2 + shift;
            const int segments = 20;
            for (int i = 0; i <= segments; i++)
                path.Add(Bezier(p0, p1, q2, q3, i / (float)segments));

            headTip = p3;
            headDir = tangent;
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        private void AddStroke(VertexHelper vh)
        {
            float half = LineWidth * 0.5f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();
                Vector2 n = new Vector2(-dir.y, dir.x) * half;
                int start = vh.currentVertCount;
                vh.AddVert(a - n, color, Vector2.zero);
                vh.AddVert(a + n, color, Vector2.zero);
                vh.AddVert(b + n, color, Vector2.zero);
                vh.AddVert(b - n, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }

            Vector2 headNormal = new Vector2(-headDir.y, headDir.x) * (HeadSize * 0.5f);
            Vector2 baseCentre = headTip - headDir * HeadSize;
            int h = vh.currentVertCount;
            vh.AddVert(headTip, color, Vector2.zero);
            vh.AddVert(baseCentre + headNormal, color, Vector2.zero);
            vh.AddVert(baseCentre - headNormal, color, Vector2.zero);
            vh.AddTriangle(h, h + 1, h + 2);
        }
    }
}
