using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// Places a personal gate on the ground; standing in it channels you to its pair. Tudor's spec:
    /// a 2.5m circle within 5m of the player, at most two down at once, a 3-second channel, a 10-
    /// second cooldown before the gate can be used again.
    ///
    /// FIT WITH THE FRAMEWORK. Pressing Shift PLACES a portal - it does not travel. The base pool
    /// (cooldownSeconds/charges, on AbilityModule) is the GATE cooldown: 1 charge, 10s, and
    /// SpendsChargeOnCast is overridden false so PLACING never touches it. The charge is spent only
    /// when a channel actually completes (SpendCharge() in OwnerTick) - which is also what starts the
    /// 10s gate cooldown counting. One side effect, not a special case: while that charge is on
    /// cooldown, AbilityRunner's own CastGate already refuses a new placement before TryBuildCast
    /// ever runs (module.HasChargeGate &amp;&amp; !module.HasCharge) - "placing is blocked during the
    /// gate cooldown" needed no extra code here.
    ///
    /// A portal is a REAL networked object (NetworkedDeployable/Portal), not something this module
    /// draws locally - a player joining mid-match has to see a gate that has been standing there for
    /// two minutes. Placing therefore happens in ExecuteCast's IsCasterClient branch, exactly where
    /// FireField's own spawn happens, not in TryBuildCast: TryBuildCast only decides WHERE (clamped
    /// to Placement Range, then GROUNDED and validated exactly like Blink's own destination check -
    /// see GroundProbe.TryFindGround - refused if there is no ground within reach, it sits below the
    /// kill plane, or the grounded spot is blocked) and hands the built-once placement counter along
    /// as the payload's IntArg.
    ///
    /// THE CHANNEL ITSELF is pure logic in PortalChannelState (Assets/scripts/Combat) - this class
    /// only supplies, every OwnerTick, "which of my own portals (if any) am I standing in" and
    /// "can I channel right now" (a pair exists AND the gate has a charge AND canAct), and reacts to
    /// whatever PortalChannelState reports by sending the matching phase: 1 when a channel starts, 3
    /// when it cancels (both purely visual - see ExecuteCast), and the actual travel at Completed,
    /// which spends the charge and hands the moment to ExecuteCast's IsCasterClient branch exactly
    /// like Blink hands its jump to Owner.Displacement.TeleportTo.
    /// </summary>
    public sealed class TeleportAbility : AbilityModule
    {
        // Phase numbers this module defines. 0 is always the cast itself (placing a portal).
        private const byte PhaseChannelStart = 1;
        private const byte PhaseTravelled = 2;
        private const byte PhaseChannelCancel = 3;

        [Header("Placement")]
        [SerializeField, Tooltip("How far from the player, in metres, a portal can be placed. The " +
                 "cursor's ground point is clamped to this distance when it points further away.")]
        private float placementRange = 5f;

        [SerializeField, Tooltip("How many portals one player can have down at once. Placing one " +
                 "more than this destroys the OLDEST portal first - the newest two are always the " +
                 "ones that work.")]
        private int maxPortals = 2;

        [SerializeField, Tooltip("The portal that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Its own Portal " +
                 "Diameter field is the single home for how big a portal is; this ability only reads " +
                 "that value to send along with the placement, and reads the prefab's name to spawn it.")]
        private GameObject portalPrefab;

        [Header("Placement - ground probe (same rule as Blink)")]
        [SerializeField, Tooltip("How far, in metres, BELOW the player's OWN current height the " +
                 "ground is allowed to be for a placement spot to count as solid ground. Too small " +
                 "refuses a valid spot on a gentle slope or a step down; too large can accept a spot " +
                 "far below the arena - past a thin floor - as if it were ground.")]
        private float groundProbeDistance = 2f;

        [SerializeField, Tooltip("How far, in metres, ABOVE the player's OWN current height the " +
                 "ground is allowed to be - a small step or curb, not a roof. A portal stays at " +
                 "roughly the caster's own level, the same reason Blink's own step-up is small.")]
        private float maxStepUp = 0.6f;

        [Header("Channel")]
        [SerializeField, Tooltip("Seconds you must stand inside your own portal, without leaving, " +
                 "before it teleports you to its pair. Interrupted by leaving the circle or by dying, " +
                 "being stunned or silenced - see PortalChannelState.")]
        private float channelSeconds = 3f;

        [Header("Remote visuals (cosmetic only)")]
        [SerializeField, Tooltip("Radius in metres of the marker shown at a portal while ANYONE - " +
                 "your own screen included - is channelling in it. Purely visual; 0 shows nothing.")]
        private float channelVfxRadius = 0.6f;

        [SerializeField, Tooltip("Radius in metres of the brief marker shown at both portals, on " +
                 "every OTHER client's screen, the moment a travel completes. The caster's own screen " +
                 "already shows the real teleport. 0 shows nothing.")]
        private float arrivalVfxRadius = 0.5f;

        [SerializeField, Tooltip("Seconds the arrival marker stays up before it disappears on its own.")]
        private float arrivalVfxSeconds = 0.3f;

        // Not a design tunable, like BlinkAbility's own blockMask: Default|Building is both what
        // counts as ground (the arena floor sits on Default, same as every living player; roofs and
        // walls are Building) and what a placement may not overlap once grounded - see IsBlocked.
        // Fixed here rather than exposed, the same reasoning PlayerDisplacement gives for its mask.
        private int blockMask;

        // Owner only: the portal template's own numbers (diameter), read once so TryBuildCast's
        // validity check and ExecuteCast's spawn agree on the same radius without a second
        // GetComponent every cast. Also doubles as the "is Portal Prefab actually usable" check.
        private Portal portalTemplate;

        // Owner only: increments once per successful placement, travels as CastPayload.IntArg so
        // every client's Portal.Seq (and this owner's own pruning) agree on placement order. Safe to
        // restart at 0 on every fresh equip (a new module instance) ONLY because Interrupt(Unequipped)
        // destroys every portal this owner had before the old instance goes away - there is never a
        // live portal left whose Seq could collide with a restarted counter.
        private int nextSeq;

        // Owner only: the pure channel timer this module drives every OwnerTick.
        private PortalChannelState channelState;

        // Owner only: which of the owner's own portals is currently mid-channel, or null - kept only
        // so IsActive (the HUD glow) can answer without asking PortalChannelState for its private state.
        private Portal channelingPortal;

        // Every client: the cosmetic marker for a channel in progress on THIS screen, and the count-
        // down that turns the brief arrival marker off without a second network message.
        private GameObject channelVfx;

        public override bool IsActive => channelingPortal != null;

        protected override bool SpendsChargeOnCast => false;

        private void Awake()
        {
            blockMask = LayerMask.GetMask("Default", "Building");
            channelState = new PortalChannelState(channelSeconds);
        }

        public override void OnEquip()
        {
            portalTemplate = portalPrefab != null ? portalPrefab.GetComponent<Portal>() : null;

            if (portalPrefab == null)
                Debug.LogError($"[TeleportAbility] {name}: Portal Prefab is not assigned - teleport cannot place anything.");
            else if (portalTemplate == null)
                Debug.LogError($"[TeleportAbility] {name}: Portal Prefab '{portalPrefab.name}' has no Portal component.");
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            placementRange = Mathf.Max(0f, placementRange);
            maxPortals = Mathf.Max(1, maxPortals);
            channelSeconds = Mathf.Max(0.01f, channelSeconds);
            channelState?.SetChannelSeconds(channelSeconds);
            groundProbeDistance = Mathf.Max(0f, groundProbeDistance);
            maxStepUp = Mathf.Max(0f, maxStepUp);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (portalTemplate == null || Owner.Motor == null)
                return false; // OnEquip already logged the missing prefab; a missing Motor means no KillHeight to check the ground against.

            Vector3 flatXZ = ClampToRange(ctx.Origin, ctx.TargetPoint, placementRange);

            // GroundPointUnderCursor (ctx.TargetPoint) is only a flat math plane at the player's own
            // Y - PlayerAim never asks Physics what is actually there - so without this the clamped
            // point could sit over a void, off the map edge, or buried/floating on a slope, and
            // TeleportTo would happily put the player there later. Same rule as Blink's own
            // destination check (GroundProbe's class comment), refused with nothing spent if it fails.
            if (!GroundProbe.TryFindGround(ctx.Origin.y, flatXZ, maxStepUp, groundProbeDistance,
                    Owner.Motor.KillHeight, blockMask, Owner.Root.transform, out Vector3 ground))
                return false;

            if (IsBlocked(ground))
                return false; // refuse, nothing spent - see the class comment on why placing never spends the gate charge anyway.

            payload = new CastPayload { Origin = ctx.Origin, Point = ground, IntArg = nextSeq };
            nextSeq++;
            return true;
        }

        public override void OwnerTick(float deltaTime, bool held, bool canAct)
        {
            IReadOnlyList<Portal> mine = Portal.ForOwner(Owner.ActorNumber);
            Portal current = FindStandingPortal(mine);
            Portal other = current != null ? FindOther(mine, current) : null;

            bool canChannel = canAct && other != null && HasCharge;
            PortalChannelState.Result result = channelState.Tick(deltaTime, current, canChannel);

            switch (result)
            {
                case PortalChannelState.Result.Started:
                    channelingPortal = current;
                    SendPhase(PhaseChannelStart, new CastPayload { Origin = current.transform.position, Point = current.transform.position });
                    break;

                case PortalChannelState.Result.Cancelled:
                    channelingPortal = null;
                    SendPhase(PhaseChannelCancel, default);
                    break;

                case PortalChannelState.Result.Completed:
                    CompleteTravel(current, other);
                    break;
            }
        }

        public override void Interrupt(InterruptReason reason)
        {
            // Portals are persistent, owner-only state - like an armor upgrade, not like a dash mid-
            // flight - so only Unequipped removes them. A death or a stun must not touch them at all;
            // Tudor's decision.
            if (reason == InterruptReason.Unequipped)
                DestroyOwnPortals();

            // The channel marker is purely cosmetic and per-client, so it must stop on every client
            // regardless of why this was called - mirrors DebugPingAbility.Interrupt.
            ClearChannelVfx();

            if (channelingPortal != null)
            {
                channelingPortal = null;
                channelState.Reset();
                // No SendPhase(Cancel) here: Died/Stunned/Silenced are followed almost immediately by
                // OwnerTick's own canAct==false path on the owner, which already sends it - sending it
                // twice would only be a harmless, noisy duplicate RPC. Unequipped has no OwnerTick left
                // to follow it, but also nobody left to show a cancel to: the module (and its portals,
                // just destroyed above) is about to be gone.
            }
        }

        private void CompleteTravel(Portal from, Portal to)
        {
            channelingPortal = null;
            // Spent here, before SendPhase/ExecuteCast actually calls TeleportTo - so a Forced
            // displacement (a knockback) that sneaks in during the one frame before ExecuteCast runs
            // can still win the race and the charge is already gone, unrefundable - the same accepted
            // race BlinkAbility.ExecuteCast's own comment documents for its jump.
            SpendCharge();
            channelState.LatchArrival(to);
            SendPhase(PhaseTravelled, new CastPayload { Origin = from.transform.position, Point = to.transform.position });
        }

        private void PruneOldest()
        {
            IReadOnlyList<Portal> mine = Portal.ForOwner(Owner.ActorNumber);
            var seqs = new List<int>(mine.Count);
            foreach (Portal p in mine)
                seqs.Add(p.Seq);

            foreach (int seq in DeployablePruning.OverflowBySeq(seqs, maxPortals))
            {
                foreach (Portal p in mine)
                {
                    if (p.Seq == seq)
                    {
                        PhotonNetwork.Destroy(p.gameObject);
                        break;
                    }
                }
            }
        }

        private void DestroyOwnPortals()
        {
            if (!Owner.IsMine)
                return; // Only the owner may PhotonNetwork.Destroy these - see FireField.Burn's "eight errors" lesson.

            // Copied first: PhotonNetwork.Destroy leads to Portal.OnDestroy unregistering itself from
            // the very list this loop would otherwise be mutating while iterating it.
            var mine = new List<Portal>(Portal.ForOwner(Owner.ActorNumber));
            foreach (Portal p in mine)
            {
                if (p != null)
                    PhotonNetwork.Destroy(p.gameObject);
            }
        }

        private Portal FindStandingPortal(IReadOnlyList<Portal> mine)
        {
            Vector3 position = Owner.Root.transform.position;
            Portal best = null;
            float bestDistanceSqr = float.MaxValue;

            foreach (Portal p in mine)
            {
                if (p == null)
                    continue;

                Vector3 delta = p.transform.position - position;
                delta.y = 0f;
                float distanceSqr = delta.sqrMagnitude;
                float radiusSqr = p.Radius * p.Radius;

                if (distanceSqr <= radiusSqr && distanceSqr < bestDistanceSqr)
                {
                    best = p;
                    bestDistanceSqr = distanceSqr;
                }
            }

            return best;
        }

        /// <summary>The paired portal to travel to - the most recently placed of the owner's OTHER
        /// portals. With the normal cap of two this is simply "the other one"; picking the newest
        /// among more than one (only possible for the one frame before a same-frame prune finishes)
        /// keeps the answer deterministic without waiting on that prune.</summary>
        private static Portal FindOther(IReadOnlyList<Portal> mine, Portal current)
        {
            Portal best = null;
            foreach (Portal p in mine)
            {
                if (p == null || p == current)
                    continue;
                if (best == null || p.Seq > best.Seq)
                    best = p;
            }
            return best;
        }

        // ---- every client ---------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            switch (cast.Phase)
            {
                case 0:
                    if (cast.IsCasterClient)
                        PlacePortal(cast.Payload);
                    return;

                case PhaseChannelStart:
                    ClearChannelVfx();
                    channelVfx = SpawnMarker(cast.Payload.Point, channelVfxRadius);
                    return;

                case PhaseTravelled:
                    ClearChannelVfx();
                    if (cast.IsCasterClient)
                    {
                        var displacement = Owner.Displacement as PlayerDisplacement;
                        if (displacement == null || !displacement.TeleportTo(cast.Payload.Point))
                        {
                            // Only possible if a knockback started in the single frame between the
                            // channel completing and here - the same race BlinkAbility's own class
                            // comment documents. The charge is already spent and cannot be refunded.
                            Debug.LogWarning($"[TeleportAbility] {name}: teleport refused at execute " +
                                              "time - a knockback must have started after the channel completed.");
                        }
                    }
                    else
                    {
                        PlayArrivalVfx(cast.Payload.Origin);
                        PlayArrivalVfx(cast.Payload.Point);
                    }
                    return;

                case PhaseChannelCancel:
                    ClearChannelVfx();
                    return;
            }
        }

        /// <summary>Caster only (called from ExecuteCast's phase-0, IsCasterClient branch). Spawns the
        /// networked portal and prunes the oldest if that puts this owner over Max Portals.</summary>
        private void PlacePortal(CastPayload payload)
        {
            object[] data = { portalTemplate.PortalDiameter, payload.IntArg };
            GameObject spawned = NetworkedDeployable.Spawn(portalPrefab.name, payload.Point, data);
            if (spawned == null)
                return; // Spawn already logged why.

            PruneOldest();
        }

        private static Vector3 ClampToRange(Vector3 origin, Vector3 requested, float range)
        {
            Vector3 originXZ = new Vector3(origin.x, 0f, origin.z);
            Vector3 requestedXZ = new Vector3(requested.x, 0f, requested.z);
            Vector3 toRequested = requestedXZ - originXZ;
            float distance = toRequested.magnitude;

            if (distance <= range || distance < 0.0001f)
                return requested;

            Vector3 clampedXZ = originXZ + toRequested / distance * range;
            return new Vector3(clampedXZ.x, requested.y, clampedXZ.z);
        }

        // The check volume's vertical band above the grounded point - not a design tunable, the
        // same reasoning as blockMask: an arbitrary human-height band, not something a designer
        // should be able to mis-set into checking the wrong height entirely.
        private const float BlockCheckBottom = 0.1f;
        private const float BlockCheckTop = 1.8f;

        /// <summary>
        /// True if a portal-sized volume at this already-grounded point would overlap a wall or
        /// another player's body - the same Default|Building mask and self-exclusion rule
        /// BlinkAbility.IsCapsuleBlocked uses, so a portal is refused by the same "something solid
        /// occupies this spot" idea as any other destination check.
        ///
        /// A BOX, not a capsule like Blink's own check: Physics.OverlapCapsule's two hemispherical
        /// end caps extend a FURTHER radius beyond the two points passed to it. That is invisible for
        /// Blink's player capsule, whose radius is well under half its height, but not for a 1.25m
        /// portal radius against a band only 1.7m tall - built the same way, the bottom cap would dip
        /// (radius - halfBandHeight) below the intended floor, back down into the ground itself. Once
        /// Default (the floor's own layer) is in the mask, as it now is, every placement on ordinary
        /// flat ground would read as "blocked" by the floor no candidate could ever clear. A box's
        /// flat faces have no such overshoot - found by a live placement test refusing a portal on
        /// perfectly open ground once this mask changed to match Blink's.
        /// </summary>
        private bool IsBlocked(Vector3 groundPoint)
        {
            float radius = portalTemplate.Radius;
            Vector3 center = groundPoint + Vector3.up * ((BlockCheckBottom + BlockCheckTop) * 0.5f);
            Vector3 halfExtents = new Vector3(radius, (BlockCheckTop - BlockCheckBottom) * 0.5f, radius);

            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, Quaternion.identity, blockMask, QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap.transform.IsChildOf(Owner.Root.transform))
                    continue; // never blocked by the caster's own body.
                return true;
            }
            return false;
        }

        private void ClearChannelVfx()
        {
            if (channelVfx != null)
                Destroy(channelVfx);
            channelVfx = null;
        }

        private void PlayArrivalVfx(Vector3 point)
        {
            if (arrivalVfxRadius <= 0f)
                return;
            Destroy(SpawnMarker(point, arrivalVfxRadius), arrivalVfxSeconds);
        }

        private static GameObject SpawnMarker(Vector3 point, float radius)
        {
            if (radius <= 0f)
                return null;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Teleport VFX (cheap, cosmetic only)";
            // Removed immediately, not with Destroy, which waits for the end of the frame - see
            // BlinkAbility.PlayRemoteVfx's identical trick: for that one frame the sphere would
            // otherwise be a solid object sitting in the world.
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * radius * 2f;
            return marker;
        }
    }
}
