using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The edges of a floating pack shape (the cross, the icosahedron) and the mesh that draws them as lines: each edge a thin square bar
    /// straddling it, half in and half out of the solid, so an unlit shape whose faces are one flat colour still shows its form.
    /// </summary>
    public static class ShapeEdges
    {
        public const string MeshName = "Pack Edge Lines";

        /// <summary>The 36 edges of a plus <paramref name="length"/> across in X and Y, its arms <paramref name="arm"/> wide and deep, centred
        /// on the origin: round its outline front and back and between them, never through the middle where the two bars overlap.</summary>
        public static List<(Vector3, Vector3)> Plus(float length, float arm)
        {
            float h = length * 0.5f, w = arm * 0.5f;
            var outline = new[]
            {
                new Vector2(w, h), new Vector2(-w, h), new Vector2(-w, w), new Vector2(-h, w), new Vector2(-h, -w), new Vector2(-w, -w),
                new Vector2(-w, -h), new Vector2(w, -h), new Vector2(w, -w), new Vector2(h, -w), new Vector2(h, w), new Vector2(w, w),
            };
            var edges = new List<(Vector3, Vector3)>(36);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                edges.Add((new Vector3(a.x, a.y, -w), new Vector3(b.x, b.y, -w)));
                edges.Add((new Vector3(a.x, a.y, w), new Vector3(b.x, b.y, w)));
                edges.Add((new Vector3(a.x, a.y, -w), new Vector3(a.x, a.y, w)));
            }
            return edges;
        }

        /// <summary>The 30 edges of IcosahedronMesh's icosahedron of this radius.</summary>
        public static List<(Vector3, Vector3)> Icosahedron(float radius)
        {
            var seen = new HashSet<(int, int)>();
            var edges = new List<(Vector3, Vector3)>(30);
            int[] faces = IcosahedronMesh.Faces;
            for (int k = 0; k < faces.Length; k += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = faces[k + e], b = faces[k + (e + 1) % 3];
                    if (seen.Add((Mathf.Min(a, b), Mathf.Max(a, b))))
                        edges.Add((IcosahedronMesh.Corners[a].normalized * radius, IcosahedronMesh.Corners[b].normalized * radius));
                }
            return edges;
        }

        /// <summary>One mesh of square bars <paramref name="width"/> across, one per edge, each running a half width past both ends so the
        /// corners close.</summary>
        public static Mesh LinesMesh(List<(Vector3, Vector3)> edges, float width)
        {
            float half = Mathf.Max(0.001f, width) * 0.5f;
            var vertices = new List<Vector3>(edges.Count * 8);
            var triangles = new List<int>(edges.Count * 36);
            foreach ((Vector3 a, Vector3 b) in edges)
            {
                Vector3 dir = (b - a).normalized;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized * half;
                Vector3 v = Vector3.Cross(dir, u).normalized * half;
                Vector3 from = a - dir * half, to = b + dir * half, centre = (a + b) * 0.5f;
                int first = vertices.Count;
                foreach (Vector3 end in new[] { from, to })
                {
                    vertices.Add(end - u - v);
                    vertices.Add(end + u - v);
                    vertices.Add(end + u + v);
                    vertices.Add(end - u + v);
                }
                Quad(vertices, triangles, centre, first + 0, first + 1, first + 2, first + 3); // the near end
                Quad(vertices, triangles, centre, first + 4, first + 5, first + 6, first + 7); // the far end
                for (int s = 0; s < 4; s++)
                {
                    int n = (s + 1) % 4;
                    Quad(vertices, triangles, centre, first + s, first + n, first + 4 + n, first + 4 + s);
                }
            }
            var mesh = new Mesh { name = MeshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Two triangles, turned so their front (Unity: clockwise seen from it) faces away from the bar's middle.
        private static void Quad(List<Vector3> v, List<int> tris, Vector3 centre, int a, int b, int c, int d)
        {
            bool outward = Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] + v[c] - 2f * centre) > 0f;
            if (outward)
                tris.AddRange(new[] { a, b, c, a, c, d });
            else
                tris.AddRange(new[] { a, c, b, a, d, c });
        }
    }
}
