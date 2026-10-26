using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Raybeam's wind-up on every client: a flat red hexagon (the Raybeam's row of the look table) with a thin edge in the caster's team
    /// colour, at the caster's gun and turning with it, shrinking over the wind-up until the beams fire or it is cancelled. The lines live on a
    /// child this view switches on and off; the view's own object stays active, and its fog gate (following the caster, enabled only while
    /// charging) only ever touches Renderer.enabled. The gate collects renderers once, so both lines are built before it attaches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaybeamChargeView : MonoBehaviour
    {
        public const string HexChildName = "Hex";
        private const int HexSides = 6;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock block;

        private UiTheme theme;
        private ArenaLook look;
        private int abilityId;
        private PhotonView caster;
        private WeaponFiring weapon;
        private Transform body;
        private GameObject hex;
        private LineRenderer band;
        private LineRenderer edge;
        private VisibleWhenSeen gate;
        private float startTime;
        private float windupSeconds;

        public bool Running { get; private set; }

        /// <summary>Builds the hidden hexagon for one caster, or returns null (with a warning) when the theme has no Arena Look with FX Alpha.
        /// <paramref name="weapon"/> gives the gun's position; without one the hexagon sits at <paramref name="body"/>.</summary>
        public static RaybeamChargeView TryCreate(UiTheme theme, int abilityId, PhotonView caster, WeaponFiring weapon, Transform body)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (look == null || look.fxAlpha == null)
            {
                Debug.LogWarning("[RaybeamChargeView] no UiTheme Arena Look with FX Alpha - the Raybeam's wind-up shows no hexagon.");
                return null;
            }

            var root = new GameObject("Raybeam Charge");
            var hex = new GameObject(HexChildName);
            hex.transform.SetParent(root.transform, false);
            hex.SetActive(false);
            LineRenderer band = NewLine(hex.transform, "Band", look);
            LineRenderer edge = NewLine(hex.transform, "Edge", look);

            RaybeamChargeView view = root.AddComponent<RaybeamChargeView>();
            view.theme = theme;
            view.look = look;
            view.abilityId = abilityId;
            view.caster = caster;
            view.weapon = weapon;
            view.body = body;
            view.hex = hex;
            view.band = band;
            view.edge = edge;
            view.gate = VisibleWhenSeen.AttachToCaster(root, caster);
            if (view.gate != null)
                view.gate.enabled = false; // asks the sight only while a wind-up shows
            return view;
        }

        /// <summary>Starts the shrink from <paramref name="pressedAt"/> (this client's clock, already moved back by the message's lag).</summary>
        public void Begin(float pressedAt, float windupSeconds, int casterTeam)
        {
            startTime = pressedAt;
            this.windupSeconds = windupSeconds;
            Color own = LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.Danger);
            Paint(band, VisualTint.WithAlpha(own, look.raybeamChargeAlpha), look.raybeamChargeWidth, look.raybeamChargeGlow);
            Paint(edge, VisualTint.WithAlpha(LookColours.Accent(casterTeam, theme, own), look.raybeamEdgeAlpha), look.raybeamEdgeWidth, look.raybeamEdgeGlow);
            // Null when this view was built before the local sight existed (a late join); asked again so the hexagon never shows ungated.
            if (gate == null)
                gate = VisibleWhenSeen.AttachToCaster(gameObject, caster);
            Running = true;
            Place(Time.time - startTime);
            hex.SetActive(true);
            if (gate != null)
            {
                gate.enabled = true;
                gate.Apply();
            }
        }

        public void End()
        {
            Running = false;
            if (hex != null)
                hex.SetActive(false);
            if (gate != null)
                gate.enabled = false;
        }

        private void LateUpdate()
        {
            if (!Running)
                return;
            float elapsed = Time.time - startTime;
            if (RaybeamWindup.ChargeShown(elapsed, windupSeconds))
                Place(elapsed);
            else
                End();
        }

        private void Place(float elapsed)
        {
            Vector3 at = weapon != null ? weapon.MuzzlePosition : body != null ? body.position : transform.position;
            Vector3 facing = body != null ? body.forward : Vector3.forward;
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            transform.SetPositionAndRotation(at, Quaternion.Euler(90f, yaw, 0f));
            float radius = RaybeamWindup.ChargeRadius(elapsed, windupSeconds, look.raybeamChargeStartRadius, look.raybeamChargeEndRadius);
            VisualTint.FillFlatCircle(band, radius, HexSides);
            VisualTint.FillFlatCircle(edge, RaybeamLook.HexEdgeRadius(radius, look.raybeamChargeWidth, look.raybeamEdgeWidth, look.raybeamEdgeGap), HexSides);
        }

        private static LineRenderer NewLine(Transform parent, string name, ArenaLook look)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = look.fxAlpha;
            line.alignment = LineAlignment.TransformZ; // with the view turned 90 degrees on X the band lies flat (VisualTint.FillFlatCircle)
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static void Paint(LineRenderer line, Color colour, float width, float glow)
        {
            line.widthMultiplier = width;
            VisualTint.SetLineColor(line, colour);
            if (block == null)
                block = new MaterialPropertyBlock();
            line.GetPropertyBlock(block);
            block.SetColor(BaseColorId, new Color(glow, glow, glow, 1f));
            line.SetPropertyBlock(block);
        }
    }
}
