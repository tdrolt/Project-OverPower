using UnityEngine;
using System.Collections;
using Photon.Pun;

public class PlayerDashWithProjectile : MonoBehaviourPun
{
    [Header("Dash Settings")]
    public float dashDistance = 5f;
    public float dashDuration = 0.3f;
    public float dashCooldown = 2f;
    public float projectileSpawnDelay = 0.2f; // Delay before spawning projectile after dash
    public Animator animator;

    [Header("Projectile Settings")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 15f;
    public int projectileDamage = 20;
    public Transform bulletSpawnPosition;

    [Header("Obstacle Detection")]
    // Adjust this to roughly match your player's collider radius.
    public float dashColliderRadius = 0.5f;

    private bool isDashing = false;
    private float lastDashTime = -Mathf.Infinity;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public bool CanDash()
    {
        return !isDashing && Time.time - lastDashTime >= dashCooldown;
    }

    public bool IsDashing()
    {
        return isDashing;
    }

    public IEnumerator Dash(Vector3 inputDirection)
    {
        if (!photonView.IsMine) yield break; // Only local player can dash

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
            float t = elapsedTime / dashDuration;
            Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, Mathf.SmoothStep(0f, 1f, t));

            // Obstacle check, restored to match Dash.cs and Dash with buff.cs. This was lost in
            // the copy-paste that created this file, which is why this dash went through walls.
            Vector3 moveDirection = (newPosition - rb.position).normalized;
            float moveDistance = (newPosition - rb.position).magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(rb.position, dashColliderRadius, moveDirection, out hit, moveDistance))
            {
                rb.MovePosition(hit.point);
                hitObstacle = true;
                break;
            }

            rb.MovePosition(newPosition);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (!hitObstacle)
        {
            rb.MovePosition(targetPosition);
        }

        isDashing = false;

        // Wait before spawning the projectile
        yield return new WaitForSeconds(projectileSpawnDelay);

        // Call the RPC to spawn the projectile across all clients
        photonView.RPC("RPC_SpawnProjectile", RpcTarget.All);
    }

    [PunRPC]
    void RPC_SpawnProjectile(PhotonMessageInfo info)
    {
        if (projectilePrefab == null || bulletSpawnPosition == null)
        {
            Debug.LogError("[PlayerDashWithProjectile] Projectile prefab or spawn position not assigned!");
            return;
        }

        Vector3 cursorDirection = GetCursorDirection();
        if (cursorDirection == Vector3.zero)
        {
            Debug.LogError("[PlayerDashWithProjectile] Unable to calculate cursor direction!");
            return;
        }

        GameObject projectile = Instantiate(projectilePrefab, bulletSpawnPosition.position, Quaternion.identity);
        projectile.transform.forward = cursorDirection.normalized;

        Rigidbody projectileRb = projectile.GetComponent<Rigidbody>();
        if (projectileRb != null)
        {
            projectileRb.linearVelocity = cursorDirection.normalized * projectileSpeed;
        }

        MultiplayerBulletController bulletController = projectile.GetComponent<MultiplayerBulletController>();
        if (bulletController != null)
        {
            bulletController.damage = projectileDamage;
            // Ownership must come from the sender, never PhotonNetwork.LocalPlayer: inside an RPC
            // body that resolves to whoever RECEIVED it, so every machine credited the projectile
            // to itself. That also made the new self-damage check treat it as your own bullet.
            bulletController.owner = info.Sender;
        }

        Debug.Log("[PlayerDashWithProjectile] Projectile spawned via RPC!");
    }

    private Vector3 GetCursorDirection()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        float rayDistance;

        if (groundPlane.Raycast(ray, out rayDistance))
        {
            Vector3 cursorPosition = ray.GetPoint(rayDistance);
            Vector3 direction = (cursorPosition - transform.position).normalized;
            direction.y = 0;
            return direction;
        }

        return Vector3.zero;
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
