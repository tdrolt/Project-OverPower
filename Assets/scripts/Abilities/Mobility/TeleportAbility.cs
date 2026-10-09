using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Vision;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// Places a teleport gate on the ground, usable by you and your teammates; standing in it channels you to its pair
    /// (Placement Range, Max Portals, Channel Seconds and the gate cooldown are on this module). Pressing Shift PLACES, it
    /// does not travel: SpendsChargeOnCast is false, the gate charge is spent only when a channel completes (which starts
    /// the cooldown), and AbilityRunner's CastGate already refuses placing while that charge is on cooldown. A portal is a
    /// REAL networked object (NetworkedDeployable/Portal) so a late joiner sees it: placing happens in ExecuteCast's
    /// IsCasterClient branch, TryBuildCast only decides WHERE (grounded and validated like Blink, via GroundProbe) and
    /// carries the placement counter as IntArg. The channel is pure logic in PortalChannelState; this class feeds it each
    /// OwnerTick and sends phases: 1 channel start, 3 cancel (visual only), 2 travelled (the jump, in ExecuteCast).
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
        [SerializeField, Tooltip("Seconds you (or a teammate) must stand on one of these portals, without leaving, " +
                 "before it teleports you to its pair. Interrupted by leaving the circle or by dying, " +
                 "being stunned or silenced - see PortalChannelState. Teammates use the same number on your portals. " +
                 "A change only reaches portals placed after it (the value is sent when a portal is placed).")]
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

        // Not a design tunable (like BlinkAbility's blockMask): Default|Building is both what counts as ground (the arena
        // floor sits on Default, roofs and walls are Building) and what a placement may not overlap - see IsBlocked.
        private int blockMask;

        // The caster's own capsule, read in OnEquip like Blink's: a portal is placed for a player to arrive on, so it
        // is checked against the real player shape.
        private CapsuleCollider capsule;

        // The owner's Rigidbody, read in OnEquip: FindStandingPortal reads its position, which is up to date right after TeleportTo.
        private Rigidbody body;

        // Owner only: the prefab's Portal, read once so TryBuildCast and ExecuteCast agree on the radius; null means
        // Portal Prefab is not usable.
        private Portal portalTemplate;

        // Owner only: once per placement, travels as CastPayload.IntArg so every client's Portal.Seq (and the pruning)
        // agree on order. Restarting at 0 on a fresh equip is safe ONLY because Interrupt(Unequipped) destroys every
        // portal first, so no live Seq can collide.
        private int nextSeq;

        // Owner only: the pure channel timer this module drives every OwnerTick.
        private PortalChannelState channelState;

        // Owner only: the last "has a portal charge" answer sent to teammates, null until the first publish.
        private bool? lastPublishedReady;

        // Owner only: the portal currently mid-channel, or null - so IsActive (the HUD glow) needn't ask PortalChannelState.
        private Portal channelingPortal;

        // Every client: the cosmetic marker for a channel in progress on THIS screen.
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

            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            body = Owner.Root.GetComponent<Rigidbody>();
            if (capsule == null)
                Debug.LogError($"[TeleportAbility] {name}: the player has no CapsuleCollider - a portal cannot be " +
                                "checked for the player who would arrive on it, so placing will always refuse.");
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

            if (portalTemplate == null || Owner.Motor == null || capsule == null)
                return false; // OnEquip already logged the missing prefab or capsule; a missing Motor means no KillHeight to check the ground against.

            Vector3 flatXZ = ClampToRange(ctx.Origin, ctx.TargetPoint, placementRange);

            // ctx.TargetPoint is only a flat math plane at the player's own Y (PlayerAim never asks Physics), so the clamped
            // point could sit over a void, off the map edge, or buried/floating on a slope and TeleportTo would put the
            // player there later. Same rule as Blink's destination check (GroundProbe); refused with nothing spent.
            if (!GroundProbe.TryFindGround(ctx.Origin.y, flatXZ, maxStepUp, groundProbeDistance,
                    Owner.Motor.KillHeight, blockMask, Owner.Root.transform, out Vector3 ground))
                return false;

            if (IsBlocked(ground))
                return false; // refuse, nothing spent.

            // Inside the arena with room for the player who arrives on it. The terrain carries on past the boundary
            // walls, so the ground probe alone would place a gate outside the arena.
            if (!Overpower.Arena.ArenaSymmetry.IsInsideArena(ground, capsule.radius))
                return false;

            // A portal's path is blocked by the arena's own boundary walls only, never by a crate, a house or deployable
            // cover. PlayerSpaceProbe.IsPathClear checks the whole Building layer (right for a mine), so this asks
            // ArenaSymmetry's boundary-only sweep instead, at the same knee height and probe radius.
            Vector3 feetKnee = PlayerSpaceProbe.FeetOf(capsule, ctx.Origin) + Vector3.up * PlayerSpaceProbe.KneeHeightMetres;
            Vector3 groundKnee = ground + Vector3.up * PlayerSpaceProbe.KneeHeightMetres;
            if (Overpower.Arena.ArenaSymmetry.PathCrossesBoundary(feetKnee, groundKnee, PlayerSpaceProbe.PathProbeRadiusMetres))
                return false;

            // After a rejoin the portals PUN kept have higher Seqs than this fresh module's counter: continue above them.
            IReadOnlyList<Portal> standing = Portal.ForOwner(Owner.ActorNumber);
            var standingSeqs = new List<int>(standing.Count);
            foreach (Portal p in standing)
                standingSeqs.Add(p.Seq);
            nextSeq = DeployablePruning.NextSeq(nextSeq, standingSeqs);

            payload = new CastPayload { Origin = ctx.Origin, Point = ground, IntArg = nextSeq };
            nextSeq++;
            return true;
        }

        public override void OwnerTick(float deltaTime, bool held, bool canAct)
        {
            PublishReadyIfChanged(HasCharge);

            IReadOnlyList<Portal> mine = Portal.ForOwner(Owner.ActorNumber);
            Portal current = FindStandingPortal(mine);
            Portal other = current != null ? FindOther(mine, current) : null;

            // The exit is re-checked every tick, so a wall, crate or cover built on it later can't channel anyone into
            // geometry or out of the arena. Part of the gate, not a refusal at travel time: by then the charge is spent.
            bool canChannel = canAct && other != null && HasCharge && IsExitClear(other);
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
            // Portals are persistent, owner-only state (like an armor upgrade, not a dash mid-flight): only Unequipped
            // removes them. A death or a stun must not touch them.
            if (reason == InterruptReason.Unequipped)
            {
                DestroyOwnPortals();
                PublishReadyIfChanged(false);
            }

            // The channel marker is cosmetic and per-client, so it stops on every client whatever the reason.
            ClearChannelVfx();

            if (channelingPortal != null)
            {
                channelingPortal = null;
                channelState.Reset();
                // No SendPhase(Cancel) here: Died/Stunned/Silenced are followed at once by OwnerTick's canAct==false
                // path, which sends it. Unequipped has no OwnerTick left, but nobody left to show a cancel to either.
            }
        }

        /// <summary>Owner only. Publishes "I have a portal charge" as a Player Property (AllyPortalTraveller.ReadyKey)
        /// on this player, only when the answer changes, so teammates' clients know whether my portals can be used.
        /// Photon sends Player Properties to late joiners by itself.</summary>
        private void PublishReadyIfChanged(bool ready)
        {
            if (lastPublishedReady.HasValue && lastPublishedReady.Value == ready)
                return;
            if (!PhotonNetwork.InRoom || !Owner.IsMine)
                return;

            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { AllyPortalTraveller.ReadyKey, ready } });
            lastPublishedReady = ready;
        }

        /// <summary>Owner only. A teammate finished a trip through one of my portals: spends one charge, exactly as my
        /// own trip does (so the cooldown starts and the HUD shows it). Floors at 0 - if my own trip and theirs land
        /// in the same instant with one charge left, both trips happen and nobody is refunded (accepted race).</summary>
        public void SpendChargeForAllyTrip()
        {
            if (!Owner.IsMine)
                return;
            SpendCharge();
            PublishReadyIfChanged(HasCharge);
        }

        private void CompleteTravel(Portal from, Portal to)
        {
            channelingPortal = null;
            // Spent here, before ExecuteCast calls TeleportTo: a knockback landing in the frame between still wins and
            // the charge is gone, unrefundable - the accepted race BlinkAbility documents for its jump.
            SpendCharge();
            channelState.LatchArrival(to);
            SendPhase(PhaseTravelled, new CastPayload { Origin = from.transform.position, Point = ArrivalRoot(to) });
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

            // Copied first: PhotonNetwork.Destroy leads to Portal.OnDestroy unregistering from the list being iterated.
            var mine = new List<Portal>(Portal.ForOwner(Owner.ActorNumber));
            foreach (Portal p in mine)
            {
                if (p != null)
                    PhotonNetwork.Destroy(p.gameObject);
            }
        }

        /// <summary>The owner's portal standing at a point (the departure point a trip's message carries), or null.</summary>
        private static Portal FindPortalAt(int ownerActor, Vector3 point)
        {
            foreach (Portal p in Portal.ForOwner(ownerActor))
            {
                if (p != null && (p.transform.position - point).sqrMagnitude < 0.01f)
                    return p;
            }
            return null;
        }

        private Portal FindStandingPortal(IReadOnlyList<Portal> mine)
        {
            // The Rigidbody's position, not the Transform's: TeleportTo writes the Rigidbody and the Transform lags a
            // physics step, which would lift the arrival latch (same fix as AllyPortalTraveller).
            Vector3 position = body != null ? body.position : Owner.Root.transform.position;
            float bodyRadius = capsule != null ? capsule.radius : 0f;
            Portal best = null;
            float bestDistanceSqr = float.MaxValue;

            foreach (Portal p in mine)
            {
                if (p == null)
                    continue;

                Vector3 delta = p.transform.position - position;
                delta.y = 0f;
                float distanceSqr = delta.sqrMagnitude;

                // The body touching the circle counts (D15), not only the body's middle.
                if (PortalUseRules.IsOnPortal(p.transform.position, position, p.Radius, bodyRadius) && distanceSqr < bestDistanceSqr)
                {
                    best = p;
                    bestDistanceSqr = distanceSqr;
                }
            }

            return best;
        }

        /// <summary>The paired portal: the most recently placed of the owner's OTHER portals. Normally simply "the other
        /// one"; picking the newest keeps the answer deterministic for the frame before a prune finishes.</summary>
        public static Portal FindOther(IReadOnlyList<Portal> mine, Portal current)
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
                    // Vision: it lasts seconds, so it follows my team's sight instead of being judged once (friendly: always).
                    VisibleWhenSeen.Attach(channelVfx, cast.CasterTeam);
                    return;

                case PhaseTravelled:
                    ClearChannelVfx();
                    if (cast.IsCasterClient)
                    {
                        var displacement = Owner.Displacement as PlayerDisplacement;
                        if (displacement == null || !displacement.TeleportTo(cast.Payload.Point))
                        {
                            // Only possible if a knockback started between the channel completing and here (the race
                            // BlinkAbility documents); the charge is already spent and cannot be refunded.
                            Debug.LogWarning($"[TeleportAbility] {name}: teleport refused at execute " +
                                              "time - a knockback must have started after the channel completed.");
                        }
                    }
                    else
                    {
                        // Both rings at floor level: Point carries the ARRIVAL ROOT height (so the caster's TeleportTo lands
                        // standing), but these cosmetic rings are ground rings - FeetOf derives the floor back out of it.
                        PlayArrivalVfx(cast.Payload.Origin, cast.CasterTeam); // departure: only if its spot is seen
                        PlayArrivalVfx(capsule != null ? PlayerSpaceProbe.FeetOf(capsule, cast.Payload.Point) : cast.Payload.Point, cast.CasterTeam); // arrival: only if its spot is seen

                        // A trip just completed here: teammates standing on the departure portal travel with it. Each
                        // client moves only its own player (AllyPortalTraveller.Local); no new message.
                        if (PortalUseRules.IsFreshGroupSignal(cast.SecondsLate))
                            AllyPortalTraveller.Local?.JoinGroupTrip(Owner.ActorNumber, FindPortalAt(Owner.ActorNumber, cast.Payload.Origin), cast.CasterActor);
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
            object[] data = { portalTemplate.PortalDiameter, payload.IntArg, channelSeconds };
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

        /// <summary>Where a traveller's root lands on a portal: standing on its floor point, the same height Blink uses.
        /// The raw ground point would sink the capsule half a metre into the floor and physics would pop it out sideways.</summary>
        private Vector3 ArrivalRoot(Portal to) => ArrivalRoot(capsule, to);

        /// <summary>For a traveller that is not this ability's owner (a teammate, AllyPortalTraveller): brings its own capsule.</summary>
        public static Vector3 ArrivalRoot(CapsuleCollider travellerCapsule, Portal to) =>
            PlayerSpaceProbe.RootOnGround(travellerCapsule, to.transform.position);

        /// <summary>True when a player can arrive on this portal: inside the arena with a player's width to spare, and not
        /// inside a wall, house, crate, cover or barrier (crossing by portal is allowed, arriving fused into one is not).
        /// Other players don't count (Building | Barrier, not a body layer).</summary>
        private bool IsExitClear(Portal to) => IsExitClear(capsule, to, Owner.Root.transform);

        /// <summary>The exit check for any traveller; a teammate passes their own capsule and root so their own body
        /// is not what blocks the exit.</summary>
        public static bool IsExitClear(CapsuleCollider travellerCapsule, Portal to, Transform travellerRoot)
        {
            if (travellerCapsule == null)
                return false;

            return IsRootClear(travellerCapsule, ArrivalRoot(travellerCapsule, to), travellerRoot);
        }

        /// <summary>The same exit check for an arbitrary arrival root - a group member landing beside the centre.</summary>
        public static bool IsRootClear(CapsuleCollider travellerCapsule, Vector3 root, Transform travellerRoot)
        {
            if (travellerCapsule == null)
                return false;

            return Overpower.Arena.ArenaSymmetry.IsInsideArena(root, travellerCapsule.radius)
                   && !PlayerSpaceProbe.IsCapsuleBlocked(travellerCapsule, root, ArenaLayers.WallsAndBarriers, travellerRoot);
        }

        /// <summary>The owner as a group member (a teammate's trip through my portal pulled me along): my own channel
        /// stops and the arrival portal is latched as after my own trip. No charge - whoever triggered it paid.</summary>
        public bool IsLatchedOn(Portal portal) => channelState.IsLatchedOn(portal);

        public void NoteArrivedWithGroup(Portal arrival)
        {
            if (channelingPortal != null)
            {
                channelingPortal = null;
                SendPhase(PhaseChannelCancel, default);
            }
            channelState.Reset();
            channelState.LatchArrival(arrival);
        }

        // The check volume's vertical band above the grounded point: a human-height band, not a design tunable (as blockMask).
        private const float BlockCheckBottom = 0.1f;
        private const float BlockCheckTop = 1.8f;

        /// <summary>
        /// True if a portal-sized volume at this already-grounded point would overlap a wall or another player's body (the
        /// Default|Building mask and self-exclusion BlinkAbility's check uses).
        ///
        /// A BOX, not a capsule: Physics.OverlapCapsule's hemispherical end caps extend a FURTHER radius beyond the two
        /// points. Invisible for Blink's player capsule, but a 1.0m portal radius against a 1.7m band would dip the bottom
        /// cap below the floor, into the ground itself, so with Default in the mask every placement on open ground would
        /// read as blocked. A box's flat faces have no overshoot.
        /// </summary>
        private bool IsBlocked(Vector3 groundPoint)
        {
            float radius = portalTemplate.Radius;
            Vector3 center = groundPoint + Vector3.up * ((BlockCheckBottom + BlockCheckTop) * 0.5f);
            Vector3 halfExtents = new Vector3(radius, (BlockCheckTop - BlockCheckBottom) * 0.5f, radius);

            // Default|Building only, NOT BodiesWallsAndBarriers: this box is wide (2.0 m portal diameter) and a Tier III
            // recess is only ~3 m deep, so checking it against a barrier too refused almost every spot in the recess,
            // silently undoing "a portal crosses a barrier" (GDD p.29). A barrier is refused below instead, at the
            // tighter, player-sized capsule a traveller will really arrive in.
            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, Quaternion.identity, blockMask, QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap.transform.IsChildOf(Owner.Root.transform))
                    continue; // never blocked by the caster's own body.
                return true;
            }

            // Still refuse a portal placed square on a barrier (a traveller would land fused into its collider); this
            // check is only the player's capsule width, so it never refuses the rest of a recess.
            if (capsule != null && PlayerSpaceProbe.IsInsideBarrier(capsule, PlayerSpaceProbe.RootOnGround(capsule, groundPoint), Owner.Root.transform))
                return true;

            return false;
        }

        private void ClearChannelVfx()
        {
            if (channelVfx != null)
                Destroy(channelVfx);
            channelVfx = null;
        }

        private void PlayArrivalVfx(Vector3 point, int casterTeam)
        {
            if (arrivalVfxRadius <= 0f || !TeamSight.ShotShownAt(casterTeam, point))
                return;
            Destroy(SpawnMarker(point, arrivalVfxRadius), arrivalVfxSeconds);
        }

        private static GameObject SpawnMarker(Vector3 point, float radius)
        {
            if (radius <= 0f)
                return null;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Teleport VFX (cheap, cosmetic only)";
            // Removed immediately, not with Destroy (end of frame): for that frame the sphere would be a solid object in
            // the world. Same trick as BlinkAbility.PlayRemoteVfx.
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * radius * 2f;
            return marker;
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"Place up to {ShopNumberFormat.Compact(placementRange)}m away · {maxPortals} portals",
                                   $"{ShopNumberFormat.Compact(channelSeconds)}s standing in one to travel");
    }
}
