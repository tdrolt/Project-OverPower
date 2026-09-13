using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// Sprays a short forward cone that ignites every enemy it touches - Tudor's Equipment spec: 5
    /// damage per second for 5 seconds, 13s cooldown. Contact starts the burn; the burn then ticks on
    /// its own through StatusEffectState/PlayerStatusEffects (the Refresh stack rule) and does NOT
    /// need the target to stay inside the cone - Tudor's own clarification, and the difference between
    /// a usable ability and an unusable one in a top-down game (Task 1.9 addendum).
    ///
    /// RUNS ON EVERY CLIENT, INCLUDING THE CASTER'S OWN. ExecuteCast starts a Spray Seconds coroutine
    /// on every machine that receives the cast; each FixedUpdate it re-reads the CASTER's CURRENT
    /// position and facing from Owner (Root.transform / Weapon.MuzzlePosition), never the payload's
    /// Origin/Direction, which are only a snapshot of the instant the trigger was pulled - a spray is
    /// meant to track wherever the caster is aiming as it plays out, not freeze at the press. This is
    /// safe to read from Owner rather than looking the caster up by actor number: this exact module
    /// instance was Equipped onto the caster's own "Abilities" child transform (AbilityRunner.Equip),
    /// on every client, so Owner already IS the caster here, on whichever machine this is running.
    /// Facing comes from the caster's own Transform, which PlayerAim keeps rotated toward its aim and
    /// PlayerNetSync replicates - see PlayerAim.cs's own class comment.
    ///
    /// ONCE PER TARGET PER CAST, VIA A HASHSET (the addendum's own warning). Without it, a FixedUpdate
    /// re-application roughly every 0.02s would re-Refresh the same 5s burn on the same standing
    /// target every physics step for the whole 1s spray, so it would take damage for close to 6s (5s
    /// tail plus ~1s of continuous refreshing) instead of 5 - the addendum's own worked example puts
    /// this at up to 30 instead of 25. The set lives on the coroutine's own stack, so it is naturally
    /// fresh for every new cast and never leaks between them. ConeFilter.SelectCandidates only READS
    /// the set (see its own class comment); this class is the one that decides which of the survivors
    /// actually get marked hit, AFTER the occlusion check below, so a target standing behind cover
    /// this tick can still catch fire the moment it steps into the open, rather than being written off
    /// for the rest of the spray.
    ///
    /// WALLS AND COVER BOTH BLOCK IT. "No Building between the muzzle and the target" is one raycast
    /// against the Building layer - the same layer every wall in the arena sits on AND the layer
    /// Deployable Cover's own collider sits on (CoverWall.cs's class comment), so a player hiding
    /// behind either is safe without this file knowing cover exists. IStructure candidates (cover
    /// itself) are excluded before that check even runs - ConeFilter's own rule, the same reason
    /// MineTargeting excludes it: cover's fails-open -1/-1 identity would otherwise read as "an enemy
    /// with no known team", and it has no IStatusReceiver to burn anyway.
    ///
    /// INTERRUPT(DIED) STOPS THE SPRAY ON EVERY CLIENT. AbilityRunner.HandleAliveChanged already calls
    /// Interrupt for every module on every client once alive state replicates (see its own class
    /// comment), so this override only has to stop ITS OWN coroutine - nothing here needs to know how
    /// that reached this machine. Stunned/Silenced are deliberately NOT handled: AbilityModule's own
    /// contract only delivers those on the OWNER's machine, and stopping the spray there alone while
    /// every other client kept ticking it would make a stun landing on the caster look different on
    /// their own screen than on everyone else's - worse than not reacting at all. A cast already
    /// committed the instant the trigger RPC went out, matching Mine and Cover's own "once cast,
    /// nothing but death (or, for those two, nothing at all) calls it back" precedent.
    ///
    /// THE CASTER CAN STILL FIRE THEIR WEAPON WHILE SPRAYING - Tudor's spec. This occupies only the
    /// Equipment slot and its own cooldown, exactly like every other equipment item; nothing here
    /// touches WeaponFiring or the other ability slots.
    /// </summary>
    public sealed class FlamethrowerAbility : AbilityModule
    {
        [Header("Burn")]
        [SerializeField, Tooltip("Damage per second the burn deals, routed through the normal damage " +
                 "funnel so armor absorbs it like any other hit. Tudor's spec: 5.")]
        private float burnDamagePerSecond = 5f;

        [SerializeField, Tooltip("How many seconds the burn lasts once applied - it keeps ticking even " +
                 "after the target leaves the cone. Tudor's spec: 5. Landing it again before it expires " +
                 "REFRESHES the full duration rather than stacking a second one (StatusEffectState's " +
                 "Refresh rule for Burn), so two overlapping sprays still deal 5 damage per second, " +
                 "never 10.")]
        private float burnSeconds = 5f;

        [Header("Cone")]
        [SerializeField, Tooltip("Full angle of the spray cone, in degrees, measured on the ground " +
                 "plane - 45 means 22.5 degrees either side of wherever the caster is currently " +
                 "facing. Controller's call.")]
        private float coneAngle = 45f;

        [SerializeField, Tooltip("How far the spray reaches, in metres, measured on the ground plane " +
                 "from the caster's own muzzle. Controller's call.")]
        private float coneRange = 7f;

        [SerializeField, Tooltip("How long the spray stays live once cast, in seconds - the cone is " +
                 "re-checked every physics step for this long and then stops on its own. The burn " +
                 "already applied keeps ticking for its own Burn Seconds well past this. Controller's call.")]
        private float spraySeconds = 1f;

        [SerializeField, Tooltip("Which layers count as a target. Default is where a living player or " +
                 "a practice dummy sits; widening this only costs performance, since anything without " +
                 "an IDamageable is skipped anyway.")]
        private LayerMask detectionMask = ~0;

        [Header("VFX (cheap, cosmetic only)")]
        [SerializeField, Tooltip("Colour of the placeholder cone mesh shown on every client for the " +
                 "duration of Spray Seconds. This is a stretched primitive, not a real particle " +
                 "system - cheap enough to run for several simultaneous sprays without a hitch. Swap " +
                 "for real flame art whenever that becomes this project's priority; nothing about the " +
                 "burn itself depends on it.")]
        private Color vfxColor = new Color(1f, 0.45f, 0.1f, 1f);

        // Not a design tunable: how many overlapping colliders one cone check considers - matches
        // Mine.MaxOverlapColliders' own reasoning, comfortably covering every player plus every
        // practice dummy within a 7m sphere at once.
        private const int MaxOverlapColliders = 16;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Reused every tick rather than allocated fresh, the same reason Mine.cs keeps its own
        // overlapBuffer/caught as fields instead of locals.
        private readonly HashSet<IDamageable> seenThisTick = new HashSet<IDamageable>();
        private readonly List<ConeCandidate> candidateBuffer = new List<ConeCandidate>();

        // Not a design tunable: walls and cover occlude a spray whatever this module's own Detection
        // Mask is set to catch - see the class comment. Computed once since NameToLayer never changes
        // at runtime, the same trick ExplodeOnImpact.buildingMask uses.
        private int buildingMask;

        private Coroutine sprayCoroutine;
        private Transform vfx;

        public override bool IsActive => sprayCoroutine != null;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer("Building");
            buildingMask = layer >= 0 ? 1 << layer : 0;
        }

        private void OnDestroy()
        {
            // The module prefab has no Collider (AbilityDefinition.Validate forbids one), so the VFX
            // is a separate, unparented GameObject (see BuildVfx) - it must be cleaned up explicitly
            // here, or unequipping/re-equipping the flamethrower would leak one every time.
            if (vfx != null)
                Destroy(vfx.gameObject);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            burnDamagePerSecond = Mathf.Max(0f, burnDamagePerSecond);
            burnSeconds = Mathf.Max(0f, burnSeconds);
            coneAngle = Mathf.Clamp(coneAngle, 0f, 360f);
            coneRange = Mathf.Max(0f, coneRange);
            spraySeconds = Mathf.Max(0f, spraySeconds);
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        public override void Interrupt(InterruptReason reason)
        {
            if (reason != InterruptReason.Died)
                return; // See the class comment for why only Died is handled here.

            StopSpray();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0)
                return;

            if (sprayCoroutine != null)
                StopCoroutine(sprayCoroutine); // A re-press restarts the spray rather than running two at once.

            sprayCoroutine = StartCoroutine(SprayRoutine(cast.CasterActor, cast.CasterTeam));
        }

        private IEnumerator SprayRoutine(int casterActor, int casterTeam)
        {
            // Fresh per cast, on purpose - see the class comment on "once per target per cast".
            var alreadyHit = new HashSet<IDamageable>();
            var burn = new StatusEffectSpec { kind = StatusKind.Burn, duration = burnSeconds, magnitude = burnDamagePerSecond };

            ShowVfx();

            float elapsed = 0f;
            while (elapsed < spraySeconds)
            {
                TickCone(casterActor, casterTeam, burn, alreadyHit);
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
            }

            HideVfx();
            sprayCoroutine = null;
        }

        private void TickCone(int casterActor, int casterTeam, in StatusEffectSpec burn, HashSet<IDamageable> alreadyHit)
        {
            Vector3 muzzle = Owner.Weapon != null ? Owner.Weapon.MuzzlePosition : Owner.Root.transform.position;
            Vector3 forward = Owner.Root.transform.forward;

            PositionVfx(muzzle, forward);

            int count = Physics.OverlapSphereNonAlloc(muzzle, coneRange, overlapBuffer, detectionMask,
                                                       QueryTriggerInteraction.Ignore);

            seenThisTick.Clear();
            candidateBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                // A body is several colliders to Physics - seenThisTick keeps each target to one
                // ConeCandidate per tick, the same reasoning as Mine.cs's own caught HashSet.
                if (candidate == null || !seenThisTick.Add(candidate))
                    continue;

                candidateBuffer.Add(new ConeCandidate(candidate, collider.transform.position));
            }

            List<ConeCandidate> eligible = ConeFilter.SelectCandidates(candidateBuffer, muzzle, forward, coneRange,
                                                                        coneAngle, casterActor, casterTeam, alreadyHit);

            foreach (ConeCandidate candidate in eligible)
            {
                if (IsOccludedByWall(muzzle, candidate.Position))
                    continue; // Behind a wall or cover - not marked hit, so it can still catch fire once it steps clear.

                IStatusReceiver receiver = (candidate.Target as Component)?.GetComponentInParent<IStatusReceiver>();
                if (receiver == null)
                    continue; // Has health but nothing to burn - should not happen once IStructure is excluded.

                receiver.ApplyStatus(burn, casterActor);
                alreadyHit.Add(candidate.Target); // From here on, this cast never re-applies to this target.
            }
        }

        /// <summary>True when a Building-layer collider (a wall, or cover - CoverWall.cs's own class
        /// comment) stands between the muzzle and the target - the addendum's own occlusion rule, the
        /// same idea as ExplodeOnImpact.IsOccludedByAWall but without needing to exclude a struck
        /// collider, since nothing here has one - a cone check has no impact point to nudge away
        /// from.</summary>
        private bool IsOccludedByWall(Vector3 muzzle, Vector3 targetPosition)
        {
            Vector3 delta = targetPosition - muzzle;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
                return false;

            return Physics.Raycast(muzzle, delta / distance, distance, buildingMask, QueryTriggerInteraction.Ignore);
        }

        private void StopSpray()
        {
            if (sprayCoroutine == null)
                return;

            StopCoroutine(sprayCoroutine);
            sprayCoroutine = null;
            HideVfx();
        }

        // ---- VFX (cheap, cosmetic only - see the Inspector tooltip) --------------------------------

        private void ShowVfx()
        {
            if (vfx == null)
                vfx = BuildVfx();

            vfx.gameObject.SetActive(true);
        }

        private void PositionVfx(Vector3 muzzle, Vector3 forward)
        {
            if (vfx == null)
                return;

            // A cylinder's own length runs along its local Y - LookRotation points Z at forward, so
            // the extra 90 degree tilt lays that length axis flat along the spray direction instead.
            vfx.position = muzzle + forward * (coneRange * 0.5f);
            vfx.rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(90f, 0f, 0f);

            float width = Mathf.Tan(coneAngle * 0.5f * Mathf.Deg2Rad) * coneRange * 2f;
            vfx.localScale = new Vector3(width, coneRange * 0.5f, width);
        }

        private void HideVfx()
        {
            if (vfx != null)
                vfx.gameObject.SetActive(false);
        }

        private Transform BuildVfx()
        {
            GameObject cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = "Flamethrower Spray VFX (cheap, cosmetic only)";
            // Removed immediately, not with Destroy, so this never sits in the world as a solid,
            // physics-blocking object even for one frame - the same trick ZipGunAbility's own tether
            // marker uses.
            DestroyImmediate(cone.GetComponent<Collider>());

            var renderer = cone.GetComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = vfxColor };
            renderer.sharedMaterial = material;

            return cone.transform;
        }
    }
}
