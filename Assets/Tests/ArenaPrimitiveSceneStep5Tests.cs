using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overpower.Arena;
using Overpower.EditorTools;

namespace Overpower.Tests
{
    /// <summary>Arena step 5 (amended): pins the whole scene once ArenaPrimitiveBuilder.BuildAll has run against
    /// it - every solid piece an upright, static, Building-layer box except a barrier (its own layer 8), no
    /// MeshCollider or old-art renderer left active, one flat Default-layer floor, the boundary closed at every
    /// corner and seam (this is step 5's GREEN evidence against step 4's 12-notch BEFORE coverage record), and one
    /// jersey barrier per Tier III recess meeting cover at both ends. Uses the same WithGameScene pattern as
    /// ArenaSymmetrySceneTests, so it never opens the scene Single (which would prompt to save a dirty scene).</summary>
    public class ArenaPrimitiveSceneStep5Tests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";
        private const string OldArtGroupName = ArenaPrimitiveBuilder.OldArtGroupName;

        [Test]
        public void EveryArenaPieceIsAnUprightStaticBoxOnTheBuildingLayerExceptBarriersOnTheirOwnLayer()
        {
            WithGameScene(scene =>
            {
                int buildingLayer = LayerMask.NameToLayer("Building");
                int barrierLayer = LayerMask.NameToLayer(ArenaLayers.BarrierLayerName);
                Transform[] thirds = FindThirds(scene);

                foreach (Transform third in thirds)
                {
                    foreach (BoxCollider box in third.GetComponentsInChildren<BoxCollider>(true))
                    {
                        if (!box.gameObject.activeInHierarchy || !box.enabled) continue;
                        int expectedLayer = box.gameObject.layer == barrierLayer ? barrierLayer : buildingLayer;
                        Assert.AreEqual(expectedLayer, box.gameObject.layer,
                            $"'{box.name}' under {third.name} should be on Building unless it is a barrier");

                        float upDot = Vector3.Dot(box.transform.up, Vector3.up);
                        Assert.Greater(upDot, 0.999f, $"'{box.name}' under {third.name} is not upright (up.y dot {upDot:0.000})");

                        Renderer renderer = box.GetComponent<Renderer>();
                        if (renderer != null)
                            Assert.IsTrue(GameObjectUtility.AreStaticEditorFlagsSet(box.gameObject, StaticEditorFlags.BatchingStatic),
                                $"'{box.name}' under {third.name} should be BatchingStatic");
                    }
                }
            });
        }

        [Test]
        public void NoMeshColliderAndNoOldArtIsActive()
        {
            WithGameScene(scene =>
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshCollider mesh in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (mesh.enabled && mesh.gameObject.activeInHierarchy)
                            Assert.Fail($"active enabled MeshCollider found: '{GetPath(mesh.transform)}'");
                    }
                }

                Transform environment = FindEnvironment(scene);
                Assert.IsNotNull(environment, "no 'Enviorment' root found");
                foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (renderer.transform.IsChildOf(environment.Find(OldArtGroupName))) continue;

                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                        Assert.IsFalse(meshPath.StartsWith("Assets/Sources/"),
                            $"'{GetPath(renderer.transform)}' still renders a mesh from Assets/Sources/ ('{meshPath}')");
                    }
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        string materialPath = AssetDatabase.GetAssetPath(material);
                        Assert.IsFalse(materialPath.StartsWith("Assets/Sources/"),
                            $"'{GetPath(renderer.transform)}' still uses a material from Assets/Sources/ ('{materialPath}')");
                    }
                }
            });
        }

        [Test]
        public void TheFloorIsOneDefaultLayerSlabWithItsTopAtZero()
        {
            WithGameScene(scene =>
            {
                Transform environment = FindEnvironment(scene);
                Transform floor = environment.Find(ArenaPrimitiveBuilder.FloorObjectName);
                Assert.IsNotNull(floor, "no Arena Floor object found directly under Enviorment");
                Assert.IsFalse(floor.IsChildOf(FindArena(scene).source), "the floor must live outside Source");

                Assert.AreEqual(LayerMask.NameToLayer("Default"), floor.gameObject.layer);
                Renderer renderer = floor.GetComponent<Renderer>();
                float top = renderer.bounds.max.y;
                Assert.AreEqual(0f, top, 0.001f, "the floor's top should sit exactly at world Y 0");
            });
        }

        [Test]
        public void TheBoundaryIsClosedAtEveryCornerAndSeam()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = FindArena(scene);
                ArenaBounds outline = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre);
                var footprints = new List<BoxFootprint>();
                foreach (Transform third in FindThirds(scene))
                {
                    Transform boundry = third.Find(ArenaSymmetry.BoundaryGroupName);
                    Assert.IsNotNull(boundry, $"{third.name} has no Boundry group");
                    foreach (BoxCollider box in boundry.GetComponentsInChildren<BoxCollider>(true))
                        footprints.Add(BoxFootprintFromBox(box));
                }

                List<string> holes = ArenaWallCoverage.FindHoles(outline, footprints, 0.724f);
                Assert.IsEmpty(holes, "the boundary must have no hole at any corner or seam:\n" + string.Join("\n", holes));

                List<string> intrusions = ArenaWallCoverage.FindIntrusions(outline, footprints);
                Assert.IsEmpty(intrusions, "no wall should intrude into the arena:\n" + string.Join("\n", intrusions));
            });
        }

        [Test]
        public void EveryBoundaryWallStandsFromBelowTheFloorToFullHeight()
        {
            WithGameScene(scene =>
            {
                foreach (Transform third in FindThirds(scene))
                {
                    Transform boundry = third.Find(ArenaSymmetry.BoundaryGroupName);
                    foreach (BoxCollider box in boundry.GetComponentsInChildren<BoxCollider>(true))
                    {
                        Bounds worldBounds = box.bounds;
                        Assert.LessOrEqual(worldBounds.min.y, 0f, $"'{box.name}' under {third.name} should reach below the floor");
                        Assert.GreaterOrEqual(worldBounds.max.y, 5.9f, $"'{box.name}' under {third.name} should reach full wall height");
                    }
                }
            });
        }

        [Test]
        public void EachTierIIIRecessHasOneBarrierOnItsOwnLayerMeetingCoverAtBothEnds()
        {
            WithGameScene(scene =>
            {
                int barrierLayer = LayerMask.NameToLayer(ArenaLayers.BarrierLayerName);
                Assert.GreaterOrEqual(barrierLayer, 0);

                var allBarriers = new List<BoxCollider>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                        if (box.gameObject.layer == barrierLayer)
                            allBarriers.Add(box);

                Assert.AreEqual(3, allBarriers.Count, "exactly one barrier per third");

                ArenaSymmetry arena = FindArena(scene);
                var mouthLines = new List<(Vector2 a, Vector2 b)>();
                for (int third = 0; third < 3; third++)
                {
                    Vector3 p0 = RadialSymmetry.RotatePoint(new Vector3(arena.sourceOutline[0].x, 0f, arena.sourceOutline[0].y), arena.centre, third);
                    Vector3 p3 = RadialSymmetry.RotatePoint(new Vector3(arena.sourceOutline[3].x, 0f, arena.sourceOutline[3].y), arena.centre, third);
                    mouthLines.Add((new Vector2(p0.x, p0.z), new Vector2(p3.x, p3.z)));
                }

                var allBuildingBoxes = new List<BoxFootprint>();
                foreach (Transform t in FindThirds(scene))
                    foreach (BoxCollider box in t.GetComponentsInChildren<BoxCollider>(true))
                        if (box.gameObject.layer == LayerMask.NameToLayer("Building"))
                            allBuildingBoxes.Add(BoxFootprintFromBox(box));

                foreach (BoxCollider barrier in allBarriers)
                {
                    // The look and the blocking band are separate (D24): only the collider's band is checked here.
                    Bounds worldBounds = barrier.bounds;
                    Assert.LessOrEqual(worldBounds.min.y, -0.5f, $"'{barrier.name}' blocking band should reach below -0.5 m");
                    Assert.GreaterOrEqual(worldBounds.max.y, 2.7f, $"'{barrier.name}' blocking band should reach above 2.7 m");

                    // Its world length axis is local X (a review nit: a future builder change must not silently
                    // rotate the barrier's own long axis onto Z without this catching it). The BoxCollider's own
                    // `size` is the default unit cube (Vector3.one) - the real length/thickness live on the
                    // transform's localScale, which BuildSource sets directly from the layout's Piece.size.
                    Assert.Greater(barrier.transform.localScale.x, barrier.transform.localScale.z,
                        $"'{barrier.name}' should be longer along local X than Z");

                    Vector2 centreXZ = new Vector2(barrier.transform.position.x, barrier.transform.position.z);
                    float bestDistance = float.MaxValue;
                    foreach ((Vector2 a, Vector2 b) in mouthLines)
                        bestDistance = Mathf.Min(bestDistance, ArenaBounds.DistanceToSegment(centreXZ, a, b));
                    Assert.LessOrEqual(bestDistance, 1f, $"'{barrier.name}' should sit within 1 m of a recess mouth line");

                    BoxFootprint footprint = BoxFootprintFromBox(barrier);
                    Vector2 endA = footprint.Centre - footprint.Along * footprint.HalfLength;
                    Vector2 endB = footprint.Centre + footprint.Along * footprint.HalfLength;
                    Assert.IsTrue(allBuildingBoxes.Any(f => f.OverlapsCircle(endA, 0.05f)),
                        $"'{barrier.name}' end A should meet a Building box (a plank)");
                    Assert.IsTrue(allBuildingBoxes.Any(f => f.OverlapsCircle(endB, 0.05f)),
                        $"'{barrier.name}' end B should meet a Building box (a plank)");
                }
            });
        }

        private static BoxFootprint BoxFootprintFromBox(BoxCollider box)
        {
            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            Vector3 worldCentre = box.transform.TransformPoint(box.center);
            return BoxFootprint.FromBox(worldCentre, box.transform.rotation, worldSize);
        }

        private static Transform FindEnvironment(Scene scene) => FindArena(scene).transform.parent;

        private static ArenaSymmetry FindArena(Scene scene) =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ArenaSymmetry>(true)).First();

        private static Transform[] FindThirds(Scene scene)
        {
            ArenaSymmetry arena = FindArena(scene);
            return new[] { arena.source, arena.generated120, arena.generated240 };
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        // Uses Game Scene if it's already open (the normal case: read-only checks on what's loaded), otherwise opens it
        // additively and closes it again. Never opens it Single, which would prompt to save a dirty scene.
        private static void WithGameScene(Action<Scene> body)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try { body(scene); }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
