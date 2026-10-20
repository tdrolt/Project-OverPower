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
    /// The Sonic Pulse's see-through electric-blue bands: they sweep from where it went off over exactly the cone it hits (the ability's
    /// live range and angle, handed in by ExecuteCast on every client, no RPC), then fade. The bands are one unparented object so they stay
    /// where the pulse went off while the caster is thrown back; the fog gate follows the caster, so a pulse from the fog draws nothing.
    /// Each band carries a paler core line, so the bands also read by brightness over the grey floor.
    /// Only line colours and point counts change per frame (VisibleWhenSeen owns Renderer.enabled).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SonicPulseView : MonoBehaviour
    {
        public const string BandsName = "Sonic Pulse Bands";
        public const string CoreName = "Core";
        private const int ArcPoints = 17; // keeps the drawn arc within a few centimetres of the real one at 5 m; not a look number

        [SerializeField, Tooltip("Where the bands' colour (the Sonic Pulse row of the look table), material and timing come from: " +
                 "Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        private AbilityModule module;
        private GameObject bands;
        private LineRenderer[] lines;
        private LineRenderer[] cores;
        private ArenaLook look;
        private float range;
        private float fullAngle;
        private Color colour;
        private float playedAt = -1f;
        private static bool warnedMissingLook;

        public GameObject Bands => bands;

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

            if (bands == null)
                Build();
            float floor = GroundSnap.TryFindGroundY(origin, out float groundY) ? groundY : origin.y;
            bands.transform.SetPositionAndRotation(new Vector3(origin.x, floor + look.pulseBandHeight, origin.z), Quaternion.LookRotation(forward));
            this.range = range;
            fullAngle = fullAngleDegrees;
            if (module == null)
                module = GetComponent<AbilityModule>();
            int abilityId = module != null && module.Definition != null ? module.Definition.Id : -1;
            colour = LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.ElectricBlue);
            playedAt = Time.time;
            Draw(0f);
            VisibleWhenSeen.AttachToCaster(bands, caster);
            bands.SetActive(true);
        }

        // Built active with empty lines (nothing drawn): the gate added in Play then wakes as it attaches, not later when the bands
        // are switched on, which would switch every band back on from a fogged caster.
        private void Build()
        {
            bands = new GameObject(BandsName);
            lines = new LineRenderer[look.pulseBandCount];
            cores = new LineRenderer[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var band = new GameObject("Band " + i);
                band.transform.SetParent(bands.transform, false);
                lines[i] = AddLine(band, look.pulseBandWidth, 0);
                var core = new GameObject(CoreName);
                core.transform.SetParent(band.transform, false);
                cores[i] = AddLine(core, look.pulseBandWidth * look.pulseCoreWidth, 1);
            }
        }

        private LineRenderer AddLine(GameObject owner, float width, int sortingOrder)
        {
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = look.fxAlpha;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = width;
            line.sortingOrder = sortingOrder; // the core over its band, whichever is nearer the camera
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private void Update()
        {
            if (playedAt < 0f || bands == null)
                return;
            float age = Time.time - playedAt;
            if (age > look.pulseSweepSeconds + look.pulseFadeSeconds)
            {
                bands.SetActive(false);
                playedAt = -1f;
                return;
            }
            Draw(age);
        }

        /// <summary>The bands as they stand <paramref name="secondsSinceCast"/> after the cast: the leading band strongest, the last one at
        /// Pulse Trail Alpha of it, all fading together once the sweep is done.</summary>
        public void Draw(float secondsSinceCast)
        {
            if (lines == null)
                return;
            float fade = secondsSinceCast <= look.pulseSweepSeconds ? 1f
                : AbilityVisualGeometry.Fade01(secondsSinceCast - look.pulseSweepSeconds, look.pulseFadeSeconds);
            Color pale = Color.Lerp(colour, Color.white, look.pulseCoreWhiteness);
            for (int i = 0; i < lines.Length; i++)
            {
                LineRenderer line = lines[i], core = cores[i];
                float radius = AbilityVisualGeometry.ArcBandRadius(i, secondsSinceCast, look.pulseSweepSeconds, range, look.pulseBandSpacing);
                if (radius <= 0.01f)
                {
                    line.positionCount = 0;
                    core.positionCount = 0;
                    continue;
                }
                line.positionCount = ArcPoints;
                core.positionCount = ArcPoints;
                for (int k = 0; k < ArcPoints; k++)
                {
                    Vector3 point = AbilityVisualGeometry.ArcPoint(radius, fullAngle, k, ArcPoints);
                    line.SetPosition(k, point);
                    core.SetPosition(k, point);
                }
                float trail = lines.Length > 1 ? i / (float)(lines.Length - 1) : 0f;
                float strength = look.pulseBandAlpha * fade * Mathf.Lerp(1f, look.pulseTrailAlpha, trail);
                VisualTint.SetLineColor(line, VisualTint.WithAlpha(colour, strength));
                VisualTint.SetLineColor(core, VisualTint.WithAlpha(pale, strength));
            }
        }

        private void OnDestroy()
        {
            if (bands != null)
                Destroy(bands);
        }
    }
}
