using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The Shield's plates snapping up when it triggers: each plate moves rigidly from the point of the floor ring under it to its
    /// place on the shell, lowest plates first. All in the shell's own space (unit diameter, centre at zero).</summary>
    public static class ShieldShellRise
    {
        /// <summary>What to add to a plate's rest position at <paramref name="t01"/> of the rise (0 = on the ring, 1 and later = at rest).
        /// <paramref name="stagger"/> is the share of the rise by which the lowest plate leads the highest.</summary>
        public static Vector3 PlateOffset(Vector3 plateCentre, float t01, float ringRadius, float ringY, float stagger)
        {
            stagger = Mathf.Clamp(stagger, 0f, 0.95f);
            float height01 = plateCentre.sqrMagnitude > 0f ? (plateCentre.normalized.y + 1f) * 0.5f : 0.5f;
            float own = Mathf.Clamp01((t01 - stagger * height01) / (1f - stagger));
            float left = 1f - own;
            left = left * left * left; // fast out of the ring, settling into place: a snap, not a float

            Vector2 across = new Vector2(plateCentre.x, plateCentre.z);
            Vector2 onRing = across.sqrMagnitude > 1e-8f ? across.normalized * ringRadius : Vector2.zero;
            Vector3 start = new Vector3(onRing.x, ringY, onRing.y);
            return (start - plateCentre) * left;
        }
    }
}
