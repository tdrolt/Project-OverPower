using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The floor under a point, for visuals that must lie on it (ability visuals step 2). Mines and the electric fence
    /// are placed at the caster's ROOT, 0.5 m above their feet, and a rocket blows up at muzzle height - so their flat
    /// visuals used to float. Visual only: nothing here moves a gameplay object or where a hit is measured from.
    ///
    /// Looks past anything with health (a player, a dummy, a cover wall), so the caster standing on the spot is never
    /// taken for the floor. Floor = Default (the terrain) or Building (floors, roofs): the same layers GroundProbe uses.
    /// </summary>
    public static class GroundSnap
    {
        // Not design tunables: the probe starts a curb's height above the point (never a roof) and looks down far enough
        // for a rocket blast about 2 m up.
        public const float ProbeUp = 0.25f;
        public const float ProbeDown = 4f;

        private const int MaxHits = 8;
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        private static int groundMask;

        private static int GroundMask
        {
            get
            {
                if (groundMask == 0)
                    groundMask = LayerMask.GetMask("Default", "Building");
                return groundMask;
            }
        }

        /// <summary>The floor height under <paramref name="from"/> in the game's own physics scene.</summary>
        public static bool TryFindGroundY(Vector3 from, out float groundY) =>
            TryFindGroundY(Physics.defaultPhysicsScene, from, ProbeUp, ProbeDown, GroundMask, out groundY);

        /// <summary>The nearest surface without health straight below <paramref name="from"/>, from probeUp above it to
        /// probeDown below it. False, with groundY = from.y, when there is none (a void, the map edge).</summary>
        public static bool TryFindGroundY(PhysicsScene physics, Vector3 from, float probeUp, float probeDown, int mask, out float groundY)
        {
            groundY = from.y;
            int count = physics.Raycast(from + Vector3.up * probeUp, Vector3.down, Hits, probeUp + probeDown, mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Hits[i].collider;
                if (collider == null || collider.GetComponentInParent<IDamageable>() != null)
                    continue;
                if (Hits[i].distance < nearest)
                {
                    nearest = Hits[i].distance;
                    groundY = Hits[i].point.y;
                    found = true;
                }
            }
            return found;
        }
    }
}
