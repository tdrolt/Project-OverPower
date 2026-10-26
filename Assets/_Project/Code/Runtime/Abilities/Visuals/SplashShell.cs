using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// What a rocket blast shows instead of a floor BlastMarker: a flat, hard-edged shockwave ring lying level at the real burst
    /// point - never snapped to the ground (no SnapVisualToGround, no GroundSnap read) - in the shooter's team colour. It snaps
    /// out from Start Scale of the blast's Splash Radius and spends most of its short life close to that radius (RingRadius), never
    /// past it, while it fades; then it removes itself. Local and cosmetic, the same contract BlastMarker has - each client spawns
    /// its own from the blast it already simulates, never touches damage.
    /// </summary>
    public sealed class SplashShell : MonoBehaviour
    {
        // Enough corners that the ring reads round at any blast size, few enough to rebuild every frame of its short life.
        private const int RingSegments = 64;

        [SerializeField, Tooltip("The flat ring line (a LineRenderer lying flat, drawn round the burst point); sized every frame to the blast's own Splash Radius.")]
        private LineRenderer ring;

        [SerializeField, Tooltip("Seconds the ring takes to snap out, fade and remove itself; keep it short so it reads as a blast, not a lingering zone.")]
        private float fadeSeconds = 0.3f;

        [SerializeField, Range(0f, 1f), Tooltip("Fraction of the full blast radius the ring starts at (it reaches the full " +
                 "radius by the time it has faded out, most of the way there by half its life).")]
        private float startScale = 0.6f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the ring at the moment of the blast; it fades to nothing over Fade Seconds.")]
        private float opacity = 0.85f;

        private Color color = Color.white;
        private float radius;
        private float width;
        private float age;

        /// <summary>The radius this ring was drawn for, in metres (read by the capture checks).</summary>
        public float Radius => radius;

        /// <summary>Spawns a ring centred on <paramref name="worldPoint"/> - the real burst point, at whatever height that is, <paramref name="width"/>
        /// metres thick. Nothing when there is no prefab or the radius isn't positive.</summary>
        public static SplashShell Spawn(SplashShell prefab, Vector3 worldPoint, float radius, Color color, float width)
        {
            if (prefab == null || radius <= 0f)
                return null;
            SplashShell shell = Instantiate(prefab, worldPoint, Quaternion.identity);
            shell.Show(radius, color, width);
            return shell;
        }

        /// <summary>Where the ring's line runs <paramref name="age"/> seconds into its life: its outer edge goes from Start Scale of the radius,
        /// easing out so it sits near the real radius for most of the fade, and exactly on it at the end - never beyond, so nobody outside
        /// the ring was in the blast. The line is <paramref name="width"/> thick and centred on this radius, so it runs half a width inside.</summary>
        public static float RingRadius(float radius, float age, float seconds, float startScale, float width)
        {
            float t = seconds > 0f ? Mathf.Clamp01(age / seconds) : 1f;
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            return Mathf.Max(0f, radius * Mathf.Lerp(startScale, 1f, eased) - width * 0.5f);
        }

        private void Show(float blastRadius, Color tint, float lineWidth)
        {
            radius = blastRadius;
            color = tint;
            if (ring != null && lineWidth > 0f)
            {
                ring.startWidth = lineWidth;
                ring.endWidth = lineWidth;
            }
            width = ring != null ? ring.startWidth : 0f;
            Apply(1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float fade = AbilityVisualGeometry.Fade01(age, fadeSeconds);
            Apply(fade);
            if (fade <= 0f)
                Destroy(gameObject);
        }

        private void Apply(float fade)
        {
            VisualTint.FillFlatCircle(ring, RingRadius(radius, age, fadeSeconds, startScale, width), RingSegments);
            VisualTint.SetLineColor(ring, VisualTint.WithAlpha(color, opacity * fade));
        }
    }
}
