using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Runs the arena tool against a tiny arena built in a PREVIEW scene. A preview scene is never saved and never
    /// marks Game Scene dirty. A dirty Game Scene makes the Editor raise a modal "save?" dialog that hangs the command
    /// server, which is why these tests don't just create objects in the open scene.
    /// </summary>
    public class ArenaSymmetryBuilderTests
    {
        private static readonly Vector3 Centre = new Vector3(10f, 0f, 20f);
        private Scene scene;
        private ArenaSymmetry arena;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            arena = MakeRoot("Arena").AddComponent<ArenaSymmetry>();
            arena.centre = Centre;
            arena.source = MakeChild("Source", arena.transform);
            arena.generated120 = MakeChild("Generated 120", arena.transform);
            arena.generated240 = MakeChild("Generated 240", arena.transform);
        }

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

        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F3} but was {actual:F3}");

        [Test]
        public void RebuildPlacesATurnedCopyOfEachSourceObjectInBothGeneratedThirds()
        {
            Transform crate = MakeChild("Crate", arena.source);
            crate.position = Centre + new Vector3(0f, 1f, 10f);          // map angle 90°
            crate.localScale = new Vector3(2f, 1f, 1f);

            Assert.IsEmpty(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));

            Assert.AreEqual(1, arena.generated120.childCount);
            Transform at120 = arena.generated120.GetChild(0);
            Assert.AreEqual("Crate", at120.name);
            AssertClose(Centre + new Vector3(-8.660254f, 1f, -5f), at120.position);   // map angle 210°
            AssertClose(new Vector3(-0.8660254f, 0f, -0.5f), at120.forward);
            AssertClose(new Vector3(2f, 1f, 1f), at120.localScale);

            AssertClose(Centre + new Vector3(8.660254f, 1f, -5f), arena.generated240.GetChild(0).position); // 330°
        }

        [Test]
        public void RebuildingTwiceReplacesTheCopiesInsteadOfAddingMore()
        {
            MakeChild("Crate", arena.source).position = Centre + new Vector3(0f, 0f, 10f);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            Assert.AreEqual(1, arena.generated120.childCount);
            Assert.AreEqual(1, arena.generated240.childCount);
        }

        [Test]
        public void GeneratedCopiesAndTheirChildrenAreNotEditable()
        {
            Transform house = MakeChild("House", arena.source);
            MakeChild("Roof", house);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Transform copy = arena.generated120.GetChild(0);
            Assert.AreNotEqual(0, (int)(copy.gameObject.hideFlags & HideFlags.NotEditable));
            Assert.AreNotEqual(0, (int)(copy.GetChild(0).gameObject.hideFlags & HideFlags.NotEditable));
        }

        [Test]
        public void RebuildMovesSnappedPartnersAndCentredObjectsWithoutCopyingThem()
        {
            Transform towerSource = MakeRoot("Tower A").transform;
            towerSource.position = Centre + new Vector3(0f, 0f, 30f);
            Transform tower120 = MakeRoot("Tower B").transform;
            Transform tower240 = MakeRoot("Tower C").transform;
            arena.snappedTriplets.Add(new ArenaSymmetry.SnappedTriplet { source = towerSource, at120 = tower120, at240 = tower240 });
            Transform middle = MakeRoot("Middle").transform;
            middle.position = new Vector3(3f, 5f, 3f);
            arena.centred.Add(middle);

            Assert.IsEmpty(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));

            AssertClose(Centre + new Vector3(-25.98076f, 0f, -15f), tower120.position);
            AssertClose(Centre + new Vector3(25.98076f, 0f, -15f), tower240.position);
            AssertClose(new Vector3(Centre.x, 5f, Centre.z), middle.position);
            Assert.AreEqual(0, arena.generated120.childCount);
        }

        [Test]
        public void RebuildRefusesATowerUnderSourceAndBuildsNothing()
        {
            Transform tower = MakeChild("Tower 8", arena.source);
            tower.gameObject.AddComponent<BuildingCapture>();

            List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Assert.IsNotEmpty(problems);
            StringAssert.Contains("Tower 8", string.Join("\n", problems));
            Assert.AreEqual(0, arena.generated120.childCount);
        }

        [Test]
        public void ValidateReportsACopyMovedByHand()
        {
            MakeChild("Crate", arena.source).position = Centre + new Vector3(0f, 0f, 10f);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            arena.generated120.GetChild(0).position += new Vector3(1f, 0f, 0f);

            List<string> problems = ArenaSymmetryBuilder.Validate(arena);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Crate", problems[0]);
        }

        [Test]
        public void ValidateReportsASourceObjectAddedWithoutARebuild()
        {
            MakeChild("Crate", arena.source);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            MakeChild("Barrel", arena.source);

            Assert.IsNotEmpty(ArenaSymmetryBuilder.Validate(arena));
        }
    }
}
