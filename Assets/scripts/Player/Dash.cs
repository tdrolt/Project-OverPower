using UnityEngine;
using System.Collections;

public class PlayerDash : MonoBehaviour
{
    public float dashDistance = 5f;
    public float dashDuration = 0.3f;
    public float dashCooldown = 2f;
    public Animator animator; // Assign this in the Inspector and ensure it has the "Dash" trigger

    private bool isDashing = false;
    private float lastDashTime = -Mathf.Infinity;
    private Rigidbody rb;

    // Adjust this to roughly match your player's collider radius.
    public float dashColliderRadius = 0.5f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    // Check if dash is available
    public bool CanDash()
    {
        return !isDashing && Time.time - lastDashTime >= dashCooldown;
    }

    // Returns true if currently dashing
    public bool IsDashing()
    {
        return isDashing;
    }

    // Coroutine to perform the dash
    public IEnumerator Dash(Vector3 inputDirection)
    {
        isDashing = true;
        lastDashTime = Time.time;

        if (animator != null)
        {
            animator.SetTrigger("Dash");
        }

        if (inputDirection == Vector3.zero)
            inputDirection = transform.forward;

        Vector3 startPosition = rb.position;
        Vector3 targetPosition = startPosition + inputDirection.normalized * dashDistance;
        float elapsedTime = 0f;
        bool hitObstacle = false;

        while (elapsedTime < dashDuration)
        {
            // Lerp for smooth dash movement
            Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, Mathf.SmoothStep(0f, 1f, elapsedTime / dashDuration));

            // Use SphereCast to detect obstacles along the dash path.
            Vector3 moveDirection = (newPosition - rb.position).normalized;
            float moveDistance = (newPosition - rb.position).magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(rb.position, dashColliderRadius, moveDirection, out hit, moveDistance))
            {
                // Stop dash at the obstacle's hit point.
                newPosition = hit.point;
                rb.MovePosition(newPosition);
                hitObstacle = true;
                break;
            }

            rb.MovePosition(newPosition);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // If no obstacle was hit, complete the dash.
        if (!hitObstacle)
        {
            rb.MovePosition(targetPosition);
        }
        
        isDashing = false;
    }

    void OnEnable()
    {
        Debug.Log($"{GetType().Name} enabled");
    }

    void OnDisable()
    {
        Debug.Log($"{GetType().Name} disabled");
    }
}
