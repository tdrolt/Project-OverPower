using UnityEngine;
using UnityEngine.Rendering;
using Overpower.UI;

namespace Overpower.Weapons
{
    /// <summary>
    /// The wind-up telegraph for a laser shot: a LineRenderer from the muzzle to wherever Hitscan.PredictBeamLength says the beam will stop, in the
    /// shooter's team colour, thickening and brightening over the wind-up so a player has time to step out of the line before the real beam lands
    /// (design [T]).
    /// Created by WeaponFiring.ShowWarnings for every shot of a laser with a wind-up, and destroyed by FireAfterWindup the moment the real beam
    /// replaces it. PRESENTATION ONLY: never touches damage, physics or network state. The beam pierces every player in its path (every laser has
    /// unlimited Pierce), so the line never shortens because someone is standing in it - only PredictBeamLength's wall/structure check does.
    /// </summary>
    [DisallowMultipleComponent]
    public class LaserWarningLine : MonoBehaviour
    {
        private LineRenderer line;
        private UiTheme theme;
        private Color baseColor;
        private float duration;
        private float startTime;

        // Fallbacks for a wind-up shown with no UiTheme assigned (WeaponFiring logs this once - see
        // its own ResolveWarningColor) - keeps the line visible instead of invisible or default-pink.
        private const float FallbackStartWidth = 0.03f;
        private const float FallbackEndWidth = 0.14f;
        private const float FallbackStartAlpha = 0.15f;
        private const float FallbackEndAlpha = 0.9f;

        /// <summary>
        /// Builds and configures a warning line in one call - the only way this component is ever
        /// created. A plain LineRenderer needs its positions, material and starting colour set before
        /// the first frame renders, so AddComponent alone would show one garbage frame first.
        /// </summary>
        /// <param name="direction">The shot's locked direction - already normalised, or close enough that LineRenderer does not care.</param>
        /// <param name="length">How far along that direction the beam will visibly stop - from Hitscan.PredictBeamLength.</param>
        /// <param name="windupSeconds">How long the line takes to grow from its Start to its End width/alpha - the same Windup Seconds the beam waits out.</param>
        /// <param name="theme">Where the width/alpha numbers and the shared material come from. May be null - the line still draws, using this class's
        /// own fallback numbers.</param>
        public static LaserWarningLine Create(Vector3 origin, Vector3 direction, float length,
                                              Color teamColor, float windupSeconds, UiTheme theme)
        {
            var go = new GameObject("Laser Warning Line");
            var warning = go.AddComponent<LaserWarningLine>();
            warning.Initialize(origin, direction, length, teamColor, windupSeconds, theme);
            return warning;
        }

        private void Initialize(Vector3 origin, Vector3 direction, float length, Color teamColor,
                                float windupSeconds, UiTheme uiTheme)
        {
            theme = uiTheme;
            baseColor = teamColor;
            duration = Mathf.Max(0.0001f, windupSeconds);
            startTime = Time.time;

            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, origin);
            line.SetPosition(1, origin + direction.normalized * length);
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;

            // sharedMaterial, not material - see AimConeView/ShotTeamVisuals's own comments on the
            // same line: one asset serves every warning line on screen at once, never a clone per shot.
            line.sharedMaterial = theme != null ? theme.laserWarningMaterial : null;

            // A purely informational overlay every player sees, but still never worth a shadow, a
            // light probe or a reflection probe for what is a flat, unlit, half-second line.
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.allowOcclusionWhenDynamic = false;

            ApplyVisual(0f);
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.time - startTime) / duration);
            ApplyVisual(t);
        }

        private void ApplyVisual(float t)
        {
            float startWidth = theme != null ? theme.laserWarningStartWidth : FallbackStartWidth;
            float endWidth = theme != null ? theme.laserWarningEndWidth : FallbackEndWidth;
            float startAlpha = theme != null ? theme.laserWarningStartAlpha : FallbackStartAlpha;
            float endAlpha = theme != null ? theme.laserWarningEndAlpha : FallbackEndAlpha;

            float width = Mathf.Lerp(startWidth, endWidth, t);
            line.startWidth = width;
            line.endWidth = width;

            Color drawn = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(startAlpha, endAlpha, t));
            line.startColor = drawn;
            line.endColor = drawn;
        }
    }
}
