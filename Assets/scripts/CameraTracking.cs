using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
public class CameraTracking : MonoBehaviour
{
    public Transform target; // The player to follow
    public Vector3 offset; // Offset to keep the camera at a good distance
    public float followSpeed = 5f; // Speed at which the camera follows

    private Vector3 velocity = Vector3.zero;

void FixedUpdate()
{
    if (target != null)
    {
        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, 0.1f); // 0.1f is smooth time
        transform.LookAt(target);
    }
}

}
*/


/*public class CameraTracking : MonoBehaviour
{
    public Transform target; // The player to follow
    public Vector3 offset = new Vector3(0f, 10f, -5f); // Camera offset from player

    // Camera follow settings
    public bool fixedHeight = true; // Keep constant height regardless of player Y position
    public float height = 10f; // Fixed height value (if fixedHeight is true)

    private Vector3 currentOffset;

    void Start()
    {
        if (target != null)
        {
            // Initialize offset
            currentOffset = offset;
            if (fixedHeight)
            {
                currentOffset.y = height - target.position.y;
            }

            // Set initial camera position
            transform.position = target.position + currentOffset;
            transform.LookAt(target);
        }
    }

    void LateUpdate()
    {
        if (target != null)
        {
            // Update offset if using fixed height
            if (fixedHeight)
            {
                currentOffset.y = height - target.position.y;
            }

            // Move camera exactly with player
            transform.position = target.position + currentOffset;

            // Keep looking at player
            transform.LookAt(target);
        }
    }
}*/


public class CameraTracking : MonoBehaviour
{
    public Transform target;
    public Vector3 baseOffset = new Vector3(0f, 10f, -5f); // Base offset
    public float zoomSpeed = 2f; // How fast zoom adjusts
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

    // Scope ability (Tudor, 2026-09-18): a SEPARATE keyed stack from currentZoom above, on purpose - see
    // CameraZoomStack's class comment for why an extra-zoom multiplier must never be folded into currentZoom
    // itself. Mirrors PlayerMotor.speedMultipliers exactly (PlayerMotor.cs:57, 252-254): a dictionary indexer, so
    // a duplicate key overwrites rather than stacking, and removing an absent key is a no-op. Keyed so two
    // independent systems could each own one entry without knowing about each other, the same reason
    // PlayerMotor's stack is keyed rather than a single float.
    private readonly Dictionary<object, float> zoomMultipliers = new Dictionary<object, float>();

    /// <summary>The camera following the local player (there is one, on the scene's main camera). The capture rings and
    /// the minimap read its Yaw, so "up" on them is "up" on screen.</summary>
    public static CameraTracking Instance { get; private set; }

    /// <summary>Degrees this camera is turned about the vertical axis (Unity yaw: clockwise seen from above; 0 = looking
    /// toward +Z). 0 until this player's team is known - see ResolveTeamYaw - then fixed for the match. World
    /// direction (sin Yaw, cos Yaw) is the top of the screen.</summary>
    public float Yaw => yaw;

    public bool YawResolved => teamYawResolved;

    /// <summary>Diagnostic only, like RemoteSnapCount above - the product of every active extra-zoom multiplier
    /// (1 when nothing is scoping). Tests read this instead of reflecting into the private stack.</summary>
    public float ZoomMultiplierProduct => CameraZoomStack.Product(zoomMultipliers.Values);

    /// <summary>Diagnostic only - how many keyed extra-zoom multipliers are active right now. 0 means the stack is
    /// genuinely empty, not merely "at 1x" - a snap-back test checks this, not just the product.</summary>
    public int ActiveZoomMultiplierCount => zoomMultipliers.Count;

    /// <summary>
    /// Lets another component (the Scope ability) add an extra zoom-out on top of the scroll wheel's own clamp,
    /// without that component needing to know about anyone else doing the same. A duplicate key overwrites its
    /// previous value rather than stacking - same semantics as PlayerMotor.AddSpeedMultiplier. Composes with the
    /// scroll zoom AFTER its clamp (CameraZoomStack.ApplyZoom, called from LateUpdate below), and is NEVER folded
    /// into currentZoom - see CameraZoomStack's class comment for why that ordering is the whole point.
    /// </summary>
    public void AddZoomMultiplier(object key, float multiplier) => zoomMultipliers[key] = multiplier;

    /// <summary>Removing a key that was never added (or already removed) is a no-op - same semantics as
    /// PlayerMotor.RemoveSpeedMultiplier, so a module can always call this defensively (e.g. on every Interrupt
    /// reason) without first checking whether it had actually added anything.</summary>
    public void RemoveZoomMultiplier(object key) => zoomMultipliers.Remove(key);

    void Awake()
    {
        Instance = this;
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
        if (target == null) return;

        // Zoom in/out with Mouse Scroll
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed, minZoomMultiplier, maxZoomMultiplier);

        ResolveTeamYaw();

        // Apply zoom (scroll clamp, then every active extra-zoom multiplier - CameraZoomStack's class comment has
        // the full reasoning), then rotation.
        Vector3 zoomedOffset = CameraZoomStack.ApplyZoom(baseOffset, currentZoom, CameraZoomStack.Product(zoomMultipliers.Values));
        currentOffset = Quaternion.AngleAxis(yaw, Vector3.up) * zoomedOffset;

        // Update camera position
        transform.position = target.position + currentOffset;
        transform.LookAt(target);
    }

    /// Works out this player's camera rotation from where their team actually spawns, so all three
    /// teams get the same view of the arena relative to their own base rather than three different
    /// ones. Runs until it succeeds, because the team arrives as a network property and is not
    /// known on the first frame.
    void ResolveTeamYaw()
    {
        if (teamYawResolved || target == null)
            return;

        PlayerTeam team = target.GetComponent<PlayerTeam>();
        if (team == null || !team.HasTeam)
            return;

        RoomManager room = FindObjectOfType<RoomManager>();
        if (room == null || room.teamSpawnPoints == null || team.teamID >= room.teamSpawnPoints.Length)
            return;

        Vector3 centre = Vector3.zero;
        int counted = 0;
        foreach (Transform spawn in room.teamSpawnPoints)
        {
            if (spawn == null) continue;
            centre += spawn.position;
            counted++;
        }

        if (counted == 0)
            return;

        centre /= counted;

        Vector3 toSpawn = room.teamSpawnPoints[team.teamID].position - centre;
        toSpawn.y = 0f;

        yaw = Mathf.Atan2(toSpawn.x, toSpawn.z) * Mathf.Rad2Deg + teamYawOffset;
        teamYawResolved = true;

        Debug.Log($"[TEAM] camera yaw {yaw:0} deg for team {team.teamID}");
    }
}