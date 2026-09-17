using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// Drops a proximity mine - Tudor's Equipment spec: 2 charges, 10 seconds each. WHERE changed
    /// (A9, Tudor 2026-09-17 evening): a mine used to always land at the caster's own feet, with no
    /// aiming at all; it now lands at the player's aim point on the floor, clamped to Placement Range
    /// metres from the player - see TryBuildCast and MinePlacementRule (Assets/scripts/Combat).
    ///
    /// A MINE IS A REAL NETWORKED OBJECT, placed in ExecuteCast's IsCasterClient branch exactly like
    /// TeleportAbility places a Portal - a player joining mid-match has to see mines that have been
    /// sitting there for two minutes, which only PhotonNetwork.Instantiate gives.
    ///
    /// PRUNING MIRRORS TELEPORTABILITY EXACTLY: Seq travels as instantiationData so every client's
    /// Mine.Seq agrees on placement order, and placing a fifth mine destroys the OLDEST of this
    /// owner's own mines - DeployablePruning.OverflowBySeq is the same pure logic Portal's own
    /// pruning already uses, just against Mine's static per-owner registry instead of Portal's.
    ///
    /// INTERRUPT IS NOT OVERRIDDEN. Tudor's decision: mines survive the placer's own death, exactly
    /// like a portal survives - so Died must do nothing here, and the base no-op already gives that
    /// for free. Nothing in this task specifies what should happen to a player's mines if the
    /// Equipment slot is later swapped to something else (there is no shop yet to do that with) - so
    /// unlike Portal, which explicitly destroys its own gates on Unequipped, this module leaves that
    /// decision unmade rather than guessing: an untriggered mine still expires on its own Persist
    /// Seconds (NetworkedDeployable's Lifetime Seconds field) regardless.
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

        // The caster's own capsule, read once in OnEquip like Blink's and Teleport's - a mine's safety pull-back
        // needs the real player shape to find "the caster's own feet" (PlayerSpaceProbe.FeetOf) and to stand a
        // candidate point the same way a player would (PlayerSpaceProbe.RootOnGround).
        private CapsuleCollider capsule;

        // Owner only: increments once per successful placement, travels as CastPayload.IntArg so
        // every client's Mine.Seq (and this owner's own pruning) agree on placement order - same
        // counter shape as TeleportAbility.nextSeq, and safe to restart at 0 on every fresh equip
        // for the identical reason: nothing here destroys this owner's mines on Unequipped, so a
        // restarted counter WOULD collide with live Seqs from a previous instance of this module -
        // unlike Portal, this is only safe because Unequipping and re-equipping the mines ability
        // mid-match is not a reachable path yet (no shop). Revisit if that changes.
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
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (minePrefab == null)
                return false; // OnEquip already logged why.

            // A9: the aim point on the floor (ctx.TargetPoint - PlayerAim.GroundPointUnderCursor, the same point
            // weapons and TeleportAbility already use), clamped to Placement Range - not the caster's own feet any
            // more (see the class comment for why that changed).
            Vector3 requested = MinePlacementRule.ClampToRange(ctx.Origin, ctx.TargetPoint, placementRange);
            Vector3 point = FindSafePlacement(ctx.Origin, requested);

            payload = new CastPayload { Point = point, IntArg = nextSeq };
            nextSeq++;
            return true;
        }

        /// <summary>
        /// Walks the clamped point back toward the player, in Placement Search Step increments, until one is on real
        /// floor with nothing on the Building layer between the caster and it - the same "clamp, then walk back
        /// toward the caster until something works" idea BlinkDestinationSearch already proves pure, just against a
        /// different pair of checks (a straight-line path and a floor point, not an arena-bounded capsule check). No
        /// new path/ground primitives: GroundSnap.TryFindGroundY is the Task 2 floor finder, and
        /// PlayerSpaceProbe.IsPathClear is the same knee-height sphere cast Blink's and Teleport's own destination
        /// checks are built from. Falls back to the caster's own position - today's placement - if even that fails.
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

            float distance = requestedDistance;
            while (true)
            {
                Vector3 candidateXZ = originXZ + direction * distance;
                if (GroundSnap.TryFindGroundY(candidateXZ, out float groundY))
                {
                    Vector3 candidateFeet = new Vector3(candidateXZ.x, groundY, candidateXZ.z);
                    if (PlayerSpaceProbe.IsPathClear(casterFeet, candidateFeet))
                        return PlayerSpaceProbe.RootOnGround(capsule, candidateFeet);
                }

                if (distance <= 0f)
                    return origin; // even the caster's own spot failed - fall back to today's placement.

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
            // Seq, then this ability's own id (Task T3, telemetry) - Mine.OnPlaced reads both at
            // their fixed indices; appending keeps Seq's own index unchanged for anyone else reading it.
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
    }
}
