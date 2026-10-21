using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Sonic Pulse's see-through electric-blue arch: it stands upright on the floor with its feet on the edges of the cone the pulse
    /// really hits (the ability's live range and angle, handed in by ExecuteCast on every client, no RPC), travels from the caster to the
    /// end of the reach, fading as it arrives, with fainter after-images behind it. One unparented object, so it stays where the pulse went
    /// off while the caster is thrown back; the fog gate follows the caster, so a pulse from the fog draws nothing.
    /// Each arch is a tube with a paler core line (it reads by brightness over the grey floor) and a slightly wider, fainter edge line under it.
    /// Only line colours and points change per frame (VisibleWhenSeen owns Renderer.enabled).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SonicPulseView : MonoBehaviour
    {
        public const string ArchName = "Sonic Pulse Arch";
        public const string CoreName = "Core";
        public const string EdgeName = "Edge";
        private const int ArchPoints = 25; // keeps the drawn curve within a couple of centimetres of the rule's at 5 m; not a look number

        [SerializeField, Tooltip("Where the arch's colour (the Sonic Pulse row of the look table), material and timing come from: " +
                 "Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        private AbilityModule module;
        private GameObject arch;
        private LineRenderer[] bodies;
        private LineRenderer[] cores;
        private LineRenderer[] edges;
        private ArenaLook look;
        private float range;
        private float fullAngle;
        private Color colour;
        // The edge's own colour slot: the arch's blue unless a team accent is given to it.
        private Color edgeColour;
        private float playedAt = -1f;
        private static bool warnedMissingLook;

        public GameObject Arch => arch;

        public void Play(Vector3 origin, Vector3 forward, float range, float fullAngleDegrees, int casterTeam, PhotonView caster)
        {
            look = theme != null ? theme.arenaLook : null;
            if (look == null || look.fxAlpha == null)
            {
                if (!warnedMissingLook)
                {
                    warnedMissingLook = true;
                    Debug.LogWarning($"[SonicPulseView] {name}: no UiTheme Arena Look with FX Alpha - the Sonic Pulse draws nothing.");
                }
                return;
            }
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            if (arch == null)
                Build();
            float floor = GroundSnap.TryFindGroundY(origin, out float groundY) ? groundY : origin.y;
            arch.transform.SetPositionAndRotation(new Vector3(origin.x, floor, origin.z), Quaternion.LookRotation(forward));
            this.range = range;
            fullAngle = fullAngleDegrees;
            if (module == null)
                module = GetComponent<AbilityModule>();
            int abilityId = module != null && module.Definition != null ? module.Definition.Id : -1;
            colour = LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.ElectricBlue);
            edgeColour = colour;
            playedAt = Time.time;
            Draw(0f);
            VisibleWhenSeen.AttachToCaster(arch, caster);
            arch.SetActive(true);
        }

        // Built active with empty lines (nothing drawn): the gate added in Play then wakes as it attaches, not later when the arch is
        // switched on, which would switch every line back on from a fogged caster.
        private void Build()
        {
            arch = new GameObject(ArchName);
            bodies = new LineRenderer[look.pulseArchCount];
            cores = new LineRenderer[bodies.Length];
            edges = new LineRenderer[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = new GameObject("Arch " + i);
                body.transform.SetParent(arch.transform, false);
                bodies[i] = AddLine(body, look.pulseArchWidth, 0);
                cores[i] = AddLine(Child(body, CoreName), look.pulseArchWidth * look.pulseCoreWidth, 1);
                edges[i] = AddLine(Child(body, EdgeName), look.pulseArchWidth + 2f * look.pulseEdgeWidth, -1);
            }
        }

        private static GameObject Child(GameObject parent, string childName)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private LineRenderer AddLine(GameObject owner, float width, int sortingOrder)
        {
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = look.fxAlpha;
            line.alignment = LineAlignment.View; // a ribbon facing the camera reads as a round tube from any angle
            line.widthMultiplier = width;
            line.sortingOrder = sortingOrder; // edge under the tube under the core, whichever is nearer the camera
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private void Update()
        {
            if (playedAt < 0f || arch == null)
                return;
            float age = Time.time - playedAt;
            if (age > look.pulseSweepSeconds + look.pulseFadeSeconds)
            {
                arch.SetActive(false);
                playedAt = -1f;
                return;
            }
            Draw(age);
        }

        /// <summary>The arch and its after-images as they stand <paramref name="secondsSinceCast"/> after the cast: the arch strongest, the
        /// last after-image at Pulse Trail Alpha of it, all fading together as they reach the end of the pulse's reach.</summary>
        public void Draw(float secondsSinceCast)
        {
            if (bodies == null)
                return;
            float fade = AbilityVisualGeometry.ArchStrength(secondsSinceCast, look.pulseSweepSeconds, look.pulseFadeSeconds, look.pulseArchEndStrength);
            Color pale = Color.Lerp(colour, Color.white, look.pulseCoreWhiteness);
            for (int i = 0; i < bodies.Length; i++)
            {
                LineRenderer body = bodies[i], core = cores[i], edge = edges[i];
                float radius = AbilityVisualGeometry.ArcBandRadius(i, secondsSinceCast, look.pulseSweepSeconds, range, look.pulseArchSpacing);
                if (radius <= 0.01f)
                {
                    body.positionCount = 0;
                    core.positionCount = 0;
                    edge.positionCount = 0;
                    continue;
                }
                float height = AbilityVisualGeometry.ArchHeight(radius, fullAngle, look.pulseArchHeight, look.pulseArchMaxHeight);
                body.positionCount = ArchPoints;
                core.positionCount = ArchPoints;
                edge.positionCount = ArchPoints;
                for (int k = 0; k < ArchPoints; k++)
                {
                    Vector3 point = AbilityVisualGeometry.ArchPoint(radius, fullAngle, height, look.pulseArchBend, k, ArchPoints);
                    body.SetPosition(k, point);
                    core.SetPosition(k, point);
                    edge.SetPosition(k, point);
                }
                float trail = bodies.Length > 1 ? i / (float)(bodies.Length - 1) : 0f;
                float strength = look.pulseArchAlpha * fade * Mathf.Lerp(1f, look.pulseTrailAlpha, trail);
                VisualTint.SetLineColor(body, VisualTint.WithAlpha(colour, strength));
                VisualTint.SetLineColor(core, VisualTint.WithAlpha(pale, strength));
                VisualTint.SetLineColor(edge, VisualTint.WithAlpha(edgeColour, strength * look.pulseEdgeStrength));
            }
        }

        private void OnDestroy()
        {
            if (arch == null)
                return;
            if (Application.isPlaying) Destroy(arch);
            else DestroyImmediate(arch);
            arch = null;
        }
    }
}
