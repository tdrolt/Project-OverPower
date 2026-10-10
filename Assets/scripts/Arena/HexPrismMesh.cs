using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>A six-sided prism that replaces the built-in cylinder (radius 1, height 2, centred) one for one, so the cylinders' scales still apply.
    /// Corners stand at 0, 60 ... 300 degrees of yaw (the first faces +Z, like the first column). Flat-shaded.</summary>
    public static class HexPrismMesh
    {
        public const int Sides = 6;
        public const int VertexCount = Sides * 4 + 2 * (Sides + 1);

        public static Mesh Create()
        {
            var vertices = new Vector3[VertexCount];
            var normals = new Vector3[VertexCount];
            var triangles = new System.Collections.Generic.List<int>();
            int next = 0;

            for (int i = 0; i < Sides; i++)
            {
                float a0 = 360f * i / Sides, a1 = 360f * (i + 1) / Sides;
                Vector3 p0 = Corner(a0), p1 = Corner(a1);
                Vector3 outward = Corner((a0 + a1) / 2f).normalized;
                int b0 = next;
                vertices[next] = p0 + Vector3.down; normals[next++] = outward;
                vertices[next] = p0 + Vector3.up; normals[next++] = outward;
                vertices[next] = p1 + Vector3.up; normals[next++] = outward;
                vertices[next] = p1 + Vector3.down; normals[next++] = outward;
                triangles.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
            }

            foreach (float y in new[] { 1f, -1f })
            {
                bool top = y > 0f;
                int centre = next;
                vertices[next] = new Vector3(0f, y, 0f); normals[next++] = new Vector3(0f, y, 0f);
                int rim = next;
                for (int i = 0; i < Sides; i++)
                {
                    vertices[next] = Corner(360f * i / Sides) + new Vector3(0f, y, 0f);
                    normals[next++] = new Vector3(0f, y, 0f);
                }
                for (int i = 0; i < Sides; i++)
                {
                    int r0 = rim + i, r1 = rim + (i + 1) % Sides;
                    triangles.AddRange(top ? new[] { centre, r0, r1 } : new[] { centre, r1, r0 });
                }
            }

            var mesh = new Mesh { name = "Hex Prism", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Corner(float yawDegrees)
        {
            float radians = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
