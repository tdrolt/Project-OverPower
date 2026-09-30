using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Net;

/// <summary>
/// Ground movement, remote-player interpolation and the fall-through-the-floor safety net for
/// one player. Split out of Multiplayer.cs (Task 0.10) so the movement tuning surface is one
/// small file instead of being buried in a 900-line class.
///
/// Speed is a keyed multiplier stack rather than a settable field on purpose: a real playtest
/// bug once had respawn code assign a raw speed field directly (10, hardcoded) while the prefab
/// said 5, silently overwriting it. Sprint and slow debuffs need to compose (a slowed sprinter is still
/// faster than an un-sprinting target), which a single field can never do but a product of
/// multipliers does for free.
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

    // Logged once, not every frame the reference stays missing - see MovementInput().
    private bool loggedMissingRouter;

    // Resolved once from gameplayConfig in Awake, not read from it every FixedUpdate - killHeight
    // does not change mid-match, and re-reading it every frame would mean re-logging the missing-
    // config error every frame too. Used to be its own [SerializeField] here, disagreeing with
    // GameplayConfig.KillHeight and making a designer's edit to the "authoritative" asset do
    // nothing (Task 0.11a defect 2).
    private float killHeight;

    // Multiplied together against GameplayConfig.BaseMoveSpeed to get CurrentSpeed. Keyed so two
    // independent systems (e.g. a sprint ability and a slow debuff) can each own one entry
    // without needing to know about each other or fight over a single number.
    private readonly Dictionary<object, float> speedMultipliers = new Dictionary<object, float>();

    // Movement step 4, owner only: the last spot this player stood on floor with a player's width inside the arena
    // outline - where the safety net puts them back - and the shapes the check needs.
    private Vector3 lastSafePosition;
    private bool hasLastSafePosition;
    private CapsuleCollider capsule;
    private int groundMask;
    private float groundedProbeMetres;

    // Not a tuning value: how far BELOW the capsule's own bottom still counts as standing on something, so a small
    // bump or a step doesn't read as being in mid-air.
    private const float GroundedSlackMetres = 0.3f;

    // Where a remote (non-owner) copy of this player lerps toward. Pushed in by PlayerNetSync's
    // OnPhotonSerializeView.
    private Vector3 networkPosition;
    private Quaternion networkRotation;

    // Movement step 5, remote copies only: the owner's previous update (for RemoteSnapRule) and a snap waiting for the
    // next physics step.
    private bool hasNetworkUpdate;
    private Vector3 previousNetworkPosition;
    private int previousNetworkStampMs;
    private bool snapPending;

    /// <summary>How many times this remote copy has jumped to its owner's position. Diagnostic only - the two-client
    /// harness reads it, like PlayerAim.SetAimOverride; nothing in the game does.</summary>
    public int RemoteSnapCount { get; private set; }

    /// <summary>True while a dash, blink or similar ability owns this player's position for the
    /// frame - Move() is skipped so the two systems cannot fight over the Rigidbody.</summary>
    public bool ExternalMotionControl { get; set; }

    /// <summary>This frame's input vector was non-zero. PlayerAim reads this for its
    /// standing-still accuracy bonus, which the design wants to apply the instant movement
    /// stops.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Exposed only so PlayerLifecycle's fall-log can report the threshold it just crossed.</summary>
    public float KillHeight => killHeight;

    /// <summary>Base speed times the product of every active multiplier.</summary>
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
    /// detects the fall - PlayerLifecycle owns respawning and decides what to do about it, so this
    /// stays an event rather than a direct call.</summary>
    public event System.Action FellBelowKillHeight;

    /// <summary>Fired when this (locally owned) player's centre ends up outside the arena outline, with the last spot
    /// they stood safely inside. Like FellBelowKillHeight, PlayerMotor only notices - PlayerLifecycle decides what to
    /// do about it.</summary>
    public event System.Action<Vector3> LeftArena;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        photonView = GetComponent<PhotonView>();
        router = GetComponent<PlayerInputRouter>();

        // A silent null here would mean a player who falls off the map falls forever instead of
        // ever being caught, so this falls back to the old hardcoded value rather than leaving
        // killHeight at C#'s default of 0 (which would return anyone standing at ground level).
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerMotor] {name}: GameplayConfig is not assigned - falling back " +
                            "to a kill height of -10.");
        killHeight = gameplayConfig != null ? gameplayConfig.KillHeight : -10f;

        capsule = GetComponent<CapsuleCollider>();
        groundMask = LayerMask.GetMask("Default", "Building");
        // The capsule's bottom sits (height/2 - centre.y) below the root; the slack is what a bump or a step may add.
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
            return; // Only the local player's own keyboard should ever drive this.

        IsMoving = MovementInput() != Vector3.zero;
    }

    private void FixedUpdate()
    {
        if (photonView.IsMine)
        {
            // Falling off the map costs nothing and needs no keybind, which beats a manual
            // respawn button: someone falling should not have to know a shortcut, and a free
            // respawn key would be an escape hatch out of a losing fight.
            //
            // rb.position, not transform.position (review fix): with transform auto-sync off (movement step 5) the
            // transform can trail the body by up to a physics step, which could read this check and CheckArenaBounds
            // below against two different, momentarily disagreeing positions.
            if (rb.position.y < killHeight)
                FellBelowKillHeight?.Invoke();

            CheckArenaBounds();

            if (!ExternalMotionControl)
                Move();
        }
        else
        {
            // Movement step 5: a snap is applied HERE, in the physics step, not where the update arrived. Writing
            // rb.position on arrival lost to this lerp's own MovePosition in the same step (PhotonHandler and this
            // component both run at execution order 0), so a blink or a respawn drew as a quarter-second slide.
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

            // From the body's own physics pose, not transform.position: with interpolation on and transform auto-sync
            // off, the transform trails the body, which is the other half of why a snap used to be undone.
            float catchUp = Time.deltaTime * networkLerpSpeed;
            rb.MovePosition(Vector3.Lerp(rb.position, networkPosition, catchUp));
            rb.MoveRotation(Quaternion.Lerp(rb.rotation, networkRotation, catchUp));
        }
    }

    /// <summary>Called by PlayerNetSync's receive side with the update's own server send time, so PlayerMotor never has
    /// to reach into the networking code itself. Whether this update is a jump rather than a step is
    /// RemoteSnapRule's decision; the jump itself happens in the next physics step.</summary>
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

    /// <summary>Movement step 4, owner only: remembers where this player last stood safely inside the arena, and
    /// reports it when they end up outside (OutOfArenaRule). A scene without an arena outline - the test range, a
    /// preview scene - has nothing to check.</summary>
    private void CheckArenaBounds()
    {
        ArenaBounds bounds = ArenaSymmetry.ActiveBounds;
        if (bounds == null || capsule == null)
            return;

        Vector3 position = rb.position;
        float signed = bounds.SignedDistance(position);
        // The ray only matters for a spot that could be remembered, so it is skipped everywhere else.
        // Amendment 1: never remembered while standing on a barrier - a spot mid-crossing could otherwise be
        // remembered as "safe" and the safety net would return a later fall here, back inside the barrier's own
        // footprint.
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
    /// WASD relative to where the camera is looking, not to world axes. Required now that Q/E
    /// orbit the camera: with world-space input, rotating the view 90 degrees would make W move
    /// the player sideways across the screen. Public because a dash or blink needs a raw input
    /// direction to travel along - the four Space-bound dash scripts that used this were deleted in
    /// Task 0.11b, and the ability system replacing them will want it again.
    ///
    /// The raw WASD read itself moved to PlayerInputRouter in Task 0.12 (it also owns the alive
    /// and chat-focus gates, so a dead or typing player's axis already reads zero by the time it
    /// gets here) - this method keeps only the camera-relative conversion, which is motor-specific
    /// rather than input-specific.
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

        // Time.deltaTime, not Time.fixedDeltaTime, even though this runs in FixedUpdate - kept
        // exactly as it was before this refactor. Changing the physics integration is a separate
        // decision for the designer, not something a refactor should sneak in.
        rb.MovePosition(rb.position + movementDir * CurrentSpeed * Time.deltaTime);
    }
}
