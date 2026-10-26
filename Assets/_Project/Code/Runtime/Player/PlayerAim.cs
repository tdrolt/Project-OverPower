using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// Faces the player at the mouse cursor and owns the active weapon's aim cone (bloom on fire,
/// tighter while standing still, recovers over time). The serialized cone is only a fallback seeded
/// from the assault rifle, until WeaponFiring's ConfigureCone repoints it at the active weapon.
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

    // Test/harness hook, not gameplay - see SetAimOverride.
    private Vector3? aimOverride;

    /// <summary>Flat, normalised, toward the cursor.</summary>
    public Vector3 AimDirection => aimDirection;

    /// <summary>Where the cursor ray meets the player's own ground plane, for abilities that target a location.</summary>
    public Vector3 GroundPointUnderCursor => groundPointUnderCursor;

    /// <summary>The spread in effect right now, after the standing-still bonus.</summary>
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
        // Only the local player controls their own rotation; the rest learn it from PlayerNetSync.
        if (!photonView.IsMine)
            return;

        if (aimOverride.HasValue)
        {
            AimAt(aimOverride.Value);
        }
        else if (Application.isFocused)
        {
            UpdateRotationFromMouse();
        }
        // Input.mousePosition keeps reading the real OS cursor while this window is UNFOCUSED
        // (alt-tabbed, or a background Editor a script is driving), so an idle player would keep
        // turning toward wherever the mouse is on the desktop. Hence the isFocused gate: unfocused
        // with no override keeps the last aim. Only the aim read is gated, the cone still recovers.
        // Test scripts use SetAimOverride instead of fighting this gate.

        coneState.Tick(Time.deltaTime, motor != null && motor.IsMoving);
    }

    private void UpdateRotationFromMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, transform.position.y, 0));

        if (groundPlane.Raycast(ray, out float rayDistance))
            AimAt(ray.GetPoint(rayDistance));
    }

    /// <summary>The one place both the mouse path and the SetAimOverride path turn the body, so they
    /// can never disagree about what "aiming at a point" does.</summary>
    private void AimAt(Vector3 worldPoint)
    {
        groundPointUnderCursor = worldPoint;

        Vector3 direction = worldPoint - transform.position;
        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            aimDirection = direction.normalized;
            transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    /// <summary>
    /// TEST/HARNESS HOOK, not a gameplay feature. While set, this player aims at worldPoint every
    /// frame regardless of Application.isFocused or the OS cursor. Null releases it. Prefer this over
    /// reflecting into aimDirection/groundPointUnderCursor (two-client-harness.md §11): it also turns
    /// the body, which those fields alone do not.
    /// </summary>
    public void SetAimOverride(Vector3? worldPoint) => aimOverride = worldPoint;

    public void RegisterShot() => coneState.RegisterShot();

    /// <summary>
    /// AimDirection rotated by a random offset sampled from the current cone. The RNG is a parameter
    /// so AimConeState stays deterministic under test. Random spread is the designer's explicit choice
    /// over deterministic twin-ray spread: do not add a hidden deterministic mode.
    /// </summary>
    public Vector3 GetShotDirection(System.Random rng)
    {
        float offsetDegrees = coneState.SampleOffsetDegrees(rng);
        return Quaternion.AngleAxis(offsetDegrees, Vector3.up) * aimDirection;
    }
}
