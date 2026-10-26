using Overpower.Data;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraTracking : MonoBehaviour
{
    public Transform target;
    /// <summary>Whose team decides the view angle. Null means the followed target (the normal case). A knocked-out
    /// player watching someone else (SpectateView) sets their own body here, so the arena does not turn round to that
    /// player's team angle and the minimap's "up" stays where it was.</summary>
    [System.NonSerialized] public Transform yawSource;
    /// <summary>A spectator seat has no body, so no team decides the view angle; the camera turns this many degrees
    /// (Unity yaw) whoever it follows, so the arena never swings round between one team's player and the next. Null
    /// (the normal case) leaves the team-based angle in charge.</summary>
    [System.NonSerialized] public float? fixedYaw;
    public Vector3 baseOffset = new Vector3(0f, 10f, -5f);
    public float zoomSpeed = 2f;
    [Tooltip("Closest the camera can zoom in with the scroll wheel, as a multiple of Base Offset. " +
             "0.5 = half the normal distance.")]
    public float minZoomMultiplier = 0.5f;
    [Tooltip("Farthest the camera can zoom out with the scroll wheel, as a multiple of Base Offset. " +
             "2 = twice the normal distance. Zooming out also lets the cursor reach further from the " +
             "player, so this limits how far cursor-aimed abilities can be placed.")]
    public float maxZoomMultiplier = 2f;

    [Header("Per-team orientation")]
    // Every team should see the arena from the same relative angle, so that "toward the centre"
    // is the same direction on screen for everyone. The rotation for each team is derived from
    // where its spawn actually sits, so it stays correct if the map moves.
    //
    // This offset rotates all three together. It is the one value to nudge if the bases do not
    // sit where you want them on screen; 0 leaves the team whose spawn is due north looking
    // exactly as the camera did before.
    public float teamYawOffset = 0f;

    private Vector3 currentOffset;
    private float currentZoom = 1f; // Default zoom level (1 = baseOffset)
    private float yaw = 0f;         // degrees rotated around the player
    private bool teamYawResolved = false;
    // Which team teamYawResolved was resolved FOR, so ResolveTeamYaw can tell a body that moved onto another team
    // (a safety net: seats are locked at Start) from "already resolved". PlayerTeam.NoTeam until the first resolve.
    private int teamYawResolvedForTeam = PlayerTeam.NoTeam;

    // Scope ability: a SEPARATE keyed stack from currentZoom, on purpose (see CameraZoomStack's class comment).
    // Mirrors PlayerMotor.speedMultipliers: a duplicate key overwrites rather than stacking, removing an absent key
    // is a no-op, and keying lets independent systems each own an entry without knowing about each other.
    private readonly Dictionary<object, float> zoomMultipliers = new Dictionary<object, float>();

    /// <summary>The camera following the local player (there is one, on the scene's main camera). The capture rings and
    /// the minimap read its Yaw, so "up" on them is "up" on screen.</summary>
    public static CameraTracking Instance { get; private set; }

    /// <summary>Degrees this camera is turned about the vertical axis (Unity yaw: clockwise seen from above; 0 = looking
    /// toward +Z). 0 until this player's team is known - see ResolveTeamYaw - then fixed for the match. World
    /// direction (sin Yaw, cos Yaw) is the top of the screen.</summary>
    public float Yaw => yaw;

    /// <summary>The scroll wheel's own zoom back to the normal distance (1 x Base Offset). A spectator's Space frames the whole map from
    /// a known zoom; extra-zoom multipliers (AddZoomMultiplier) are not touched.</summary>
    public void ResetScrollZoom() => currentZoom = 1f;

    /// <summary>The camera's tilt: degrees the view looks down below the horizon, from Base Offset (it looks from there at the target).</summary>
    public float TiltDegrees => Mathf.Atan2(baseOffset.y, new Vector2(baseOffset.x, baseOffset.z).magnitude) * Mathf.Rad2Deg;

    public bool YawResolved => teamYawResolved;

    /// <summary>Diagnostic only: the product of every active extra-zoom multiplier (1 when nothing is scoping).
    /// Tests read this instead of reflecting into the private stack.</summary>
    public float ZoomMultiplierProduct => CameraZoomStack.Product(zoomMultipliers.Values);

    /// <summary>Diagnostic only: how many keyed extra-zoom multipliers are active. 0 means the stack is genuinely
    /// empty, not merely "at 1x"; a snap-back test checks this, not just the product.</summary>
    public int ActiveZoomMultiplierCount => zoomMultipliers.Count;

    /// <summary>
    /// Lets another component (the Scope ability) add an extra zoom-out on top of the scroll wheel's clamp. A
    /// duplicate key overwrites its previous value rather than stacking (as PlayerMotor.AddSpeedMultiplier). Composes
    /// AFTER the clamp (CameraZoomStack.ApplyZoom, called from LateUpdate) and is NEVER folded into currentZoom.
    /// </summary>
    public void AddZoomMultiplier(object key, float multiplier) => zoomMultipliers[key] = multiplier;

    /// <summary>Removing a key that was never added is a no-op (as PlayerMotor.RemoveSpeedMultiplier), so a module
    /// can call this defensively, e.g. on every Interrupt reason.</summary>
    public void RemoveZoomMultiplier(object key) => zoomMultipliers.Remove(key);

    // Where gun sounds are heard from: the followed player, not this camera (see ListenerRig).
    private Transform listener;

    void Awake()
    {
        Instance = this;
        listener = ListenerRig.MoveListenerToChild(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        currentOffset = baseOffset;
        if (target != null)
        {
            transform.position = target.position + currentOffset;
            transform.LookAt(target);
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            // No one to follow: the listener goes back onto the camera (ListenerRig.Follow's no-target branch).
            ListenerRig.Follow(listener, null);
            return;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed, minZoomMultiplier, maxZoomMultiplier);

        ResolveTeamYaw();

        // Scroll clamp, then every active extra-zoom multiplier (CameraZoomStack), then rotation.
        Vector3 zoomedOffset = CameraZoomStack.ApplyZoom(baseOffset, currentZoom, CameraZoomStack.Product(zoomMultipliers.Values));
        currentOffset = Quaternion.AngleAxis(yaw, Vector3.up) * zoomedOffset;

        transform.position = target.position + currentOffset;
        transform.LookAt(target);
        ListenerRig.Follow(listener, target);
    }

    /// Works out this player's camera rotation from where their team spawns, so all three teams get the same view
    /// of the arena relative to their own base. Runs until it succeeds: the team arrives as a network property
    /// and is not known on the first frame.
    void ResolveTeamYaw()
    {
        if (fixedYaw.HasValue)
        {
            yaw = fixedYaw.Value;
            teamYawResolved = true;
            teamYawResolvedForTeam = PlayerTeam.NoTeam;
            return;
        }

        Transform yawFrom = yawSource != null ? yawSource : target;
        if (yawFrom == null)
            return;

        PlayerTeam team = yawFrom.GetComponent<PlayerTeam>();
        if (team == null || !team.HasTeam)
            return;

        // If the LOCAL player's body ends up on a different team after this camera already resolved (a safety net:
        // seats are locked at Start), re-resolve the same way as the first time, or the first team's angle sticks for
        // the match. CaptureRingView self-corrects off Yaw changing (Building capture.cs), not off this flag, so a
        // moment unresolved here breaks nothing.
        if (teamYawResolved)
        {
            if (team.teamID == teamYawResolvedForTeam)
                return;
            teamYawResolved = false;
        }

        RoomManager room = FindObjectOfType<RoomManager>();
        if (room == null || room.teamSpawnPoints == null || team.teamID >= room.teamSpawnPoints.Length)
            return;

        if (!CameraYawRules.ToSpawnFromCentre(room.teamSpawnPoints, team.teamID, out Vector2 toSpawn))
            return;

        yaw = CameraYawRules.TeamYaw(SceneCameraConfig.SceneWantsOwnSpawnOnLeft(), toSpawn.x, toSpawn.y, teamYawOffset); // the straight lane view, or the arena's angled one
        teamYawResolved = true;
        teamYawResolvedForTeam = team.teamID;

        Debug.Log($"[TEAM] camera yaw {yaw:0} deg for team {team.teamID}");
    }
}