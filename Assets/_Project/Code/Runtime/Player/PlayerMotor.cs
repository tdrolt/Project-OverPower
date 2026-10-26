using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Net;

/// <summary>
/// Ground movement, remote-player interpolation and the fall-through-the-floor safety net for
/// one player.
///
/// Speed is a keyed multiplier stack, not a settable field: a raw field let respawn code silently
/// overwrite the prefab's speed, and sprint and slow debuffs must compose (a slowed sprinter is
/// still faster than an un-sprinting target), which a product of multipliers does for free.
/// </summary>
public class PlayerMotor : MonoBehaviour
{
    [Header("Tuning")]
    [SerializeField, Tooltip("Match tuning asset. Base walking speed (metres/second, no buffs or " +
             "slows applied) comes from here, so this component can never disagree with the " +
             "number balance actually tunes.")]
    private GameplayConfig gameplayConfig;

    [SerializeField, Tooltip("How fast a remote player's transform catches up to the position and " +
             "rotation received over the network, per second. Higher snaps faster but looks less " +
             "smooth on a laggy connection.")]
    private float networkLerpSpeed = 10f;

    [SerializeField, Tooltip("How far a remote player may move between two network updates before other screens show " +
             "the move as a jump instead of a glide - a blink, a portal, a respawn or a teleport. Measured between " +
             "the owner's own consecutive updates (with more allowed when updates were lost), not from where the " +
             "smoothed copy sits: a zip pull trails its copy by metres. Ordinary movement covers at most about 1.25 m " +
             "per update (a 25 m/s zip pull at SerializationRate 20, RoomManager.cs). A blink shorter than this still " +
             "glides.")]
    private float remoteSnapDistance = 3f;

    private Rigidbody rb;
    private PhotonView photonView;
    private PlayerInputRouter router;

    // Logged once, not every frame.
    private bool loggedMissingRouter;

    // Resolved once from gameplayConfig in Awake, not read every FixedUpdate: it does not change
    // mid-match, and re-reading would re-log the missing-config error every frame.
    private float killHeight;

    // Multiplied against GameplayConfig.BaseMoveSpeed to get CurrentSpeed. Keyed so independent
    // systems (a sprint, a slow debuff) each own an entry without knowing about each other.
    private readonly Dictionary<object, float> speedMultipliers = new Dictionary<object, float>();

    // Owner only: the last spot this player stood on floor with a player's width inside the arena
    // outline, where the safety net puts them back, and the shapes the check needs.
    private Vector3 lastSafePosition;
    private bool hasLastSafePosition;
    private CapsuleCollider capsule;
    private int groundMask;
    private float groundedProbeMetres;

    // Not a tuning value: how far BELOW the capsule's bottom still counts as standing on something, so a
    // small bump or step doesn't read as mid-air.
    private const float GroundedSlackMetres = 0.3f;

    // Where a remote copy lerps toward; pushed in by PlayerNetSync.
    private Vector3 networkPosition;
    private Quaternion networkRotation;

    // Remote copies only: the owner's previous update (for RemoteSnapRule) and a snap waiting for the
    // next physics step.
    private bool hasNetworkUpdate;
    private Vector3 previousNetworkPosition;
    private int previousNetworkStampMs;
    private bool snapPending;

    /// <summary>Diagnostic only: the two-client harness reads it; nothing in the game does.</summary>
    public int RemoteSnapCount { get; private set; }

    /// <summary>True while a dash, blink or similar ability owns this player's position for the
    /// frame - Move() is skipped so the two systems cannot fight over the Rigidbody.</summary>
    public bool ExternalMotionControl { get; set; }

    /// <summary>This frame's input vector was non-zero. PlayerAim's standing-still bonus applies the
    /// instant movement stops.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Exposed only so PlayerLifecycle's fall-log can report the threshold.</summary>
    public float KillHeight => killHeight;

    public float CurrentSpeed
    {
        get
        {
            float speed = gameplayConfig != null ? gameplayConfig.BaseMoveSpeed : 5f;
            foreach (float multiplier in speedMultipliers.Values)
                speed *= multiplier;
            return speed;
        }
    }

    /// <summary>Fired when this (locally owned) player falls below killHeight. PlayerMotor only
    /// detects; PlayerLifecycle owns respawning.</summary>
    public event System.Action FellBelowKillHeight;

    /// <summary>Fired when this (locally owned) player's centre ends up outside the arena outline, with
    /// the last spot they stood safely inside. Like FellBelowKillHeight, PlayerLifecycle decides.</summary>
    public event System.Action<Vector3> LeftArena;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        photonView = GetComponent<PhotonView>();
        router = GetComponent<PlayerInputRouter>();

        // Fall back to a hardcoded value: a silent null would let a player fall forever, and
        // killHeight left at C#'s default 0 would return anyone standing at ground level.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerMotor] {name}: GameplayConfig is not assigned - falling back " +
                            "to a kill height of -10.");
        killHeight = gameplayConfig != null ? gameplayConfig.KillHeight : -10f;

        capsule = GetComponent<CapsuleCollider>();
        groundMask = LayerMask.GetMask("Default", "Building");
        // The capsule's bottom sits (height/2 - centre.y) below the root; the slack covers a bump or step.
        groundedProbeMetres = capsule != null
            ? capsule.height * 0.5f - capsule.center.y + GroundedSlackMetres
            : 1f;
    }

    private void Start()
    {
        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    private void Update()
    {
        if (!photonView.IsMine)
            return; // only the local player's own keyboard drives this

        IsMoving = MovementInput() != Vector3.zero;
    }

    private void FixedUpdate()
    {
        if (photonView.IsMine)
        {
            // Falling off the map costs nothing and needs no keybind: a free respawn key would be an
            // escape hatch out of a losing fight.
            //
            // rb.position, not transform.position: with transform auto-sync off the transform can trail
            // the body by up to a physics step, so this check and CheckArenaBounds would read two
            // momentarily disagreeing positions.
            if (rb.position.y < killHeight)
                FellBelowKillHeight?.Invoke();

            CheckArenaBounds();

            if (!ExternalMotionControl)
                Move();
        }
        else
        {
            // A snap is applied HERE, in the physics step, not where the update arrived: writing rb.position
            // on arrival lost to this lerp's own MovePosition in the same step (PhotonHandler and this
            // component both run at execution order 0), so a blink or respawn drew as a slide.
            if (snapPending)
            {
                snapPending = false;
                RemoteSnapCount++;
                rb.position = networkPosition;
                rb.rotation = networkRotation;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }

            // From the body's physics pose, not transform.position: with interpolation on and transform
            // auto-sync off the transform trails the body, which would undo a snap.
            float catchUp = Time.deltaTime * networkLerpSpeed;
            rb.MovePosition(Vector3.Lerp(rb.position, networkPosition, catchUp));
            rb.MoveRotation(Quaternion.Lerp(rb.rotation, networkRotation, catchUp));
        }
    }

    /// <summary>Called by PlayerNetSync's receive side with the update's server send time. Whether the
    /// update is a jump rather than a step is RemoteSnapRule's decision; the jump happens in the next
    /// physics step.</summary>
    public void SetNetworkTarget(Vector3 position, Quaternion rotation, int sentServerTimestampMs)
    {
        float sendIntervalMs = 1000f / Mathf.Max(1, PhotonNetwork.SerializationRate);
        if (RemoteSnapRule.ShouldSnap(hasNetworkUpdate, previousNetworkPosition, previousNetworkStampMs,
                position, sentServerTimestampMs, remoteSnapDistance, sendIntervalMs))
            snapPending = true;

        hasNetworkUpdate = true;
        previousNetworkPosition = position;
        previousNetworkStampMs = sentServerTimestampMs;

        networkPosition = position;
        networkRotation = rotation;
    }

    /// <summary>Owner only: remembers where this player last stood safely inside the arena and reports
    /// it when they end up outside (OutOfArenaRule). A scene without an arena outline has nothing to check.</summary>
    private void CheckArenaBounds()
    {
        ArenaBounds bounds = ArenaSymmetry.ActiveBounds;
        if (bounds == null || capsule == null)
            return;

        Vector3 position = rb.position;
        float signed = bounds.SignedDistance(position);
        // The ray only matters for a spot that could be remembered, so it is skipped everywhere else.
        // Never remembered while standing on a barrier: a mid-crossing spot would send a later fall back
        // inside the barrier's own footprint.
        bool grounded = signed >= capsule.radius && IsStandingOnFloor(position)
                         && !PlayerSpaceProbe.IsInsideBarrier(capsule, position, transform);

        switch (OutOfArenaRule.Decide(signed, capsule.radius, grounded, hasLastSafePosition))
        {
            case OutOfArenaAction.RememberAsSafe:
                lastSafePosition = position;
                hasLastSafePosition = true;
                break;

            case OutOfArenaAction.ReturnToLastSafe:
                LeftArena?.Invoke(lastSafePosition);
                break;
        }
    }

    // The ray starts inside this player's own capsule, which a raycast never reports, so only real floor below counts.
    private bool IsStandingOnFloor(Vector3 rootPosition) =>
        Physics.Raycast(rootPosition, Vector3.down, groundedProbeMetres, groundMask, QueryTriggerInteraction.Ignore);

    public void AddSpeedMultiplier(object key, float multiplier) => speedMultipliers[key] = multiplier;

    public void RemoveSpeedMultiplier(object key) => speedMultipliers.Remove(key);

    /// <summary>
    /// WASD relative to where the camera is looking, not world axes: Q/E orbit the camera, so with
    /// world-space input W would move the player sideways across the screen. Public because a dash or
    /// blink needs a raw input direction to travel along.
    ///
    /// The raw read is PlayerInputRouter's, which also owns the alive and chat-focus gates, so a dead
    /// or typing player's axis already reads zero here; this keeps only the camera-relative conversion.
    /// </summary>
    public Vector3 MovementInput()
    {
        if (router == null)
        {
            if (!loggedMissingRouter)
            {
                loggedMissingRouter = true;
                Debug.LogError($"[PlayerMotor] {name}: PlayerInputRouter is missing - movement disabled.");
            }
            return Vector3.zero;
        }

        Vector2 rawInput = router.MoveAxis;
        float horizontalInput = rawInput.x;
        float verticalInput = rawInput.y;

        Camera cam = Camera.main;
        if (cam == null)
            return new Vector3(horizontalInput, 0f, verticalInput);

        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return right * horizontalInput + forward * verticalInput;
    }

    private void Move()
    {
        Vector3 movementDir = MovementInput();
        if (movementDir.magnitude > 1)
            movementDir.Normalize();

        // Time.deltaTime, not Time.fixedDeltaTime, though this runs in FixedUpdate: changing the
        // physics integration is a decision for the designer.
        rb.MovePosition(rb.position + movementDir * CurrentSpeed * Time.deltaTime);
    }
}
