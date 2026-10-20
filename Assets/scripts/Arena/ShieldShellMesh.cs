using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Where one plate of the Shield's shell lives in the mesh: its rest centre and its own run of vertices.</summary>
    public readonly struct PlateRange
    {
        public readonly Vector3 Centre;
        public readonly int FirstVertex;
        public readonly int VertexCount;

        public PlateRange(Vector3 centre, int firstVertex, int vertexCount)
        {
            Centre = centre;
            FirstVertex = firstVertex;
            VertexCount = vertexCount;
        }
    }

    /// <summary>The Shield's shell: a Goldberg polyhedron (the dual of an icosahedron split once), 12 pentagon and 30 hexagon plates, each
    /// shrunk toward its own centre so real gaps (the seams) stay between them. Submesh 0 is the plates, each with its own flat-shaded
    /// vertices so the rise can move it on its own; submesh 1 is a closed backing just inside them, facing in, after the last plate's
    /// vertices: through a seam it is the far side's backing that shows (dark), or the player standing in front of it. Unit diameter,
    /// centred at zero, so the Shield's diameter scales it.</summary>
    public static class ShieldShellMesh
    {
        public const string Name = "Shield Shell";
        private const float Radius = 0.5f;
        private const float BackingRadius = 0.46f; // just inside the plates' centres (0.469), so it never pokes through one and the rim seams stay dark

        public static Mesh Create(float seamFraction, out PlateRange[] plates)
        {
            BuildSplitIcosahedron(out List<Vector3> points, out List<int[]> faces);

            var faceCentres = new Vector3[faces.Count];
            var facesOf = new List<int>[points.Count];
            for (int p = 0; p < points.Count; p++)
                facesOf[p] = new List<int>(6);
            for (int f = 0; f < faces.Count; f++)
            {
                int[] t = faces[f];
                faceCentres[f] = ((points[t[0]] + points[t[1]] + points[t[2]]) / 3f).normalized * Radius;
                foreach (int p in t)
                    facesOf[p].Add(f);
            }

            float keep = 1f - Mathf.Clamp01(seamFraction);
            var vertices = new List<Vector3>(240);
            var normals = new List<Vector3>(240);
            var triangles = new List<int>(156 * 3);
            plates = new PlateRange[points.Count];
            for (int p = 0; p < points.Count; p++)
            {
                List<Vector3> corners = CornersAround(points[p], facesOf[p], faceCentres);
                Vector3 centre = Vector3.zero;
                foreach (Vector3 c in corners)
                    centre += c;
                centre /= corners.Count;

                int first = vertices.Count;
                Vector3 normal = points[p].normalized;
                foreach (Vector3 c in corners)
                {
                    vertices.Add(centre + (c - centre) * keep);
                    normals.Add(normal);
                }
                for (int i = 1; i + 1 < corners.Count; i++)
                    triangles.AddRange(Outward(vertices, first, first + i, first + i + 1));
                plates[p] = new PlateRange(centre, first, corners.Count);
            }

            int backingFirst = vertices.Count;
            foreach (Vector3 p in points)
            {
                vertices.Add(p.normalized * BackingRadius);
                normals.Add(-p.normalized);
            }
            var backing = new List<int>(faces.Count * 3);
            foreach (int[] f in faces)
            {
                int[] outward = Outward(vertices, backingFirst + f[0], backingFirst + f[1], backingFirst + f[2]);
                backing.Add(outward[0]);
                backing.Add(outward[2]);
                backing.Add(outward[1]);
            }

            var mesh = new Mesh { name = Name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(triangles, 0);
            mesh.SetTriangles(backing, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] Outward(List<Vector3> v, int a, int b, int c)
        {
            Vector3 normal = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            return Vector3.Dot(normal, v[a] + v[b] + v[c]) >= 0f ? new[] { a, b, c } : new[] { a, c, b };
        }

        /// <summary>The centres of the faces around one point, in order round it, so they make one convex plate.</summary>
        private static List<Vector3> CornersAround(Vector3 point, List<int> around, Vector3[] faceCentres)
        {
            Vector3 n = point.normalized;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 w = Vector3.Cross(n, u);
            var corners = new List<Vector3>(around.Count);
            foreach (int f in around)
                corners.Add(faceCentres[f]);
            corners.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a, w), Vector3.Dot(a, u)).CompareTo(Mathf.Atan2(Vector3.Dot(b, w), Vector3.Dot(b, u))));
            return corners;
        }

        /// <summary>An icosahedron with a corner straight up, each triangle split into four at its edge midpoints (42 points, 80 faces).</summary>
        private static void BuildSplitIcosahedron(out List<Vector3> points, out List<int[]> faces)
        {
            float g = (1f + Mathf.Sqrt(5f)) / 2f;
            var raw = new[]
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            int[,] ico =
            {
                { 0, 11, 5 }, { 0, 5, 1 }, { 0, 1, 7 }, { 0, 7, 10 }, { 0, 10, 11 },
                { 1, 5, 9 }, { 5, 11, 4 }, { 11, 10, 2 }, { 10, 7, 6 }, { 7, 1, 8 },
                { 3, 9, 4 }, { 3, 4, 2 }, { 3, 2, 6 }, { 3, 6, 8 }, { 3, 8, 9 },
                { 4, 9, 5 }, { 2, 4, 11 }, { 6, 2, 10 }, { 8, 6, 7 }, { 9, 8, 1 },
            };
            // Turn corner 0 to the top, so a pentagon crowns the Shield and the plates sit level around it.
            Quaternion up = Quaternion.FromToRotation(raw[0].normalized, Vector3.up);
            points = new List<Vector3>(42);
            foreach (Vector3 r in raw)
                points.Add(up * r.normalized * Radius);

            faces = new List<int[]>(80);
            var midpoints = new Dictionary<long, int>();
            for (int f = 0; f < 20; f++)
            {
                int a = ico[f, 0], b = ico[f, 1], c = ico[f, 2];
                int ab = Midpoint(points, midpoints, a, b), bc = Midpoint(points, midpoints, b, c), ca = Midpoint(points, midpoints, c, a);
                faces.Add(new[] { a, ab, ca });
                faces.Add(new[] { b, bc, ab });
                faces.Add(new[] { c, ca, bc });
                faces.Add(new[] { ab, bc, ca });
            }
        }

        private static int Midpoint(List<Vector3> points, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index))
                return index;
            points.Add(((points[a] + points[b]) * 0.5f).normalized * Radius);
            cache[key] = points.Count - 1;
            return points.Count - 1;
        }
    }
}
