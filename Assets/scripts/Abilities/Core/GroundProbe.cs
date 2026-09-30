using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Shared "is there real ground here" check for any ability that resolves a destination from a
    /// flat cursor point - Blink's landing spot and Teleport's portal placement both delegate here
    /// rather than keeping their own copy of the same physics. Extracted from BlinkAbility (Task
    /// 1.7a code review): a portal placed with no ground check at all could sit over a void, off the
    /// map edge, or buried/floating on a slope, because PlayerAim.GroundPointUnderCursor is only a
    /// flat math plane at the player's own Y - it knows nothing about what is actually beneath it.
    ///
    /// Ground is looked for in the vertical band [refHeight + maxStepUp .. refHeight -
    /// groundProbeDistance] - refHeight is normally the CASTER'S OWN current height, not the
    /// candidate's (unknown) height, which is exactly what this probe exists to find. Starting the
    /// ray only maxStepUp (a curb, not a ceiling) above refHeight, rather than groundProbeDistance
    /// above it, is deliberate: a house's roof is Building layer like everything else, and starting
    /// the ray at ceiling height would hit the ROOF'S TOP first, reading it as ground under an
    /// overhang instead of the floor beneath it. This probe stays at roughly the caster's own level;
    /// it is not a way onto a rooftop.
    ///
    /// RaycastAll, not Raycast, plus excludeRoot: a candidate close to the caster can put the ray's
    /// span through the caster's OWN capsule, which sits on the same layers as ordinary ground - a
    /// single Raycast would happily report that as "ground" and land the caster on their own head
    /// instead of refusing.
    /// </summary>
    public static class GroundProbe
    {
        /// <summary>
        /// True and the ground point (world space, real height) if solid ground exists at
        /// candidateXZ within reach and above killHeight. False - nothing valid, off the map edge,
        /// over a void, or below the kill plane - with groundPoint left at default.
        /// </summary>
        public static bool TryFindGround(float refHeight, Vector3 candidateXZ, float maxStepUp,
            float groundProbeDistance, float killHeight, int mask, Transform excludeRoot,
            out Vector3 groundPoint)
        {
            groundPoint = default;

            Vector3 rayOrigin = new Vector3(candidateXZ.x, refHeight + maxStepUp, candidateXZ.z);
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down,
                maxStepUp + groundProbeDistance, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (excludeRoot != null && hit.collider.transform.IsChildOf(excludeRoot))
                    continue; // never treat the caster's own body as ground.

                // The first non-self hit wins - if IT sits below the kill plane, refuse outright
                // rather than searching further for a higher one, matching the original check this
                // was extracted from.
                if (hit.point.y <= killHeight)
                    return false;

                groundPoint = hit.point;
                return true;
            }

            return false; // no ground within reach at all - off the map edge or over a void.
        }
    }
}
