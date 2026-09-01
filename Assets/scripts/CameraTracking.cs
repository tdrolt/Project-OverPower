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

    private Vector3 currentOffset;
    private float currentZoom = 1f; // Default zoom level (1 = baseOffset)

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

        // Apply zoom to offset
        currentOffset = baseOffset * currentZoom;

        // Update camera position
        transform.position = target.position + currentOffset;
        transform.LookAt(target);
    }
}