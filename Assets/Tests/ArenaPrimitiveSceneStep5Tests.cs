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
                // Arena step 9: 'Old Arena (off)' is permanently deleted once Tudor says so, so it may legitimately
                // be absent - unlike step 4/5, where it existed but was switched off. IsChildOf(null) throws, so
                // guard it instead of assuming the group is still there.
                Transform oldArt = environment.Find(OldArtGroupName);
                foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (oldArt != null && renderer.transform.IsChildOf(oldArt)) continue;

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

                    // Review finding (2026-09-19): a 5 cm circle at the END'S CENTRE passes even when a CORNER of
                    // that end (offset by the barrier's own half-thickness across its width) pokes past a plank's
                    // edge - the planks sit at a real angle to the mouth line, so the two corners of an end do not
                    // clear by the same margin. Check both corners of both ends, each >= 0.10 m inside a plank's
                    // OWN rotated footprint: ContainsWithMargin uses the plank's own long axis, so "never past its
                    // far end" is enforced by the same check, not a separate one.
                    const float CornerMarginMetres = 0.10f;
                    BoxFootprint footprint = BoxFootprintFromBox(barrier);
                    Vector2 endACentre = footprint.Centre - footprint.Along * footprint.HalfLength;
                    Vector2 endBCentre = footprint.Centre + footprint.Along * footprint.HalfLength;
                    Vector2[] endACorners = { endACentre + footprint.Across * footprint.HalfWidth, endACentre - footprint.Across * footprint.HalfWidth };
                    Vector2[] endBCorners = { endBCentre + footprint.Across * footprint.HalfWidth, endBCentre - footprint.Across * footprint.HalfWidth };
                    for (int i = 0; i < endACorners.Length; i++)
                        Assert.IsTrue(allBuildingBoxes.Any(f => ContainsWithMargin(f, endACorners[i], CornerMarginMetres)),
                            $"'{barrier.name}' end A corner {i + 1} ({endACorners[i]}) should sit >= {CornerMarginMetres} m inside a plank's own footprint");
                    for (int i = 0; i < endBCorners.Length; i++)
                        Assert.IsTrue(allBuildingBoxes.Any(f => ContainsWithMargin(f, endBCorners[i], CornerMarginMetres)),
                            $"'{barrier.name}' end B corner {i + 1} ({endBCorners[i]}) should sit >= {CornerMarginMetres} m inside a plank's own footprint");
                }
            });
        }

        /// <summary>Load-time-and-stones brief (2026-09-20): Tudor - "please also remove the little cubes (i
        /// think they are supposed to represent the stones from the map) since they really dont add anything."
        /// Written red against the pre-removal scene (it still had 6 Rock_03 + 8 Plant_01 blocks per third),
        /// green once ArenaLayout.asset drops those rows and the builder re-runs. The six Cover Crate blocks
        /// and every house block are real cover and must survive untouched.</summary>
        [Test]
        public void TheOldStoneAndBushBlocksAreGoneButCoverCratesAndHousesRemain()
        {
            WithGameScene(scene =>
            {
                Transform[] thirds = FindThirds(scene);
                Assert.AreEqual(3, thirds.Length);

                foreach (Transform third in thirds)
                {
                    Transform blocks = third.Find(ArenaPrimitiveBuilder.BlocksGroupName);
                    Assert.IsNotNull(blocks, $"{third.name} has no Blocks group");

                    int crateCount = 0, houseCount = 0;
                    foreach (Transform block in blocks)
                    {
                        Assert.IsFalse(block.name.StartsWith("Rock_03"),
                            $"'{block.name}' under {third.name} should have been removed (Tudor: the stones add nothing)");
                        Assert.IsFalse(block.name.StartsWith("Plant_01"),
                            $"'{block.name}' under {third.name} should have been removed (Tudor: the bushes add nothing)");

                        if (block.name.StartsWith("Cover Crate")) crateCount++;
                        else houseCount++;
                    }
                    Assert.AreEqual(6, crateCount, $"{third.name} should still have all six Cover Crate blocks");
                    Assert.AreEqual(6, houseCount, $"{third.name} should still have all six house blocks");
                }
            });
        }

        private static BoxFootprint BoxFootprintFromBox(BoxCollider box)
        {
            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            Vector3 worldCentre = box.transform.TransformPoint(box.center);
            return BoxFootprint.FromBox(worldCentre, box.transform.rotation, worldSize);
        }

        // BoxFootprint.Contains has no margin of its own (it is shared, pure geometry used by the wall-coverage
        // hole finder and the barrier crossing rule, where an exact edge means something different). A margin
        // shrinks the box on all four sides in ITS OWN axes, so checking against the plank's own footprint also
        // catches "past its far end" for free - the far end is just one more side of the same box.
        private static bool ContainsWithMargin(BoxFootprint fp, Vector2 point, float margin)
        {
            Vector2 d = point - fp.Centre;
            float along = Vector2.Dot(d, fp.Along);
            float across = Vector2.Dot(d, fp.Across);
            return Mathf.Abs(along) <= fp.HalfLength - margin && Mathf.Abs(across) <= fp.HalfWidth - margin;
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
