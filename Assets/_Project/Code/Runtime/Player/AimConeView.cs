using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using Overpower.Data;
using Overpower.UI;
using Overpower.Weapons;

/// <summary>
/// Draws where this player's shots can actually go: two edge lines from the muzzle out to the
/// weapon's range, plus an arc joining them at that range.
///
/// OWNER ONLY (as PlayerHud). Built as plain child GameObjects in Awake, not prefab children, so
/// nothing on the prefab has to be kept in sync by hand.
///
/// Reads PlayerAim.EffectiveConeAngle, the SAME value WeaponFiring hands the shot sampler this frame
/// (WeaponFiring.TryFire, AimConeState.SampleOffsetDegrees), never recomputed, so the lines cannot
/// disagree with where a shot lands.
/// </summary>
public class AimConeView : MonoBehaviourPun
{
    [SerializeField, Tooltip("Colours, widths and the line material every aim-cone visual on this player shares.")]
    private UiTheme theme;

    private PlayerAim aim;
    private WeaponFiring weaponFiring;
    private PlayerLifecycle lifecycle;
    private PlayerInputRouter input;

    // The layer every wall and deployable cover stands on, computed once. Assumes every weapon's hit
    // mask includes Building, as WeaponFiring's buildingMask does: a future Hitscan that excludes
    // Building on purpose would still have its lines clip on a wall its shots pass through.
    private int buildingMask;

    private LineRenderer leftEdgeLine;
    private LineRenderer rightEdgeLine;
    private LineRenderer rangeArcLine;
    private LineRenderer fanLeftLine;
    private LineRenderer fanRightLine;

    // Reused every frame, not re-allocated.
    private Vector3[] arcPoints;

    // ---- per-weapon lookups, refreshed only when the equipped weapon changes --------
    //
    // cachedWeapon is the guard: RefreshWeaponCache is a no-op while the weapon is unchanged, and
    // every field below is written only inside it, so there is exactly one place that can go stale.
    private WeaponDefinition cachedWeapon;
    private Hitscan cachedBeam;
    private bool cachedIgnoresWalls;
    private DetonateAtCursor cachedCursorDetonator;

    private void Awake()
    {
        aim = GetComponent<PlayerAim>();
        weaponFiring = GetComponent<WeaponFiring>();
        lifecycle = GetComponent<PlayerLifecycle>();
        input = GetComponent<PlayerInputRouter>();

        // Owner only: every read here is meaningless for a remote copy.
        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        buildingMask = LayerMask.GetMask("Building");

        // Loud: a silent null would leave the cone invisible with no clue why.
        bool missingDependency = false;
        if (theme == null)
        {
            Debug.LogError($"[AimConeView] {name}: UI Theme is not assigned - the aim cone will not be drawn.");
            missingDependency = true;
        }
        else if (theme.coneLineMaterial == null)
        {
            Debug.LogError($"[AimConeView] {name}: UiTheme.Cone Line Material is not assigned - the aim cone will not be drawn.");
            missingDependency = true;
        }
        if (aim == null)
        {
            Debug.LogError($"[AimConeView] {name}: no PlayerAim on this object - the aim cone will not be drawn.");
            missingDependency = true;
        }
        if (weaponFiring == null)
        {
            Debug.LogError($"[AimConeView] {name}: no WeaponFiring on this object - the aim cone will not be drawn.");
            missingDependency = true;
        }

        if (missingDependency)
        {
            enabled = false;
            return;
        }

        BuildLines();
    }

    private void BuildLines()
    {
        leftEdgeLine = CreateLine("Aim Cone Left Edge");
        rightEdgeLine = CreateLine("Aim Cone Right Edge");
        rangeArcLine = CreateLine("Aim Cone Range Arc");
        fanLeftLine = CreateLine("Aim Cone Fan Left");
        fanRightLine = CreateLine("Aim Cone Fan Right");
    }

    private LineRenderer CreateLine(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, worldPositionStays: false);

        var line = go.AddComponent<LineRenderer>();
        // sharedMaterial, not material: .material clones the asset the first time it is READ, not just
        // written, which would mean five clones per player. Colour comes from each line's own
        // start/end colour (SetLine).
        line.sharedMaterial = theme.coneLineMaterial;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.widthMultiplier = 1f;
        line.startWidth = theme.coneLineWidth;
        line.endWidth = theme.coneLineWidth;
        line.positionCount = 2;

        // A flat informational overlay: no shadows, light probes or reflection probes.
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.allowOcclusionWhenDynamic = false;

        line.enabled = false; // LateUpdate decides visibility every frame
        return line;
    }

    private void LateUpdate()
    {
        // LoadoutScreen.IsOpen is checked explicitly even though its tool-focus claim already makes
        // InputSuppressed true, so this does not silently break if that focus semantics change.
        bool hide = (lifecycle != null && !lifecycle.IsAlive) ||
                    (input != null && input.InputSuppressed) ||
                    LoadoutScreen.IsOpen ||
                    weaponFiring.Weapon == null;

        if (hide)
        {
            SetLinesEnabled(false);
            return;
        }

        WeaponDefinition weapon = weaponFiring.Weapon;

        // SafeMuzzlePosition, not the raw muzzle: a shot fired flush against a wall starts on the near
        // side of it, and the lines must start where a shot would.
        Vector3 origin = weaponFiring.SafeMuzzlePosition;
        Vector3 forward = aim.AimDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            forward.Normalize();
        else
            forward = transform.forward;

        float half = aim.EffectiveConeAngle / 2f;

        RefreshWeaponCache(weapon);

        // OverPower's range buff (GDD p.20) must show here too, or the lines promise a shorter reach
        // than the shot has.
        float rangeMultiplier = weaponFiring.CurrentRangeMultiplier;

        // Only a BEAM weapon reaches further when charged: ProjectileContext copies weapon.MaxRange
        // verbatim for a spawned projectile, so a charging burst/rocket flies exactly MaxRange.
        // Hitscan.ChargedRange returns MaxRange unchanged for a non-charging weapon, so it is safe
        // once a beam is confirmed.
        float range = cachedBeam != null
            ? Hitscan.ChargedRange(weapon, weaponFiring.CurrentChargeFraction, rangeMultiplier)
            : weapon.MaxRange * rangeMultiplier;

        // Weapon 4 (Rocket -> Cursor) detonates at the cursor, clamped to the weapon's range
        // (DetonateAtCursor.ClampedDistanceToTarget). The bare MaxRange would overstate its reach
        // whenever the cursor is closer, so the lines track the cursor from the SAME origin and
        // target (SafeMuzzlePosition / GroundPointUnderCursor) via the one helper the real shot calls.
        // MaxRange * rangeMultiplier, not the bare asset value: the real shot's cap is the buffed one.
        if (cachedCursorDetonator != null)
            range = DetonateAtCursor.ClampedDistanceToTarget(origin, aim.GroundPointUnderCursor, weapon.MaxRange * rangeMultiplier);

        bool ignoresWalls = cachedIgnoresWalls;

        // A shotgun's fixed pellet fan (SpreadDegrees) is centred on the aim, and every pellet gets its
        // own jitter from the SAME aim cone (WeaponFiring.BuildShots -> FanOffset +
        // AimConeState.SampleOffsetDegrees). The farthest a pellet can land is fan half + cone half, so
        // the outer lines and arc show that, not the bare cone. The fan's own dimmer lines
        // (coneFanLineColor) mark the fixed pattern without jitter.
        bool isShotgun = weapon.Simultaneous && weapon.SpreadDegrees > 0f;
        float outerHalf = isShotgun ? weapon.SpreadDegrees / 2f + half : half;

        Vector3 leftEnd = RayEnd(origin, forward, -outerHalf, range, ignoresWalls);
        Vector3 rightEnd = RayEnd(origin, forward, outerHalf, range, ignoresWalls);

        SetLine(leftEdgeLine, origin, leftEnd, theme.coneLineColor);
        SetLine(rightEdgeLine, origin, rightEnd, theme.coneLineColor);
        leftEdgeLine.enabled = true;
        rightEdgeLine.enabled = true;

        if (theme.showRangeArc)
        {
            UpdateArc(origin, forward, outerHalf, range, ignoresWalls, leftEnd, rightEnd);
            rangeArcLine.enabled = true;
        }
        else
        {
            rangeArcLine.enabled = false;
        }

        if (isShotgun)
        {
            float fanHalf = weapon.SpreadDegrees / 2f;
            Vector3 fanLeftEnd = RayEnd(origin, forward, -fanHalf, range, ignoresWalls);
            Vector3 fanRightEnd = RayEnd(origin, forward, fanHalf, range, ignoresWalls);
            SetLine(fanLeftLine, origin, fanLeftEnd, theme.coneFanLineColor);
            SetLine(fanRightLine, origin, fanRightEnd, theme.coneFanLineColor);
            fanLeftLine.enabled = true;
            fanRightLine.enabled = true;
        }
        else
        {
            fanLeftLine.enabled = false;
            fanRightLine.enabled = false;
        }
    }

    /// <summary>
    /// Re-reads the equipped weapon's projectile prefab for the components that decide how it is drawn
    /// (Hitscan: beam and charged range; IgnoreWalls: clips on Building; DetonateAtCursor: tracks the
    /// cursor), and skips the work while the weapon is unchanged. Every answer changes only on a
    /// weapon switch, while LateUpdate calls this per player every frame.
    /// </summary>
    private void RefreshWeaponCache(WeaponDefinition weapon)
    {
        if (weapon == cachedWeapon)
            return;

        cachedWeapon = weapon;

        GameObject prefab = weapon != null ? weapon.ProjectilePrefab : null;
        cachedBeam = prefab != null ? prefab.GetComponent<Hitscan>() : null;

        // Only Hitscan's ray query (BuildMask) honours IgnoreWalls; ProjectileMotor's sweep never
        // checks it. Gating on cachedBeam avoids a wall-piercing line for a weapon whose real shots
        // would not pierce.
        cachedIgnoresWalls = cachedBeam != null && prefab.GetComponent<IgnoreWalls>() != null;

        cachedCursorDetonator = prefab != null ? prefab.GetComponent<DetonateAtCursor>() : null;
    }

    /// <summary>Where one edge of the cone (or the arc) ends: the wall it hits within range, or the
    /// bare range if nothing is in the way or the weapon ignores walls (Weapon 13). Ignores triggers,
    /// like SafeMuzzlePosition and Hitscan.BuildMask.</summary>
    private Vector3 RayEnd(Vector3 origin, Vector3 forward, float angleDegrees, float range, bool ignoresWalls)
    {
        Vector3 direction = Quaternion.AngleAxis(angleDegrees, Vector3.up) * forward;
        if (!ignoresWalls && Physics.Raycast(origin, direction, out RaycastHit hit, range, buildingMask, QueryTriggerInteraction.Ignore))
            return hit.point;

        return origin + direction * range;
    }

    /// <summary>The range arc, clipped at walls like the edge lines. The first and last points are
    /// handed in, not recomputed, so they are the identical value the edge lines drew and the arc
    /// meets them exactly rather than by two raycasts coinciding.</summary>
    private void UpdateArc(Vector3 origin, Vector3 forward, float outerHalf, float range,
                           bool ignoresWalls, Vector3 leftEnd, Vector3 rightEnd)
    {
        int segments = Mathf.Max(1, theme.coneArcSegments);
        if (arcPoints == null || arcPoints.Length != segments + 1)
            arcPoints = new Vector3[segments + 1];

        arcPoints[0] = leftEnd;
        arcPoints[segments] = rightEnd;
        for (int i = 1; i < segments; i++)
        {
            float angle = Mathf.Lerp(-outerHalf, outerHalf, (float)i / segments);
            arcPoints[i] = RayEnd(origin, forward, angle, range, ignoresWalls);
        }

        rangeArcLine.positionCount = arcPoints.Length;
        rangeArcLine.SetPositions(arcPoints);
        rangeArcLine.startColor = theme.coneArcColor;
        rangeArcLine.endColor = theme.coneArcColor;
    }

    private static void SetLine(LineRenderer line, Vector3 from, Vector3 to, Color color)
    {
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startColor = color;
        line.endColor = color;
    }

    private void SetLinesEnabled(bool value)
    {
        leftEdgeLine.enabled = value;
        rightEdgeLine.enabled = value;
        rangeArcLine.enabled = value;
        fanLeftLine.enabled = value;
        fanRightLine.enabled = value;
    }
}
