using System.Collections.Generic;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The Heal Zone's ring: hexagon cells on one grid (a corner toward +Z, as the floor's tiles) in a band just inside the zone's
    /// real radius, which of them are lit at a moment, and their colours. The lit cells come only from a seed (StunArcsRule.Seed on the
    /// server clock and the caster), so every client flickers the same cells; the cell order is the grid's, the same on every client.</summary>
    public static class AoeRingLook
    {
        private const float Sqrt3 = 1.7320508f;

        /// <summary>The centres, round the zone's middle, of every whole cell inside <paramref name="radius"/> whose centre is within
        /// <paramref name="band"/> of it. <paramref name="cellSize"/> is the width across the flats (the distance between neighbours).</summary>
        public static void CellCentres(float radius, float band, float cellSize, List<Vector2> into)
        {
            into.Clear();
            if (radius <= 0f || cellSize <= 0f)
                return;
            float whole = cellSize / Sqrt3;
            int reach = Mathf.CeilToInt(radius / cellSize * 2f / Sqrt3) + 1;
            for (int r = -reach; r <= reach; r++)
                for (int q = -reach; q <= reach; q++)
                {
                    Vector2 c = HexGrid.CentreOf(new Vector2Int(q, r), Vector2.zero, cellSize);
                    float d = c.magnitude;
                    if (d + whole <= radius && d >= radius - band)
                        into.Add(c);
                }
        }

        /// <summary>A drawn cell's centre-to-corner size: the grid cell less the gap left between neighbours.</summary>
        public static float Circumradius(float cellSize, float gap) => Mathf.Max(0f, cellSize - gap) / Sqrt3;

        /// <summary>Corner <paramref name="k"/> of a cell on the ground plane (x, z), clockwise from the corner toward +Z.</summary>
        public static Vector2 Corner(Vector2 centre, float circumradius, int k)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            return centre + circumradius * new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        public static bool Lit(int seed, int cell, float litShare) => StunArcsRule.Hash01(seed, cell, 0) < litShare;

        public static Color Line(Color colour, bool lit, float litAlpha, float dimAlpha) => VisualTint.WithAlpha(colour, lit ? litAlpha : dimAlpha);

        public static Color Fill(Color colour, bool lit, float fillAlpha) => VisualTint.WithAlpha(colour, lit ? fillAlpha : 0f);
    }
}
