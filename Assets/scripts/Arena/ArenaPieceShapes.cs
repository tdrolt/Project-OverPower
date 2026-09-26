using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Box maths shared by the edit-time arena builder and the play-time phase-two cut, so a barrier built
    /// either way blocks exactly the same band, and so a piece's footprint is worked out the same way wherever it's
    /// asked (ArenaPhaseTwoCut's hide rule, the scene tests that guard it).</summary>
    public static class ArenaPieceShapes
    {
        /// <summary>The four X/Z corners of a piece's footprint on the floor: every Block, Barrier and recess plank is
        /// a unit cube scaled and turned about Y (ArenaPrimitiveBuilder, ArenaPhaseTwoCut), so its footprint is just
        /// its lossy X/Z scale, rotated. Height and Y position don't matter - only where it stands on the ground,
        /// which is all "is this piece behind the wall" or "do these two pieces overlap" ever ask.</summary>
        public static Vector2[] FootprintCorners(Vector3 position, Quaternion rotation, Vector3 lossyScale)
        {
            Vector3 right = rotation * Vector3.right * (lossyScale.x * 0.5f);
            Vector3 forward = rotation * Vector3.forward * (lossyScale.z * 0.5f);
            return new[]
            {
                new Vector2(position.x + right.x + forward.x, position.z + right.z + forward.z),
                new Vector2(position.x + right.x - forward.x, position.z + right.z - forward.z),
                new Vector2(position.x - right.x + forward.x, position.z - right.z + forward.z),
                new Vector2(position.x - right.x - forward.x, position.z - right.z - forward.z),
            };
        }

        /// <summary>The same, for a piece that isn't (or isn't yet) a live Transform: a flat centre, a Unity yaw and
        /// a plan-view size (x = width, y = depth - matching the x/z of a Piece or a plank, never a Transform's own
        /// x/y/z order).</summary>
        public static Vector2[] FootprintCorners(Vector2 centreXZ, float yawDegrees, Vector2 sizeXZ) =>
            FootprintCorners(new Vector3(centreXZ.x, 0f, centreXZ.y), Quaternion.Euler(0f, yawDegrees, 0f),
                new Vector3(sizeXZ.x, 0f, sizeXZ.y));

        /// <summary>True when two footprints (each the four corners <see cref="FootprintCorners"/> returns, in that
        /// winding) overlap at all, by the separating axis theorem: for two rectangles, only their own four edge
        /// directions (two each, since opposite edges are parallel) can ever separate them, so testing those four is
        /// enough - no axis among them with a gap means the rectangles intersect.</summary>
        public static bool FootprintsOverlap(Vector2[] a, Vector2[] b)
        {
            Vector2[] axes =
            {
                (a[0] - a[1]).normalized, (a[0] - a[2]).normalized,
                (b[0] - b[1]).normalized, (b[0] - b[2]).normalized,
            };
            foreach (Vector2 axis in axes)
            {
                ProjectExtent(a, axis, out float aMin, out float aMax);
                ProjectExtent(b, axis, out float bMin, out float bMax);
                if (aMax < bMin || bMax < aMin)
                    return false; // this axis separates them - they can't overlap
            }
            return true;
        }

        private static void ProjectExtent(Vector2[] corners, Vector2 axis, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (Vector2 c in corners)
            {
                float d = Vector2.Dot(c, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        /// <summary>A barrier's BoxCollider, in its own (scaled) space, that blocks from world bottomY to world topY
        /// whatever its look: the barrier's origin sits at world positionY and its look is lookHeight tall (its Y
        /// scale). Amendment 1: only a living player's body collides with the Barrier layer, so a tall band costs
        /// nothing and nobody can be knocked up onto the barrier.</summary>
        public static void BarrierBlockingBox(float bottomY, float topY, float positionY, float lookHeight,
                                              out Vector3 centre, out Vector3 size)
        {
            float blockingCentreYWorld = (bottomY + topY) * 0.5f;
            float blockingHeightWorld = topY - bottomY;
            float scaleY = Mathf.Max(0.0001f, lookHeight);
            centre = new Vector3(0f, (blockingCentreYWorld - positionY) / scaleY, 0f);
            size = new Vector3(1f, blockingHeightWorld / scaleY, 1f);
        }
    }
}
