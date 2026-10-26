using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>A ring of sharp triangles round an empty middle, as flat UI geometry (no sprite), centred on its RectTransform's pivot.
    /// The Raybeam's mark; the shape comes from RaybeamMarkRule.Spike.</summary>
    [RequireComponent(typeof(CanvasRenderer))] // Graphic itself does not add one; Image declares its own
    public class SpikeRingGraphic : MaskableGraphic
    {
        private int spikeCount = 8;
        private float innerRadius = 10f;
        private float length = 10f;
        private float spikeWidth = 0.5f;

        public void Configure(int count, float inner, float spikeLength, float width)
        {
            spikeCount = Mathf.Max(3, count);
            innerRadius = inner;
            length = spikeLength;
            spikeWidth = width;
            rectTransform.sizeDelta = Vector2.one * 2f * (inner + spikeLength);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 centre = rectTransform.rect.center;
            for (int i = 0; i < spikeCount; i++)
            {
                RaybeamMarkRule.Spike(i, spikeCount, innerRadius, length, spikeWidth, out Vector2 left, out Vector2 tip, out Vector2 right);
                int at = vh.currentVertCount;
                vh.AddVert(centre + left, color, Vector2.zero);
                vh.AddVert(centre + tip, color, Vector2.zero);
                vh.AddVert(centre + right, color, Vector2.zero);
                vh.AddTriangle(at, at + 1, at + 2);
            }
        }
    }
}
