using System.Collections.Generic;
using UnityEngine;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// Drops a proximity mine at the player's aim point on the floor, clamped to Placement Range from the player (see
    /// TryBuildCast and MinePlacementRule).
    /// A MINE IS A REAL NETWORKED OBJECT, placed in ExecuteCast's IsCasterClient branch like TeleportAbility places a
    /// Portal: a player joining mid-match must see mines that have sat there for minutes, which only
    /// PhotonNetwork.Instantiate gives.
    /// PRUNING MIRRORS TELEPORTABILITY: Seq travels as instantiationData so every client's Mine.Seq agrees on placement
    /// order, and placing past Max Active Mines destroys the OLDEST of this owner's mines (DeployablePruning.OverflowBySeq,
    /// against Mine's per-owner registry).
    /// INTERRUPT IS NOT OVERRIDDEN: mines survive the placer's death, like a portal, so Died must do nothing and the base
    /// no-op gives that. What happens to a player's mines when the Attachment slot is swapped is left undecided (unlike
    /// Portal, which destroys its gates on Unequipped); an untriggered mine still expires on its Lifetime Seconds.
    /// </summary>
    public sealed class MineAbility : AbilityModule
    {
        [SerializeField, Tooltip("The mine that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Its own damage, " +
                 "slow, radii and arm delay are the single home for those numbers; this ability only " +
                 "reads its name to spawn it.")]
        private GameObject minePrefab;

        [SerializeField, Tooltip("How many of this player's own mines can be armed at once. Placing " +
                 "one more than this destroys the OLDEST of their own mines first - the newest ones " +
                 "are always the ones still ticking. Controller's call: two charges and a 45-second " +
                 "fuse would otherwise let mines pile up indefinitely over a long match.")]
        private int maxActiveMines = 4;

        [Header("Placement (A9, Tudor 2026-09-17 evening)")]
        [SerializeField, Tooltip("How far from the player, in metres, a mine can be placed - horizontal " +
                 "distance from the player's centre, along the aim direction. The cursor's floor point " +
                 "is clamped to this distance when it points further away - see MinePlacementRule.")]
        private float placementRange = 2f;

        [SerializeField, Tooltip("How far, in metres, a blocked or floor-less placement is walked back " +
                 "toward the player before giving up and dropping the mine at the caster's own feet " +
                 "instead - same idea as BlinkAbility's own Search Step.")]
        private float placementSearchStep = 0.25f;

        [Header("Placement - ground probe (same rule as Blink and Teleport)")]
        [SerializeField, Tooltip("How far, in metres, BELOW the player's OWN current height the " +
                 "ground is allowed to be for a candidate spot to count as solid ground. Too small " +
                 "refuses a valid spot on a gentle slope or a step down; too large can accept a spot " +
                 "far below the arena - past a thin floor - as if it were ground.")]
        private float groundProbeDistance = 2f;

        [SerializeField, Tooltip("How far, in metres, ABOVE the player's OWN current height the " +
                 "ground is allowed to be - a small step or curb, not a roof. A mine stays at roughly " +
                 "the caster's own level, the same reason Blink's own step-up is small.")]
        private float maxStepUp = 0.6f;

        // The caster's own capsule, read once in OnEquip like Blink's and Teleport's - a mine's safety pull-back
        // needs the real player shape to find "the caster's own feet" (PlayerSpaceProbe.FeetOf) and to stand a
        // candidate point the same way a player would (PlayerSpaceProbe.RootOnGround).
        private CapsuleCollider capsule;

        // Not a design tunable, like Blink's/Teleport's own blockMask: which layers count as ground and what a
        // placement may not overlap is fixed here rather than exposed for a designer to mis-set into something that
        // places a mine through the arena floor. Computed in Awake, not a static field initializer - the same
        // LayerMask.GetMask crash-on-spawn PlayerDisplacement's class comment documents.
        private int blockMask;

        // The sphere FindSafePlacement checks a candidate's feet with, so a mine is never hidden inside a barrier's own
        // concrete - not a design tunable, the same reasoning as blockMask.
        private const float BarrierCheckUpMetres = 0.3f;
        private const float BarrierCheckRadiusMetres = 0.3f;

        private void Awake()
        {
            blockMask = LayerMask.GetMask("Default", "Building");
        }

        // Owner only: increments once per placement, travels as CastPayload.IntArg so every client's Mine.Seq and this
        // owner's pruning agree on order. It restarts at 0 on a fresh equip, so TryBuildCast first moves it above the
        // Seqs of mines already standing (DeployablePruning.NextSeq).
        private int nextSeq;

        public override void OnEquip()
        {
            if (minePrefab == null)
                Debug.LogError($"[MineAbility] {name}: Mine Prefab is not assigned - mines cannot be placed.");
            else if (minePrefab.GetComponent<Mine>() == null)
                Debug.LogError($"[MineAbility] {name}: Mine Prefab '{minePrefab.name}' has no Mine component.");

            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[MineAbility] {name}: the player has no CapsuleCollider - a mine's " +
                                "placement safety check cannot run, so every mine falls back to the caster's own feet.");
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxActiveMines = Mathf.Max(1, maxActiveMines);
            placementRange = Mathf.Max(0f, placementRange);
            placementSearchStep = Mathf.Max(0.01f, placementSearchStep); // never 0 or negative - that would search forever.
            groundProbeDistance = Mathf.Max(0f, groundProbeDistance);
            maxStepUp = Mathf.Max(0f, maxStepUp);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (minePrefab == null)
                return false; // OnEquip already logged why.

            // The aim point on the floor (ctx.TargetPoint, PlayerAim.GroundPointUnderCursor, as weapons and TeleportAbility
            // use), clamped to Placement Range.
            Vector3 requested = MinePlacementRule.ClampToRange(ctx.Origin, ctx.TargetPoint, placementRange);
            Vector3 point = FindSafePlacement(ctx.Origin, requested);

            // After a rejoin the mines PUN kept have higher Seqs than this fresh module's counter - continue above them.
            IReadOnlyList<Mine> standing = Mine.ForOwner(Owner.ActorNumber);
            var standingSeqs = new List<int>(standing.Count);
            foreach (Mine m in standing)
                standingSeqs.Add(m.Seq);
            nextSeq = DeployablePruning.NextSeq(nextSeq, standingSeqs);

            payload = new CastPayload { Point = point, IntArg = nextSeq };
            nextSeq++;
            return true;
        }

        /// <summary>
        /// Walks the clamped point back toward the player, in Placement Search Step increments, until one is on real floor
        /// with nothing on the Building layer between the caster and it (the clamp-then-walk-back idea
        /// BlinkDestinationSearch proves pure). GroundProbe.TryFindGround is the SAME gameplay floor finder Blink and
        /// Teleport use, not GroundSnap: that is visual-only and probes an ABSOLUTE world-Y band, so a caster on a ledge,
        /// ramp, crate or roof would have every candidate fail and fall back to their feet; GroundProbe's refHeight is
        /// the caster's current height. PlayerSpaceProbe.IsPathClear is the same knee-height sphere cast Blink's and
        /// Teleport's checks use. Falls back to the caster's own position if even that fails.
        /// </summary>
        private Vector3 FindSafePlacement(Vector3 origin, Vector3 requestedPoint)
        {
            if (capsule == null)
                return origin; // OnEquip already logged why - no capsule to check feet/floor with.

            Vector3 originXZ = new Vector3(origin.x, 0f, origin.z);
            Vector3 requestedXZ = new Vector3(requestedPoint.x, 0f, requestedPoint.z);
            Vector3 toRequested = requestedXZ - originXZ;
            float requestedDistance = toRequested.magnitude;
            Vector3 direction = requestedDistance > 0.0001f ? toRequested / requestedDistance : Vector3.zero;
            Vector3 casterFeet = PlayerSpaceProbe.FeetOf(capsule, origin);
            float killHeight = Owner.Motor != null ? Owner.Motor.KillHeight : float.NegativeInfinity;

            float distance = requestedDistance;
            while (true)
            {
                Vector3 candidateXZ = originXZ + direction * distance;
                if (GroundProbe.TryFindGround(origin.y, candidateXZ, maxStepUp, groundProbeDistance,
                        killHeight, blockMask, Owner.Root.transform, out Vector3 ground))
                {
                    Vector3 candidateFeet = ground;

                    // Rejected like a blocked path: neither the ground probe (blockMask) nor IsPathClear (Building only)
                    // sees a barrier, so a mine could land hidden inside one. The walk-back then lands it in front of the
                    // barrier; a mine deliberately thrown OVER one (a candidate beyond it, with a clear path) still lands there.
                    bool insideBarrier = Physics.CheckSphere(candidateFeet + Vector3.up * BarrierCheckUpMetres,
                        BarrierCheckRadiusMetres, ArenaLayers.Barrier, QueryTriggerInteraction.Ignore);

                    if (!insideBarrier && PlayerSpaceProbe.IsPathClear(casterFeet, candidateFeet))
                        return PlayerSpaceProbe.RootOnGround(capsule, candidateFeet);
                }

                if (distance <= 0f)
                    return origin; // even the caster's own spot failed - fall back to the caster's own position.

                distance = Mathf.Max(0f, distance - placementSearchStep);
            }
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0 || !cast.IsCasterClient)
                return; // Only the caster's own machine ever places the real networked object.

            PlaceMine(cast.Payload);
        }

        private void PlaceMine(CastPayload payload)
        {
            // Seq, then this ability's own id - Mine.OnPlaced reads both at their fixed indices; appending keeps Seq's
            // own index unchanged for anyone else reading it.
            object[] data = { payload.IntArg, Definition.Id };
            GameObject spawned = NetworkedDeployable.Spawn(minePrefab.name, payload.Point, data);
            if (spawned == null)
                return; // Spawn already logged why.

            PruneOldest();
        }

        private void PruneOldest()
        {
            IReadOnlyList<Mine> mine = Mine.ForOwner(Owner.ActorNumber);
            var seqs = new List<int>(mine.Count);
            foreach (Mine m in mine)
                seqs.Add(m.Seq);

            foreach (int seq in DeployablePruning.OverflowBySeq(seqs, maxActiveMines))
            {
                foreach (Mine m in mine)
                {
                    if (m.Seq == seq)
                    {
                        // Through the shared guard, not PhotonNetwork.Destroy directly - the oldest
                        // mine being pruned here could be the SAME one a detonation on some other
                        // client already scheduled for destruction (Mine's own Destroy Delay Seconds
                        // wait); RequestDestroy is what stops that from becoming a second real call.
                        m.RequestDestroy();
                        break;
                    }
                }
            }
        }

        public override string ShopStatsText()
        {
            Mine mine = minePrefab != null ? minePrefab.GetComponent<Mine>() : null;
            if (mine == null)
                return $"Up to {maxActiveMines} mines";
            return ShopNumberFormat.Lines(
                $"Damage {ShopNumberFormat.Compact(mine.Damage)} · blast radius {ShopNumberFormat.Compact(mine.ExplosionRadius)}m",
                $"Slows {ShopNumberFormat.Compact(mine.SlowMagnitude * 100f)}% for {ShopNumberFormat.Compact(mine.SlowSeconds)}s",
                $"Up to {maxActiveMines} mines" + (mine.LifetimeSeconds > 0f ? $" · each lasts {ShopNumberFormat.Compact(mine.LifetimeSeconds)}s" : ""));
        }
    }
}
