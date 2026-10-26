using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The divider lines on one bar, as children of the rect the bar's fill draws in (so they draw over that fill and under any
    /// later sibling). Where they fall is BarSegments'; this only pools the Images and places them. Layout returns at once when
    /// nothing changed, so a per-frame caller costs nothing.
    /// </summary>
    public sealed class BarDividers
    {
        private const int MaxLines = 64;

        private readonly RectTransform parent;
        private readonly float lineWidth;
        private readonly Color colour;
        private readonly List<Image> lines = new List<Image>();
        private readonly float[] positions = new float[MaxLines];
        private int count;
        private float laidMax = -1f;
        private float laidSize = -1f;

        public BarDividers(RectTransform parent, float lineWidth, Color colour)
        {
            this.parent = parent;
            this.lineWidth = lineWidth;
            this.colour = colour;
        }

        public IReadOnlyList<Image> Lines => lines;
        public int Count => count;

        public void Layout(float maxValue, float segmentSize)
        {
            if (maxValue == laidMax && segmentSize == laidSize)
                return;
            laidMax = maxValue;
            laidSize = segmentSize;
            count = BarSegments.Dividers(maxValue, segmentSize, positions);
            while (lines.Count < count)
                lines.Add(NewLine());
            for (int i = 0; i < lines.Count; i++)
            {
                bool used = i < count;
                lines[i].enabled = used;
                if (!used)
                    continue;
                RectTransform rt = lines[i].rectTransform;
                rt.anchorMin = new Vector2(positions[i], 0f);
                rt.anchorMax = new Vector2(positions[i], 1f);
            }
        }

        /// <summary>Shows only the lines the fill reaches (a line past it would float over the empty part of the bar).</summary>
        public void ShowUpTo(float fill)
        {
            for (int i = 0; i < count; i++)
            {
                bool shown = BarSegments.OnFill(positions[i], fill);
                if (lines[i].enabled != shown)
                    lines[i].enabled = shown;
            }
        }

        private Image NewLine()
        {
            var go = new GameObject("Divider", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(lineWidth, 0f);
            Image image = go.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }
    }
}
