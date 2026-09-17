using System.Collections.Generic;
using System.IO;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// OverPower > Arena > Bake minimap image: renders the arena from above into Assets/Gameplay/UI/ArenaMinimap.png
    /// and records the world square it covers on Assets/Gameplay/Config/MinimapConfig.asset (created if missing).
    ///
    /// Framing: a square centred on ArenaSymmetry's centre, whose side clears the farthest mesh under Source and both
    /// generated thirds by the config's margin on every side of the triangular mask (Tudor, 2026-09-17; see
    /// MinimapLayout.TriangleCircumradius). Centring on the symmetry centre (not the bounding box) means turning the
    /// map to any team's camera yaw keeps the arena inside the triangular minimap, and all three teams see the same map.
    ///
    /// Rebuild thirds calls this too (ArenaSymmetryInspector), so the image can't go stale after an arena edit. It
    /// never touches the scene. It saves only the config asset, never SaveAssets(), which would also write any other
    /// dirty asset.
    /// </summary>
    public static class MinimapBaker
    {
        public const string ImagePath = "Assets/Gameplay/UI/ArenaMinimap.png";
        public const string ConfigPath = "Assets/Gameplay/Config/MinimapConfig.asset";

        [MenuItem("OverPower/Arena/Bake minimap image")]
        private static void BakeFromMenu() => LogResult(BakeOpenScene());

        /// <summary>Logs a Bake/BakeOpenScene result at the right severity, so ArenaSymmetryInspector's two call
        /// sites (the Rebuild thirds button and its menu item) share it too: a warning for a "not baked: ..."
        /// outcome (nothing changed - worth the designer's attention), a plain log once it actually baked.</summary>
        public static void LogResult(string result)
        {
            if (result != null && result.StartsWith("not baked"))
                Debug.LogWarning("[Minimap] " + result);
            else
                Debug.Log("[Minimap] " + result);
        }

        /// <summary>Bakes the one ArenaSymmetry in the open scenes. Returns a one-line report (what the menu logs).</summary>
        public static string BakeOpenScene()
        {
            ArenaSymmetry[] arenas = UnityEngine.Object.FindObjectsByType<ArenaSymmetry>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (arenas.Length != 1)
                return $"not baked: found {arenas.Length} ArenaSymmetry components in the open scenes (need exactly one).";
            return Bake(arenas[0]);
        }

        public static string Bake(ArenaSymmetry arena)
        {
            if (arena == null)
                return "not baked: no ArenaSymmetry given.";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "not baked: stop Play Mode first (the picture would include players and capture rings).";
            if (EditorSceneManager.IsPreviewScene(arena.gameObject.scene))
                return "not baked: this arena is in a preview scene.";
            if (arena.source == null || arena.generated120 == null || arena.generated240 == null)
                return "not baked: Source, Generated 120 and Generated 240 must all be assigned on ArenaSymmetry.";

            MinimapConfig config = LoadOrCreateConfig();
            float radius = ArenaRadius(arena, out string farthest);
            if (radius <= 0f)
                return "not baked: there are no meshes under Source or the generated thirds.";

            // Triangular framing (Tudor, 2026-09-17: the minimap mask becomes a triangle, one vertex toward each
            // capital) - see MinimapLayout.TriangleCircumradius for the maths. vertexDirections come from a real
            // Tier 1 tower when one exists (never hard-coded to +Z); ArenaSymmetry's 3-fold layout means the other
            // two sit 120 degrees from it either way, so deriving them by rotation is exact, not an approximation.
            Vector2[] vertexDirections = CapitalDirections(arena, out string towerUsed);
            List<Vector2> points = ArenaPoints(arena);
            float circumradius = MinimapLayout.TriangleCircumradius(points, vertexDirections, config.MarginMetres);
            float size = 2f * circumradius;
            var centre = new Vector2(arena.centre.x, arena.centre.z);
            int pixels = Mathf.Clamp(config.ImagePixels, 256, 2048);

            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ImagePath);
            File.WriteAllBytes(fullPath, TopDownRender.RenderPng(centre, size, pixels));
            AssetDatabase.ImportAsset(ImagePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureImporter(pixels);

            var so = new SerializedObject(config);
            so.FindProperty("arenaImage").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
            so.FindProperty("worldCentre").vector2Value = centre;
            so.FindProperty("worldSizeMetres").floatValue = size;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(config);

            return $"baked {ImagePath} ({pixels} px) covering {size:0.00} m (triangle circumradius {circumradius:0.00} m, " +
                   $"vertex direction ({vertexDirections[0].x:0.000}, {vertexDirections[0].y:0.000}) from {towerUsed}) " +
                   $"centred on ({centre.x:0.00}, {centre.y:0.00}); farthest mesh corner {radius:0.00} m, set by '{farthest}'.";
        }

        /// <summary>The three vertex directions the triangular mask points its corners along (Tudor, 2026-09-17),
        /// unit vectors relative to the arena centre in (world X, world Z) - MinimapLayout's own map-space
        /// convention. Reads a real Tier 1 BuildingCapture when one exists in the open scenes (never hard-codes
        /// +Z); the other two directions are then exactly 120 degrees from it either way, since ArenaSymmetry's
        /// generated120/generated240 thirds guarantee the arena - and so its towers - repeat with that symmetry.
        /// Falls back to a plain "up" (0,1) reference (and logs a warning) only when no Tier 1 tower exists yet to
        /// read (e.g. a bare preview scene in a test) so baking never throws.</summary>
        public static Vector2[] CapitalDirections(ArenaSymmetry arena, out string towerUsed)
        {
            Vector2 reference = new Vector2(0f, 1f);
            towerUsed = null;
            foreach (BuildingCapture capture in UnityEngine.Object.FindObjectsByType<BuildingCapture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (capture.tier != 1) continue;
                Vector2 direction = new Vector2(capture.transform.position.x - arena.centre.x, capture.transform.position.z - arena.centre.z);
                if (direction.sqrMagnitude <= 0.0001f) continue;
                reference = direction.normalized;
                towerUsed = $"tower {capture.buildingID} ('{capture.name}')";
                break;
            }
            if (towerUsed == null)
            {
                towerUsed = "no Tier 1 tower found - fell back to (0,1)";
                Debug.LogWarning("[Minimap] No Tier 1 BuildingCapture found in the open scenes - the triangle's vertex " +
                                  "direction falls back to a plain 'up' (0,1) reference instead of a real capital.");
            }
            const float cos120 = -0.5f, sin120 = 0.8660254f;
            return new[]
            {
                reference,
                new Vector2(reference.x * cos120 - reference.y * sin120, reference.x * sin120 + reference.y * cos120),
                new Vector2(reference.x * cos120 + reference.y * sin120, -reference.x * sin120 + reference.y * cos120),
            };
        }

        /// <summary>Every MeshRenderer bounds corner under Source or a generated third, relative to the arena
        /// centre, in (world X, world Z) - the point set TriangleCircumradius frames.</summary>
        public static List<Vector2> ArenaPoints(ArenaSymmetry arena)
        {
            var points = new List<Vector2>();
            foreach ((Vector2 point, string _) in WalkMeshCorners(arena))
                points.Add(point);
            return points;
        }

        /// <summary>The farthest flat distance from the arena centre to any corner of a MeshRenderer's bounds under
        /// Source or a generated third, and that object's name. Meshes only: particles and lines have loose bounds.</summary>
        public static float ArenaRadius(ArenaSymmetry arena, out string farthestName)
        {
            float best = 0f;
            farthestName = "";
            foreach ((Vector2 point, string name) in WalkMeshCorners(arena))
            {
                float distance = point.magnitude;
                if (distance > best)
                {
                    best = distance;
                    farthestName = name;
                }
            }
            return best;
        }

        /// <summary>Shared by ArenaPoints and ArenaRadius (review fix, 2026-09-17: they used to repeat the same
        /// mesh-corner loop): every MeshRenderer bounds corner under Source or a generated third, relative to the
        /// arena centre, paired with that renderer's name.</summary>
        private static IEnumerable<(Vector2 Point, string RendererName)> WalkMeshCorners(ArenaSymmetry arena)
        {
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null) continue;
                foreach (MeshRenderer renderer in third.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Bounds bounds = renderer.bounds;
                    foreach (float x in new[] { bounds.min.x, bounds.max.x })
                        foreach (float z in new[] { bounds.min.z, bounds.max.z })
                            yield return (new Vector2(x - arena.centre.x, z - arena.centre.z), renderer.name);
                }
            }
        }

        private static MinimapConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<MinimapConfig>(ConfigPath);
            if (config != null)
                return config;
            config = ScriptableObject.CreateInstance<MinimapConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        // A plain colour picture, sampled smaller on the corner map: clamp so the edges don't wrap, mipmaps so it
        // stays smooth when shrunk, and uncompressed so block compression doesn't blur the hard wall/pocket edges
        // the minimap's bubbles and links line up against.
        private static void ConfigureImporter(int pixels)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
            if (importer == null)
                return;
            bool changed = importer.textureType != TextureImporterType.Default
                           || importer.wrapMode != TextureWrapMode.Clamp
                           || !importer.mipmapEnabled
                           || importer.maxTextureSize < pixels
                           || importer.textureCompression != TextureImporterCompression.Uncompressed;
            if (!changed)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, pixels);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
