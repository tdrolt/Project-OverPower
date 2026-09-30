using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Match;

namespace Overpower.UI
{
    /// <summary>
    /// Task 5b-2 (D6): draws the arrows of the shop's weapon tree - one curved line with an arrowhead at the upgrade
    /// end per (weapon -> upgrade) pair. It only reads where the node rectangles currently are, so it follows the
    /// layout groups and any new weapon. Not a raycast target: the nodes under it keep their clicks and hovers.
    ///
    /// Shapes: to a node off to the side (the two upgrades of a family sit side by side under it, Task 13), an S-curve
    /// from the weapon's bottom edge to the upgrade's top edge. To a node directly below, a short straight drop. To a
    /// node further down the same column with another node in between, a C-curve down the gap beside the column,
    /// entering it from the side. The shop's tree today only needs the S-curve; the other two keep any layout working.
    /// </summary>
    public sealed class TreeArrowGraphic : MaskableGraphic
    {
        private struct Arrow
        {
            public RectTransform from;
            public RectTransform to;
        }

        private readonly List<Arrow> arrows = new List<Arrow>();
        private readonly List<Vector2> path = new List<Vector2>(32);
        private readonly List<RectTransform> nodeRects = new List<RectTransform>();
        private readonly List<TreeNodeBox> boxes = new List<TreeNodeBox>();
        private readonly List<TreeNodeBox> others = new List<TreeNodeBox>();
        private readonly Vector3[] corners = new Vector3[4];
        private Vector2 headTip;
        private Vector2 headDir;

        public float LineWidth = 2f;
        public float HeadSize = 10f;
        /// <summary>How far left of the column the side arrow bows out.</summary>
        public float SideLaneWidth = 24f;

        public int ArrowCount => arrows.Count;

        public void Add(RectTransform from, RectTransform to)
        {
            arrows.Add(new Arrow { from = from, to = to });
            SetVerticesDirty();
        }

        // Node positions come from layout groups that settle after this graphic exists, so repaint each frame while shown (a handful of arrows).
        private void LateUpdate() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // Every node's box, so each arrow's shape can be chosen by where the nodes really are.
            nodeRects.Clear();
            foreach (Arrow arrow in arrows)
            {
                if (arrow.from == null || arrow.to == null) continue;
                if (!nodeRects.Contains(arrow.from)) nodeRects.Add(arrow.from);
                if (!nodeRects.Contains(arrow.to)) nodeRects.Add(arrow.to);
            }
            boxes.Clear();
            foreach (RectTransform node in nodeRects)
            {
                Rect r = LocalRect(node);
                boxes.Add(new TreeNodeBox(r.xMin, r.xMax, r.yMin, r.yMax));
            }

            foreach (Arrow arrow in arrows)
            {
                if (arrow.from == null || arrow.to == null) continue;
                int fromIndex = nodeRects.IndexOf(arrow.from);
                int toIndex = nodeRects.IndexOf(arrow.to);
                Rect from = LocalRect(arrow.from);
                Rect to = LocalRect(arrow.to);
                if (from.width <= 0f || to.width <= 0f) continue; // Layout has not run yet.
                others.Clear();
                for (int i = 0; i < boxes.Count; i++)
                    if (i != fromIndex && i != toIndex) others.Add(boxes[i]);
                BuildPath(from, to, TreeArrowShapeRule.Choose(boxes[fromIndex], boxes[toIndex], others));
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

        private void BuildPath(Rect from, Rect to, TreeArrowShape shape)
        {
            path.Clear();
            Vector2 p0, p1, p2, p3;
            if (shape == TreeArrowShape.SCurve)
            {
                // S-curve: leaves the weapon's bottom edge straight down, arrives at the upgrade's top edge straight down.
                p0 = new Vector2(from.center.x, from.yMin);
                p3 = new Vector2(to.center.x, to.yMax);
                float midY = (p0.y + p3.y) * 0.5f;
                p1 = new Vector2(p0.x, midY);
                p2 = new Vector2(p3.x, midY);
            }
            else if (shape == TreeArrowShape.Straight)
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
