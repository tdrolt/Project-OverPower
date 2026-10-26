using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The mitred wall mesh: placed by its run's box (centre, yaw, length x height x thickness) it covers exactly the run's drawn
    /// plan, its bounds stay the whole box (the collider), every face is drawn from outside, and its top line runs along both long
    /// edges but never across a slanted end, which is the cross line it replaces.</summary>
    public class MitredWallMeshTests
    {
        private const float Thickness = 0.724f, Height = 7f;

        // A 30 degree outward corner at (4, 8) and a 120 degree reflex one at (2, 11.46): runs 2 and 3 are mitred at one end each,
        // run 3 at both.
        private static List<ArenaWallPlan.Run> Runs() => ArenaWallPlan.ForSource(new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 5f), new Vector2(4f, 8f), new Vector2(2f, 11.4641016f), new Vector2(5f, 11.4641016f),
        }, 6, Thickness);

        private static Matrix4x4 Placement(ArenaWallPlan.Run run)
        {
            Vector2 c = run.Centre(Thickness);
            return Matrix4x4.TRS(new Vector3(c.x, 0f, c.y), Quaternion.Euler(0f, run.UnityYawDegrees, 0f), new Vector3(run.Length, Height, Thickness));
        }

        [Test]
        public void PlacedByItsRunItCoversTheRunsDrawnPlanAndItsBoundsAreTheWholeBox()
        {
            foreach (ArenaWallPlan.Run run in Runs().Where(r => r.IsMitred))
            {
                Mesh mesh = MitredWallMesh.Create(run.Length, run.StartMitre, run.EndMitre);
                Assert.AreEqual(MitredWallMesh.MeshName, mesh.name);
                Assert.That(Vector3.Distance(mesh.bounds.center, Vector3.zero), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(mesh.bounds.size, Vector3.one), Is.LessThan(1e-5f), "the BoxCollider sized from it is the run's box");

                Matrix4x4 m = Placement(run);
                Vector2[] plan = ArenaPieceShapes.ConvexHull(mesh.vertices.Select(v => { Vector3 w = m.MultiplyPoint3x4(v); return new Vector2(w.x, w.z); }));
                Vector2[] drawn = run.DrawnCorners(Thickness);
                Assert.AreEqual(drawn.Length, plan.Length);
                foreach (Vector2 corner in drawn)
                    Assert.That(plan.Min(p => Vector2.Distance(p, corner)), Is.LessThan(1e-3f), $"the mesh reaches the drawn corner {corner}");
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void EveryFaceIsDrawnFromOutside()
        {
            ArenaWallPlan.Run run = Runs()[3];
            Mesh mesh = MitredWallMesh.Create(run.Length, run.StartMitre, run.EndMitre);
            Vector3[] v = mesh.vertices, n = mesh.normals;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 facing = Vector3.Cross(b - a, c - a);
                Assert.Greater(Vector3.Dot(facing, n[t[i]]), 0f, $"triangle {i / 3} is wound toward its normal");
                Assert.Greater(Vector3.Dot((a + b + c) / 3f, n[t[i]]), 0f, $"triangle {i / 3}'s normal points out of the wall");
            }
            Object.DestroyImmediate(mesh);
        }

        // The shader's top line: metres from the face's nearest UV edge (ArenaHex.hlsl ArenaFaceEdgeMetres), U scaled by the wall's
        // length over the U the top spans, V by its thickness.
        [Test]
        public void TheTopLineRunsAlongBothLongEdgesButNotAcrossAMitredEnd()
        {
            ArenaWallPlan.Run run = Runs()[2]; // square start (a straight run), mitred end (the 30 degree corner)
            Assert.AreEqual(0f, run.StartMitre);
            Assert.That(run.EndMitre, Is.GreaterThan(0f));
            Mesh mesh = MitredWallMesh.Create(run.Length, run.StartMitre, run.EndMitre);
            Vector2 topU = MitredWallMesh.TopURange(run.StartMitre, run.EndMitre);
            float metresPerU = run.Length / (topU.y - topU.x);

            var top = new List<(Vector3 position, Vector2 uv)>();
            Vector3[] v = mesh.vertices, n = mesh.normals;
            Vector2[] uv = mesh.uv;
            for (int i = 0; i < v.Length; i++)
                if (n[i].y > 0.5f) top.Add((v[i], uv[i]));
            Assert.AreEqual(4, top.Count);

            float FromUEdge(Vector2 at) => Mathf.Min(at.x, 1f - at.x) * metresPerU;
            float FromVEdge(Vector2 at) => Mathf.Min(at.y, 1f - at.y) * Thickness;
            Vector2 Middle(bool atEnd)
            {
                var end = top.Where(p => atEnd ? p.position.x > 0f : p.position.x < 0f).Select(p => p.uv).ToArray();
                return (end[0] + end[1]) * 0.5f;
            }

            Assert.AreEqual(0f, FromUEdge(Middle(false)), 1e-4f, "a square end keeps its line");
            Assert.Greater(FromUEdge(Middle(true)), 1f, "no line across the mitred end");
            foreach ((Vector3 _, Vector2 at) in top)
                Assert.AreEqual(0f, FromVEdge(at), 1e-4f, "every top corner lies on a long edge, which keeps its line");
            Object.DestroyImmediate(mesh);
        }
    }
}
