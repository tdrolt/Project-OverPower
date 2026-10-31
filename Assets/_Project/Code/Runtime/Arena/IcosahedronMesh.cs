using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>A flat-shaded icosahedron (20 faces) of the given radius, centred: the shape floating over an ultimate charge pack.</summary>
    public static class IcosahedronMesh
    {
        public const string MeshName = "Ultimate Pack Icosahedron";

        public static Mesh Create(float radius)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var corners = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            // Three corners of its own per face, so each face is lit flat and reads as a facet.
            var vertices = new Vector3[faces.Length];
            var triangles = new int[faces.Length];
            for (int k = 0; k < faces.Length; k += 3)
            {
                Vector3 a = corners[faces[k]].normalized * radius, b = corners[faces[k + 1]].normalized * radius, c = corners[faces[k + 2]].normalized * radius;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                    (b, c) = (c, b); // the face's front (Unity: clockwise seen from it) must look outward
                vertices[k] = a;
                vertices[k + 1] = b;
                vertices[k + 2] = c;
                triangles[k] = k;
                triangles[k + 1] = k + 1;
                triangles[k + 2] = k + 2;
            }
            var mesh = new Mesh { name = MeshName, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
