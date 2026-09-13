using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Places a wall of cover in front of the caster - Tudor's Equipment spec: 10s or 100 damage
    /// absorbed (whichever comes first), 20s cooldown, blocking projectiles - and movement - in BOTH
    /// directions, including the caster's own. This module only decides WHEN and WHERE; everything
    /// about what the wall itself does once it exists lives on CoverWall.cs - see its own class
    /// comment for the full mechanism (why friendly shots still stop, why the through-walls laser
    /// does not, why HP is owner-authoritative and not synced).
    ///
    /// PLACEMENT IS A FLAT AIM PROJECTION, NOT A GROUND PROBE. Unlike Portal/Blink, which validate a
    /// destination against real ground height (GroundProbe), a wall is placed Placement Distance in
    /// front of the caster along their own flat aim direction, at the caster's own current height -
    /// the Task 1.8 addendum gives no ground-probe step for cover, and placing it at the caster's own
    /// height is the correct behaviour on the flat arena floor this prototype ships with.
    ///
    /// THE PLACEMENT CHECK IS A SINGLE Physics.CheckBox, not an OverlapBox-and-filter like
    /// TeleportAbility.IsBlocked: the check box's own THICKNESS (not its Width - the box is built
    /// facing the aim direction, so Thickness is the axis toward the caster) is all that separates
    /// its near face from Placement Distance, and with the default numbers that is still 2.8m of
    /// clearance from a caster's own body - so no explicit self-exclusion is needed on top of the
    /// geometry.
    ///
    /// NO CAP, NO PRUNING. Tudor's decision: with a 20s cooldown and a 10s lifetime there can only
    /// ever be one of this player's own walls alive at once anyway, so this module needs none of
    /// MineAbility/TeleportAbility's oldest-first pruning machinery, and no per-instance placement
    /// data (Seq, a networked diameter) travels with the cast either - every number the wall needs
    /// lives on its own prefab already.
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

        // Default|Building - the same "what counts as solid" mask TeleportAbility.blockMask uses for
        // its own placement check; not a design tunable, for the same reason that field is not one.
        private int blockMask;

        // Owner only: the prefab's own CoverWall, read once so TryBuildCast's placement check and
        // ExecuteCast's spawn agree on the same size without a GetComponent every cast. Also doubles
        // as the "is Cover Prefab actually usable" check, the same shape as Portal's portalTemplate.
        private CoverWall coverTemplate;

        private void Awake()
        {
            blockMask = LayerMask.GetMask("Default", "Building");
        }

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
                return false; // Refused, nothing spent - Tudor's addendum.

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
        /// True if a wall-sized box at this point, facing direction, would overlap a wall or a
        /// player's body. Uses CoverWall.GroundLift - the exact lift the real collider is built with
        /// (CoverWall.ApplyDimensions) - so the check volume and the real one agree on the same
        /// geometry; see the class comment for why no explicit self-exclusion is needed on top of it.
        /// </summary>
        private bool IsBlocked(Vector3 point, Vector3 direction)
        {
            float halfHeight = coverTemplate.Height * 0.5f;
            Vector3 center = point + new Vector3(0f, CoverWall.GroundLift + halfHeight, 0f);
            Vector3 halfExtents = new Vector3(coverTemplate.Width * 0.5f, halfHeight, coverTemplate.Thickness * 0.5f);
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

            return Physics.CheckBox(center, halfExtents, rotation, blockMask, QueryTriggerInteraction.Ignore);
        }
    }
}
