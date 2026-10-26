using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The builder Hex Disc.asset was made with: a flat hexagon one metre across its corners, facing up, the first corner toward
    /// +Z like HexPrismMesh and a flat-circle LineRenderer of six points. The game only loads the saved asset (the portal's floor, the
    /// mine's light); the tests check the asset still matches this.</summary>
    internal static class HexDiscMesh
    {
        public const string MeshName = "Hex Disc";
        public const string AssetPath = "Assets/_Project/Shared/Art/Hex Disc.asset";
        private const int Sides = 6;
        private const float CornerRadius = 0.5f;

        public static Mesh Create()
        {
            var vertices = new Vector3[Sides + 1];
            var normals = new Vector3[Sides + 1];
            var uvs = new Vector2[Sides + 1];
            var triangles = new int[Sides * 3];
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < Sides; i++)
            {
                float radians = i * 2f * Mathf.PI / Sides;
                vertices[i + 1] = new Vector3(Mathf.Sin(radians) * CornerRadius, 0f, Mathf.Cos(radians) * CornerRadius);
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(0.5f + vertices[i + 1].x, 0.5f + vertices[i + 1].z);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % Sides + 1;
            }
            var mesh = new Mesh { name = MeshName };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
