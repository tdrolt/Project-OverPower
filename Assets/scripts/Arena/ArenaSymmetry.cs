using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Makes the arena three identical thirds, turned 120° apart about Centre.
    ///
    /// HOW TO EDIT THE ARENA: change only the objects under Source. Then press "Rebuild thirds" on this component
    /// (or OverPower > Arena > Rebuild thirds), look at the result, and save the scene. The two generated thirds are
    /// deleted and copied again from Source on every rebuild, so an edit made directly to them is thrown away. The
    /// Inspector greys them out to make that obvious.
    ///
    /// Towers, their flag carpets and spawn points are NOT copied: they are networked or carry ids that must stay
    /// unique. They are listed in Snapped Triplets instead. You place the Source one, and the rebuild moves its two
    /// partners to match. Objects in Centred are moved onto the centre (their height is kept).
    ///
    /// During play this component has exactly one job: it publishes the arena outline as ArenaBounds (Active,
    /// ActiveBounds, IsInsideArena) for blink, portals and the out-of-arena safety net. Everything else it stores is
    /// read by the Editor tool (Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs).
    /// </summary>
    public class ArenaSymmetry : MonoBehaviour
    {
        /// <summary>The child of Source (and of each generated third) holding the boundary walls. The spelling is the
        /// scene's own. Lives here, not on the Editor-only ArenaSymmetryBuilder, because a portal's path check
        /// (movement step 3, controller decision R1) needs it from runtime code too - only the arena's own boundary
        /// walls block a portal's path, never a crate or a house.</summary>
        public const string BoundaryGroupName = "Boundry";
        [Serializable]
        public class SnappedTriplet
        {
            [Tooltip("The one you place by hand, in the Source third.")]
            public Transform source;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 120° about the centre. Don't place it by hand.")]
            public Transform at120;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 240° about the centre. Don't place it by hand.")]
            public Transform at240;
        }

        [Tooltip("The point the thirds turn about, in world space (only X and Z matter). Moving it moves every " +
                 "generated object and snapped partner on the next rebuild.")]
        public Vector3 centre = new Vector3(65.05f, 0f, 53.34f);

        [Tooltip("Drawing aid only: the map angle (degrees counter-clockwise from +X, seen from above) where the Source " +
                 "third's lines are drawn in the Scene view. Rebuild copies everything under Source regardless.")]
        public float sourceStartDegrees = 25f;

        [Tooltip("The only third you edit. Everything under it is copied into the two generated thirds.")]
        public Transform source;

        [Tooltip("Rebuilt from Source turned 120°. Never edit its children by hand.")]
        public Transform generated120;

        [Tooltip("Rebuilt from Source turned 240°. Never edit its children by hand.")]
        public Transform generated240;

        [Tooltip("Networked or id-carrying objects kept symmetric by moving, not copying: towers, their flag carpets, " +
                 "spawn points.")]
        public List<SnappedTriplet> snappedTriplets = new List<SnappedTriplet>();

        [Tooltip("Objects moved onto the centre by Rebuild thirds, keeping their height (the Tier 4 tower and its " +
                 "carpet).")]
        public List<Transform> centred = new List<Transform>();

        [Tooltip("The arena's outer edge in the Source third, seen from above: the inner faces of the boundary walls " +
                 "under Source/Boundry, in order round the edge (x = world X, y = world Z). The other two thirds use " +
                 "it turned 120 and 240 degrees. Blink and portals can't land outside it, and a player who ends up " +
                 "outside is put back. Move a boundary wall and you must move these points onto its new inner face: " +
                 "Validate reports any wall more than 15 cm off this outline.")]
        public List<Vector2> sourceOutline = new List<Vector2>();

        /// <summary>The arena in the running game, or null outside Play Mode and in a scene without one.</summary>
        public static ArenaSymmetry Active { get; private set; }

        /// <summary>This arena's outline, built when it starts. Null when Source Outline has fewer than two points.</summary>
        public ArenaBounds Bounds { get; private set; }

        /// <summary>The running arena's outline, or null when there is no arena or no outline (a test scene).</summary>
        public static ArenaBounds ActiveBounds => Active != null ? Active.Bounds : null;

        /// <summary>True when the point is at least <paramref name="margin"/> metres inside the arena. With no arena
        /// bounds known it is also true: a scene without the arena allows everything rather than refusing every
        /// blink, portal and placement in it.</summary>
        public static bool IsInsideArena(Vector3 worldPoint, float margin)
        {
            ArenaBounds bounds = ActiveBounds;
            return bounds == null || bounds.Contains(worldPoint, margin);
        }

        // Reused every call: a portal's path check runs on the caster's own client, one at a time, on the main thread.
        // Sized generously (review fix): SphereCastNonAlloc's hits are not sorted or prioritised by relevance, so a
        // full buffer over a placement range this small (up to Placement Range + a player's width) risks dropping the
        // one boundary-wall hit that mattered behind unrelated clutter (crates, houses, cover) rather than a farther,
        // less important one.
        private const int BoundaryPathHitCapacity = 32;
        private static readonly RaycastHit[] boundaryPathHits = new RaycastHit[BoundaryPathHitCapacity];

        /// <summary>
        /// True when a sphere of <paramref name="radius"/> swept from <paramref name="from"/> to <paramref name="to"/>
        /// would touch a boundary wall in any of the three thirds (movement step 3, controller decision R1: a
        /// portal's path is blocked by the arena's own boundary walls only - a crate, a house or deployable cover
        /// stays exactly as placeable behind as it is today, since none of those ever reach this check). Checks the
        /// Building layer, then keeps only hits under a Boundry child of Source or a generated third. With no arena
        /// known, nothing can be crossed.
        /// </summary>
        public static bool PathCrossesBoundary(Vector3 from, Vector3 to, float radius)
        {
            ArenaSymmetry arena = Active;
            if (arena == null)
                return false;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return false;

            int mask = LayerMask.GetMask("Building");
            int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, boundaryPathHits, distance, mask,
                QueryTriggerInteraction.Ignore);
            if (count >= BoundaryPathHitCapacity)
                Debug.LogError($"[Arena] PathCrossesBoundary: {count} hits filled the buffer - some may have been " +
                                "dropped. Raise BoundaryPathHitCapacity if this fires for real placements.");
            for (int i = 0; i < count; i++)
                if (IsUnderBoundary(arena, boundaryPathHits[i].collider))
                    return true;

            return false;
        }

        private static bool IsUnderBoundary(ArenaSymmetry arena, Collider collider)
        {
            if (collider == null)
                return false;

            Transform t = collider.transform;
            return IsUnderGroup(t, arena.source) || IsUnderGroup(t, arena.generated120) || IsUnderGroup(t, arena.generated240);
        }

        private static bool IsUnderGroup(Transform t, Transform third)
        {
            Transform group = third != null ? third.Find(BoundaryGroupName) : null;
            return group != null && t.IsChildOf(group);
        }

        private void OnEnable()
        {
            // Play Mode only: without ExecuteInEditMode, the Editor never runs this, and the tool reads the fields
            // directly anyway.
            Bounds = ArenaBounds.FromSourceOutline(sourceOutline, centre);
            if (Bounds == null)
                Debug.LogError($"[Arena] {name}: Source Outline has fewer than two points - blink, portals and the " +
                                "out-of-arena safety net cannot tell inside from outside.");
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        private void OnDrawGizmosSelected()
        {
            // The three dividing lines, so a designer can see where the Source third ends.
            Gizmos.color = Color.yellow;
            for (int i = 0; i < 3; i++)
            {
                float radians = (sourceStartDegrees + RadialSymmetry.ThirdDegrees * i) * Mathf.Deg2Rad;
                Gizmos.DrawLine(centre, centre + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * 80f);
            }
            Gizmos.DrawWireSphere(centre, 1f);

            // The outline, all three thirds, so a designer can see it lying on the walls' inner faces.
            ArenaBounds outline = ArenaBounds.FromSourceOutline(sourceOutline, centre);
            if (outline == null)
                return;
            Gizmos.color = Color.cyan;
            IReadOnlyList<Vector2> points = outline.Polygon;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                Gizmos.DrawLine(new Vector3(a.x, centre.y + 0.2f, a.y), new Vector3(b.x, centre.y + 0.2f, b.y));
            }
        }
    }
}
