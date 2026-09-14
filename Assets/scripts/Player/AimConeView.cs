using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using Overpower.Data;
using Overpower.UI;
using Overpower.Weapons;

/// <summary>
/// Draws where this player's shots can actually go: two edge lines from the muzzle out to the
/// weapon's range, plus an arc joining them at that range - Tudor could not SEE the aim cone
/// widening while moving (Task 1/2), so this makes it visible instead of asking him to read
/// PlayerAim.EffectiveConeAngle off a debugger.
///
/// OWNER ONLY. Nobody needs to see another player's aim - see PlayerHud's class comment for the
/// same reasoning applied to the screen-space HUD. Built as plain child GameObjects in Awake, not
/// prefab children, so nothing on the prefab has to be kept in sync with this file by hand.
///
/// Reads PlayerAim.EffectiveConeAngle directly, the SAME value WeaponFiring hands the shot
/// sampler this frame (see WeaponFiring.TryFire and AimConeState.SampleOffsetDegrees) - never
/// recomputed here, so the lines can never disagree with where a shot actually lands.
/// </summary>
public class AimConeView : MonoBehaviourPun
{
    [SerializeField, Tooltip("Colours, widths and the line material every aim-cone visual on this player shares.")]
    private UiTheme theme;

    private PlayerAim aim;
    private WeaponFiring weaponFiring;
    private PlayerLifecycle lifecycle;
    private PlayerInputRouter input;

    // Same layer every wall and every piece of deployable cover stands on (see WeaponFiring's own
    // comment on SafeMuzzlePosition and CoverWall's class comment) - computed once since
    // LayerMask.NameToLayer never changes at runtime.
    private int buildingMask;

    private LineRenderer leftEdgeLine;
    private LineRenderer rightEdgeLine;
    private LineRenderer rangeArcLine;
    private LineRenderer fanLeftLine;
    private LineRenderer fanRightLine;

    // Reused every frame instead of re-allocated, since LateUpdate runs it once per player per
    // frame for as long as the match lasts.
    private Vector3[] arcPoints;

    private void Awake()
    {
        aim = GetComponent<PlayerAim>();
        weaponFiring = GetComponent<WeaponFiring>();
        lifecycle = GetComponent<PlayerLifecycle>();
        input = GetComponent<PlayerInputRouter>();

        // Nobody but the owner should see this player's own accuracy - and every downstream
        // read here (aim, weaponFiring, PhotonView.IsMine) is meaningless for a remote copy.
        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        buildingMask = LayerMask.GetMask("Building");

        // Loud, matching WeaponFiring/PlayerHealth: a silent null here would leave the cone
        // invisible with no clue why, which for a visual-only component is easy to miss for days.
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
        line.material = theme.coneLineMaterial;
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.widthMultiplier = 1f;
        line.startWidth = theme.coneLineWidth;
        line.endWidth = theme.coneLineWidth;
        line.positionCount = 2;

        // A purely informational overlay for the local player only - it must never cast a shadow,
        // sample a light probe or a reflection probe, all of which cost a little and mean nothing
        // for a flat, unlit line only its own owner can see.
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
        // Dead: nothing to aim. Suppressed: chatting, or a tool (F1, and later Task 9's loadout
        // screen - LoadoutScreen.IsOpen does not exist yet, so InputSuppressed's toolHasFocus flag
        // is the only signal available; Task 9 should read LoadoutScreen.IsOpen here directly once
        // it exists, the same way it will make AimConeView hide for it. No weapon: nothing to draw.
        bool hide = (lifecycle != null && !lifecycle.IsAlive) ||
                    (input != null && input.InputSuppressed) ||
                    weaponFiring.Weapon == null;

        if (hide)
        {
            SetLinesEnabled(false);
            return;
        }

        WeaponDefinition weapon = weaponFiring.Weapon;

        // SafeMuzzlePosition, not the raw muzzle - see its own comment on WeaponFiring: a shot
        // fired flush against a wall starts on the near side of it, and the lines must start from
        // the exact point a shot actually would.
        Vector3 origin = weaponFiring.SafeMuzzlePosition;
        Vector3 forward = aim.AimDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            forward.Normalize();
        else
            forward = transform.forward;

        // The same value WeaponFiring hands the shot sampler THIS frame - never recomputed here.
        float half = aim.EffectiveConeAngle / 2f;

        GameObject prefab = weapon.ProjectilePrefab;
        Hitscan beam = prefab != null ? prefab.GetComponent<Hitscan>() : null;
        bool ignoresWalls = prefab != null && prefab.GetComponent<IgnoreWalls>() != null;

        // Only a BEAM weapon's shots actually reach further when charged - ProjectileContext
        // always copies weapon.MaxRange verbatim for a spawned projectile (see its Initialize),
        // so a charging burst/rocket path flies exactly MaxRange no matter how long the trigger
        // was held. Hitscan.ChargedRange already returns MaxRange unchanged for a non-charging
        // weapon, so it is always safe to call once a beam is confirmed - reused, not re-derived,
        // per the task brief.
        float range = beam != null
            ? Hitscan.ChargedRange(weapon, weaponFiring.CurrentChargeFraction)
            : weapon.MaxRange;

        // A shotgun's fixed pellet fan (SpreadDegrees) is centred on the aim, and every pellet
        // then gets its own random jitter from the SAME aim cone every other weapon uses (see
        // WeaponFiring.BuildShots -> FanOffset + AimConeState.SampleOffsetDegrees). The farthest a
        // pellet can actually land is the outermost fan slot PLUS the full jitter half-width in the
        // same direction - fan half + cone half - so that is what the outer lines and the arc need
        // to represent, not the bare cone. The fan's own two lines (dimmer, coneFanLineColor) mark
        // the fixed pattern underneath, with no jitter added, so Tudor can tell the two apart.
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

    /// <summary>Where one edge of the cone (or the arc) actually ends: the wall it hits within
    /// range, or the bare range if nothing is in the way or this weapon ignores walls entirely
    /// (the through-walls laser leaf, Weapon 13 - see IgnoreWalls's own class comment). Ignores
    /// triggers, matching every other shot/cast origin check in this file's neighbourhood
    /// (SafeMuzzlePosition, Hitscan.BuildMask).</summary>
    private Vector3 RayEnd(Vector3 origin, Vector3 forward, float angleDegrees, float range, bool ignoresWalls)
    {
        Vector3 direction = Quaternion.AngleAxis(angleDegrees, Vector3.up) * forward;
        if (!ignoresWalls && Physics.Raycast(origin, direction, out RaycastHit hit, range, buildingMask, QueryTriggerInteraction.Ignore))
            return hit.point;

        return origin + direction * range;
    }

    /// <summary>The range arc: coneArcSegments+1 points from -outerHalf to +outerHalf at radius
    /// range, clipped at walls exactly like the edge lines. The first and last points are handed
    /// in rather than recomputed, so they are the SAME Vector3 the edge lines just drew - not
    /// merely equal, but the identical value - which is what makes the arc meet the lines exactly
    /// rather than by coincidence of two separate raycasts landing on the same point.</summary>
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
