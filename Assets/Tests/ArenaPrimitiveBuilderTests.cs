using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>Arena step 4 (amended): ArenaLayoutCapture (reading Source into rows) and ArenaPrimitiveBuilder's
    /// BuildSource (writing the outline's walls plus every Block/Barrier row back into Source), all in a PREVIEW
    /// scene - never the open Game Scene (ArenaSymmetryBuilderTests' own reasoning: a dirty Game Scene hangs the
    /// Editor on a modal save dialog).</summary>
    public class ArenaPrimitiveBuilderTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject MakeRoot(string name)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private Transform MakeChild(string name, Transform parent)
        {
            GameObject go = MakeRoot(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        [Test]
        public void CaptureMakesOneRowPerUnitFromItsOwnCollider()
        {
            Transform source = MakeRoot("Source").transform;
            Transform buildings = MakeChild("Buildings", source);
            Transform house = MakeChild("House", buildings);
            house.SetPositionAndRotation(new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 30f, 0f));
            house.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            BoxCollider box = house.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(2f, 3f, 4f);

            // A child's own collider must be ignored (and counted), never turned into a second row.
            Transform window = MakeChild("Window", house);
            window.gameObject.AddComponent<BoxCollider>();

            var report = new List<string>();
            List<ArenaLayout.Piece> pieces = ArenaLayoutCapture.Capture(source, new List<(Vector3, float)>(), report);

            Assert.AreEqual(1, pieces.Count);
            ArenaLayout.Piece piece = pieces[0];
            Assert.AreEqual("House", piece.name);
            Assert.AreEqual(ArenaLayout.PieceKind.Block, piece.kind);
            Assert.That(Vector3.Distance(piece.centre, new Vector3(5f, 0f, 5f)), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(piece.size, new Vector3(1.6f, 2.4f, 3.2f)), Is.LessThan(1e-3f));
            Assert.AreEqual(30f, piece.yawDegrees, 0.01f);

            StringAssert.Contains("1 child colliders ignored", string.Join("\n", report));
        }

        [Test]
        public void CaptureSkipsAUnitInsideATowerFootprintAndReportsIt()
        {
            Transform source = MakeRoot("Source").transform;
            Transform props = MakeChild("Props", source);
            Transform crate = MakeChild("Crate", props);
            var towerCentre = new Vector3(20f, 0f, 20f);
            crate.position = towerCentre + new Vector3(2f, 0f, 0f); // 2 m from the tower, well inside radius 2.6 + 0.5
            crate.gameObject.AddComponent<BoxCollider>();

            var keepClear = new List<(Vector3 centre, float radius)> { (towerCentre, 2.6f) };
            var report = new List<string>();
            List<ArenaLayout.Piece> pieces = ArenaLayoutCapture.Capture(source, keepClear, report);

            Assert.IsEmpty(pieces);
            string joined = string.Join("\n", report);
            StringAssert.Contains("skipped", joined);
            StringAssert.Contains("Crate", joined);
        }

        [Test]
        public void CaptureLeavesWallsToTheOutlineAndReadsBarriersBack()
        {
            Transform source = MakeRoot("Source").transform;
            Transform boundry = MakeChild(ArenaSymmetry.BoundaryGroupName, source);
            for (int i = 0; i < 3; i++)
            {
                Transform wall = MakeChild($"Wall_{i}", boundry);
                wall.gameObject.AddComponent<BoxCollider>();
            }
            Transform barriers = MakeChild("Barriers", source);
            Transform plank = MakeChild("Jersey Barrier", barriers);
            plank.position = new Vector3(50f, 0f, 50f);
            plank.gameObject.AddComponent<BoxCollider>();

            var report = new List<string>();
            List<ArenaLayout.Piece> pieces = ArenaLayoutCapture.Capture(source, new List<(Vector3, float)>(), report);

            Assert.AreEqual(1, pieces.Count, "Boundry units must never become rows - walls come from the outline");
            Assert.AreEqual(ArenaLayout.PieceKind.Barrier, pieces[0].kind);
            Assert.AreEqual("Jersey Barrier", pieces[0].name);
            StringAssert.Contains("Boundry skipped 3 (from the outline)", string.Join("\n", report));
        }

        // A small closed outline (a plain rectangle in the flat XZ plane), reused by the BuildSource tests below.
        private static readonly List<Vector2> TestOutline = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(20f, 0f), new Vector2(20f, 20f), new Vector2(0f, 20f),
        };

        private ArenaSymmetry MakeArena()
        {
            var arena = MakeRoot("Arena").AddComponent<ArenaSymmetry>();
            arena.centre = Vector3.zero;
            arena.source = MakeChild("Source", arena.transform);
            arena.generated120 = MakeChild("Generated 120", arena.transform);
            arena.generated240 = MakeChild("Generated 240", arena.transform);
            arena.sourceOutline = new List<Vector2>(TestOutline);
            return arena;
        }

        private static ArenaLayout MakeLayout(List<ArenaLayout.Piece> pieces)
        {
            var layout = ScriptableObject.CreateInstance<ArenaLayout>();
            ArenaLayoutCapture.WriteMaterials(layout, null, 0.724f, -1f, 6.036f, null, null, -1f, 3f, null, new Vector2(220f, 220f), 4f);
            ArenaLayoutCapture.WritePieces(layout, pieces);
            return layout;
        }

        [Test]
        public void BuildMakesWallsFromTheOutlineBlocksOnBuildingAndBarriersOnTheirOwnLayer()
        {
            ArenaSymmetry arena = MakeArena();
            var pieces = new List<ArenaLayout.Piece>
            {
                new ArenaLayout.Piece { name = "Crate", kind = ArenaLayout.PieceKind.Block, centre = new Vector3(5f, 1f, 5f), yawDegrees = 0f, size = new Vector3(2f, 2f, 2f) },
                new ArenaLayout.Piece { name = "Jersey Barrier", kind = ArenaLayout.PieceKind.Barrier, centre = new Vector3(10f, 0f, 0.3f), yawDegrees = 0f, size = new Vector3(9.8f, 1f, 0.6f) },
            };
            ArenaLayout layout = MakeLayout(pieces);

            List<string> report = ArenaPrimitiveBuilder.BuildSource(arena, layout);
            Assert.IsFalse(report.Any(l => l.Contains("PROBLEM")), string.Join("\n", report));

            Transform boundry = arena.source.Find(ArenaPrimitiveBuilder.BoundryGroupName);
            Transform blocks = arena.source.Find(ArenaPrimitiveBuilder.BlocksGroupName);
            Transform barriers = arena.source.Find(ArenaPrimitiveBuilder.BarriersGroupName);
            Assert.IsNotNull(boundry); Assert.IsNotNull(blocks); Assert.IsNotNull(barriers);
            Assert.IsNotNull(boundry.GetComponent<ArenaBuiltGroup>());
            Assert.IsNotNull(blocks.GetComponent<ArenaBuiltGroup>());
            Assert.IsNotNull(barriers.GetComponent<ArenaBuiltGroup>());

            Assert.AreEqual(TestOutline.Count, boundry.childCount, "one wall per outline edge");
            int buildingLayer = LayerMask.NameToLayer("Building");
            foreach (Transform wall in boundry)
            {
                Assert.AreEqual(buildingLayer, wall.gameObject.layer);
                Assert.IsNotNull(wall.GetComponent<BoxCollider>());
                Assert.IsNull(wall.GetComponent<MeshCollider>());
            }

            Assert.AreEqual(1, blocks.childCount);
            Assert.AreEqual(buildingLayer, blocks.GetChild(0).gameObject.layer);

            Assert.AreEqual(1, barriers.childCount);
            Transform barrier = barriers.GetChild(0);
            int barrierLayer = LayerMask.NameToLayer(ArenaLayers.BarrierLayerName);
            Assert.AreEqual(barrierLayer, barrier.gameObject.layer);
            Assert.AreEqual(0f, barrier.position.y, 0.001f, "a barrier sits on the floor");
            BoxCollider barrierBox = barrier.GetComponent<BoxCollider>();
            float scaleY = barrier.localScale.y;
            float worldBottom = barrier.position.y + (barrierBox.center.y - barrierBox.size.y * 0.5f) * scaleY;
            float worldTop = barrier.position.y + (barrierBox.center.y + barrierBox.size.y * 0.5f) * scaleY;
            Assert.AreEqual(-1f, worldBottom, 0.01f);
            Assert.AreEqual(3f, worldTop, 0.01f);
        }

        [Test]
        public void BuildingTwiceReplacesItsOwnGroupsAndRefusesForeignOnes()
        {
            ArenaSymmetry arena = MakeArena();
            ArenaLayout layout = MakeLayout(new List<ArenaLayout.Piece>());

            ArenaPrimitiveBuilder.BuildSource(arena, layout);
            int afterFirst = arena.source.childCount;

            ArenaPrimitiveBuilder.BuildSource(arena, layout);
            Assert.AreEqual(afterFirst, arena.source.childCount, "a second build replaces its own groups, not adds to them");

            MakeChild("Hand Placed Rock", arena.source);
            List<string> report = ArenaPrimitiveBuilder.BuildSource(arena, layout);

            Assert.IsTrue(report.Any(l => l.Contains("PROBLEM")), "a foreign child under Source must refuse the build");
            Assert.AreEqual(afterFirst + 1, arena.source.childCount, "nothing was deleted when the build refused");
        }

        [Test]
        public void BuiltWallsSitOnTheOutline()
        {
            ArenaSymmetry arena = MakeArena();
            ArenaLayout layout = MakeLayout(new List<ArenaLayout.Piece>());

            ArenaPrimitiveBuilder.BuildSource(arena, layout);
            List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
