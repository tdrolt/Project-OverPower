using System.Collections.Generic;
using Overpower.Arena;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Finds arena pieces (the boundary walls, blocks and barriers the builders make) whose drawn shapes reach into each other.
    /// The arena shader draws a line round every piece's top and paints coplanar faces twice, so two pieces sharing space show
    /// crossed lines and flicker; pieces must meet face to face instead. A wall's drawn shape is its mesh (a mitred wall's
    /// slanted end included), not its collider. One overlap is let through, only as deep as it needs: a barrier end buried in
    /// taller cover (on purpose, so nobody slips past the end).
    /// </summary>
    public static class ArenaOverlapCheck
    {
        /// <summary>Two faces meeting exactly, or a straight wall's hairline joint (ArenaWallPlan.StraightOverlapMetres),
        /// is not an overlap anyone can see.</summary>
        public const float TouchToleranceMetres = 0.02f;

        /// <summary>How much of a barrier's length may sit inside the cover its end is buried in: the scene's recess
        /// barriers bury about 0.3 m; more would be a barrier pushed into or through the cover.</summary>
        private const float BuriedBarrierEndMetres = 0.5f;

        private readonly struct Piece
        {
            public readonly string Name;
            public readonly string Group;
            public readonly Vector2[] Footprint;
            public readonly float Bottom, Top;

            public Piece(Transform t, Mesh mesh)
            {
                Name = t.parent != null && t.parent.parent != null ? $"{t.parent.parent.name}/{t.parent.name}/{t.name}" : t.name;
                Group = t.parent != null ? t.parent.name : "";
                var plan = new List<Vector2>();
                Bottom = float.MaxValue;
                Top = float.MinValue;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 world = t.TransformPoint(vertex);
                    plan.Add(new Vector2(world.x, world.z));
                    Bottom = Mathf.Min(Bottom, world.y);
                    Top = Mathf.Max(Top, world.y);
                }
                Footprint = ArenaPieceShapes.ConvexHull(plan);
            }
        }

        /// <summary>One line per pair of pieces that overlap by more than <see cref="TouchToleranceMetres"/> and is not one
        /// of the two allowed kinds; empty when the arena is clean.</summary>
        public static List<string> FindOverlaps(ArenaSymmetry arena) => FindOverlaps(arena, out _);

        /// <param name="piecesChecked">How many pieces were looked at, so an empty list can be told from nothing checked.</param>
        public static List<string> FindOverlaps(ArenaSymmetry arena, out int piecesChecked)
        {
            var pieces = new List<Piece>();
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null) continue;
                foreach (MeshFilter filter in third.GetComponentsInChildren<MeshFilter>(false))
                    if (filter.sharedMesh != null && (filter.sharedMesh.name == "Cube" || filter.sharedMesh.name == MitredWallMesh.MeshName))
                        pieces.Add(new Piece(filter.transform, filter.sharedMesh));
            }
            piecesChecked = pieces.Count;

            var findings = new List<string>();
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    Piece a = pieces[i], b = pieces[j];
                    if (Mathf.Min(a.Top, b.Top) - Mathf.Max(a.Bottom, b.Bottom) <= TouchToleranceMetres) continue;
                    float depth = ArenaPieceShapes.ConvexOverlapDepth(a.Footprint, b.Footprint);
                    if (depth <= TouchToleranceMetres || IsAllowed(a, b)) continue;
                    findings.Add($"{a.Name} and {b.Name} reach {depth:0.000} m into each other");
                }
            return findings;
        }

        private static bool IsAllowed(Piece a, Piece b)
        {
            if (a.Group == ArenaSymmetry.BarriersGroupName && b.Group == ArenaSymmetry.BlocksGroupName)
                return IsABuriedBarrierEnd(a, b);
            if (b.Group == ArenaSymmetry.BarriersGroupName && a.Group == ArenaSymmetry.BlocksGroupName)
                return IsABuriedBarrierEnd(b, a);
            return false;
        }

        private static bool IsABuriedBarrierEnd(Piece barrier, Piece cover)
        {
            if (cover.Top <= barrier.Top) return false;
            Vector2 side1 = barrier.Footprint[0] - barrier.Footprint[1], side2 = barrier.Footprint[1] - barrier.Footprint[2];
            Vector2 along = (side1.sqrMagnitude > side2.sqrMagnitude ? side1 : side2).normalized;
            Extent(barrier.Footprint, along, out float barrierMin, out float barrierMax);
            Extent(cover.Footprint, along, out float coverMin, out float coverMax);
            bool coversAnEnd = coverMin <= barrierMin + TouchToleranceMetres || coverMax >= barrierMax - TouchToleranceMetres;
            return coversAnEnd && Mathf.Min(barrierMax, coverMax) - Mathf.Max(barrierMin, coverMin) <= BuriedBarrierEndMetres;
        }

        private static void Extent(Vector2[] corners, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (Vector2 c in corners)
            {
                float d = Vector2.Dot(c, axis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }

        [MenuItem("OverPower/Arena/List overlapping arena pieces")]
        private static void MenuList()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (ArenaSymmetry arena in root.GetComponentsInChildren<ArenaSymmetry>(true))
                {
                    List<string> findings = FindOverlaps(arena);
                    Debug.Log($"[ArenaOverlapCheck] {findings.Count} overlapping pairs:\n" + string.Join("\n", findings));
                }
        }
    }
}
