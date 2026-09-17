using System;
using System.Collections.Generic;
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

    // The player's own capsule: the shape every step is swept with (movement step 2). Read once in Awake, so what a
    // move is checked against is always the shape physics pushes around, never a second Inspector radius.
    private CapsuleCollider capsule;

    // Reused every physics step, so a move in flight allocates nothing. A player's capsule never touches 16 things at
    // once; anything past that is missed rather than allocated for.
    private readonly Collider[] overlapHits = new Collider[16];
    private readonly RaycastHit[] sweepHits = new RaycastHit[16];
    private readonly List<SweepContact> sweepContacts = new List<SweepContact>(16);
    private readonly List<Collider> sweepColliders = new List<Collider>(16);

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
        capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
            Debug.LogError($"[PlayerDisplacement] {name}: CapsuleCollider is missing - no move can be checked against a wall.");

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

    /// <summary>
    /// Runs whenever this component (or its GameObject) is switched off, disabled being the only
    /// path a move-in-progress can vanish through without Finish ever being asked to run: the
    /// loadout swapping the player prefab out, the object pool recycling it, a domain reload mid-
    /// play. Without this, a move cut off here left activeKind set and, worse,
    /// PlayerMotor.ExternalMotionControl stuck true forever - the player would never be able to
    /// walk again even after coming back. Unity calls OnDisable before OnDestroy for an active
    /// object being destroyed, so this alone already covers that path too; OnDestroy below only
    /// unsubscribes and has nothing left to finish by the time it runs.
    /// </summary>
    private void OnDisable()
    {
        if (activeKind != null)
            Finish(DisplaceOutcome.Cancelled, null, rb != null ? rb.position : transform.position);
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

        // One stop rule for a dash, a zip pull and a knockback (DisplacementSweepRule, movement step 2). The player's
        // own capsule is swept along the step and stops a skin width short of the first wall, and a wall the capsule
        // already overlaps blocks only a move deeper into it. Rigidbody.SweepTestAll, which this replaces, stopped
        // exactly on contact and did not report a wall the capsule had already been pressed into by walking - so a
        // second dash from there went straight through a thin (0.72 m) wall (measured, movement step 1: at 0.70 m and
        // 0.60 m from the wall's inner face - touching, then 0.108 m deep - SweepTestAll returned no hit at all).
        float allowed = AllowedTravel(fromPosition, direction, step, out Collider blocker);
        if (blocker != null)
        {
            Vector3 blockedPosition = fromPosition + direction * allowed;
            rb.MovePosition(blockedPosition);
            Finish(DisplaceOutcome.Blocked, blocker, blockedPosition);
            return;
        }

        // MovePosition on this Rigidbody (dynamic, gravity on, not kinematic) is solved by PhysX alongside whatever
        // contacts it is already resting in - not applied as a bare teleport - so a run can overshoot its requested
        // distance by a few centimetres (measured: 3.115m for a requested 3m). Accepted rather than snapped to the
        // exact figure: snapping would fight the same contact solving that keeps the capsule from sinking into the
        // floor it stands on.
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
    /// Read-only query for a blink to check BEFORE it spends its charge: TeleportTo would refuse
    /// while a Forced move (a knockback) is running, but by the time TeleportTo actually runs the
    /// caster's charge is already spent and cannot be handed back. Small addition to this class
    /// rather than a workaround in the ability - it is one more read of the same priority rule
    /// TeleportTo itself already checks, not a new rule.
    /// </summary>
    public bool CanTeleport => DisplacementPriority.Accepts(activeKind, DisplaceKind.Teleport);

    /// <summary>
    /// Read-only query for a dash to check BEFORE it spends its charge (movement step 2): false while a knockback is
    /// running, and false when a Voluntary move along <paramref name="direction"/> could not cover even a centimetre
    /// because the player is touching, or pressed into, a wall that way. It asks the same rule the move itself asks
    /// every step, so "refused" and "would have gone nowhere" cannot disagree.
    /// </summary>
    public bool CanStartVoluntary(Vector3 direction)
    {
        if (!photonView.IsMine || rb == null)
            return false;

        if (!DisplacementPriority.Accepts(activeKind, DisplaceKind.Voluntary))
            return false;

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        float allowed = AllowedTravel(rb.position, dir, DisplacementSweepRule.MinUsefulTravelMetres, out _);
        return DisplacementSweepRule.IsUsefulTravel(allowed);
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

    /// <summary>
    /// How far the capsule may travel from <paramref name="from"/> along <paramref name="dir"/> (unit length), up to
    /// <paramref name="distance"/>, and the collider that stops it (null when nothing does). This half only gathers
    /// physics facts; DisplacementSweepRule makes the decision and is tested in edit mode.
    /// </summary>
    private float AllowedTravel(Vector3 from, Vector3 dir, float distance, out Collider blocker)
    {
        blocker = null;
        if (capsule == null)
            return distance; // Awake already logged it; moving unchecked beats a player who can never dash.

        Quaternion rotation = rb.rotation;
        // Lifted a little, so the floor the capsule rests on is neither an overlap nor a hit.
        Vector3 lift = Vector3.up * DisplacementSweepRule.LiftMetres;
        Vector3 centre = from + rotation * capsule.center + lift;
        float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
        Vector3 top = centre + Vector3.up * halfSegment;
        Vector3 bottom = centre - Vector3.up * halfSegment;
        // Where the push-out is asked for: one skin width along the move. At that pose a capsule merely TOUCHING a wall
        // it moves into already overlaps it, while one moving away or along it does not overlap it any deeper.
        Vector3 probe = from + dir * DisplacementSweepRule.SkinMetres + lift;

        sweepContacts.Clear();
        sweepColliders.Clear();

        // Walls the capsule already overlaps, asked for directly rather than left to the cast below: a cast reports a
        // collider it starts inside with distance 0 and no usable normal, and not dependably at all.
        int overlapCount = Physics.OverlapCapsuleNonAlloc(bottom, top, capsule.radius, overlapHits, blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
            AddStartInside(overlapHits[i], probe, rotation, dir);

        int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, capsule.radius, dir, sweepHits,
            distance + DisplacementSweepRule.SkinMetres, blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = sweepHits[i];
            if (hit.distance <= 0f)
            {
                AddStartInside(hit.collider, probe, rotation, dir); // touching at the start: judged the same way
                continue;
            }

            if (IsOwnOrGround(hit.collider) || sweepColliders.Contains(hit.collider))
                continue;

            sweepContacts.Add(SweepContact.Hit(hit.distance, hit.normal));
            sweepColliders.Add(hit.collider);
        }

        float allowed = DisplacementSweepRule.AllowedTravel(distance, sweepContacts, dir, out int blockerIndex);
        if (blockerIndex >= 0)
            blocker = sweepColliders[blockerIndex];
        return allowed;
    }

    private void AddStartInside(Collider other, Vector3 probe, Quaternion rotation, Vector3 dir)
    {
        if (IsOwnOrGround(other) || sweepColliders.Contains(other))
            return;

        // [C] Controller decision (movement step 2 opus review): only static geometry may refuse a dash outright -
        // see DisplacementSweepRule.CanBlockAsStartInside. A living player (or anything else physics-driven) has a
        // Rigidbody; a wall does not. Without this, standing flush against an enemy read exactly like standing flush
        // against a wall and refused the dash - a player brawling in melee could never dash away from someone they
        // were touching. A dash travelling TOWARD a player from a distance still stops at their body (see the cast
        // loop above), which is unaffected.
        if (!DisplacementSweepRule.CanBlockAsStartInside(other.attachedRigidbody != null))
            return;

        Transform otherTransform = other.transform;
        bool overlapsAhead = Physics.ComputePenetration(capsule, probe, rotation, other, otherTransform.position,
            otherTransform.rotation, out Vector3 pushOut, out float depth) && depth > 0f;

        if (!overlapsAhead && IsNonConvexMesh(other))
        {
            // Physics.ComputePenetration is unsupported for a non-convex MeshCollider and always returns false for
            // one - found by movement step 2's opus review: a player pressed into a non-convex mesh (a door frame,
            // Door_01 and its kin - 22 enabled on Building, 43 on Default in this scene) was invisible to the overlap
            // check the exact same way a wall used to be invisible to Rigidbody.SweepTestAll, so a second dash sailed
            // straight through it. A raycast toward the move, starting just short of the capsule so it does not
            // start embedded in the mesh itself (a raycast that starts inside a shape does not report a hit on that
            // shape either - the same limitation ComputePenetration and the old SweepTestAll both had), stands in
            // for ComputePenetration's push-out direction here.
            Vector3 rayStart = probe + capsule.center - dir * (capsule.radius + DisplacementSweepRule.SkinMetres);
            float rayDistance = 2f * (capsule.radius + DisplacementSweepRule.SkinMetres);
            if (other.Raycast(new Ray(rayStart, dir), out RaycastHit meshHit, rayDistance)
                && meshHit.normal.y <= DisplacementSweepRule.FloorNormalY
                && Vector3.Dot(dir, meshHit.normal) < DisplacementSweepRule.IntoSurfaceDot)
            {
                sweepContacts.Add(SweepContact.Inside(-meshHit.normal));
                sweepColliders.Add(other);
                return;
            }
        }

        sweepContacts.Add(overlapsAhead ? SweepContact.Inside(pushOut) : SweepContact.InsideWithoutPushOut());
        sweepColliders.Add(other);
    }

    /// <summary>A non-convex MeshCollider - a door frame, Door_01 and its kin - can never overlap via
    /// Physics.ComputePenetration (Unity does not support it for that shape; the call simply returns false).</summary>
    private static bool IsNonConvexMesh(Collider collider) => collider is MeshCollider mesh && !mesh.convex;

    /// <summary>Never blocked by our own body, and the ground is not a wall - a TerrainCollider and a non-convex
    /// MeshCollider are the two shapes Physics.ComputePenetration cannot be asked about (see AddStartInside's own
    /// fallback for the mesh case).</summary>
    private bool IsOwnOrGround(Collider other) =>
        other == null
        || other.attachedRigidbody == rb || other.transform.IsChildOf(transform)
        || other is TerrainCollider;
}
