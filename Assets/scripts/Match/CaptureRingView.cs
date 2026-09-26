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
    /// Three flat lines:
    /// - a thin EDGE, always shown, on the Capture Radius: team colour when owned, dim white when neutral;
    /// - a dark full-loop TRACK at the band's own radius, shown whenever the band is (readability polish,
    ///   2026-09-17: a bare band on open ground read poorly as "how full" - a loading bar needs a track behind it);
    /// - a thicker BAND on top of the track that fills clockwise from the top of this player's screen.
    /// Which colours, fill, pulse and blink to use is decided by the pure CaptureRingState; this class only draws it.
    ///
    /// Line renderers, not a mesh: the same unlit, double-sided, vertex-colour material the aim cone lines use, tinted
    /// per ring through each line's own colour (no material copies). No collider, no shadows. The band's and track's
    /// points are rewritten only when the camera's yaw changes (the band's count also rewrites on a fill change);
    /// pulses and blinks only change a colour. The track sits a hair below the band in world height, at the exact
    /// same radius and the exact same per-vertex angles (same step, same yaw-driven start) as the band, so the two
    /// polygons never cross and never z-fight.
    /// </summary>
    public sealed class CaptureRingView : MonoBehaviour
    {
        // How far below the band's own height the track sits, in world metres - just enough that the two line
        // polygons (same radius, same vertex angles) never occupy the same depth, without reading as "floating".
        private const float TrackHeightBelowBand = 0.01f;

        private UiTheme theme;
        private LineRenderer edge;
        private LineRenderer band;
        private LineRenderer track;
        private Vector3 centre;
        private Vector3 trackCentre;
        private float bandRadius;
        private int segments;
        private Vector3[] bandPoints;
        private Vector3[] trackPoints;

        // What is currently drawn, so an unchanged frame touches nothing.
        private int shownBandPointCount = -1;
        private float shownBandStartYaw = float.NaN;
        private float shownTrackStartYaw = float.NaN;
        private Color shownEdgeColor;
        private Color shownBandColor;
        private Color shownTrackColor;
        private bool edgeColorSet;
        private bool bandColorSet;
        private bool trackColorSet;

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
            view.trackCentre = new Vector3(view.centre.x, view.centre.y - TrackHeightBelowBand, view.centre.z);

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

            // A full loop, same radius and same per-vertex angles as the band (both driven by the same yaw and the
            // same ArcStepDegrees), so it always sits cleanly under the band with no crossing polygon edges - see
            // TrackHeightBelowBand. Shown/hidden together with the band; never blinks or pulses.
            view.track = CreateLine(root.transform, "Progress Track", theme.captureRingMaterial, theme.captureRingArcWidth);
            view.track.loop = true;
            view.track.positionCount = view.segments;
            view.track.enabled = false;
            view.trackPoints = new Vector3[view.segments];

            view.band = CreateLine(root.transform, "Progress Band", theme.captureRingMaterial, theme.captureRingArcWidth);
            view.band.loop = false;
            view.band.positionCount = 0;
            view.band.enabled = false;
            view.bandPoints = new Vector3[view.segments + 1];
            return view;
        }

        /// <summary>Centre-circle-and-cut-rule, 2026-09-26: re-sizes this ring in place when its tower's CaptureRadius
        /// changes at runtime - today only the centre, when a cut starts or ends (BuildingCapture.RefreshRingView).
        /// Recomputes exactly what Create derived from captureRadius (the ground height under the new radius, and the
        /// edge/band radii); the edge is redrawn immediately (it is always visible), while the band and track are only
        /// invalidated here - their own Refresh below only rewrites their points on a fill or yaw change, neither of
        /// which necessarily happened, so the shown-cache is cleared to force that on the very next Refresh call
        /// instead of adding a second "or the radius changed" check to two hot per-frame comparisons.</summary>
        public void Resize(float captureRadius)
        {
            Vector3 towerPosition = transform.parent != null ? transform.parent.position : transform.position;
            centre = new Vector3(towerPosition.x, GroundHeight(towerPosition, captureRadius) + theme.captureRingHeightOffset,
                                 towerPosition.z);
            trackCentre = new Vector3(centre.x, centre.y - TrackHeightBelowBand, centre.z);

            float edgeRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth / 2f);
            bandRadius = Mathf.Max(0.01f, captureRadius - theme.captureRingOutlineWidth - theme.captureRingArcGap
                                                         - theme.captureRingArcWidth / 2f);

            float step = CaptureRingGeometry.ArcStepDegrees(segments);
            for (int i = 0; i < segments; i++)
                edge.SetPosition(i, CaptureRingGeometry.PointOnRing(centre, edgeRadius, 0f, step * i));

            shownBandPointCount = -1;
            shownBandStartYaw = float.NaN;
            shownTrackStartYaw = float.NaN;
        }

        /// <summary>Called every frame by BuildingCapture.Update on every client.</summary>
        public void Refresh(CaptureRingState state, float cameraYawDegrees)
        {
            float time = Time.unscaledTime; // presentation only: a paused Time.timeScale must not freeze a pulse

            // 2.7b Decision 8: an out-of-play zone's edge is the theme's own colour, with no pulse - state.Phase is
            // always Idle and state.UnderAttack always false for an out-of-play state (CaptureRingState.From), so
            // neither pulse below can touch it once OwnerPaint.From's own OutOfPlay branch wins.
            // Arena rebuild step 2: moved onto the same rule a tower's crown and caps use (OwnerPaint/
            // OwnerPaintColours), so the ring and the tower can never disagree. Behaviour is unchanged - every branch
            // was checked against the code this replaced (TowerLookPrefabTests/OwnerPaintColoursTests, ring-before.txt).
            Color edgeColor = OwnerPaintColours.For(OwnerPaint.From(state), theme, theme.captureRingNeutralColor, time);
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
                if (track.enabled)
                    track.enabled = false;
                return;
            }

            // Rewritten only when the camera's yaw actually changes - a full loop looks identical whatever angle its
            // vertices start at, but keeping that angle equal to the band's own (same step, same start) is what keeps
            // the two polygons from crossing (see the class comment).
            if (!Mathf.Approximately(cameraYawDegrees, shownTrackStartYaw))
            {
                float trackStep = CaptureRingGeometry.ArcStepDegrees(segments);
                for (int i = 0; i < segments; i++)
                    trackPoints[i] = CaptureRingGeometry.PointOnRing(trackCentre, bandRadius, cameraYawDegrees, trackStep * i);
                track.SetPositions(trackPoints);
                shownTrackStartYaw = cameraYawDegrees;
            }
            if (!trackColorSet || theme.captureRingTrackColor != shownTrackColor)
            {
                track.startColor = theme.captureRingTrackColor;
                track.endColor = theme.captureRingTrackColor;
                shownTrackColor = theme.captureRingTrackColor;
                trackColorSet = true;
            }
            if (!track.enabled)
                track.enabled = true;

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
