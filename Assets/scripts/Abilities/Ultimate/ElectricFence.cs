using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// The ring itself - Tudor's ultimate spec (2026-09-13): "a ring around the caster that damages
    /// and slows enemies passing through it. Stays where it is cast." ElectricFenceAbility only
    /// decides WHEN it goes down (at the caster's own feet, spending the ultimate meter); everything
    /// about what the ring does once it exists lives here, on the prefab - the same split Mine.cs and
    /// MineAbility already use.
    ///
    /// VICTIM-SIDE, EVERY CLIENT, PER-TARGET STATE. FixedUpdate below runs on every machine, the
    /// owner's own included. It DISCOVERS enemies of OwnerTeam with an OverlapSphere sized to the
    /// ring itself (so anything already inside, or just outside the band, is found), then - once a
    /// target is discovered - keeps tracking it by its own live Transform every frame from then on,
    /// never re-relying on the overlap query for a target it already knows about. That second part
    /// matters for the "teleported across the ring in one step" case the Task 1.11b addendum's
    /// verify steps call out: a target already inside the ring is guaranteed to have been discovered
    /// (the overlap radius covers the whole interior), so when it jumps outside in a single frame,
    /// this fence is still reading its real position that frame and catches the crossing - even
    /// though the jump may have landed the target outside the discovery radius itself.
    ///
    /// ApplyDamage/ApplyStatus already no-op on every machine but the target's own owner
    /// (PlayerHealth.ApplyDamage, PlayerStatusEffects.Apply) - the same trust model FireField and
    /// Mine use - so every client is free to call them on every enemy it can see; only the real
    /// owner of that target ever does anything with the call. One FenceCrossingState per target (see
    /// its own class comment for the in-band/side-flip/cooldown rule) is what lets each victim's own
    /// client keep its own independent cooldown, exactly as the addendum requires.
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

        [SerializeField, Tooltip("Which layers this fence can hit. Default is where living players " +
                 "and practice dummies are; nothing on any other layer has an IDamageable to find, so " +
                 "widening this only costs performance.")]
        private LayerMask detectionMask = ~0;

        [SerializeField, Tooltip("The ring's visible mesh, if any. Resized sideways to match Radius " +
                 "the moment the fence is placed, so one prefab covers every size of ring. Left empty " +
                 "skips resizing anything.")]
        private Transform visual;

        // Not a design tunable: how many colliders one discovery pass considers - matches
        // FireField.MaxBurningColliders' identical reasoning for a similarly small arena radius.
        private const int MaxOverlapColliders = 32;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Scratch set for one discovery pass, so a target made of several colliders (a player's own
        // body) is only considered once per pass - the same trick FireField.burning and Mine.caught use.
        private readonly HashSet<IDamageable> seenThisPass = new HashSet<IDamageable>();

        // Every discovered enemy's own crossing/cooldown state, kept for as long as this fence lives -
        // see the class comment for why discovery only needs to happen once per target.
        private readonly Dictionary<IDamageable, FenceCrossingState> tracked =
            new Dictionary<IDamageable, FenceCrossingState>();

        // Scratch list for one evaluation pass: entries whose target turned out to be destroyed get
        // removed from `tracked` after the foreach below finishes, never during it (mutating a
        // Dictionary mid-enumeration throws) - reused every frame instead of allocated fresh.
        private readonly List<IDamageable> pruneBuffer = new List<IDamageable>();

        private CasterFollower follower;

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
            follower = new CasterFollower(followsCaster, OwnerActor);

            if (visual != null)
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);
        }

        private void FixedUpdate()
        {
            // If Follows Caster is on, the ring itself moves here before targets are evaluated below
            // - a perfectly STATIONARY enemy the moving ring sweeps past still reads as a crossing
            // (their distance from the ring's new centre passes through Radius) exactly as if they
            // had walked through a fixed ring. Intended, not a bug: the ring passed through them
            // either way, and FenceCrossingState only ever sees relative distance, never who moved.
            follower?.Tick(transform);

            DiscoverNewTargets();
            EvaluateTrackedTargets();
        }

        /// <summary>
        /// Finds enemies of OwnerTeam within reach of the ring and starts tracking any not already
        /// known. The search radius covers the whole interior plus a little past the outer band edge
        /// - generous enough that an enemy approaching at normal speed is discovered a frame or two
        /// before it could reach the band, so its "outside" side is on record before any crossing.
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

                // Unity's fake-null: true for a destroyed dummy/player object even though the C#
                // reference itself is not null - the same check MineTargeting already relies on.
                // Review fix (minor): a destroyed target is never coming back, so its entry is
                // forgotten entirely instead of being skipped forever on every future frame.
                if (targetComponent == null || target == null)
                {
                    pruneBuffer.Add(target);
                    continue;
                }

                if (!target.IsAlive)
                {
                    // Review fix (respawn false-crossing): forget which side this target was last
                    // seen on while it is dead. PlayerLifecycle.RespawnPlayer teleports a revived
                    // player straight to a spawn point with no regard for where the fence is - without
                    // this, the very next alive sample would read that teleport as a "crossing" and
                    // land a free hit + slow on someone who just respawned, possibly nowhere near this
                    // fence. Resetting every frame the target reads as dead (rather than only once, on
                    // the dead-to-alive edge) needs no extra "was it already dead last frame" bit to
                    // keep in sync - re-resetting an already-reset state is a no-op. Side-flip damage
                    // for a target that blinks/teleports across the ring WHILE ALIVE is untouched: this
                    // branch only ever runs for a target that is currently dead.
                    pair.Value.Reset();
                    continue;
                }

                Vector3 delta = targetComponent.transform.position - transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;

                if (!pair.Value.ShouldHit(distance, now))
                    continue;

                target.ApplyDamage(new DamageInfo(damagePerPass, OwnerActor, OwnerTeam, -1,
                                                   DamageSource.Zone, false, targetComponent.transform.position));

                var slow = new StatusEffectSpec { kind = StatusKind.Slow, duration = slowSeconds, magnitude = slowMagnitude };
                (target as IStatusReceiver)?.ApplyStatus(slow, OwnerActor);
            }

            foreach (IDamageable destroyed in pruneBuffer)
                tracked.Remove(destroyed);
        }

        /// <summary>Not a teammate, not the caster, not a structure, and locally authoritative -
        /// the same three rules MineTargeting.SelectTargets enforces for a mine's own trigger, reused
        /// individually here since discovery walks colliders one at a time rather than building a
        /// list to filter in bulk.</summary>
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
