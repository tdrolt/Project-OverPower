using UnityEngine;

namespace Overpower.UI
{
    /// <summary>Screen-pixel rects of UI on screen-space canvases, for placing one panel clear of another. An overlay canvas's world corners are
    /// already screen pixels; a canvas switched to camera space (a capture renders the HUD that way) goes through its camera.</summary>
    public static class ScreenRects
    {
        private static readonly Vector3[] corners = new Vector3[4];

        /// <summary>The screen pixels the rect covers; empty when it is missing or not shown.</summary>
        public static Rect Of(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return Rect.zero;
            rect.GetWorldCorners(corners);
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static bool IsEmpty(Rect r) => r.width <= 0f || r.height <= 0f;

        /// <summary>The smallest rect holding both; an empty one adds nothing.</summary>
        public static Rect Union(Rect a, Rect b)
        {
            if (IsEmpty(a)) return b;
            if (IsEmpty(b)) return a;
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }
    }
}
