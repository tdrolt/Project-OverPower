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

    [Header("Rotation")]
    public KeyCode rotateLeftKey = KeyCode.Q;
    public KeyCode rotateRightKey = KeyCode.E;
    public float rotationSpeed = 90f;   // degrees per second held

    private Vector3 currentOffset;
    private float currentZoom = 1f; // Default zoom level (1 = baseOffset)
    private float yaw = 0f;         // degrees rotated around the player

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

        // Orbit around the player. Rotating the offset around the world Y axis keeps the height,
        // the distance and therefore the viewing angle exactly as they were: only the compass
        // direction changes, so nobody gains a better view, they just pick one they prefer.
        if (Input.GetKey(rotateLeftKey))
            yaw -= rotationSpeed * Time.deltaTime;
        if (Input.GetKey(rotateRightKey))
            yaw += rotationSpeed * Time.deltaTime;

        // Apply zoom, then rotation.
        currentOffset = Quaternion.AngleAxis(yaw, Vector3.up) * (baseOffset * currentZoom);

        // Update camera position
        transform.position = target.position + currentOffset;
        transform.LookAt(target);
    }
}