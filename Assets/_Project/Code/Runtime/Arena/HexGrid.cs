using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The floor's hexagon tiles on the ground plane (x, z), in axial coordinates (q, r). A corner points to +Z, as in
    /// HexPrismMesh; size is the distance between neighbouring tile centres (the width across the flats). With the origin at
    /// ArenaSymmetry's centre the tiles turn onto themselves with the thirds. ArenaHex.hlsl repeats these sums line for line:
    /// change both or neither.</summary>
    public static class HexGrid
    {
        private const float Sqrt3 = 1.7320508f;

        public static Vector2Int TileAt(Vector2 xz, Vector2 origin, float size)
        {
            Vector2 p = (xz - origin) / size;
            return Round(p.x - p.y / Sqrt3, 2f * p.y / Sqrt3);
        }

        public static Vector2 CentreOf(Vector2Int axial, Vector2 origin, float size) =>
            origin + size * new Vector2(axial.x + 0.5f * axial.y, 0.5f * Sqrt3 * axial.y);

        public static Vector2Int Texel(Vector2Int axial, Vector2Int min) => axial - min;

        // Cube rounding: round all three cube coordinates, then rebuild the one that moved most from the other two.
        private static Vector2Int Round(float q, float r)
        {
            float s = -q - r;
            float rq = Mathf.Round(q), rr = Mathf.Round(r), rs = Mathf.Round(s);
            float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new Vector2Int((int)rq, (int)rr);
        }
    }
}
