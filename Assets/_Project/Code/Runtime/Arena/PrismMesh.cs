using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>A flat-shaded prism that replaces the built-in cylinder (radius 1, height 2, centred) one for one, so the
    /// cylinders' scales still apply. Corners stand at 360/sides steps of yaw, the first facing +Z. No UVs: the arena
    /// shader then draws no top-edge border on its caps. Each caller names its mesh, and its leftover sweep finds it by that name.</summary>
    public static class PrismMesh
    {
        public static int VertexCountFor(int sides) => sides * 4 + 2 * (sides + 1);

        public static Mesh Create(int sides, string name)
        {
            var vertices = new Vector3[VertexCountFor(sides)];
            var normals = new Vector3[vertices.Length];
            var triangles = new System.Collections.Generic.List<int>(sides * 12);
            int next = 0;

            for (int i = 0; i < sides; i++)
            {
                float a0 = 360f * i / sides, a1 = 360f * (i + 1) / sides;
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
                for (int i = 0; i < sides; i++)
                {
                    vertices[next] = Corner(360f * i / sides) + new Vector3(0f, y, 0f);
                    normals[next++] = new Vector3(0f, y, 0f);
                }
                for (int i = 0; i < sides; i++)
                {
                    int r0 = rim + i, r1 = rim + (i + 1) % sides;
                    triangles.AddRange(top ? new[] { centre, r0, r1 } : new[] { centre, r1, r0 });
                }
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
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

    /// <summary>The eight sides of a capture tower; six sides stay the Dominion spawn's mark (HexPrismMesh).</summary>
    public static class OctagonPrismMesh
    {
        public const string MeshName = "Octagon Prism";
        public const int Sides = 8;
        public const int VertexCount = Sides * 4 + 2 * (Sides + 1);

        public static Mesh Create() => PrismMesh.Create(Sides, MeshName);
    }
}
