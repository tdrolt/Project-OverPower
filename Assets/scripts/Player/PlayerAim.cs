using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Faces the player at the mouse cursor and owns the active weapon's aim cone (bloom on fire,
/// tighter while standing still, recovers over time). Split out of Multiplayer.cs (Task 0.10).
///
/// No weapon system exists yet, so the cone below is seeded from the assault rifle's own numbers
/// as a fallback. A later weapon task calls ConfigureCone when the active weapon changes instead
/// of editing this file.
/// </summary>
public class PlayerAim : MonoBehaviour
{
    [Header("Fallback aim cone (baseline assault rifle - a weapon task repoints this later)")]
    [SerializeField, Tooltip("Spread in degrees with a fully recovered cone while standing still - " +
             "the tightest this weapon ever fires.")]
    private float minAngle = 1.5f;

    [SerializeField, Tooltip("Spread in degrees at maximum bloom - the loosest this weapon ever fires.")]
    private float maxAngle = 7f;

    [SerializeField, Tooltip("Degrees the cone widens on every shot fired.")]
    private float bloomPerShot = 0.8f;

    [SerializeField, Tooltip("Degrees the cone recovers per second toward minAngle.")]
    private float recoveryPerSecond = 2f;

    [SerializeField, Tooltip("How much tighter the cone gets the instant the player stops moving. " +
             "1.5 means the standing spread is the moving spread divided by 1.5.")]
    private float standingStillMultiplier = 1.5f;

    private PhotonView photonView;
    private PlayerMotor motor;
    private AimConeState coneState;

    private Vector3 aimDirection = Vector3.forward;
    private Vector3 groundPointUnderCursor;

    /// <summary>Flat, normalised, toward the cursor.</summary>
    public Vector3 AimDirection => aimDirection;

    /// <summary>Where the cursor ray meets the player's own ground plane - abilities that target
    /// a location on the ground need this rather than a direction.</summary>
    public Vector3 GroundPointUnderCursor => groundPointUnderCursor;

    /// <summary>The spread actually in effect right now, after the standing-still bonus. For a
    /// future HUD crosshair.</summary>
    public float EffectiveConeAngle => coneState.EffectiveAngle;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        motor = GetComponent<PlayerMotor>();
        aimDirection = transform.forward;
        groundPointUnderCursor = transform.position;
        BuildCone();
    }

    private void BuildCone()
    {
        coneState = new AimConeState(minAngle, maxAngle, bloomPerShot, recoveryPerSecond, standingStillMultiplier);
    }

    /// <summary>Lets a later weapon task repoint the cone to the active weapon's own numbers
    /// without editing this file.</summary>
    public void ConfigureCone(float min, float max, float bloom, float recovery, float standingStill)
    {
        minAngle = min;
        maxAngle = max;
        bloomPerShot = bloom;
        recoveryPerSecond = recovery;
        standingStillMultiplier = standingStill;
        BuildCone();
    }

    private void Update()
    {
        // Only the local player controls their own rotation - every other client learns it
        // through OnPhotonSerializeView instead.
        if (!photonView.IsMine)
            return;

        UpdateRotationFromMouse();
        coneState.Tick(Time.deltaTime, motor != null && motor.IsMoving);
    }

    private void UpdateRotationFromMouse()
    {
        // Cast a ray from the mouse position to the game world.
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));

        if (groundPlane.Raycast(ray, out float rayDistance))
        {
            groundPointUnderCursor = ray.GetPoint(rayDistance);

            Vector3 direction = groundPointUnderCursor - transform.position;
            direction.y = 0f; // Keep rotation horizontal.

            if (direction != Vector3.zero)
            {
                aimDirection = direction.normalized;
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }

    public void RegisterShot() => coneState.RegisterShot();

    /// <summary>
    /// AimDirection rotated by a random offset sampled from the current cone. Takes the RNG as a
    /// parameter rather than owning one, since AimConeState was deliberately built to be
    /// deterministic under test - random spread was the designer's explicit choice over
    /// deterministic twin-ray spread, so this must not grow a hidden deterministic mode.
    /// </summary>
    public Vector3 GetShotDirection(System.Random rng)
    {
        float offsetDegrees = coneState.SampleOffsetDegrees(rng);
        return Quaternion.AngleAxis(offsetDegrees, Vector3.up) * aimDirection;
    }
}
