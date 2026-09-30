using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>Task T6 step 2: a top-down PNG of the arena for the report's death/position heatmaps,
    /// plus the world-to-pixel mapping the HTML needs to place dots on it. Same approach as the
    /// arena-symmetry design's own ArenaRender (docs/superpowers/plans/2026-09-16-arena-symmetry.md,
    /// Task A3 step 1): a HideAndDontSave orthographic camera renders once and is destroyed
    /// immediately after, so Game Scene is never dirtied by building a report.
    ///
    /// Framed on x -7..137, z -7..137 (144m square: the arena including its capital pockets) at 1024
    /// px, i.e. 1024/144 px per metre.</summary>
    public static class ArenaReportRender
    {
        private const float MinX = -7f;
        private const float MinZ = -7f;
        private const float SpanMetres = 144f; // 137 - (-7)
        private const int PixelSize = 1024;
        private const string GameScenePath = "Assets/Scenes/Game Scene.unity";

        public sealed class Result
        {
            /// <summary>True if the render happened - false means Game Scene wasn't open, and every
            /// other field is meaningless (Note explains why for the report header).</summary>
            public bool Available;
            public string Note;
            public string Base64Png;
            public float MinX;
            public float MinZ;
            public float MetresPerPixel;
            public int PixelSize;
        }

        /// <summary>Review fix (T6 item 6): the same world-to-pixel mapping the HTML's own toPixel()
        /// JS function implements (HtmlReportWriter), exposed here as a pure function so the mapping
        /// itself - including the Y-FLIP ("image up is +Z", per the arena-symmetry design doc's own
        /// ArenaRender comment) - can be unit tested without Game Scene open or a real render.</summary>
        public static (float Px, float Py) WorldToPixel(float x, float z)
        {
            float metresPerPixel = SpanMetres / PixelSize;
            float px = (x - MinX) / metresPerPixel;
            float py = PixelSize - (z - MinZ) / metresPerPixel;
            return (px, py);
        }

        /// <summary>Renders the arena if (and only if) a scene named "Game Scene" is currently open -
        /// checked across every LOADED scene, not just the active one, so the report still gets its
        /// heatmaps if Game Scene is merely not the active scene in a multi-scene setup.</summary>
        public static Result Render()
        {
            if (!IsGameSceneOpen())
            {
                return new Result
                {
                    Available = false,
                    Note = "Game Scene isn't open - death/position heatmaps omitted. Open " +
                           GameScenePath + " and rebuild the report to include them.",
                };
            }

            // Captured before the try so `finally` can always restore it - even if ReadPixels/Apply/EncodeToPNG
            // throws while rt is still the active RenderTexture, leaving RenderTexture.active pointing at rt right
            // as it's about to be destroyed below.
            RenderTexture prevActive = RenderTexture.active;
            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = EditorUtility.CreateGameObjectWithHideFlags("ArenaReportRenderCam", HideFlags.HideAndDontSave, typeof(Camera));
                var cam = go.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = SpanMetres / 2f;
                float centre = MinX + SpanMetres / 2f; // same value for X and Z: the frame is square
                cam.transform.SetPositionAndRotation(new Vector3(centre, 150f, centre), Quaternion.Euler(90f, 0f, 0f));
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;

                rt = new RenderTexture(PixelSize, PixelSize, 24);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(PixelSize, PixelSize, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, PixelSize, PixelSize), 0, 0);
                tex.Apply();

                byte[] png = ImageConversion.EncodeToPNG(tex);
                return new Result
                {
                    Available = true,
                    Note = "",
                    Base64Png = Convert.ToBase64String(png),
                    MinX = MinX,
                    MinZ = MinZ,
                    MetresPerPixel = SpanMetres / PixelSize,
                    PixelSize = PixelSize,
                };
            }
            finally
            {
                // Restore before destroying rt: once destroyed, RenderTexture.active must not still reference it.
                RenderTexture.active = prevActive;
                if (rt != null)
                {
                    // cam.targetTexture must be cleared before the RenderTexture is released, else the
                    // camera (already about to be destroyed too) is left pointing at a freed texture.
                    var cam = go != null ? go.GetComponent<Camera>() : null;
                    if (cam != null) cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static bool IsGameSceneOpen()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.path == GameScenePath) return true;
            }
            return false;
        }
    }
}
