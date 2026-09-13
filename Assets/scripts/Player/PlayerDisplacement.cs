using System;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

/// <summary>
/// The one component that moves a player's body over time for anything other than ordinary
/// walking - a dash's travel, a knockback's shove, a blink's instant jump. PlayerMotor only ever
/// walks under the player's own held-down input; everything else that shoves a player around,
/// voluntary or not, goes through here and here alone - see IDisplaceable's class comment for why
/// one shared mover exists instead of every ability re-deriving its own movement and its own way
/// of reporting what stopped it.
///
/// THE ONLY WRITER of PlayerMotor.ExternalMotionControl. Nothing else may set it - two systems each
/// assuming the other owns the Rigidbody for a frame is exactly the kind of fight that flag exists
/// to prevent.
///
/// PRIORITY. Three kinds of request share this one mover - see DisplacementPriority
/// (Assets/scripts/Combat), which is pure and edit-mode tested so "does a knockback interrupt a
/// dash" is provable without a scene:
///  - Displace (Forced) - a knockback. Always starts, replacing whatever was running.
///  - DisplaceVoluntary (Voluntary) - a dash, a zip pull. Refused while a Forced move is running;
///    otherwise replaces a Voluntary move already in flight.
///  - TeleportTo (Teleport) - a blink. Instant, so it never itself becomes "the move running";
///    refused while a Forced move is running, otherwise replaces a Voluntary move in flight.
///
/// OWNER-ONLY, like PlayerStatusEffects and PlayerHealth: only the machine that owns this player
/// decides where its own body is going next. A remote copy's Rigidbody is driven by PlayerMotor's
/// own lerp toward the network position instead (PlayerMotor.SetNetworkTarget), so every public
/// method here is a no-op on a copy that is not photonView.IsMine.
/// </summary>
public class PlayerDisplacement : MonoBehaviour, IDisplaceable
{
    // Not a tuning value: the line between "a wall in the way" and "the floor underfoot" a sweep
    // can hit. A wall's surface normal points roughly sideways; a floor's points roughly up, so
    // anything closer to straight up than this is the ground, not something to stop for.
    private const float FloorNormalYThreshold = 0.5f;

    private PhotonView photonView;
    private Rigidbody rb;
    private PlayerMotor motor;
    private PlayerLifecycle lifecycle;

    // Not a design tunable, the way a weapon's hit mask is: a dash that could be tuned to pass
    // through walls would break the arena, so which layers can block a displacement is fixed here
    // rather than exposed for a designer to mis-set. Default carries level geometry's absence and
    // every living player (see PlayerLifecycle - a corpse's collider is switched off entirely, so
    // it can never appear in this sweep at all); Building is the walls.
    //
    // Computed in Awake, NOT as a static field initializer: Unity refuses to run LayerMask.NameToLayer
    // from a MonoBehaviour type's static constructor (it throws TypeInitializationException the first
    // time the type is touched, which - for a networked prefab - is during PhotonNetwork.Instantiate
    // itself). Found by playing a dash immediately after landing this file.
    private int blockMask;

    // The move in progress, or null when nothing is running. Kept as the three primitives a step
    // needs rather than a struct so FixedUpdate never allocates one every tick.
    private DisplaceKind? activeKind;
    private Vector3 direction;
    private float remainingDistance;
    private float speed;
    private Action<DisplaceEnd> onEnd;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        rb = GetComponent<Rigidbody>();
        motor = GetComponent<PlayerMotor>();
        lifecycle = GetComponent<PlayerLifecycle>();
        blockMask = LayerMask.GetMask("Default", "Building");

        if (rb == null)
            Debug.LogError($"[PlayerDisplacement] {name}: Rigidbody is missing - nothing can be displaced.");
        if (motor == null)
            Debug.LogError($"[PlayerDisplacement] {name}: PlayerMotor is missing - ExternalMotionControl cannot be claimed or released.");

        // Subscribed in Awake, not Start, matching AbilityRunner's own reasoning: PlayerLifecycle's
        // Start can already raise AliveChanged (a late joiner learning this player is dead) before
        // this component's Start would otherwise have run.
        if (lifecycle != null)
            lifecycle.AliveChanged += HandleAliveChanged;
    }

    private void OnDestroy()
    {
        if (lifecycle != null)
            lifecycle.AliveChanged -= HandleAliveChanged;
    }

    private void FixedUpdate()
    {
        if (!photonView.IsMine || activeKind == null)
            return;

        float step = Mathf.Min(speed * Time.fixedDeltaTime, remainingDistance);
        Vector3 fromPosition = rb.position;

        // Triggers ignored (QueryTriggerInteraction.Ignore) and everything else PlayerDisplacement
        // does not treat as a wall (wrong layer, a floor, our own body) filtered out in IsBlocker -
        // SweepTest itself only knows how to ask "does the capsule touch anything at all".
        if (rb.SweepTest(direction, out RaycastHit hit, step, QueryTriggerInteraction.Ignore) && IsBlocker(hit))
        {
            Vector3 blockedPosition = fromPosition + direction * hit.distance;
            rb.MovePosition(blockedPosition);
            Finish(DisplaceOutcome.Blocked, hit.collider, blockedPosition);
            return;
        }

        Vector3 nextPosition = fromPosition + direction * step;
        rb.MovePosition(nextPosition);
        remainingDistance -= step;

        if (remainingDistance <= 0f)
            Finish(DisplaceOutcome.Completed, null, nextPosition);
    }

    // ---- IDisplaceable - Forced, used by knockback -----------------------------------------

    /// <summary>Forced priority: always starts, cutting short whatever was running. No-op on a
    /// remote copy, matching every other owner-only mutator on this player.</summary>
    public void Displace(Vector3 direction, float distance, float speed, Action<DisplaceEnd> onEnd)
    {
        if (!photonView.IsMine)
            return;

        StartMove(DisplaceKind.Forced, direction, distance, speed, onEnd);
    }

    // ---- Voluntary and instant moves, for abilities that shove THIS player around ----------

    /// <summary>
    /// Voluntary priority: a dash, a zip pull's own travel. Returns false and starts nothing while
    /// a Forced move (a knockback) is running - the caller's onEnd never fires for a refused
    /// request, the same contract TryBuildCast uses for "nothing spent". Replaces a Voluntary move
    /// already in flight (that move's onEnd fires Cancelled first).
    /// </summary>
    public bool DisplaceVoluntary(Vector3 direction, float distance, float speed, Action<DisplaceEnd> onEnd)
    {
        if (!photonView.IsMine)
            return false;

        if (!DisplacementPriority.Accepts(activeKind, DisplaceKind.Voluntary))
            return false;

        StartMove(DisplaceKind.Voluntary, direction, distance, speed, onEnd);
        return true;
    }

    /// <summary>
    /// Instant reposition for a blink/teleport - no travel time, so nothing here is ever "the move
    /// running" afterwards. Refused (returns false) while a Forced move is running; otherwise ends
    /// a Voluntary move in flight as Cancelled before the jump, so a dash mid-travel cannot keep
    /// stepping from a position the player already left.
    /// </summary>
    public bool TeleportTo(Vector3 position)
    {
        if (!photonView.IsMine)
            return false;

        if (!DisplacementPriority.Accepts(activeKind, DisplaceKind.Teleport))
            return false;

        if (DisplacementPriority.CancelsCurrent(activeKind, DisplaceKind.Teleport))
            Finish(DisplaceOutcome.Cancelled, null, rb.position);

        rb.position = position;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        return true;
    }

    /// <summary>Stops whatever is running right now, Forced included, and reports Cancelled. A
    /// no-op with nothing running or on a remote copy.</summary>
    public void Cancel()
    {
        if (!photonView.IsMine || activeKind == null)
            return;

        Finish(DisplaceOutcome.Cancelled, null, rb.position);
    }

    // ---- shared start/stop -------------------------------------------------------------------

    private void StartMove(DisplaceKind kind, Vector3 direction, float distance, float speed, Action<DisplaceEnd> onEnd)
    {
        if (DisplacementPriority.CancelsCurrent(activeKind, kind))
            Finish(DisplaceOutcome.Cancelled, null, rb.position);

        activeKind = kind;
        this.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        remainingDistance = Mathf.Max(0f, distance);
        this.speed = Mathf.Max(0.01f, speed);
        this.onEnd = onEnd;

        if (motor != null)
            motor.ExternalMotionControl = true;
    }

    private void Finish(DisplaceOutcome outcome, Collider blocker, Vector3 endPosition)
    {
        Action<DisplaceEnd> callback = onEnd;

        activeKind = null;
        onEnd = null;
        remainingDistance = 0f;
        if (motor != null)
            motor.ExternalMotionControl = false;

        callback?.Invoke(new DisplaceEnd(outcome, endPosition, blocker));
    }

    /// <summary>A stun cancels only through the ability that is running (see AbilityModule.Interrupt)
    /// - death cancels unconditionally, here, so a dash or a knockback can never carry a corpse past
    /// the moment it stopped being a body worth moving.</summary>
    private void HandleAliveChanged(bool alive)
    {
        if (!alive)
            Cancel();
    }

    private bool IsBlocker(RaycastHit hit)
    {
        if (hit.collider == null)
            return false;

        if ((blockMask & (1 << hit.collider.gameObject.layer)) == 0)
            return false; // Not a layer a displacement stops for (e.g. Bullet).

        if (hit.normal.y > FloorNormalYThreshold)
            return false; // Ground underfoot, not a wall in the way.

        if (hit.rigidbody == rb || hit.collider.transform.IsChildOf(transform))
            return false; // Never blocked by our own body.

        return true;
    }
}
