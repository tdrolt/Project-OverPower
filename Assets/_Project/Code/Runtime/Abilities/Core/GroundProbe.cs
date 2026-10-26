using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Shared "is there real ground here" check for any ability that resolves a destination from a flat cursor
    /// point (Blink's landing, Teleport's portal placement): PlayerAim.GroundPointUnderCursor is only a flat plane at
    /// the player's own Y and knows nothing about voids, map edges or slopes beneath it.
    /// Ground is searched in [refHeight + maxStepUp .. refHeight - groundProbeDistance], refHeight normally the
    /// CASTER'S height. The ray starts only maxStepUp (a curb, not a ceiling) up on purpose: a roof is Building layer
    /// like everything else, so a higher start would read the ROOF'S TOP as ground; this is not a way onto a rooftop.
    /// RaycastAll plus excludeRoot: a candidate near the caster can put the ray through the caster's OWN capsule,
    /// which a single Raycast would report as ground, landing the caster on their own head.
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
