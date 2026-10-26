using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;

/// <summary>
/// The one mover for a player's body over time other than walking: a dash's travel, a knockback's shove, a
/// blink's jump (PlayerMotor only walks under the player's own input). Priority between the three kinds is
/// DisplacementPriority. The ONLY writer of PlayerMotor.ExternalMotionControl and of rb.excludeLayers: a second
/// writer would fight over the Rigidbody, or leave a player walking through every barrier forever.
/// Barriers (GDD p.29): a Forced shove stops at one like a wall; a Voluntary move crosses it (StartMove excludes the
/// layer for that move, Settle then nudges the body clear before the exclusion comes off). A remote copy excludes
/// the layer once, in Start: its owner's body already obeys barriers, and a copy that collided would stall on screen.
/// Owner-only: every public method is a no-op on a copy that is not photonView.IsMine (a copy follows
/// PlayerMotor.SetNetworkTarget).
/// </summary>
public class PlayerDisplacement : MonoBehaviour, IDisplaceable
{
    private PhotonView photonView;
    private Rigidbody rb;
    private PlayerMotor motor;
    private PlayerLifecycle lifecycle;
    private Overpower.Dominion.IEffectShield effectShield; // Dominion respawn shield: stops an enemy's push while it is up (null in a scene without Dominion)

    // Not a design tunable: a dash tunable into walls would break the arena. Default is level geometry plus every
    // living player (a corpse's collider is off, see PlayerLifecycle); Building is the walls. Only forcedBlockMask
    // carries Barrier: AllowedTravel takes the mask by move kind, and CanStartVoluntary always uses the voluntary
    // one, so standing flush against a barrier never refuses a dash over it.
    //
    // Computed in Awake, NOT a static initializer: Unity throws TypeInitializationException if LayerMask.NameToLayer
    // runs from a MonoBehaviour's static constructor, which for a networked prefab is during PhotonNetwork.Instantiate.
    private int voluntaryBlockMask;
    private int forcedBlockMask;

    // True only between StartMove(Voluntary) and the settle that follows it clearing: the owner's own crossing of a
    // barrier. Guards every write of rb.excludeLayers here, so a remote copy's permanent exclusion (Start) is never touched.
    private bool ignoringBarriers;

    // Where the crossing move started, so Settle's last resort (no clear exit after MaxSettleSteps) can put the body
    // back exactly where it began rather than leave it stuck.
    private Vector3 moveStart;

    // True after a Voluntary move ended still overlapping a barrier's footprint (Finish), until Settle finds the body
    // clear and releases everything.
    private bool settlePending;
    private int settleStepCount;

    // Should never be reached in real play (a barrier is at most ~0.6 m thick and a settle step moves
    // the body clear in one shot) - a hard cap so a settle can never loop forever if the geometry is
    // ever wrong.
    private const int MaxSettleSteps = 5;

    // The player's own capsule: the shape every step is swept with. Read once in Awake, so a move is always checked
    // against the shape physics pushes around, never a second Inspector radius.
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
        effectShield = GetComponent<Overpower.Dominion.IEffectShield>();
        voluntaryBlockMask = LayerMask.GetMask("Default", "Building");
        forcedBlockMask = ArenaLayers.BodiesWallsAndBarriers;
        capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
            Debug.LogError($"[PlayerDisplacement] {name}: CapsuleCollider is missing - no move can be checked against a wall.");

        if (rb == null)
            Debug.LogError($"[PlayerDisplacement] {name}: Rigidbody is missing - nothing can be displaced.");
        if (motor == null)
            Debug.LogError($"[PlayerDisplacement] {name}: PlayerMotor is missing - ExternalMotionControl cannot be claimed or released.");

        // Subscribed in Awake, not Start: PlayerLifecycle's Start can already raise AliveChanged (a late joiner
        // learning this player is dead) before this component's Start would run.
        if (lifecycle != null)
            lifecycle.AliveChanged += HandleAliveChanged;
    }

    /// <summary>A copy only follows its owner, whose body already obeys barriers on its own machine; a copy that
    /// collided with one would stall on screen while its owner hopped over it. Set once, here, never touched again.</summary>
    private void Start()
    {
        if (rb != null && !photonView.IsMine)
            rb.excludeLayers |= ArenaLayers.Barrier;
    }

    /// <summary>
    /// Disabling is the one path a move in progress can vanish through without Finish running (the loadout swapping
    /// the prefab, the object pool recycling it, a domain reload). Without this, ExternalMotionControl stays stuck
    /// true and the player can never walk again. Unity calls OnDisable before OnDestroy, so this covers destroy too;
    /// OnDestroy only unsubscribes.
    /// </summary>
    private void OnDisable()
    {
        if (activeKind != null)
            Finish(DisplaceOutcome.Cancelled, null, rb != null ? rb.position : transform.position);

        // Release unconditionally, even if Finish just above decided a settle should keep going: a disabled object has
        // no FixedUpdate left to run one, and leaving it mid-settle would leave rb.excludeLayers permanently wrong.
        ReleaseAllBarrierAndMotionState();
    }

    private void OnDestroy()
    {
        if (lifecycle != null)
            lifecycle.AliveChanged -= HandleAliveChanged;
    }

    private void FixedUpdate()
    {
        if (!photonView.IsMine)
            return;

        // A settle in progress runs first: it finishes the correction the LAST move left behind, and a fresh move
        // (started from that move's onEnd) already cleared settlePending in StartMove if it wants to take over.
        if (settlePending && Settle())
            return;

        if (activeKind == null)
            return;

        int mask = activeKind == DisplaceKind.Forced ? forcedBlockMask : voluntaryBlockMask;
        float step = Mathf.Min(speed * Time.fixedDeltaTime, remainingDistance);
        Vector3 fromPosition = rb.position;

        // One stop rule for a dash, a zip pull and a knockback (DisplacementSweepRule): the player's own capsule is
        // swept along the step and stops a skin width short of the first wall, and a wall it already overlaps blocks
        // only a move deeper into it. Rigidbody.SweepTestAll does not report a wall the capsule was already pressed
        // into by walking, so a second dash from there would go straight through a thin wall.
        float allowed = AllowedTravel(fromPosition, direction, step, mask, out Collider blocker);
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

    /// <summary>
    /// A knockback somebody else caused (the sonic pulse): Forced like Displace, but it knows who pushed. A shielded
    /// player (Dominion respawn bubble) is not pushed by an enemy: nothing moves, BLOCKED is stamped, and onEnd never
    /// fires (nothing started, the contract a refused DisplaceVoluntary has). On a copy nothing moves either, but when
    /// the push is this client's own player's on a living enemy, its respawn shield hears of it (A25).
    /// </summary>
    public void Displace(Vector3 direction, float distance, float speed, Action<DisplaceEnd> onEnd, int sourceActor)
    {
        if (!photonView.IsMine)
        {
            Overpower.Dominion.RespawnShield.NoteMyEffectOnCopy(photonView, sourceActor);
            return;
        }

        if (effectShield != null && effectShield.StopsEnemyEffectFrom(sourceActor))
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
    /// Read-only query for a blink to check BEFORE it spends its charge: TeleportTo refuses while a Forced move
    /// is running, and by the time it runs the caster's charge is already spent and cannot be handed back.
    /// </summary>
    public bool CanTeleport => DisplacementPriority.Accepts(activeKind, DisplaceKind.Teleport);

    /// <summary>
    /// Read-only query for a dash to check BEFORE it spends its charge: false while a knockback is running, and
    /// false when a Voluntary move along <paramref name="direction"/> could not cover even a centimetre because the
    /// player is touching, or pressed into, a wall that way. It asks the same rule the move asks every step, so
    /// "refused" and "would have gone nowhere" cannot disagree.
    /// </summary>
    public bool CanStartVoluntary(Vector3 direction)
    {
        if (!photonView.IsMine || rb == null)
            return false;

        if (!DisplacementPriority.Accepts(activeKind, DisplaceKind.Voluntary))
            return false;

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        // Always the voluntary mask, never forcedBlockMask: a barrier never stops a dash, so standing flush against
        // one must not refuse the cast the way standing flush against a real wall does.
        float allowed = AllowedTravel(rb.position, dir, DisplacementSweepRule.MinUsefulTravelMetres, voluntaryBlockMask, out _);
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

        if (kind == DisplaceKind.Voluntary)
        {
            // A dash or zip pull crosses a barrier (GDD p.29): excluded for the whole move, so the sweep never sees it.
            // A fresh Voluntary move owns the crossing from here, so any settle the PREVIOUS move left pending is
            // superseded, not raced against. Capture moveStart only when no crossing is ALREADY running: a dash chained
            // from the onEnd of one that ended inside a barrier's footprint (before Settle got a step) would record a
            // position inside the barrier, and the both-candidates-blocked fallback would return the body into it.
            if (!ignoringBarriers)
                moveStart = rb.position;
            ignoringBarriers = true;
            rb.excludeLayers |= ArenaLayers.Barrier;
            settlePending = false;
            settleStepCount = 0;
        }

        if (motor != null)
            motor.ExternalMotionControl = true;
    }

    private void Finish(DisplaceOutcome outcome, Collider blocker, Vector3 endPosition)
    {
        Action<DisplaceEnd> callback = onEnd;

        activeKind = null;
        onEnd = null;
        remainingDistance = 0f;

        // A Voluntary move that ended still inside a barrier's footprint (it stopped partway, or was cancelled
        // mid-crossing) needs a settle before the exclusion and ExternalMotionControl come off, or the body would rest
        // fused into the barrier's collider. Check endPosition as well as rb.position: rb.MovePosition schedules the
        // move for PhysX's NEXT step, so rb.position still reads last step's pose and a move landing inside a barrier
        // would read as clear one step early. Settle re-checks the real pose next step, so this only widens when a
        // settle starts.
        if (ignoringBarriers && capsule != null &&
            (PlayerSpaceProbe.IsInsideBarrier(capsule, rb.position, transform) ||
             PlayerSpaceProbe.IsInsideBarrier(capsule, endPosition, transform)))
        {
            settlePending = true;
            settleStepCount = 0;
        }
        else
        {
            ReleaseBarrierExclusionAndControl();
        }

        callback?.Invoke(new DisplaceEnd(outcome, endPosition, blocker));
    }

    /// <summary>Releases ExternalMotionControl, and the Barrier exclusion if this move was the one holding it - the
    /// ordinary end of a move (or a settle finding the body clear). A new move started from the callback above
    /// takes ExternalMotionControl (and, if Voluntary, the exclusion) straight back before anyone else can act on
    /// this player being "free" for even one frame.</summary>
    private void ReleaseBarrierExclusionAndControl()
    {
        if (ignoringBarriers)
        {
            rb.excludeLayers &= ~ArenaLayers.Barrier;
            ignoringBarriers = false;
        }
        settlePending = false;
        settleStepCount = 0;

        // Release ExternalMotionControl only while no move is running. Finish nulls activeKind first, so an ordinary
        // end is unaffected; but Settle can run while a DIFFERENT, still-active move owns activeKind (a Forced
        // knockback that cancelled a dash mid-crossing leaves settlePending true and becomes the active move;
        // StartMove does not clear settlePending for a Forced kind). Releasing unconditionally let PlayerMotor's
        // walking resume beside the knockback: two MovePosition calls fighting over the Rigidbody.
        if (motor != null && activeKind == null)
            motor.ExternalMotionControl = false;
    }

    /// <summary>Death and OnDisable release every barrier-crossing and motion-control flag at once, unlike
    /// ReleaseBarrierExclusionAndControl: a corpse has no collider to be inside a barrier with, and a respawn
    /// teleports elsewhere, so nothing is left worth settling.</summary>
    private void ReleaseAllBarrierAndMotionState()
    {
        activeKind = null;
        onEnd = null;
        remainingDistance = 0f;
        settlePending = false;
        settleStepCount = 0;
        if (ignoringBarriers)
        {
            if (rb != null)
                rb.excludeLayers &= ~ArenaLayers.Barrier;
            ignoringBarriers = false;
        }
        if (motor != null)
            motor.ExternalMotionControl = false;
    }

    /// <summary>
    /// Runs once per physics step while a Voluntary move ended overlapping a barrier's footprint (Finish set
    /// settlePending). Nudges the body toward the nearer clear exit (BarrierCrossingRule.ExitPoints) and returns
    /// true while still correcting; false once clear (releasing the exclusion and ExternalMotionControl the same
    /// way an ordinary Finish does) or after MaxSettleSteps gives up and teleports back to where the crossing
    /// started - this should never fire in real play, so it logs loudly if it ever does.
    /// </summary>
    private bool Settle()
    {
        Collider barrierCollider = FindOverlappedBarrier();
        if (barrierCollider == null)
        {
            ReleaseBarrierExclusionAndControl();
            return false;
        }

        BoxCollider box = barrierCollider as BoxCollider;
        if (box == null)
        {
            Debug.LogError($"[PlayerDisplacement] {name}: the Barrier collider '{barrierCollider.name}' is not a " +
                            "BoxCollider - can't build a footprint to settle against. Releasing without settling.");
            ReleaseBarrierExclusionAndControl();
            return false;
        }

        settleStepCount++;
        if (settleStepCount > MaxSettleSteps)
        {
            Debug.LogError($"[PlayerDisplacement] {name}: a barrier settle ran {MaxSettleSteps} steps without " +
                            "clearing - this should never happen. Teleporting back to where the crossing started.");
            rb.position = moveStart;
            ReleaseBarrierExclusionAndControl();
            return false;
        }

        Transform barrierTransform = box.transform;
        // TransformPoint(box.center), not the transform's position: the built barrier's collider is recentred on the
        // blocking band, offset from its transform's origin, and would otherwise build the footprint around the wrong point.
        BoxFootprint footprint = BoxFootprint.FromBox(barrierTransform.TransformPoint(box.center), barrierTransform.rotation,
            Vector3.Scale(box.size, barrierTransform.lossyScale));

        Vector2 centreXZ = new Vector2(rb.position.x, rb.position.z);
        Vector2 dirXZ = direction.sqrMagnitude > 0.0001f ? new Vector2(direction.x, direction.z) : Vector2.right;
        BarrierCrossingRule.ExitPoints(centreXZ, dirXZ, capsule.radius, footprint, out Vector2 firstXZ, out Vector2 otherXZ);

        Vector3 firstCandidate = new Vector3(firstXZ.x, rb.position.y, firstXZ.y);
        Vector3 otherCandidate = new Vector3(otherXZ.x, rb.position.y, otherXZ.y);

        Vector3 chosen;
        if (!PlayerSpaceProbe.IsCapsuleBlocked(capsule, firstCandidate, ArenaLayers.WallsAndBarriers, transform))
            chosen = firstCandidate;
        else if (!PlayerSpaceProbe.IsCapsuleBlocked(capsule, otherCandidate, ArenaLayers.WallsAndBarriers, transform))
            chosen = otherCandidate;
        else
            chosen = moveStart;

        rb.MovePosition(chosen);
        return true;
    }

    /// <summary>The Barrier collider this player's own capsule overlaps right now, or null. Reuses the same
    /// overlap buffer AllowedTravel uses - a settle never runs in the same physics step as an active sweep.</summary>
    private Collider FindOverlappedBarrier()
    {
        Vector3 centre = rb.position + rb.rotation * capsule.center;
        float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
        int count = Physics.OverlapCapsuleNonAlloc(centre + Vector3.up * halfSegment, centre - Vector3.up * halfSegment,
            capsule.radius, overlapHits, ArenaLayers.Barrier, QueryTriggerInteraction.Ignore);
        return count > 0 ? overlapHits[0] : null;
    }

    /// <summary>A stun cancels only through the ability that is running (AbilityModule.Interrupt); death cancels
    /// unconditionally, here, so a dash or knockback never carries a corpse. Also releases every barrier flag
    /// (a corpse's collider is off, see PlayerLifecycle, so there is nothing for a settle to correct).</summary>
    private void HandleAliveChanged(bool alive)
    {
        if (!alive)
        {
            Cancel();
            ReleaseAllBarrierAndMotionState();
        }
    }

    /// <summary>
    /// How far the capsule may travel from <paramref name="from"/> along <paramref name="dir"/> (unit length), up to
    /// <paramref name="distance"/>, against <paramref name="mask"/>, and the collider that stops it (null when
    /// nothing does). The caller picks voluntaryBlockMask or forcedBlockMask by move kind. This half only gathers
    /// physics facts; DisplacementSweepRule decides and is tested in edit mode.
    /// </summary>
    private float AllowedTravel(Vector3 from, Vector3 dir, float distance, int mask, out Collider blocker)
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
        int overlapCount = Physics.OverlapCapsuleNonAlloc(bottom, top, capsule.radius, overlapHits, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
            AddStartInside(overlapHits[i], probe, rotation, dir);

        int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, capsule.radius, dir, sweepHits,
            distance + DisplacementSweepRule.SkinMetres, mask, QueryTriggerInteraction.Ignore);
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

        // Only static geometry may refuse a dash outright (DisplacementSweepRule.CanBlockAsStartInside): a living
        // player has a Rigidbody, a wall does not. Without this, standing flush against an enemy read like standing
        // flush against a wall, and a player in melee could never dash away. A dash travelling TOWARD a player from a
        // distance still stops at their body (cast loop above).
        if (!DisplacementSweepRule.CanBlockAsStartInside(other.attachedRigidbody != null))
            return;

        Transform otherTransform = other.transform;
        bool overlapsAhead = Physics.ComputePenetration(capsule, probe, rotation, other, otherTransform.position,
            otherTransform.rotation, out Vector3 pushOut, out float depth) && depth > 0f;

        if (!overlapsAhead && IsNonConvexMesh(other))
        {
            // Physics.ComputePenetration is unsupported for a non-convex MeshCollider (a door frame) and always
            // returns false for one, so a player pressed into it was invisible to the overlap check and a second dash
            // sailed through. A raycast toward the move, starting just short of the capsule so it does not start
            // embedded in the mesh (a raycast starting inside a shape reports no hit on it either), stands in for
            // ComputePenetration's push-out direction.
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

    /// <summary>A non-convex MeshCollider (a door frame) cannot be asked via Physics.ComputePenetration; Unity
    /// returns false for it.</summary>
    private static bool IsNonConvexMesh(Collider collider) => collider is MeshCollider mesh && !mesh.convex;

    /// <summary>Never blocked by our own body, and the ground is not a wall (a TerrainCollider is a shape
    /// Physics.ComputePenetration cannot be asked about).</summary>
    private bool IsOwnOrGround(Collider other) =>
        other == null
        || other.attachedRigidbody == rb || other.transform.IsChildOf(transform)
        || other is TerrainCollider;
}
