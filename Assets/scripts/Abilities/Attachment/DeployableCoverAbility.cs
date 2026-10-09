using UnityEngine;
using Overpower.Arena;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// Places a wall of cover in front of the caster that blocks projectiles and movement in BOTH directions, the
    /// caster's own included. This module only decides WHEN and WHERE; what the wall does once it exists lives on
    /// CoverWall (why friendly shots still stop, why the through-walls laser does not, why HP is owner-authoritative).
    /// PLACEMENT IS A FLAT AIM PROJECTION, NOT A GROUND PROBE (unlike Portal/Blink's GroundProbe): the wall goes
    /// Placement Distance ahead along the flat aim, at the caster's own height, which is right on the flat arena floor.
    /// THE PLACEMENT CHECK IS A SINGLE Physics.CheckBox, not TeleportAbility.IsBlocked's OverlapBox-and-filter: the box
    /// faces the aim direction, so its Thickness is the axis toward the caster, and at the default numbers its near
    /// face leaves clear space before the caster's body, so no self-exclusion is needed.
    /// NO CAP, NO PRUNING: with the cooldown longer than the lifetime only one of a player's walls lives at once, so
    /// no MineAbility-style pruning, and no per-instance placement data travels with the cast.
    /// </summary>
    public sealed class DeployableCoverAbility : AbilityModule
    {
        [SerializeField, Tooltip("The wall that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Its own Width, " +
                 "Height, Thickness and Hit Points are the single home for those numbers; this " +
                 "ability only reads its size to validate a placement and its name to spawn it.")]
        private GameObject coverPrefab;

        [SerializeField, Tooltip("How far in front of the caster, in metres, along their own flat " +
                 "aim direction, the wall is centred. Tudor's spec: 3m.")]
        private float placementDistance = 3f;

        // Owner only: the prefab's own CoverWall, read once so TryBuildCast's placement check and
        // ExecuteCast's spawn agree on the same size without a GetComponent every cast. Also doubles
        // as the "is Cover Prefab actually usable" check, the same shape as Portal's portalTemplate.
        private CoverWall coverTemplate;

        public override void OnEquip()
        {
            coverTemplate = coverPrefab != null ? coverPrefab.GetComponent<CoverWall>() : null;

            if (coverPrefab == null)
                Debug.LogError($"[DeployableCoverAbility] {name}: Cover Prefab is not assigned - cover cannot be placed.");
            else if (coverTemplate == null)
                Debug.LogError($"[DeployableCoverAbility] {name}: Cover Prefab '{coverPrefab.name}' has no CoverWall component.");
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            placementDistance = Mathf.Max(0f, placementDistance);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (coverTemplate == null)
                return false; // OnEquip already logged why.

            Vector3 direction = ctx.AimDirection; // Already flat and normalised - see CastContext's own doc.
            Vector3 point = ctx.Origin + direction * placementDistance;

            if (IsBlocked(point, direction))
                return false; // Refused, nothing spent.

            payload = new CastPayload { Origin = ctx.Origin, Direction = direction, Point = point };
            return true;
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0 || !cast.IsCasterClient)
                return; // Only the caster's own machine ever places the real networked object.

            Quaternion rotation = Quaternion.LookRotation(cast.Payload.Direction, Vector3.up);
            NetworkedDeployable.Spawn(coverPrefab.name, cast.Payload.Point, rotation, null);
        }

        /// <summary>
        /// True if a wall-sized box at this point, facing direction, would overlap a wall, a barrier or a player's body
        /// (BodiesWallsAndBarriers: cover overlapping a barrier would leave part of the wall fused into it). Uses
        /// CoverWall.GroundLift, the exact lift the real collider is built with, so check volume and collider agree.
        /// </summary>
        private bool IsBlocked(Vector3 point, Vector3 direction)
        {
            float halfHeight = coverTemplate.Height * 0.5f;
            Vector3 center = point + new Vector3(0f, CoverWall.GroundLift + halfHeight, 0f);
            Vector3 halfExtents = new Vector3(coverTemplate.Width * 0.5f, halfHeight, coverTemplate.Thickness * 0.5f);
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

            return Physics.CheckBox(center, halfExtents, rotation, ArenaLayers.BodiesWallsAndBarriers, QueryTriggerInteraction.Ignore);
        }

        public override string ShopStatsText()
        {
            CoverWall wall = coverPrefab != null ? coverPrefab.GetComponent<CoverWall>() : null;
            if (wall == null)
                return "";
            return ShopNumberFormat.Lines(
                $"Wall {ShopNumberFormat.Compact(wall.Width)}m wide, {ShopNumberFormat.Compact(wall.Height)}m tall",
                $"Absorbs {ShopNumberFormat.Compact(wall.HitPoints)} damage" + (wall.LifetimeSeconds > 0f ? $" · lasts {ShopNumberFormat.Compact(wall.LifetimeSeconds)}s" : ""));
        }
    }
}
