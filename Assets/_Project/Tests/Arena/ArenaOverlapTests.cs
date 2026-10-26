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
    /// <summary>
    /// The arena shader draws a line round every piece's top, so two pieces sharing space show crossed lines and flicker:
    /// no two arena pieces may reach into each other in either saved scene, beyond the two kinds ArenaOverlapCheck allows;
    /// the check itself must catch a real overlap; and the corner block by the centre tower is a mirror image of itself
    /// across the arena's own mirror line. The saved scenes are opened additively and closed again (Game Scene stays open).
    /// </summary>
    public class ArenaOverlapTests
    {
        private const string GamePath = "Assets/_Project/Scenes/Game Scene.unity";
        private const string LanePath = "Assets/_Project/Scenes/Dominion 2v2.unity";
        private const string CornerBlockPrefix = "Corner Block";

        private static void WithArena(string path, System.Action<ArenaSymmetry> check)
        {
            Scene existing = SceneManager.GetSceneByPath(path);
            bool wasOpen = existing.IsValid() && existing.isLoaded;
            Scene scene = wasOpen ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                ArenaSymmetry arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArenaSymmetry>(true)).Single();
                check(arena);
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static int PiecesInGroups(ArenaSymmetry arena)
        {
            int count = 0;
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
                if (third != null)
                    foreach (string group in new[] { ArenaSymmetry.BoundaryGroupName, ArenaSymmetry.BlocksGroupName, ArenaSymmetry.BarriersGroupName })
                    {
                        Transform g = third.Find(group);
                        if (g != null) count += g.childCount;
                    }
            return count;
        }

        [Test]
        public void NoTwoArenaPiecesOverlapInGameScene()
        {
            WithArena(GamePath, arena =>
            {
                List<string> findings = ArenaOverlapCheck.FindOverlaps(arena, out int checkedPieces);
                Assert.AreEqual(PiecesInGroups(arena), checkedPieces, "every wall, block and barrier is checked, whatever it is made of");
                Assert.IsEmpty(findings, "rebuild the arena (Source, then Rebuild thirds) so these meet face to face:\n" + string.Join("\n", findings));
            });
        }

        [Test]
        public void NoTwoArenaPiecesOverlapInTheLaneScene()
        {
            WithArena(LanePath, arena =>
            {
                List<string> findings = ArenaOverlapCheck.FindOverlaps(arena, out int checkedPieces);
                Assert.AreEqual(PiecesInGroups(arena), checkedPieces, "every wall, block and barrier is checked, whatever it is made of");
                Assert.IsEmpty(findings, "rebuild the lane's walls so these meet face to face:\n" + string.Join("\n", findings));
            });
        }

        // ---------------------------------------------------------------- the check itself

        private static GameObject Box(Transform group, string name, Vector3 centre, float yaw, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = size;
            return go;
        }

        private static List<string> FindingsFor(System.Action<Transform, Transform, Transform> build)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Arena");
                SceneManager.MoveGameObjectToScene(root, preview);
                var source = new GameObject("Source").transform;
                source.SetParent(root.transform, false);
                Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(source, false); return g; }
                Transform boundry = Group(ArenaSymmetry.BoundaryGroupName), blocks = Group(ArenaSymmetry.BlocksGroupName), barriers = Group(ArenaSymmetry.BarriersGroupName);
                build(boundry, blocks, barriers);
                var arena = root.AddComponent<ArenaSymmetry>();
                arena.source = source;
                return ArenaOverlapCheck.FindOverlaps(arena);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void TheCheckFindsPiecesSharingSpaceButNotTheTwoAllowedKinds()
        {
            var wall = new Vector3(10f, 7f, 0.7f);
            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(boundry, "A", new Vector3(5f, 2.5f, 0f), 0f, wall);
                Box(boundry, "B", new Vector3(9.65f, 2.5f, 5f), 90f, wall); // both cover the corner square
            }).Count, "two right-angle walls sharing their corner");

            Assert.AreEqual(0, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(boundry, "A", new Vector3(5f, 2.5f, 0f), 0f, wall);
                Box(boundry, "B", new Vector3(9.65f, 2.5f, 5.35f), 90f, new Vector3(10f, 7f, 0.7f)); // starts at A's face
            }).Count, "two right-angle walls meeting face to face");

            // A 30 degree outward corner as the wall plan builds it: the two walls' outer edges meet at one point.
            const float thickness = 0.7f;
            var outline = new List<Vector2> { new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(18.660254f, 5f), new Vector2(0f, 15f) };
            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(outline, outline.Count, thickness);
            void Wall(Transform group, string name, ArenaWallPlan.Run run, Vector2 shift)
            {
                Vector2 c = run.Centre(thickness) + shift;
                Box(group, name, new Vector3(c.x, 2.5f, c.y), run.UnityYawDegrees, new Vector3(run.Length, 7f, thickness));
            }
            Vector2 along1 = (runs[1].InnerEnd - runs[1].InnerStart).normalized;
            Assert.AreEqual(0, FindingsFor((boundry, blocks, barriers) =>
            {
                Wall(boundry, "A", runs[0], Vector2.zero);
                Wall(boundry, "B", runs[1], Vector2.zero);
            }).Count, "boundary walls closing a corner that is not a right angle may share that corner");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Wall(boundry, "A", runs[0], Vector2.zero);
                Wall(boundry, "B", runs[1], -along1 * 0.5f);
            }).Count, "a wall pushed past the corner reaches deeper than the corner needs");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(boundry, "A", new Vector3(5f, 2.5f, 0f), 0f, wall);
                Box(boundry, "B", new Vector3(5f, 2.5f, 0f), 30f, wall);
            }).Count, "two boundary walls crossing away from any corner");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(blocks, "A", new Vector3(0f, 3.5f, 0f), 0f, new Vector3(4f, 7f, 4f));
                Box(blocks, "B", new Vector3(3f, 3.5f, 1f), 0f, new Vector3(4f, 7f, 4f));
            }).Count, "two blocks sharing space");

            Assert.AreEqual(0, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(blocks, "Plank", new Vector3(0f, 3f, 0f), 0f, new Vector3(2.6f, 6f, 7f));
                Box(barriers, "Barrier", new Vector3(6.1f, 0.5f, 0f), 0f, new Vector3(10f, 1f, 0.6f)); // its end 0.2 m inside the plank
            }).Count, "a barrier's end buried in taller cover");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(blocks, "Plank", new Vector3(0f, 3f, 0f), 0f, new Vector3(2.6f, 6f, 7f));
                Box(barriers, "Barrier", new Vector3(0f, 0.5f, 0f), 0f, new Vector3(10f, 1f, 0.6f));
            }).Count, "a barrier lying right through a block");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(blocks, "Plank", new Vector3(0f, 3f, 0f), 0f, new Vector3(2.6f, 6f, 7f));
                Box(barriers, "Barrier", new Vector3(4.8f, 0.5f, 0f), 0f, new Vector3(10f, 1f, 0.6f));
            }).Count, "a barrier end sunk well past the block's face");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(boundry, "Wall", new Vector3(0f, 2.5f, 0f), 90f, wall);
                Box(barriers, "Barrier", new Vector3(5.15f, 0.5f, 0f), 0f, new Vector3(10f, 1f, 0.6f));
            }).Count, "a barrier end buried in a boundary wall");

            Assert.AreEqual(1, FindingsFor((boundry, blocks, barriers) =>
            {
                Box(barriers, "Across", new Vector3(0f, 0.5f, 0f), 0f, new Vector3(6f, 1f, 0.5f));
                Box(barriers, "Along", new Vector3(0f, 0.5f, 0f), 90f, new Vector3(6f, 1f, 0.5f));
            }).Count, "two barriers crossing");
        }

        // ---------------------------------------------------------------- the corner block by the centre tower

        private static Vector2[] Footprint(ArenaLayout.Piece p) =>
            ArenaPieceShapes.FootprintCorners(new Vector2(p.centre.x, p.centre.z), p.yawDegrees, new Vector2(p.size.x, p.size.z));

        private static Vector2 Mirror(Vector2 p, Vector2 onLine, Vector2 along)
        {
            Vector2 d = p - onLine;
            return onLine + 2f * Vector2.Dot(d, along) * along - d;
        }

        /// <summary>The diagonal of the square that mirrors one arm onto the other; zero when neither does.</summary>
        private static Vector2 MirrorDiagonal(ArenaLayout.Piece square, ArenaLayout.Piece armA, ArenaLayout.Piece armB)
        {
            var centre = new Vector2(square.centre.x, square.centre.z);
            Quaternion turn = Quaternion.Euler(0f, square.yawDegrees, 0f);
            Vector3 x3 = turn * Vector3.right, z3 = turn * Vector3.forward;
            Vector2 x = new Vector2(x3.x, x3.z), z = new Vector2(z3.x, z3.z);
            foreach (Vector2 diagonal in new[] { (x + z).normalized, (x - z).normalized })
            {
                Vector2[] mirroredArm = Footprint(armA).Select(p => Mirror(p, centre, diagonal)).ToArray();
                Vector2[] otherArm = Footprint(armB);
                if (mirroredArm.All(p => otherArm.Any(q => Vector2.Distance(p, q) < 0.01f)))
                    return diagonal;
            }
            return Vector2.zero;
        }

        private static List<ArenaLayout.Piece> CornerBlock(ArenaLayout layout) =>
            layout.Pieces.Where(p => p.name != null && p.name.StartsWith(CornerBlockPrefix)).ToList();

        [Test]
        public void TheCornerBlockByTheCentreIsSymmetricAboutItsOwnDiagonal()
        {
            var layout = AssetDatabase.LoadAssetAtPath<ArenaLayout>("Assets/_Project/Shared/Config/ArenaLayout.asset");
            List<ArenaLayout.Piece> pieces = CornerBlock(layout);
            Assert.AreEqual(3, pieces.Count, $"the corner block is three '{CornerBlockPrefix}' rows: a square where the arms meet, and two arms");

            ArenaLayout.Piece apex = pieces.Single(p => Mathf.Abs(p.size.x - p.size.z) < 0.01f);
            List<ArenaLayout.Piece> arms = pieces.Where(p => !p.Equals(apex)).ToList();
            foreach (ArenaLayout.Piece p in pieces)
            {
                Assert.AreEqual(apex.yawDegrees, p.yawDegrees, 0.01f, $"{p.name} turns with the others");
                Assert.AreEqual(apex.size.y, p.size.y, 0.01f, $"{p.name} is as tall as the others");
                Assert.AreEqual(apex.centre.y, p.centre.y, 0.01f, $"{p.name} stands on the same ground");
            }

            foreach (ArenaLayout.Piece arm in arms)
            {
                float depth = ArenaPieceShapes.OverlapDepth(Footprint(apex), Footprint(arm));
                Assert.That(depth, Is.InRange(-0.01f, 0.01f), $"{arm.name} meets the square face to face");
            }

            Assert.AreNotEqual(Vector2.zero, MirrorDiagonal(apex, arms[0], arms[1]),
                $"{arms[0].name} and {arms[1].name} should be mirror images across the square's diagonal");
        }

        [Test]
        public void TheCornerBlocksMirrorLineIsTheArenasOwn()
        {
            WithArena(GamePath, arena =>
            {
                List<ArenaLayout.Piece> pieces = CornerBlock(arena.layout);
                Assert.AreEqual(3, pieces.Count);
                ArenaLayout.Piece square = pieces.Single(p => Mathf.Abs(p.size.x - p.size.z) < 0.01f);
                List<ArenaLayout.Piece> arms = pieces.Where(p => !p.Equals(square)).ToList();
                Vector2 along = MirrorDiagonal(square, arms[0], arms[1]);
                Assert.AreNotEqual(Vector2.zero, along, "the corner block is a mirror image of itself first");

                var through = new Vector2(square.centre.x, square.centre.z);
                var centre = new Vector2(arena.centre.x, arena.centre.z);
                float offLine = Mathf.Abs(Vector2.Dot(centre - through, new Vector2(-along.y, along.x)));
                Assert.That(offLine, Is.LessThan(0.02f), "the corner block's mirror line runs through the arena's centre");

                IReadOnlyList<Vector2> outline = ArenaBounds.FromSourceOutline(arena.sourceOutline, arena.centre).Polygon;
                foreach (Vector2 p in outline)
                {
                    Vector2 image = Mirror(p, through, along);
                    float nearest = outline.Min(q => Vector2.Distance(image, q));
                    Assert.That(nearest, Is.LessThan(0.05f), $"outline point {p} has its mirror image across the corner block's line");
                }
            });
        }
    }
}
