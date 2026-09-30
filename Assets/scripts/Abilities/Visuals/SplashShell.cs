using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A6 (Tudor 2026-09-17 evening): what a rocket blast shows now instead of a floor BlastMarker. Rockets stay at
    /// their real height, so this is a short, see-through SPHERE shell at the real burst point - never snapped to the
    /// ground (no SnapVisualToGround, no GroundSnap read) - sized to the blast's own Splash Radius, in the shooter's
    /// team colour at low alpha. It grows from about 60% to 100% of that radius while it fades out, then removes
    /// itself: one pooled-cheap object per blast, no lingering ring. Local and cosmetic, the same contract BlastMarker
    /// has - each client spawns its own from the blast it already simulates, never touches damage.
    /// </summary>
    public sealed class SplashShell : MonoBehaviour
    {
        [SerializeField, Tooltip("The see-through sphere (unit diameter); scaled to the blast's own Splash Radius.")]
        private Transform sphere;

        [SerializeField, Tooltip("Seconds the shell takes to grow, fade out and remove itself - about 0.3 s per A6.")]
        private float fadeSeconds = 0.3f;

        [SerializeField, Range(0f, 1f), Tooltip("Fraction of the full blast radius the shell starts at (it grows to " +
                 "100% by the time it fades out).")]
        private float startScale = 0.6f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the shell at the moment of the blast - low, so it reads " +
                 "as a brief flash, not a wall of colour.")]
        private float opacity = 0.35f;

        private Renderer sphereRenderer;
        private MaterialPropertyBlock block;
        private Color color = Color.white;
        private float radius;
        private float age;

        /// <summary>The radius this shell was drawn at, in metres (read by the capture checks).</summary>
        public float Radius => radius;

        /// <summary>Spawns a shell centred on <paramref name="worldPoint"/> - the real burst point, at whatever height
        /// that is. Nothing when there is no prefab or the radius isn't positive.</summary>
        public static SplashShell Spawn(SplashShell prefab, Vector3 worldPoint, float radius, Color color)
        {
            if (prefab == null || radius <= 0f)
                return null;
            SplashShell shell = Instantiate(prefab, worldPoint, Quaternion.identity);
            shell.Show(radius, color);
            return shell;
        }

        private void Show(float blastRadius, Color tint)
        {
            radius = blastRadius;
            color = tint;
            block = new MaterialPropertyBlock();
            if (sphere != null)
                sphereRenderer = sphere.GetComponent<Renderer>();
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
            float grow = 1f - fade; // 0 at spawn, 1 once it has fully faded out
            float diameter = Mathf.Lerp(startScale, 1f, grow) * radius * 2f;
            if (sphere != null)
                sphere.localScale = new Vector3(diameter, diameter, diameter);
            VisualTint.SetMeshColor(sphereRenderer, block, VisualTint.WithAlpha(color, opacity * fade));
        }
    }
}
