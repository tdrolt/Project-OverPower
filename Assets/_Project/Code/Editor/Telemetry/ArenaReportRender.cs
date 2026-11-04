using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Overpower.Data;

namespace Overpower.EditorTools.Telemetry
{
    /// <summary>The top-down picture under the report's death and position maps, and where it sits in the world. It is the map's own saved
    /// minimap picture (its MinimapConfig: the image, its world centre and size), so a report draws the right map whichever scene is open, or
    /// none. The page shows the patch ReportMaps.FrameFor picks out of it.</summary>
    public static class ArenaReportRender
    {
        private const float MinX = -7f;
        private const float MinZ = -7f;
        private const float SpanMetres = 144f; // 137 - (-7)
        private const int PixelSize = 1024;

        public sealed class Result
        {
            /// <summary>False when the map has no saved picture; Note then says why for the page.</summary>
            public bool Available;
            public string Note;
            public string MapName;
            public string Base64Png;
            /// <summary>The world patch the whole saved picture covers.</summary>
            public float ImageMinX;
            public float ImageMinZ;
            public float ImageWidthMetres;
            public float ImageDepthMetres;
            /// <summary>The world patch the page shows, and the page's picture size for it.</summary>
            public float FrameMinX;
            public float FrameMinZ;
            public float FrameWidthMetres;
            public float FrameDepthMetres;
            public int PixelsAcross;
            public int PixelsUp;
        }

        /// <summary>The triangle arena's world-to-pixel mapping on its 1024 px frame (x and z -7 to 137), with the Y-FLIP (image up is +Z).</summary>
        public static (float Px, float Py) WorldToPixel(float x, float z)
        {
            float metresPerPixel = SpanMetres / PixelSize;
            float px = (x - MinX) / metresPerPixel;
            float py = PixelSize - (z - MinZ) / metresPerPixel;
            return (px, py);
        }

        /// <summary>The triangle arena's picture.</summary>
        public static Result Render() => Render(ReportMap.Triangle);

        /// <summary>The map's saved picture; never null, and never throws (a report without a map is still a report).</summary>
        public static Result Render(ReportMap map)
        {
            string mapName = "";
            try
            {
                mapName = ReportMaps.Name(map);
                string configPath = ReportMaps.MinimapConfigPath(map);
                var config = AssetDatabase.LoadAssetAtPath<MinimapConfig>(configPath);
                if (config == null || config.ArenaImage == null)
                    return Unavailable(mapName, "No saved picture of " + mapName + " (" + configPath + ") - the map is left out.");

                string assetPath = AssetDatabase.GetAssetPath(config.ArenaImage);
                string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
                if (!File.Exists(fullPath))
                    return Unavailable(mapName, "The saved picture of " + mapName + " is missing (" + assetPath + ") - the map is left out.");

                var picture = new MapFrame(config.WorldCentre.x - config.WorldSizeMetres / 2f, config.WorldCentre.y - config.WorldDepthMetres / 2f,
                                           config.WorldSizeMetres, config.WorldDepthMetres);
                MapFrame frame = ReportMaps.FrameFor(map, picture);
                return new Result
                {
                    Available = true,
                    Note = "",
                    MapName = mapName,
                    Base64Png = Convert.ToBase64String(File.ReadAllBytes(fullPath)),
                    ImageMinX = picture.MinX,
                    ImageMinZ = picture.MinZ,
                    ImageWidthMetres = picture.Width,
                    ImageDepthMetres = picture.Depth,
                    FrameMinX = frame.MinX,
                    FrameMinZ = frame.MinZ,
                    FrameWidthMetres = frame.Width,
                    FrameDepthMetres = frame.Depth,
                    PixelsAcross = PixelSize,
                    PixelsUp = ReportMaps.PixelsUp(frame, PixelSize),
                };
            }
            catch (Exception e)
            {
                return Unavailable(mapName, "The map picture could not be read (" + e.Message + ") - the map is left out.");
            }
        }

        private static Result Unavailable(string mapName, string note) => new Result { Available = false, MapName = mapName, Note = note };
    }
}
