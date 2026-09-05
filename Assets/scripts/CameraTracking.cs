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
    public float minZoomDistance = 10f; // Closest zoom
    public float maxZoomDistance = 16f; // Farthest zoom

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
        currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed, 0.5f, 2f); // Adjust multiplier as needed

        ResolveTeamYaw();

        // Apply zoom, then rotation.
        currentOffset = Quaternion.AngleAxis(yaw, Vector3.up) * (baseOffset * currentZoom);

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