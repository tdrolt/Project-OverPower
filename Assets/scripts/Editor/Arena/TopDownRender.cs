using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Renders the open scene straight down onto a square PNG, like ArenaReportRender (telemetry heatmaps), for any
    /// centre and size. The camera is HideAndDontSave and destroyed straight after, so the scene is never marked dirty.
    /// Image up = world +Z, right = world +X.
    /// </summary>
    public static class TopDownRender
    {
        public static byte[] RenderPng(Vector2 centreXZ, float spanMetres, int pixels) => RenderPng(centreXZ, spanMetres, spanMetres, pixels);

        /// <summary>A picture of widthMetres (along X) by depthMetres (along Z), pixelsAcross wide; its height in pixels keeps the same scale on both axes.</summary>
        public static byte[] RenderPng(Vector2 centreXZ, float widthMetres, float depthMetres, int pixelsAcross)
        {
            int pixels = pixelsAcross;
            int pixelsUp = Mathf.Max(1, Mathf.RoundToInt(pixelsAcross * depthMetres / widthMetres));
            float spanMetres = depthMetres;
            // Captured before the try so `finally` can always restore it, even if ReadPixels/Apply/EncodeToPNG throws
            // while rt is still the active RenderTexture.
            RenderTexture previous = RenderTexture.active;
            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = EditorUtility.CreateGameObjectWithHideFlags("TopDownRenderCam", HideFlags.HideAndDontSave, typeof(Camera));
                var cam = go.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = spanMetres / 2f;
                cam.transform.SetPositionAndRotation(new Vector3(centreXZ.x, 150f, centreXZ.y), Quaternion.Euler(90f, 0f, 0f));
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;

                rt = new RenderTexture(pixels, pixelsUp, 24);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(pixels, pixelsUp, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, pixels, pixelsUp), 0, 0);
                tex.Apply();
                return ImageConversion.EncodeToPNG(tex);
            }
            finally
            {
                // Restore before destroying rt: once destroyed, RenderTexture.active must not still reference it.
                RenderTexture.active = previous;
                if (rt != null)
                {
                    Camera cam = go != null ? go.GetComponent<Camera>() : null;
                    if (cam != null) cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
