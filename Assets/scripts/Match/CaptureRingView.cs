using Overpower.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Match
{
    /// <summary>
    /// The ring on the ground that marks a capture zone and shows a capture or drain filling (Tudor, 2026-09-16: it
    /// replaces the small bar that floated above each tower). BuildingCapture.Start builds one per tower, so a new
    /// tower gets one with nothing to wire up.
    ///
    /// Two flat lines:
    /// - a thin EDGE, always shown, on the Capture Radius: team colour when owned, dim white when neutral;
    /// - a thicker BAND just inside it that fills clockwise from the top of this player's screen.
    /// Which colours, fill, pulse and blink to use is decided by the pure CaptureRingState; this class only draws it.
    ///
    /// Line renderers, not a mesh: the same unlit, double-sided, vertex-colour material the aim cone lines use, tinted
    /// per ring through each line's own colour (no material copies). No collider, no shadows. The band's points are
    /// rewritten only when its length (in whole pieces) or the camera's yaw changes; pulses and blinks only change a
    /// colour.
    /// </summary>
    public sealed class CaptureRingView : MonoBehaviour
    {
        private UiTheme theme;
        private LineRenderer edge;
        private LineRenderer band;
        private Vector3 centre;
        private float bandRadius;
        private int segments;
        private Vector3[] bandPoints;

        // What is currently drawn, so an unchanged frame touches nothing.
        private int shownBandPointCount = -1;
        private float shownBandStartYaw = float.NaN;
        private Color shownEdgeColor;
        private Color shownBandColor;
        private bool edgeColorSet;
        private bool bandColorSet;

        public static CaptureRingView Create(Transform tower, float captureRadius, UiTheme theme)
        {
            var root = new GameObject("Capture Ring");
            // Parented to the tower so it's easy to find and goes with it, but every point is in world space: the
            // tower's own scale (0.8 on these towers) must not shrink the ring away from the real Capture Radius.
            root.transform.SetParent(tower, false);

            var view = root.AddComponent<CaptureRingView>();
            view.theme = theme;
            view.segments = Mathf.Max(16, theme.captureRingSegments);
            Vector3 towerPosition = tower.position;
            view.centre = new Vector3(towerPosition.x, GroundHeight(towerPosition, captureRadius) + theme.captureRingHeightOffset,
                                      towerPosition.z);

            float edgeRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth / 2f);
            view.bandRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth - theme.captureRingArcGap
                                                - theme.captureRingArcWidth / 2f);

            view.edge = CreateLine(root.transform, "Edge", theme.captureRingMaterial, theme.captureRingOutlineWidth);
            view.edge.loop = true;
            view.edge.positionCount = view.segments;
            var edgePoints = new Vector3[view.segments];
            float step = CaptureRingGeometry.ArcStepDegrees(view.segments);
            for (int i = 0; i < view.segments; i++)
                edgePoints[i] = CaptureRingGeometry.PointOnRing(view.centre, edgeRadius, 0f, step * i);
            view.edge.SetPositions(edgePoints);

            view.band = CreateLine(root.transform, "Progress Band", theme.captureRingMaterial, theme.captureRingArcWidth);
            view.band.loop = false;
            view.band.positionCount = 0;
            view.band.enabled = false;
            view.bandPoints = new Vector3[view.segments + 1];
            return view;
        }

        /// <summary>Called every frame by BuildingCapture.Update on every client.</summary>
        public void Refresh(CaptureRingState state, float cameraYawDegrees)
        {
            float time = Time.unscaledTime; // presentation only: a paused Time.timeScale must not freeze a pulse

            Color edgeColor = state.OutlineTeam >= 0 ? theme.ShotColorFor(state.OutlineTeam) : theme.captureRingNeutralColor;
            if (state.Phase == CaptureRingPhase.Draining && state.DrainerTeam >= 0)
                edgeColor = Color.Lerp(edgeColor, theme.ShotColorFor(state.DrainerTeam),
                                       CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed));
            else if (state.UnderAttack)
                edgeColor = Color.Lerp(edgeColor, theme.captureRingWarningColor,
                                       CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed));
            if (!edgeColorSet || edgeColor != shownEdgeColor)
            {
                edge.startColor = edgeColor;
                edge.endColor = edgeColor;
                shownEdgeColor = edgeColor;
                edgeColorSet = true;
            }

            if (!state.ShowsArc)
            {
                if (band.enabled)
                    band.enabled = false;
                return;
            }

            int count = CaptureRingGeometry.ArcPointCount(state.Fill01, segments);
            if (count != shownBandPointCount || !Mathf.Approximately(cameraYawDegrees, shownBandStartYaw))
            {
                float step = CaptureRingGeometry.ArcStepDegrees(segments);
                for (int i = 0; i < count; i++)
                    bandPoints[i] = CaptureRingGeometry.PointOnRing(centre, bandRadius, cameraYawDegrees, step * i);
                band.positionCount = count;
                band.SetPositions(bandPoints); // uses only the first positionCount points
                shownBandPointCount = count;
                shownBandStartYaw = cameraYawDegrees;
            }

            Color bandColor = theme.ShotColorFor(state.ArcTeam);
            if (state.Phase == CaptureRingPhase.Paused)
                bandColor.a *= theme.captureRingPausedOpacity * CaptureRingGeometry.Blink01(time, theme.captureRingPausedBlinkSpeed);
            if (!bandColorSet || bandColor != shownBandColor)
            {
                band.startColor = bandColor;
                band.endColor = bandColor;
                shownBandColor = bandColor;
                bandColorSet = true;
            }

            if (!band.enabled)
                band.enabled = true;
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // TransformZ alignment draws a line facing this object's Z axis. Pointing Z straight up lays the line flat
            // on the ground, instead of turning it toward the camera like a normal line. The material is double-sided.
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; // sharedMaterial: .material would clone it per line
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.widthMultiplier = 1f;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.allowOcclusionWhenDynamic = false;
            return line;
        }

        /// <summary>The highest terrain point under the zone centre and eight points on its edge, so a ring on a slope
        /// never dips into the ground. The tower's own height where no terrain covers the zone. Runs once per tower.</summary>
        private static float GroundHeight(Vector3 towerPosition, float radius)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            float best = float.NegativeInfinity;
            for (int i = 0; i < 9; i++)
            {
                Vector3 point = i == 0 ? towerPosition : CaptureRingGeometry.PointOnRing(towerPosition, radius, 0f, 45f * (i - 1));
                foreach (Terrain terrain in terrains)
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;
                    if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z)
                        continue;
                    best = Mathf.Max(best, origin.y + terrain.SampleHeight(point));
                }
            }
            return float.IsNegativeInfinity(best) ? towerPosition.y : best;
        }
    }
}
