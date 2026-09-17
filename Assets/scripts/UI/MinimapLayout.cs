using System.Collections.Generic;
using System.Globalization;
using Overpower.Match;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap's maths, kept pure so it's tested: where a world point lands on the map, how the map turns with
    /// the camera, and the shapes of links.
    ///
    /// MAP SPACE is canvas units with (0, 0) at the map's centre, +x = world +X (east) and +y = world +Z (north): the
    /// baked image's own orientation. MinimapView turns the whole map by the camera's team yaw, so "up" on the map is
    /// "up" on screen, and turns labels and markers back so they stay upright.
    ///
    /// Why +yaw: Unity yaw turns clockwise seen from above, and a UI rotation turns counter-clockwise, so turning
    /// the image by the same number brings world direction "yaw" to the top.
    /// </summary>
    public static class MinimapLayout
    {
        /// <summary>A world point's map-space position. The baked image covers a square of worldSizeMetres centred on
        /// worldCentreXZ (world x, z), drawn mapSize canvas units across.</summary>
        public static Vector2 WorldToMap(Vector3 world, Vector2 worldCentreXZ, float worldSizeMetres, float mapSize)
        {
            if (worldSizeMetres <= 0f)
                return Vector2.zero;
            float scale = mapSize / worldSizeMetres;
            return new Vector2((world.x - worldCentreXZ.x) * scale, (world.z - worldCentreXZ.y) * scale);
        }

        public static bool IsInsideBakedArea(Vector3 world, Vector2 worldCentreXZ, float worldSizeMetres)
        {
            float half = worldSizeMetres / 2f;
            return Mathf.Abs(world.x - worldCentreXZ.x) <= half && Mathf.Abs(world.z - worldCentreXZ.y) <= half;
        }

        /// <summary>The map's UI rotation (z degrees) for the camera's yaw.</summary>
        public static float MapRotationDegrees(float cameraYawDegrees) => cameraYawDegrees;

        /// <summary>The UI rotation that turns a label or progress ring inside the turned map back to upright.</summary>
        public static float UprightRotationDegrees(float cameraYawDegrees) => -cameraYawDegrees;

        /// <summary>Your marker's UI rotation inside the (turned) map for the way you face (Unity yaw). A marker drawn
        /// pointing up then points where you face on screen.</summary>
        public static float FacingRotationDegrees(float facingYawDegrees) => -facingYawDegrees;

        /// <summary>Where a map-space point ends up once the map is turned by the camera's yaw: what the player sees.</summary>
        public static Vector2 TurnWithCamera(Vector2 mapPoint, float cameraYawDegrees)
        {
            float radians = cameraYawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(mapPoint.x * cos - mapPoint.y * sin, mapPoint.x * sin + mapPoint.y * cos);
        }

        /// <summary>A straight line drawn as a stretched image: its middle, length and UI rotation.</summary>
        public static (Vector2 Centre, float Length, float AngleDegrees) Segment(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return ((from + to) / 2f, delta.magnitude, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        /// <summary>The point <paramref name="distanceFromEnd"/> back from <paramref name="to"/> toward
        /// <paramref name="from"/>: where an arrowhead sits so it touches the edge of the zone it points at.</summary>
        public static Vector2 PointBeforeEnd(Vector2 from, Vector2 to, float distanceFromEnd)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length <= 0.0001f)
                return to;
            return to - delta / length * Mathf.Min(distanceFromEnd, length);
        }

        /// <summary>The side of the baked square: the arena's farthest point from its centre, plus a margin, on every
        /// side. Centred on the arena's symmetry centre, so turning the map to any team's yaw keeps the whole arena
        /// inside the round minimap.</summary>
        public static float FramedSizeMetres(float arenaRadiusMetres, float marginMetres) =>
            2f * (Mathf.Max(0f, arenaRadiusMetres) + Mathf.Max(0f, marginMetres));

        /// <summary>The triangular mask's circumradius (Tudor, 2026-09-17: the mask becomes a triangle, one vertex
        /// toward each capital): the smallest R such that a point is inside the triangle (p . n_i &lt;= R/2 for
        /// every inward edge normal n_i) for every given point, plus a margin. Each vertexDirection IS the inward
        /// normal of the opposite edge (true for an equilateral triangle, guaranteed by ArenaSymmetry's 3-fold
        /// layout) - n_i = vertexDirections[i], normalised. R = 2 x the worst-case reach (the farthest any point
        /// extends along any one of the 3 normals), so the inradius (R/2) alone decides the size; the factor of 2
        /// is the equilateral triangle's fixed circumradius/inradius ratio.</summary>
        public static float TriangleCircumradius(IReadOnlyList<Vector2> points, IReadOnlyList<Vector2> vertexDirections, float marginMetres)
        {
            float worstReach = 0f;
            foreach (Vector2 rawDirection in vertexDirections)
            {
                Vector2 n = rawDirection.normalized;
                float reach = 0f;
                foreach (Vector2 p in points)
                {
                    float along = Vector2.Dot(p, n);
                    if (along > reach) reach = along;
                }
                if (reach > worstReach) worstReach = reach;
            }
            return 2f * worstReach + Mathf.Max(0f, marginMetres);
        }

        /// <summary>The bubble label for a tier, as in the GDD (I = capital ... IV = centre).</summary>
        public static string TierLabel(int tier) => tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            _ => tier.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Every adjacent pair of the given zones once, lowest id first, sorted: the lines the map draws.</summary>
        public static List<(int A, int B)> LinkPairs(TerritoryMap map, IEnumerable<int> zones)
        {
            var known = new HashSet<int>(zones);
            var sorted = new List<int>(known);
            sorted.Sort();
            var pairs = new List<(int A, int B)>();
            foreach (int a in sorted)
                foreach (int b in map.AdjacentTo(a))
                    if (b > a && known.Contains(b))
                        pairs.Add((a, b));
            return pairs;
        }
    }
}
