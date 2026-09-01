using UnityEngine;
using System.Collections;

public class PlayerDashWithBuff : MonoBehaviour
{
    [Header("Dash Settings")]
    public float dashDistance = 5f;          // Distance of the dash
    public float dashDuration = 0.3f;        // Duration of the dash
    public float dashCooldown = 2f;          // Cooldown between dashes
    public Animator animator;                // Animator for dash animation (assign in Inspector)
    
    [Header("Damage Reduction Buff")]
    public float damageReductionPercent = 50f; // Y% damage reduction (e.g., 50% means damage becomes 50% of original)
    public float buffDuration = 3f;           // Buff duration after dash

    [Header("Obstacle Detection")]
    public float dashColliderRadius = 0.5f;   // Adjust to roughly match your player's collider radius

    private bool isDashing = false;
    private float lastDashTime = -Mathf.Infinity;
    private Rigidbody rb;
    private bool isBuffActive = false;        // Track if the damage reduction buff is active

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

    // Returns true if the damage reduction buff is active
    public bool IsBuffActive()
    {
        return isBuffActive;
    }

    // Coroutine to perform the dash and apply the damage reduction buff
    public IEnumerator Dash(Vector3 inputDirection)
    {
        isDashing = true;
        lastDashTime = Time.time;
        
        // Trigger dash animation immediately
        if (animator != null)
        {
            animator.SetTrigger("Dash");
        }
        
        // If no input provided, default to facing forward
        if (inputDirection == Vector3.zero)
            inputDirection = transform.forward;

        Vector3 startPosition = rb.position;
        Vector3 targetPosition = startPosition + inputDirection.normalized * dashDistance;
        float elapsedTime = 0f;
        bool hitObstacle = false;

        // Perform the dash movement with obstacle detection via SphereCast
        while (elapsedTime < dashDuration)
        {
            Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, Mathf.SmoothStep(0f, 1f, elapsedTime / dashDuration));

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

        // Apply damage reduction buff after the dash
        StartCoroutine(ApplyDamageReductionBuff());
    }

    // Coroutine to apply the damage reduction buff
    private IEnumerator ApplyDamageReductionBuff()
    {
        isBuffActive = true;
        Debug.Log($"[PlayerDashWithBuff] Damage reduction buff activated! {damageReductionPercent}% damage reduction for {buffDuration} seconds.");
        yield return new WaitForSeconds(buffDuration);
        isBuffActive = false;
        Debug.Log("[PlayerDashWithBuff] Damage reduction buff ended.");
    }

    // Method to calculate damage with the reduction buff applied
    public float ApplyDamageReduction(float damage)
    {
        if (isBuffActive)
        {
            float reducedDamage = damage * (1f - (damageReductionPercent / 100f));
            Debug.Log($"[PlayerDashWithBuff] Damage reduced from {damage} to {reducedDamage}.");
            return reducedDamage;
        }
        return damage;
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
