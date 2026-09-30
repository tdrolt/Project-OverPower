using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// An upright box seen from above - the footprint a wall or barrier run leaves on the floor. Pure geometry,
    /// shared by the wall-coverage hole finder (arena step 4) and the barrier crossing rule (step 4a), so both agree
    /// on what "the box's long face" and "the box's short face" mean.
    /// </summary>
    public readonly struct BoxFootprint
    {
        /// <summary>The box's centre, in the XZ plane.</summary>
        public readonly Vector2 Centre;

        /// <summary>Unit vector along the box's long axis (local +X, turned by its yaw).</summary>
        public readonly Vector2 Along;

        /// <summary>Unit vector across the box's short axis (local +Z, turned by its yaw) - perpendicular to Along.</summary>
        public readonly Vector2 Across;

        public readonly float HalfLength;
        public readonly float HalfWidth;

        public BoxFootprint(Vector2 centre, Vector2 along, float halfLength, float halfWidth)
        {
            Centre = centre;
            Along = along.sqrMagnitude > 0.0001f ? along.normalized : Vector2.right;
            Across = new Vector2(-Along.y, Along.x);
            HalfLength = halfLength;
            HalfWidth = halfWidth;
        }

        /// <summary>True when the point sits inside the box, in its own plane.</summary>
        public bool Contains(Vector2 point)
        {
            Vector2 d = point - Centre;
            float along = Vector2.Dot(d, Along);
            float across = Vector2.Dot(d, Across);
            return Mathf.Abs(along) <= HalfLength && Mathf.Abs(across) <= HalfWidth;
        }

        /// <summary>True when a circle of this radius, centred on point, touches or overlaps the box.</summary>
        public bool OverlapsCircle(Vector2 point, float radius)
        {
            Vector2 d = point - Centre;
            float along = Vector2.Dot(d, Along);
            float across = Vector2.Dot(d, Across);
            float clampedAlong = Mathf.Clamp(along, -HalfLength, HalfLength);
            float clampedAcross = Mathf.Clamp(across, -HalfWidth, HalfWidth);
            float dAlong = along - clampedAlong;
            float dAcross = across - clampedAcross;
            return dAlong * dAlong + dAcross * dAcross <= radius * radius;
        }

        /// <summary>
        /// Builds a footprint from a box's world transform. worldSize is the box's TRUE world-space size (its local
        /// size already multiplied by lossyScale - a BoxCollider's own `size` never is, on its own). Local +X is the
        /// long axis (Along), local +Z is the thickness (Across), matching every wall and barrier row this project
        /// builds (ArenaWallPlan, ArenaLayout's Barrier rows).
        /// </summary>
        public static BoxFootprint FromBox(Vector3 worldCentre, Quaternion rotation, Vector3 worldSize)
        {
            Vector3 alongWorld = rotation * Vector3.right;
            Vector2 along = new Vector2(alongWorld.x, alongWorld.z);
            Vector2 centre = new Vector2(worldCentre.x, worldCentre.z);
            return new BoxFootprint(centre, along, worldSize.x * 0.5f, worldSize.z * 0.5f);
        }
    }
}
