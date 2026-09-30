using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A flat ring on the floor that flashes where something blew up and fades away (ability visuals step 2): a mine's
    /// blast (A6, Tudor 2026-09-17 evening: a rocket's own blast no longer uses this - see SplashShell instead, since
    /// rockets stay at their real, off-the-floor height now). Local and cosmetic - each client spawns its own from the
    /// blast it already simulates, no network traffic - and it never touches damage. The caller passes the radius from
    /// the gameplay component itself.
    /// </summary>
    public sealed class BlastMarker : MonoBehaviour
    {
        [SerializeField, Tooltip("The filled disc (a unit-diameter cylinder squashed flat); scaled sideways to the blast.")]
        private Transform disc;

        [SerializeField, Tooltip("The flat outline at the blast's edge.")]
        private LineRenderer rim;

        [SerializeField, Tooltip("Seconds the marker takes to fade out and remove itself. Long enough to read, short " +
                 "enough not to clutter a fight.")]
        private float fadeSeconds = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the filled disc at the moment of the blast.")]
        private float discOpacity = 0.35f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the outline at the moment of the blast.")]
        private float rimOpacity = 0.9f;

        [SerializeField, Tooltip("Metres above the floor, so the flat parts don't flicker into the ground.")]
        private float heightAboveGround = 0.04f;

        [SerializeField, Tooltip("Points on the outline circle. 48 reads as round at the game camera's distance.")]
        private int rimSegments = 48;

        private Renderer discRenderer;
        private MaterialPropertyBlock block;
        private Color color = Color.white;
        private float age;

        /// <summary>The radius this marker was drawn at, in metres (read by the capture checks).</summary>
        public float Radius { get; private set; }

        /// <summary>Spawns a marker centred on <paramref name="groundPoint"/>, which is already on the floor. Nothing
        /// when there is no prefab or the radius isn't positive.</summary>
        public static BlastMarker Spawn(BlastMarker prefab, Vector3 groundPoint, float radius, Color color)
        {
            if (prefab == null || radius <= 0f)
                return null;
            BlastMarker marker = Instantiate(prefab, groundPoint, Quaternion.identity);
            marker.Show(radius, color);
            return marker;
        }

        private void Show(float radius, Color tint)
        {
            Radius = radius;
            color = tint;
            transform.position += Vector3.up * heightAboveGround;
            block = new MaterialPropertyBlock();
            if (disc != null)
            {
                disc.localScale = new Vector3(radius * 2f, disc.localScale.y, radius * 2f);
                discRenderer = disc.GetComponent<Renderer>();
            }
            VisualTint.FillFlatCircle(rim, radius, rimSegments);
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
            VisualTint.SetMeshColor(discRenderer, block, VisualTint.WithAlpha(color, discOpacity * fade));
            VisualTint.SetLineColor(rim, VisualTint.WithAlpha(color, rimOpacity * fade));
        }
    }
}
