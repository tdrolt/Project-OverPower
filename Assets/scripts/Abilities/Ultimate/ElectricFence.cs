using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// The ring ultimate's networked object: damages and slows enemies passing through it, stays where cast unless Follows
    /// Caster. ElectricFenceAbility only decides WHEN it goes down; what the ring does lives here (as Mine / MineAbility).
    /// VICTIM-SIDE, EVERY CLIENT, PER-TARGET STATE: FixedUpdate runs on every machine, the owner's included. An OverlapSphere
    /// sized to the ring DISCOVERS enemies of OwnerTeam once; after that each is tracked by its live Transform every frame,
    /// never re-relying on the overlap, so a target that teleports across the ring in one step (and lands outside the
    /// discovery radius) is still read and its crossing caught. ApplyDamage/ApplyStatus no-op on every machine but the
    /// target's owner (the FireField/Mine trust model), so each victim's client keeps its own FenceCrossingState and cooldown.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public sealed class ElectricFence : NetworkedDeployable
    {
        [Header("Ring")]
        [SerializeField, Tooltip("Radius of the ring, in metres, measured from where it was cast. " +
                 "Claude's number: 6.")]
        private float radius = 6f;

        [SerializeField, Tooltip("How wide the damaging band is, in metres, centred on Radius - a " +
                 "target within half of this either side of the ring counts as standing in it. " +
                 "Claude's number: 1.0.")]
        private float ringThickness = 1f;

        [Header("Hit")]
        [SerializeField, Tooltip("Damage dealt the moment a pass through the ring lands. Claude's " +
                 "number: 25.")]
        private float damagePerPass = 25f;

        [SerializeField, Tooltip("Seconds before the SAME target can be hit by this fence again - " +
                 "its own independent cooldown, kept on the victim's own client. Claude's number: 1.0.")]
        private float perTargetCooldownSeconds = 1f;

        [SerializeField, Tooltip("How much slower a hit target moves, as a fraction of normal speed " +
                 "- 0.50 means half speed. Stacks with any other slow already on them, capped by " +
                 "GameplayConfig's own Slow Cap. Claude's number: 0.50.")]
        private float slowMagnitude = 0.5f;

        [SerializeField, Tooltip("How many seconds the slow lasts. Claude's number: 1.5.")]
        private float slowSeconds = 1.5f;

        [Header("Anchor")]
        [SerializeField, Tooltip("If checked, the fence follows the caster instead of staying where " +
                 "it was cast. Tudor's spec: the fence stays put, so this defaults OFF - flip it to " +
                 "make a fence that tracks its owner instead.")]
        private bool followsCaster = false;

        protected override bool FollowsCaster => followsCaster;

        [SerializeField, Tooltip("Which layers this fence can hit. Default is where living players " +
                 "and practice dummies are; nothing on any other layer has an IDamageable to find, so " +
                 "widening this only costs performance.")]
        private LayerMask detectionMask = ~0;

        [SerializeField, Tooltip("The ring's visible mesh, if any. Resized sideways to match Radius " +
                 "the moment the fence is placed, so one prefab covers every size of ring. Left empty " +
                 "skips resizing anything.")]
        private Transform visual;

        // Not a design tunable: how many colliders one discovery pass considers (FireField.MaxBurningColliders' reasoning).
        private const int MaxOverlapColliders = 32;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Scratch set for one discovery pass, so a target of several colliders is only considered once (as FireField, Mine).
        private readonly HashSet<IDamageable> seenThisPass = new HashSet<IDamageable>();

        // Every discovered enemy's crossing/cooldown state, kept for as long as this fence lives (discovery is once per target).
        private readonly Dictionary<IDamageable, FenceCrossingState> tracked =
            new Dictionary<IDamageable, FenceCrossingState>();

        // Scratch list: destroyed targets are removed from `tracked` after the foreach, never during it (mutating a
        // Dictionary mid-enumeration throws).
        private readonly List<IDamageable> pruneBuffer = new List<IDamageable>();

        private CasterFollower follower;

        /// <summary>Telemetry: the id of the ElectricFenceAbility that placed this, threaded through instantiationData
        /// like AoeZone.AbilityId. -1 if the data is missing.</summary>
        public int AbilityId { get; private set; } = -1;

        /// <summary>Radius, read-only - FenceCageView builds the cage on this one number.</summary>
        public float Radius => radius;

        /// <summary>Damage Per Pass, Slow Magnitude and Slow Seconds, read-only - the shop's pop-up shows them.</summary>
        public float DamagePerPass => damagePerPass;
        public float SlowMagnitude => slowMagnitude;
        public float SlowSeconds => slowSeconds;

        /// <summary>Ring Thickness, read-only - FenceCageView draws the floor band exactly this wide.</summary>
        public float RingThickness => ringThickness;

        private void OnValidate()
        {
            radius = Mathf.Max(0.1f, radius);
            ringThickness = Mathf.Max(0.01f, ringThickness);
            damagePerPass = Mathf.Max(0f, damagePerPass);
            perTargetCooldownSeconds = Mathf.Max(0f, perTargetCooldownSeconds);
            slowMagnitude = Mathf.Clamp01(slowMagnitude);
            slowSeconds = Mathf.Max(0f, slowSeconds);
        }

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            if (data != null && data.Length >= 1 && data[0] is int abilityId)
                AbilityId = abilityId;

            follower = new CasterFollower(followsCaster, OwnerActor);

            if (visual != null)
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);
        }

        private void FixedUpdate()
        {
            // Defensive backstop (see NetworkedDeployable): a copy that arrived already past Lifetime Seconds (the narrow
            // cache-removal/destroy race) must never discover or hit anyone. IsExpired hides the visual but does not stop
            // the OverlapSphere query below, which looks at OTHER colliders - hence the explicit check.
            if (IsExpired)
                return;

            // If Follows Caster is on, the ring moves here before targets are evaluated: a STATIONARY enemy the moving ring
            // sweeps past still reads as a crossing. Intended: FenceCrossingState only sees relative distance, never who moved.
            follower?.Tick(transform);

            DiscoverNewTargets();
            EvaluateTrackedTargets();
        }

        /// <summary>
        /// Finds enemies of OwnerTeam within reach of the ring and starts tracking any not already known. The radius
        /// covers the whole interior plus a little past the outer band edge, so an approaching enemy is discovered a frame
        /// or two before it reaches the band and its "outside" side is on record before any crossing.
        /// </summary>
        private void DiscoverNewTargets()
        {
            float searchRadius = radius + ringThickness;
            int count = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, overlapBuffer,
                                                       detectionMask, QueryTriggerInteraction.Ignore);

            seenThisPass.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                if (candidate == null || !seenThisPass.Add(candidate) || tracked.ContainsKey(candidate))
                    continue;

                if (!IsEnemy(candidate))
                    continue;

                tracked.Add(candidate, new FenceCrossingState(radius, ringThickness, perTargetCooldownSeconds));
            }
        }

        private void EvaluateTrackedTargets()
        {
            if (tracked.Count == 0)
                return;

            float now = Time.time;
            pruneBuffer.Clear();

            foreach (KeyValuePair<IDamageable, FenceCrossingState> pair in tracked)
            {
                IDamageable target = pair.Key;
                Component targetComponent = target as Component;

                // Unity's fake-null: true for a destroyed object though the C# reference is not null (as MineTargeting).
                // A destroyed target never comes back, so its entry is forgotten rather than skipped forever.
                if (targetComponent == null || target == null)
                {
                    pruneBuffer.Add(target);
                    continue;
                }

                if (!target.IsAlive)
                {
                    // Forget which side this target was last seen on while it is dead: RespawnPlayer teleports a revived
                    // player to a spawn point regardless of the fence, and the next alive sample would read that as a
                    // "crossing" (a free hit + slow, possibly nowhere near this fence). Resetting every dead frame needs no
                    // "was dead last frame" bit; re-resetting is a no-op. A teleport across the ring WHILE ALIVE still counts.
                    pair.Value.Reset();
                    continue;
                }

                Vector3 delta = targetComponent.transform.position - transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;

                if (!pair.Value.ShouldHit(distance, now))
                    continue;

                target.ApplyDamage(PlacedEffects.FenceTick(damagePerPass, OwnerActor, OwnerTeam, targetComponent.transform.position, AbilityId,
                                                            PlacedServerTimestampMs));

                StatusEffectSpec slow = PlacedEffects.PlacedSlow(slowSeconds, slowMagnitude, AbilityId, PlacedServerTimestampMs);
                (target as IStatusReceiver)?.ApplyStatus(slow, OwnerActor);
            }

            foreach (IDamageable destroyed in pruneBuffer)
                tracked.Remove(destroyed);
        }

        /// <summary>Not a teammate, not the caster, not a structure, and locally authoritative - MineTargeting.SelectTargets'
        /// rules, applied one collider at a time.</summary>
        private bool IsEnemy(IDamageable candidate)
        {
            if (!candidate.HasLocalAuthority)
                return false;

            if (candidate is IStructure)
                return false;

            return !FriendlyFire.IsSelfOrTeammate(OwnerActor, candidate.ActorNumber, OwnerTeam, candidate.TeamId);
        }
    }
}
