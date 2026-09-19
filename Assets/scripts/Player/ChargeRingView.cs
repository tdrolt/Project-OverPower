using Overpower.Abilities;
using Overpower.Match;
using Overpower.UI;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The ring at your own feet that fills while you hold a charging weapon's trigger (Tudor, 2026-09-17: "add a charge
/// indicator"). Weapon 06 had no feedback of any kind before this - the only thing that read the charge was the aim
/// cone's range arc, and only for a BEAM weapon whose range actually grows (AimConeView:201).
///
/// OWNER ONLY, for the same reason AimConeView is: nobody needs to see how charged another player's gun is, and every
/// read here is meaningless on a remote copy. Its lines are built as plain child GameObjects in Awake, not prefab
/// children, so the prefab only ever carries this component and one Theme reference.
///
/// Three flat lines plus a tick per charge step, exactly the shape CaptureRingView already draws on the ground:
/// - a dark full-loop TRACK, so a part-filled band reads as a meter rather than a stray arc;
/// - a BAND on top of it, filling clockwise from the top of the screen;
/// - one TICK per internal charge step, at the hold fraction where the next round is earned.
/// Where the steps are comes from ChargeCountRule - the same rule WeaponFiring counts rounds with, so a tick can never
/// promise a round the gun does not give.
///
/// Deliberately NOT IPunObservable: the player's PhotonView uses AutoFindAll and would absorb a second observable.
/// PlayerNetSync is the only one.
/// </summary>
public class ChargeRingView : MonoBehaviourPun
{
    [SerializeField, Tooltip("Colours, sizes and the line material the charge ring draws with - the same UiTheme " +
             "asset the HUD, the aim cone and the capture rings read.")]
    private UiTheme theme;

    // Not design tunables. The track sits a hair below the band and the ticks a hair above it, so three line polygons
    // at the same radius never occupy the same depth and never z-fight - the same trick and the same 0.01 m
    // CaptureRingView uses. MaxTicks caps how many tick lines are built once, in Awake; no weapon has, or should
    // have, anywhere near this many charge steps.
    private const float TrackBelowBand = 0.01f;
    private const float TicksAboveBand = 0.005f;
    private const int MaxTicks = 8;

    private WeaponFiring weaponFiring;
    private PlayerLifecycle lifecycle;
    private PlayerInputRouter input;

    private LineRenderer track;
    private LineRenderer band;
    private LineRenderer[] ticks;
    private Vector3[] trackPoints;
    private Vector3[] bandPoints;
    private readonly Vector3[] tickPoints = new Vector3[2];
    private int segments;

    private void Awake()
    {
        weaponFiring = GetComponent<WeaponFiring>();
        lifecycle = GetComponent<PlayerLifecycle>();
        input = GetComponent<PlayerInputRouter>();

        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        // Loud, matching AimConeView and WeaponFiring: a silent null here leaves the ring invisible with no clue why,
        // which for a visual-only component is easy to miss for days.
        bool missing = false;
        if (theme == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: UI Theme is not assigned - the charge ring will not be drawn.");
            missing = true;
        }
        else if (theme.chargeRingMaterial == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: UiTheme.Charge Ring Material is not assigned - the charge ring will not be drawn.");
            missing = true;
        }
        if (weaponFiring == null)
        {
            Debug.LogError($"[ChargeRingView] {name}: no WeaponFiring on this object - the charge ring will not be drawn.");
            missing = true;
        }

        if (missing)
        {
            enabled = false;
            return;
        }

        segments = Mathf.Max(16, theme.chargeRingSegments);
        trackPoints = new Vector3[segments];
        bandPoints = new Vector3[segments + 1];

        track = CreateLine("Charge Ring Track", theme.chargeRingWidth);
        track.loop = true;
        track.positionCount = segments;

        band = CreateLine("Charge Ring Band", theme.chargeRingWidth);
        band.loop = false;
        band.positionCount = 0;

        ticks = new LineRenderer[MaxTicks];
        for (int i = 0; i < MaxTicks; i++)
        {
            ticks[i] = CreateLine($"Charge Ring Tick {i + 1}", theme.chargeRingStepTickWidth);
            ticks[i].positionCount = 2;
        }
    }

    private LineRenderer CreateLine(string childName, float width)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, worldPositionStays: false);
        // TransformZ alignment draws the line facing this object's own Z. Pointing Z straight up lays it flat on the
        // ground instead of turning it toward the camera. The material is double-sided. The player TURNS as they aim
        // (PlayerAim writes transform.rotation every Update), so this child's rotation is set in world terms and every
        // point below is in world space - a local-space ring would spin with the body and its fill would start
        // somewhere different every frame.
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = theme.chargeRingMaterial; // sharedMaterial: .material clones the asset per line
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
        line.enabled = false; // LateUpdate decides visibility every frame; start hidden.
        return line;
    }

    private void LateUpdate()
    {
        // The same four hide conditions AimConeView uses, plus the charge itself. Dead: nothing to charge. Suppressed:
        // chatting, or a tool (F1) has claimed focus - and that case also clears the hold itself (WeaponFiring:268).
        var weapon = weaponFiring.Weapon;
        bool hide = !theme.showChargeRing ||
                    (lifecycle != null && !lifecycle.IsAlive) ||
                    (input != null && input.InputSuppressed) ||
                    LoadoutScreen.IsOpen ||
                    weapon == null || !weapon.CanCharge || weapon.MaxChargeSeconds <= 0f ||
                    !weaponFiring.ChargeHeld;

        if (hide)
        {
            SetLinesEnabled(false);
            return;
        }

        // Every point is rebuilt each visible frame, unlike CaptureRingView, which caches on the camera's yaw: a
        // capture zone never moves, and a charging player does. One owner-only object at ~130 points and one downward
        // raycast per frame, and only while a trigger is actually held.
        Vector3 root = transform.position;
        GroundSnap.TryFindGroundY(root, out float groundY);
        Vector3 centre = new Vector3(root.x, groundY + theme.chargeRingHeightOffset, root.z);
        // The top of this player's own screen (CameraTracking.Yaw's own comment), so the ring fills clockwise on
        // screen from 12 o'clock however the camera is turned for this team.
        float startYaw = CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f;
        float radius = Mathf.Max(0.01f, theme.chargeRingRadius);
        float step = CaptureRingGeometry.ArcStepDegrees(segments);

        Vector3 trackCentre = new Vector3(centre.x, centre.y - TrackBelowBand, centre.z);
        for (int i = 0; i < segments; i++)
            trackPoints[i] = CaptureRingGeometry.PointOnRing(trackCentre, radius, startYaw, step * i);
        track.SetPositions(trackPoints);
        track.startColor = theme.chargeRingTrackColor;
        track.endColor = theme.chargeRingTrackColor;
        track.enabled = true;

        float fill = weaponFiring.CurrentChargeFraction;
        int count = CaptureRingGeometry.ArcPointCount(fill, segments);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
                bandPoints[i] = CaptureRingGeometry.PointOnRing(centre, radius, startYaw, step * i);
            band.positionCount = count;
            band.SetPositions(bandPoints); // uses only the first positionCount points
            Color fillColor = fill >= 1f ? theme.chargeRingFullColor : theme.chargeRingFillColor;
            band.startColor = fillColor;
            band.endColor = fillColor;
            band.enabled = true;
        }
        else
        {
            // A hold that has not started charging yet - the Fire Interval it must wait out first. The empty track is
            // exactly what should be on screen.
            band.enabled = false;
        }

        // A tick at each INTERNAL step boundary: the fraction where one more round is earned. The last boundary is the
        // closed ring itself and needs no mark, and a weapon with no steps (weapon 6's own smooth burst charge - the
        // only weapon that still charges since the laser tree dropped it, mark plan step 4 review) gets none.
        Vector3 tickCentre = new Vector3(centre.x, centre.y + TicksAboveBand, centre.z);
        float half = theme.chargeRingStepTickLength * 0.5f;
        int wanted = Mathf.Clamp(weapon.ChargeSteps - 1, 0, MaxTicks);
        for (int i = 0; i < MaxTicks; i++)
        {
            if (i >= wanted)
            {
                ticks[i].enabled = false;
                continue;
            }

            float degrees = ChargeCountRule.StepFraction(i + 1, weapon.ChargeSteps) * 360f;
            tickPoints[0] = CaptureRingGeometry.PointOnRing(tickCentre, radius - half, startYaw, degrees);
            tickPoints[1] = CaptureRingGeometry.PointOnRing(tickCentre, radius + half, startYaw, degrees);
            ticks[i].SetPositions(tickPoints);
            ticks[i].startColor = theme.chargeRingStepTickColor;
            ticks[i].endColor = theme.chargeRingStepTickColor;
            ticks[i].enabled = true;
        }
    }

    private void SetLinesEnabled(bool value)
    {
        track.enabled = value;
        band.enabled = value;
        for (int i = 0; i < ticks.Length; i++)
            ticks[i].enabled = value;
    }
}
