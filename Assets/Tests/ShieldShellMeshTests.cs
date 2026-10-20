using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Shield's shell is a Goldberg polyhedron: 42 plates (12 pentagons, 30 hexagons) with real gaps between them, backed by a
    /// closed inner shell facing in so the seams read dark over any floor; one small mesh, unit diameter and centred at zero so the
    /// Shield's diameter scales it.</summary>
    public class ShieldShellMeshTests
    {
        private Mesh mesh;
        private PlateRange[] plates;

        [SetUp]
        public void Build() => mesh = ShieldShellMesh.Create(0.1f, out plates);

        [TearDown]
        public void Free() => Object.DestroyImmediate(mesh);

        [Test]
        public void FortyTwoPlatesTwelvePentagons()
        {
            Assert.AreEqual(42, plates.Length);
            Assert.AreEqual(12, plates.Count(p => p.VertexCount == 5));
            Assert.AreEqual(30, plates.Count(p => p.VertexCount == 6));
            Assert.AreEqual(mesh.GetSubMesh(0).vertexCount, plates.Sum(p => p.VertexCount), "every vertex of the plates belongs to exactly one plate");
            Assert.AreEqual(ShieldShellMesh.Name, mesh.name);
            Assert.AreNotEqual(HideFlags.None, mesh.hideFlags & HideFlags.DontSave, "a runtime mesh is never saved into a scene");
        }

        [Test]
        public void UnderThreeHundredTriangles()
        {
            int triangles = mesh.triangles.Length / 3;
            Assert.Greater(triangles, 0);
            Assert.Less(triangles, 300);
        }

        [Test]
        public void UnitBoundsCentredAtZero()
        {
            Bounds b = mesh.bounds;
            Assert.Less(b.center.magnitude, 0.01f, "centred at zero");
            foreach (Vector3 v in mesh.vertices)
                Assert.LessOrEqual(v.magnitude, 0.5f + 1e-4f, "unit diameter: no corner pokes past the Shield's diameter");
            Assert.Greater(b.extents.y, 0.45f, "the shell fills its whole height");
            Assert.Greater(b.extents.x, 0.45f);
        }

        [Test]
        public void EveryPlateTriangleFacesOutward()
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.GetTriangles(0);
            Assert.IsNotEmpty(t);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Vector3 centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                Assert.Greater(Vector3.Dot(normal, centre), 0f, "triangle " + i / 3 + " faces inward and would be culled from outside");
            }
        }

        [Test]
        public void ThePlatesDoNotTouch()
        {
            Vector3[] v = mesh.vertices;
            Assert.AreEqual(42, plates.Length);
            var owner = new Dictionary<Vector3Int, int>();
            for (int p = 0; p < plates.Length; p++)
                for (int i = plates[p].FirstVertex; i < plates[p].FirstVertex + plates[p].VertexCount; i++)
                {
                    var key = Vector3Int.RoundToInt(v[i] * 1000f);
                    if (owner.TryGetValue(key, out int other) && other != p)
                        Assert.Fail($"plates {other} and {p} share a corner: the seams are closed");
                    owner[key] = p;
                }
        }

        [Test]
        public void EachPlateCentreIsTheMiddleOfItsCorners()
        {
            Vector3[] v = mesh.vertices;
            Assert.AreEqual(42, plates.Length);
            foreach (PlateRange p in plates)
            {
                Vector3 sum = Vector3.zero;
                for (int i = p.FirstVertex; i < p.FirstVertex + p.VertexCount; i++)
                    sum += v[i];
                Assert.Less((sum / p.VertexCount - p.Centre).magnitude, 1e-3f);
            }
        }

        [Test]
        public void TheSeamsAreBackedByAClosedDarkShellFacingIn()
        {
            Assert.AreEqual(2, mesh.subMeshCount, "the plates, then the backing that darkens the seams");
            Vector3[] v = mesh.vertices;
            int[] t = mesh.GetTriangles(1);
            Assert.IsNotEmpty(t);
            float innermostPlate = plates.SelectMany(p => Enumerable.Range(p.FirstVertex, p.VertexCount)).Min(i => v[i].magnitude);
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Vector3 centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                Assert.Less(Vector3.Dot(normal, centre), 0f, "a backing triangle faces outward: it would cover the plates' seams from outside");
                for (int k = 0; k < 3; k++)
                {
                    Assert.Less(v[t[i + k]].magnitude, innermostPlate, "the backing pokes through a plate");
                    int a = t[i + k], b = t[i + (k + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            Assert.IsTrue(edges.Values.All(n => n == 2), "the backing is closed: no hole shows the floor through a seam");
        }
    }
}
