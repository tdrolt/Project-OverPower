using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// Drops a proximity mine at the caster's feet - Tudor's Equipment spec: 2 charges, 10 seconds
    /// each. This module only decides WHEN and WHERE (always "right here, right now" - there is no
    /// aiming); everything about what the mine actually does once it exists lives on Mine.cs.
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
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxActiveMines = Mathf.Max(1, maxActiveMines);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (minePrefab == null)
                return false; // OnEquip already logged why.

            // Feet, not aim - Tudor's spec places a mine where the caster is standing, never where
            // they are looking. ctx.Origin is the player root's own position (CastContext's own doc).
            payload = new CastPayload { Point = ctx.Origin, IntArg = nextSeq };
            nextSeq++;
            return true;
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
            object[] data = { payload.IntArg };
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
                        PhotonNetwork.Destroy(m.gameObject);
                        break;
                    }
                }
            }
        }
    }
}
