using UnityEngine;
using Overpower.Combat;
using Overpower.Weapons;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// Fires a bolt (Range, Projectile Speed); hitting a wall or a player pulls the SHOOTER toward where it landed. Not a
    /// weapon and not a knockback: the pull is a Voluntary PlayerDisplacement move, so it loses to a real knockback and
    /// cancels on death like a dash. The first ability to fire a projectile: ProjectileMotor gets a context built from this
    /// module's fields (no WeaponDefinition) and AbilityHitRelay reports the hit. EVERY CLIENT spawns the same local
    /// projectile (as RPC_FireWeapon does); only the CASTER's copy gets a hit callback, which is what makes "only the
    /// shooter gets pulled" true without asking "am I the caster". Stopping at walls and enemies but flying through
    /// teammates is ProjectileMotor's FliesThrough rule, nothing here.
    /// </summary>
    public sealed class ZipGunAbility : AbilityModule
    {
        // Phase numbers this module defines. 0 is always the cast itself (firing the bolt).
        private const byte PhaseTether = 1;

        // Not a design tunable: the zip gun is pure utility. A field would be a number a designer could un-zero into a
        // free hitscan gun.
        private const float NoDamage = 0f;

        [Header("Projectile")]
        [SerializeField, Tooltip("How far the bolt can travel before it gives up, in metres. " +
                 "Tudor's spec: 15m.")]
        private float range = 15f;

        [SerializeField, Tooltip("How fast the bolt itself flies, in metres per second - not the " +
                 "pull that follows a hit. Faster reads as more responsive but gives less time to " +
                 "see it coming.")]
        private float projectileSpeed = 40f;

        [SerializeField, Tooltip("Radius in metres of the bolt's own hit-detection sphere - the " +
                 "same idea as a weapon's Projectile Radius. A4 (Tudor 2026-09-17 evening, gameplay " +
                 "change): raised from 0.15 to 0.225 so the hit finally matches the hook's own look " +
                 "(0.45 m head, Zip Bolt View) - the head used to be drawn bigger than what it hit.")]
        private float projectileRadius = 0.225f;

        [SerializeField, Tooltip("The projectile this ability fires - a ProjectileMotor plus " +
                 "AbilityHitRelay, no WeaponDefinition involved. Lives in Assets/Gameplay/Projectiles " +
                 "next to every other bullet.")]
        private GameObject projectilePrefab;

        [Header("Pull")]
        [SerializeField, Tooltip("How fast you travel toward wherever the bolt hit, in metres per " +
                 "second. The DISTANCE is whatever ground is left between you and the impact point, " +
                 "not a fixed number - a close hit pulls you a short way and a far one pulls you " +
                 "further, both at this same speed.")]
        private float pullSpeed = 25f;

        [Header("Tether (cosmetic only)")]
        [SerializeField, Tooltip("Half the side, in metres, of the square anchor shown where your bolt bit, on " +
                 "every client (yours included). 0 shows no anchor.")]
        private float tetherMarkerRadius = 0.35f;

        [SerializeField, Tooltip("Shortest time, in seconds, the anchor and the rope stay up. They stay longer " +
                 "when the pull itself takes longer (distance ÷ Pull Speed).")]
        private float tetherMarkerSeconds = 0.3f;

        [SerializeField, Tooltip("Material of the square anchor - Assets/Gameplay/Abilities/Ability Visual Solid.mat. " +
                 "Its colour is the hook colour on the projectile prefab (Zip Bolt View).")]
        private Material anchorMaterial;

        [SerializeField, Tooltip("Material of the rope from you to the anchor during the pull - " +
                 "Assets/Gameplay/UI/AimConeLine.mat.")]
        private Material ropeMaterial;

        [SerializeField, Tooltip("Rope thickness, in metres.")]
        private float ropeWidth = 0.05f;

        // Owner only: the player's capsule, so the pull stops a radius short of the impact point instead of driving the
        // player's centre into whatever it hit.
        private CapsuleCollider capsule;

        // Owner only: whether THIS ability's pull is in flight, so a Stunned/Died/Unequipped interrupt never cancels
        // someone else's Forced move (PlayerDisplacement's priority rules; same as DashAbility).
        private bool pulling;

        // Every client: the pull rope, built once and reused. Unparented, like the flamethrower's cone, because this
        // module sits under the player, whose hierarchy changes layer on death.
        private LineRenderer pullRope;
        private Vector3 tetherPoint;
        private float ropeUntil;
        private int ropeTeam = -1;
        private MaterialPropertyBlock anchorBlock;

        public override bool IsActive => pulling;

        public override void OnEquip()
        {
            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[ZipGunAbility] {name}: the player has no CapsuleCollider - the pull " +
                                "cannot stop short of the impact point.");

            if (projectilePrefab == null)
                Debug.LogError($"[ZipGunAbility] {name}: Projectile Prefab is not assigned - the zip gun fires nothing.");
            else if (projectilePrefab.GetComponent<ProjectileMotor>() == null)
                Debug.LogError($"[ZipGunAbility] {name}: Projectile Prefab '{projectilePrefab.name}' has no ProjectileMotor.");

            // A takedown (kill OR assist) refills the charge immediately instead of waiting out the cooldown.
            // CombatEvents.LocalTakedown fires only on the machine that earned it, but HandleLocalTakedown still checks
            // Owner.IsMine: this module is also instantiated on OTHER clients to visualise their loadout, and
            // RefillCharges is owner only.
            CombatEvents.LocalTakedown += HandleLocalTakedown;
        }

        private void OnDestroy()
        {
            CombatEvents.LocalTakedown -= HandleLocalTakedown;
            if (pullRope != null)
                Destroy(pullRope.gameObject);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            range = Mathf.Max(0.1f, range);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileRadius = Mathf.Max(0.01f, projectileRadius);
            pullSpeed = Mathf.Max(0.1f, pullSpeed);
            tetherMarkerRadius = Mathf.Max(0f, tetherMarkerRadius);
            tetherMarkerSeconds = Mathf.Max(0f, tetherMarkerSeconds);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            if (projectilePrefab == null)
                return false; // OnEquip already logged why.

            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        public override void Interrupt(InterruptReason reason)
        {
            if (reason != InterruptReason.Stunned && reason != InterruptReason.Died && reason != InterruptReason.Unequipped)
                return; // Silenced does not stop this - it is not a weapon and spends no heat, matching Dash.

            // Guarded on pulling: a Forced move (knockback) that pre-empted this pull, or PlayerDisplacement's own death
            // handling, has already made it false (as DashAbility's identical guard).
            if (!pulling)
                return;

            (Owner.Displacement as PlayerDisplacement)?.Cancel();
        }

        private void HandleLocalTakedown(bool isKill)
        {
            if (Owner.IsMine)
                RefillCharges();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            switch (cast.Phase)
            {
                case 0:
                    FireProjectile(cast);
                    return;

                case PhaseTether:
                    // Every client, the caster included, draws the square anchor and a rope for as long as the pull takes.
                    PlayTether(cast.Payload.Point, cast.CasterTeam);
                    return;
            }
        }

        /// <summary>Every client. Builds this shot's local projectile; only the caster's copy gets a hit callback.</summary>
        private void FireProjectile(in CastEvent cast)
        {
            System.Action<ProjectileHitInfo> onHit = null;
            if (cast.IsCasterClient)
                onHit = HandleZipHit;

            var shot = new ProjectileContext(Definition.Id, projectileSpeed, projectileRadius, range,
                                             NoDamage, cast.CasterActor, cast.CasterTeam,
                                             cast.Payload.Direction, cast.Payload.Origin, onHit);

            GameObject projectile = Instantiate(projectilePrefab, cast.Payload.Origin,
                                                Quaternion.LookRotation(cast.Payload.Direction));
            ProjectileMotor motor = projectile.GetComponent<ProjectileMotor>();
            if (motor == null)
            {
                Debug.LogError($"[ZipGunAbility] {name}: projectile prefab '{projectilePrefab.name}' " +
                                "has no ProjectileMotor - destroying it rather than leaking it.");
                Destroy(projectile);
                return;
            }

            motor.Initialize(shot);
            // D2: an enemy's Zip Gun bolt (and the rope that rides on it) is drawn only while inside my team's sight.
            VisibleWhenSeen.Attach(projectile, cast.CasterTeam);
        }

        /// <summary>Caster only, called by AbilityHitRelay through the context it was built with.</summary>
        private void HandleZipHit(ProjectileHitInfo hit)
        {
            // Belt and braces: only the caster's copy has a hit callback, so IsMine should already hold here.
            if (!Owner.IsMine || capsule == null)
                return;

            var displacement = Owner.Displacement as PlayerDisplacement;
            if (displacement == null)
                return;

            Vector3 toHit = hit.Point - Owner.Root.transform.position;
            toHit.y = 0f; // The pull is a ground-plane shove, like every other displacement.
            float distance = toHit.magnitude;

            if (distance <= capsule.radius)
                return; // Point-blank hit - already standing where the pull would end.

            Vector3 direction = toHit / distance;
            float travelDistance = distance - capsule.radius;

            pulling = true;
            bool started = displacement.DisplaceVoluntary(direction, travelDistance, pullSpeed, HandlePullEnd);
            if (!started)
            {
                // A knockback beat the pull to the mover between the bolt landing and here: the charge is already
                // spent (accepted race, see TeleportAbility.CompleteTravel).
                pulling = false;
                return;
            }

            // Sent from here, not from FireProjectile, so it carries where the shot ACTUALLY landed rather than where it
            // was aimed: remote clients must draw the tether at the real impact point.
            SendPhase(PhaseTether, new CastPayload { Point = hit.Point });
            LogZip(hit.Point, travelDistance);
        }

        private void HandlePullEnd(DisplaceEnd end)
        {
            pulling = false;
        }

        private void PlayTether(Vector3 point, int casterTeam)
        {
            Vector3 toPoint = point - Owner.Root.transform.position;
            toPoint.y = 0f;
            float travel = Mathf.Max(0f, toPoint.magnitude - (capsule != null ? capsule.radius : 0f));
            float seconds = Mathf.Max(tetherMarkerSeconds, AbilityVisualGeometry.PullSeconds(travel, pullSpeed));
            Color color = HookColor();

            if (tetherMarkerRadius > 0f)
            {
                GameObject anchor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                anchor.name = "Zip Anchor VFX (cheap, cosmetic only)";
                // Removed immediately, not with Destroy, so the cube is never a solid object in the world, even for a frame.
                DestroyImmediate(anchor.GetComponent<Collider>());
                anchor.transform.SetPositionAndRotation(point,
                    toPoint.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPoint) : Quaternion.identity);
                anchor.transform.localScale = Vector3.one * tetherMarkerRadius * 2f;
                var renderer = anchor.GetComponent<MeshRenderer>();
                if (anchorMaterial != null)
                    renderer.sharedMaterial = anchorMaterial;
                if (anchorBlock == null)
                    anchorBlock = new MaterialPropertyBlock();
                VisualTint.SetMeshColor(renderer, anchorBlock, color);
                // D2: an enemy's anchor is drawn only while its spot is inside my team's sight (own team's always).
                VisibleWhenSeen.Attach(anchor, casterTeam);
                Destroy(anchor, seconds);
            }

            if (ropeMaterial == null)
                return;
            if (pullRope == null)
                pullRope = BuildRope();
            VisualTint.SetLineColor(pullRope, color);
            tetherPoint = point;
            ropeTeam = casterTeam;
            ropeUntil = Time.time + seconds;
            pullRope.enabled = true;
            UpdateRope();
        }

        private Color HookColor()
        {
            ZipBoltView view = projectilePrefab != null ? projectilePrefab.GetComponent<ZipBoltView>() : null;
            return view != null ? view.HookColor : Color.white;
        }

        private LineRenderer BuildRope()
        {
            var go = new GameObject("Zip Rope VFX (cheap, cosmetic only)");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = ropeMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = ropeWidth;
            line.endWidth = ropeWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            if (pullRope == null)
                return;
            if (Time.time >= ropeUntil)
            {
                pullRope.enabled = false;
                return;
            }
            UpdateRope();
        }

        private void UpdateRope()
        {
            Vector3 start = Owner.Weapon != null ? Owner.Weapon.MuzzlePosition : Owner.Root.transform.position;
            pullRope.SetPosition(0, start);
            pullRope.SetPosition(1, tetherPoint);
            // D2: an enemy's pull rope is drawn only while the line crosses my team's sight, checked every frame it is
            // drawn (own team's always).
            pullRope.enabled = TeamSight.ShotShownAlong(ropeTeam, start, tetherPoint);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogZip(Vector3 hitPoint, float travelDistance)
        {
            Debug.Log($"[ZIP] hit={hitPoint} pullDistance={travelDistance:F2}");
        }

        public override string ShopStatsText() =>
            $"Range {ShopNumberFormat.Compact(range)}m · pulls you at {ShopNumberFormat.Compact(pullSpeed)} m/s";
    }
}
