using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>One flat mesh for every cell of the Heal Zone's ring, facing up in the ground plane: per cell a hexagon outline (outer and
    /// inner corners, 0..11) then a fill (centre and inner corners, 12..18), so the view recolours a cell by its own vertex range.</summary>
    public static class AoeRingMesh
    {
        public const string Name = "AoE Zone Cells";
        public const int VerticesPerCell = 19;
        public const int FillStart = 12;

        private const float Sqrt3 = 1.7320508f;

        public static Mesh Create(IReadOnlyList<Vector2> centres, float circumradius, float lineWidth)
        {
            int n = centres.Count;
            var vertices = new Vector3[n * VerticesPerCell];
            var triangles = new int[n * 18 * 3];
            float inner = Mathf.Max(0f, circumradius - lineWidth * 2f / Sqrt3); // the line's own thickness, measured across it
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int b = i * VerticesPerCell;
                for (int k = 0; k < 6; k++)
                {
                    vertices[b + k] = Flat(AoeRingLook.Corner(centres[i], circumradius, k));
                    vertices[b + 6 + k] = Flat(AoeRingLook.Corner(centres[i], inner, k));
                    vertices[b + FillStart + 1 + k] = vertices[b + 6 + k];
                }
                vertices[b + FillStart] = Flat(centres[i]);
                for (int k = 0; k < 6; k++)
                {
                    int next = (k + 1) % 6;
                    t = Tri(triangles, t, b + k, b + next, b + 6 + k);
                    t = Tri(triangles, t, b + 6 + k, b + next, b + 6 + next);
                    t = Tri(triangles, t, b + FillStart, b + FillStart + 1 + k, b + FillStart + 1 + next);
                }
            }

            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.up;
            var mesh = new Mesh { name = Name, hideFlags = HideFlags.DontSave };
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Flat(Vector2 xz) => new Vector3(xz.x, 0f, xz.y);

        private static int Tri(int[] triangles, int t, int a, int b, int c)
        {
            triangles[t] = a;
            triangles[t + 1] = b;
            triangles[t + 2] = c;
            return t + 3;
        }
    }
}
