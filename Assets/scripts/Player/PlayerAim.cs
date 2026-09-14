using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Faces the player at the mouse cursor and owns the active weapon's aim cone (bloom on fire,
/// tighter while standing still, recovers over time). Split out of Multiplayer.cs (Task 0.10).
///
/// The cone below is seeded from the assault rifle's own numbers as a fallback, for the moment
/// before the active weapon's first ConfigureCone call. WeaponFiring calls ConfigureCone whenever
/// the active weapon changes, repointing the cone at that weapon's own numbers instead of editing
/// this file.
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

    [SerializeField, Tooltip("Degrees added to this weapon's spread the moment the player starts moving, " +
             "removed the moment they stop. Makes shooting on the move visibly less accurate. Defaults to 0 " +
             "so this fallback cone (before any weapon has configured it) is unchanged.")]
    private float movingSpreadDegrees = 0f;

    [SerializeField, Tooltip("While the player keeps moving, the spread widens by this many degrees per second " +
             "toward maxAngle. recoveryPerSecond still pulls it back, so this only does anything when it is " +
             "larger than recoveryPerSecond. Defaults to 0 so this fallback cone is unchanged.")]
    private float movingBloomPerSecond = 0f;

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
        coneState = new AimConeState(minAngle, maxAngle, bloomPerShot, recoveryPerSecond, standingStillMultiplier,
                                     movingSpreadDegrees, movingBloomPerSecond);
    }

    /// <summary>Lets a later weapon task repoint the cone to the active weapon's own numbers
    /// without editing this file.</summary>
    public void ConfigureCone(float min, float max, float bloom, float recovery, float standingStill,
                              float movingSpread, float movingBloom)
    {
        minAngle = min;
        maxAngle = max;
        bloomPerShot = bloom;
        recoveryPerSecond = recovery;
        standingStillMultiplier = standingStill;
        movingSpreadDegrees = movingSpread;
        movingBloomPerSecond = movingBloom;
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
