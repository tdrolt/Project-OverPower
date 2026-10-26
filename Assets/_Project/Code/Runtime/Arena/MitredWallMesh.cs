using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>A boundary wall with its ends cut on ArenaWallPlan's mitres, so two walls meeting at a corner that is not a right angle
    /// touch along one line instead of sharing space. Built in the unit cube's own space: the transform still scales it to length x
    /// height x thickness (+Z inward), and its bounds stay the whole box, so the BoxCollider beside it is the box the plan closes the
    /// corner with. Each face maps 0..1 as the cube's do, except the top along the wall: a slanted end sits inside that range, because
    /// the arena shader draws its top line from the face's UV edges and a line across the mitre is the cross line this mesh removes.</summary>
    public static class MitredWallMesh
    {
        public const string MeshName = "Mitred Wall";

        /// <summary>The top's U at its two ends: a square end on the face's edge (0 or 1), a mitred one well inside it.</summary>
        public static Vector2 TopURange(float startMitre, float endMitre)
        {
            bool start = startMitre != 0f, end = endMitre != 0f;
            if (start && end) return new Vector2(0.25f, 0.75f);
            if (start) return new Vector2(0.5f, 1f);
            if (end) return new Vector2(0f, 0.5f);
            return new Vector2(0f, 1f);
        }

        public static Mesh Create(float length, float startMitre, float endMitre)
        {
            float l = Mathf.Max(length, 0.0001f);
            // Plan corners in unit space (x along -0.5..0.5, z outer -0.5 .. inner 0.5), same order as Run.DrawnCorners.
            var innerStart = new Vector2(-0.5f + Mathf.Max(startMitre, 0f) / l, 0.5f);
            var innerEnd = new Vector2(0.5f - Mathf.Max(endMitre, 0f) / l, 0.5f);
            var outerEnd = new Vector2(0.5f - Mathf.Max(-endMitre, 0f) / l, -0.5f);
            var outerStart = new Vector2(-0.5f + Mathf.Max(-startMitre, 0f) / l, -0.5f);
            Vector2 topU = TopURange(startMitre, endMitre);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            Vector2 TopUv(Vector2 p) => new Vector2(Mathf.Lerp(topU.x, topU.y, p.x + 0.5f), p.y + 0.5f);
            Vector2[] plan = { innerStart, innerEnd, outerEnd, outerStart };
            foreach (float y in new[] { 0.5f, -0.5f })
            {
                var quad = new Vector3[4];
                var quadUv = new Vector2[4];
                for (int i = 0; i < 4; i++)
                {
                    quad[i] = new Vector3(plan[i].x, y, plan[i].y);
                    quadUv[i] = TopUv(plan[i]);
                }
                AddQuad(quad, quadUv, new Vector3(0f, Mathf.Sign(y), 0f), vertices, normals, uvs, triangles);
            }

            AddSide(innerStart, innerEnd, Vector3.forward, vertices, normals, uvs, triangles);
            AddSide(outerStart, outerEnd, Vector3.back, vertices, normals, uvs, triangles);
            AddSide(outerStart, innerStart, EndNormal(outerStart, innerStart, -1f), vertices, normals, uvs, triangles);
            AddSide(outerEnd, innerEnd, EndNormal(outerEnd, innerEnd, 1f), vertices, normals, uvs, triangles);

            var mesh = new Mesh { name = MeshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // An end face's normal in unit space, square to its slanted edge and pointing off the wall's end (sign -1 start, +1 end);
        // the renderer's inverse-transpose keeps it square to the face once the transform stretches the wall.
        private static Vector3 EndNormal(Vector2 outer, Vector2 inner, float sign)
        {
            Vector2 edge = inner - outer;
            return (new Vector3(edge.y, 0f, -edge.x) * sign).normalized;
        }

        // A vertical face from a to b, full height: U along it 0..1, V up 0..1.
        private static void AddSide(Vector2 a, Vector2 b, Vector3 normal, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var quad = new[] { new Vector3(a.x, -0.5f, a.y), new Vector3(b.x, -0.5f, b.y), new Vector3(b.x, 0.5f, b.y), new Vector3(a.x, 0.5f, a.y) };
            var quadUv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            AddQuad(quad, quadUv, normal, vertices, normals, uvs, triangles);
        }

        // Two triangles, wound so Unity draws the side the normal points to.
        private static void AddQuad(Vector3[] quad, Vector2[] quadUv, Vector3 normal, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int first = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(quad[i]);
                normals.Add(normal);
                uvs.Add(quadUv[i]);
            }
            bool facing = Vector3.Dot(Vector3.Cross(quad[1] - quad[0], quad[2] - quad[0]), normal) > 0f;
            int[] order = facing ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            foreach (int i in order)
                triangles.Add(first + i);
        }
    }
}
