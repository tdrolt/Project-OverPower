using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>Following a planned route with the player's own movement: corner by corner, flat, re-planned on a timer, when the goal
    /// moves or when the body is stuck against something the map does not know (a Cover wall, another body). Routes come from two walkable
    /// maps, a tight one as wide as the body and a wide one with a margin off walls: which to walk, and how to take a tight route's bends
    /// (slower, through the middle of the gap).</summary>
    public static partial class BotPathRules
    {
        /// <summary>The first corner after the start not yet within reachMetres (flat); -1 when the last one is reached.</summary>
        public static int NextCorner(Vector3[] corners, Vector3 position, float reachMetres)
        {
            if (corners == null || corners.Length == 0) return -1;
            int last = corners.Length - 1;
            if (Flat(corners[last], position) <= reachMetres) return -1;
            for (int i = 1; i <= last; i++)
            {
                if (Flat(corners[i], position) > reachMetres) return i;
            }
            return -1;
        }

        public static bool IsStuck(bool tryingToMove, float movedMetres, float overSeconds, float stuckSeconds) =>
            tryingToMove && overSeconds >= stuckSeconds && movedMetres < 0.5f;

        public static bool NeedsRepath(bool hasPath, float secondsSincePlan, float repathSeconds, float goalMovedMetres) =>
            !hasPath || secondsSincePlan >= repathSeconds || goalMovedMetres > 1f;

        /// <summary>Which colliders the walkable map is built from: anything solid that stays put (a trigger stops nobody; a moving body
        /// is a player, not the map).</summary>
        public static bool IsWalkSource(bool isTrigger, bool dynamicBody) => !isTrigger && !dynamicBody;

        /// <summary>A flat direction of length 1 from one point toward another, or zero when they are on top of each other.</summary>
        public static Vector3 MoveToward(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? Vector3.zero : d.normalized;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
