using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// A proximity mine dropped at the caster's feet - Tudor's Equipment spec: 20 damage plus a
    /// slow, 2 charges, 10s per charge (the charges live on MineAbility, the module that places
    /// these). This class is only the networked object and its own trigger/blast; MineAbility
    /// decides when one gets placed and prunes the oldest once Max Active Mines is exceeded.
    ///
    /// EVERY BLAST NUMBER LIVES HERE, ON THE PREFAB - unlike Portal's diameter, which only matters
    /// to the placer's own channel check, a mine's damage/slow/radii/arm-delay are read by EVERY
    /// client's trigger and detonation logic, and this prefab is the same committed asset on every
    /// machine (see NetworkedDeployable's own class comment on why Portal duplicates its diameter
    /// into instantiationData - that reason does not apply here, because nothing here needs to
    /// survive a same-session retune without every machine rebuilding). Persistence is
    /// NetworkedDeployable's own Lifetime Seconds field - Tudor's Persist Seconds is that field
    /// under a different name, not a second copy. The only thing that genuinely varies per
    /// placement is Seq (this owner's Nth mine, for the oldest-first prune - see
    /// DeployablePruning), so Seq is the only value that travels through instantiationData.
    ///
    /// TRIGGER IS VICTIM-SIDE, EVERY CLIENT. FixedUpdate below runs on every machine, the owner's
    /// own included, once this mine is armed (Age plus real time elapsed on THIS client - see
    /// secondsSincePlaced - is at least Arm Delay Seconds). It looks for an IDamageable this client
    /// actually has authority over and that is an enemy of OwnerTeam (MineTargeting.SelectTargets):
    /// its own player, or its own local practice dummies - never a remote player, whose ApplyDamage
    /// would silently no-op on this machine anyway. The FIRST client to see a valid target sends
    /// RPC_Detonate AllViaServer, so every client - including one whose own local target never
    /// walked close enough - agrees on the same blast at the same point in the same relative order.
    /// MineDetonationState.TryDetonate is what makes sure a client that sees both its own trigger
    /// fire AND the resulting RPC only ever runs the explosion once.
    ///
    /// THE OWNER DOES NOT DESTROY THIS THE INSTANT IT DETONATES (code review finding). PUN silently
    /// drops any RPC addressed to a PhotonView that no longer exists (see PhotonNetworkPart.ExecuteRpc's
    /// own "Maybe GO was destroyed but RPC not cleaned up" case), and relay delivery order is not a
    /// guarantee - a bystander's own RPC_Detonate could still be in flight when the owner's
    /// PhotonNetwork.Destroy call reaches the room, and that bystander would silently take no damage
    /// at all. RPC_Detonate therefore only hides the visual and relies on the 'detonated' flag
    /// (already set by TryDetonate) to stop the trigger - on EVERY client, immediately - and the
    /// OWNER alone schedules the real PhotonNetwork.Destroy (through the shared NetworkedDeployable.
    /// RequestDestroy guard) Destroy Delay Seconds later, giving every other client's own copy of
    /// this same RPC time to arrive first.
    /// </summary>
    public sealed class Mine : NetworkedDeployable
    {
        [Header("Blast")]
        [SerializeField, Tooltip("Damage dealt to every enemy caught in the explosion. Tudor's spec: 20.")]
        private float damage = 20f;

        [SerializeField, Tooltip("How much slower an enemy caught in the blast moves, as a fraction " +
                 "of their normal speed - 0.40 means 40% slower. Stacks with any other slow already " +
                 "on them, capped by GameplayConfig's own Slow Cap.")]
        private float slowMagnitude = 0.40f;

        [SerializeField, Tooltip("How many seconds the slow lasts.")]
        private float slowSeconds = 2.0f;

        [Header("Trigger")]
        [SerializeField, Tooltip("How close an enemy must walk, in metres, for this mine to arm " +
                 "itself off and detonate. Smaller reads as a mine you can tiptoe past; larger " +
                 "punishes crossing the general area.")]
        private float triggerRadius = 1.8f;

        [SerializeField, Tooltip("How far the explosion reaches, in metres, once it goes off - " +
                 "everything with health in this radius takes the damage and the slow, not only " +
                 "whoever tripped it. Kept a little larger than Trigger Radius so a second enemy " +
                 "standing close by is caught too, without needing to step on the mine itself.")]
        private float explosionRadius = 2.2f;

        [SerializeField, Tooltip("Seconds after being placed before this mine can trigger at all, " +
                 "so it can never detonate at the caster's own feet the instant it is dropped.")]
        private float armDelaySeconds = 0.5f;

        [SerializeField, Tooltip("Which layers this mine's trigger and explosion can catch. Default " +
                 "is everything with health - a player or a practice dummy; nothing on any other " +
                 "layer has an IDamageable to find, so widening this only costs performance.")]
        private LayerMask detectionMask = ~0;

        [Header("Visibility (A5, Tudor 2026-09-17 evening - GDD spec)")]
        [SerializeField, Tooltip("Seconds after being placed before this mine turns invisible to the " +
                 "enemy team and fades to a translucent ghost for its own team, so only teammates can " +
                 "still tell where it is. Distinct from Arm Delay Seconds above: a mine can still " +
                 "detonate on an enemy who walked in before it vanished. See MineVisibilityRule.")]
        private float invisibleAfterSeconds = 1f;

        [Header("Destruction")]
        [SerializeField, Tooltip("Seconds between this mine detonating and its object actually " +
                 "leaving the game - not zero. RPC_Detonate is what applies the damage and slow, on " +
                 "EVERY client, and PUN silently drops any RPC whose target PhotonView no longer " +
                 "exists (see the class comment) - so the owner's PhotonNetwork.Destroy must wait " +
                 "long enough for every other client's own copy of this same RPC to have already " +
                 "arrived, or a bystander standing inside Explosion Radius could silently take no " +
                 "damage at all. The mine already looks and behaves gone well before this: its " +
                 "visual hides and its trigger already stopped (Mine Detonation State's own " +
                 "'detonated' flag) the instant RPC_Detonate runs on each client. Controller's call: 0.5.")]
        private float destroyDelaySeconds = 0.5f;

        [SerializeField, Tooltip("The mine's visible model. Hidden on every client the instant this " +
                 "mine detonates - see Destroy Delay Seconds for why the underlying object survives " +
                 "a little longer than that. Left empty just skips hiding anything.")]
        private Transform visual;

        // Not a design tunable: how many colliders one overlap considers. A mine's blast is small
        // enough that this comfortably covers every player plus every practice dummy at once - see
        // ExplodeOnImpact.MaxSplashColliders for the same reasoning at a larger radius.
        private const int MaxOverlapColliders = 16;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // One blast, one hit per victim - a player is several colliders to Physics, same reasoning
        // as ExplodeOnImpact.caught and FireField.burning.
        private readonly HashSet<IDamageable> caught = new HashSet<IDamageable>();

        // Owner-independent: every client (the owner's own included) ticks and detonates its own
        // copy of this state - see the class comment.
        private MineDetonationState detonation;

        // The real Time.time this mine's OnPlaced ran on THIS client, so FixedUpdate can turn Age
        // (a one-time snapshot taken at OnPhotonInstantiate - see NetworkedDeployable's own class
        // comment) into a number that keeps growing: secondsSincePlaced = Age + (Time.time - this).
        // Without this, a mine's Age would read as "however old it already was the instant this
        // client learned about it" forever, and IsArmed would never become true for a normal
        // (non-late-joining) placer, whose Age is already ~0 the moment it is set.
        private float localPlacedRealTime;

        // Set once the trigger RPC has actually been sent, so a target still standing in range on
        // the next FixedUpdate does not fire a second RPC while the first is still in flight.
        private bool triggerSent;

        public int Seq { get; private set; }

        /// <summary>Trigger Radius, read-only - MineView (ability visuals step 3) draws the owner team's trigger ring
        /// from this one number.</summary>
        public float TriggerRadius => triggerRadius;

        /// <summary>Explosion Radius, read-only - MineView flashes the blast ring at this size.</summary>
        public float ExplosionRadius => explosionRadius;

        /// <summary>Invisible After Seconds, read-only - MineView (A5) switches every viewer's MineVisibility at
        /// this age.</summary>
        public float InvisibleAfterSeconds => invisibleAfterSeconds;

        /// <summary>Seconds since this mine was ACTUALLY placed, continuously updated on THIS client - Age (a
        /// one-time network-agreed snapshot) plus real time elapsed since OnPlaced ran here. FixedUpdate's own arm-
        /// delay check and MineView's own visibility check (A5) are two readers of the exact same number, so they
        /// can never disagree about "how old is this mine right now" - see localPlacedRealTime's own comment.</summary>
        public float SecondsSincePlaced => (float)Age + (Time.time - localPlacedRealTime);

        /// <summary>Task T3 (telemetry): the id of the MineAbility that placed this, threaded through
        /// instantiationData (MineAbility.PlaceMine appends Definition.Id right after Seq) since this
        /// deployable is a separate prefab with no AbilityDefinition of its own to read. -1 if the
        /// data is missing (see the same defensive fallback as Seq, just below).</summary>
        public int AbilityId { get; private set; } = -1;

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            if (data != null && data.Length >= 1)
            {
                Seq = (int)data[0];
            }
            else
            {
                Debug.LogError($"[Mine] {name} was placed without its instantiation data - the " +
                                "oldest-mine prune may misorder it against this owner's other mines.");
            }

            if (data != null && data.Length >= 2 && data[1] is int abilityId)
                AbilityId = abilityId;

            detonation = new MineDetonationState(armDelaySeconds);
            localPlacedRealTime = Time.time;

            Register(this);
        }

        private void OnDestroy() => Unregister(this);

        private void FixedUpdate()
        {
            // Defensive backstop (NetworkedDeployable's own class comment, FAIL #15): a copy that
            // arrived already past Lifetime Seconds - the narrow cache-removal/destroy race, not the
            // normal path - must never trigger. IsExpired already hid this mine's own visual and
            // disabled its own collider, but neither of those stops FixedUpdate's OverlapSphere query
            // below, which looks at OTHER colliders, not this mine's - hence the explicit check here.
            if (IsExpired)
                return;

            if (detonation == null || detonation.Detonated || triggerSent)
                return;

            if (!detonation.IsArmed(SecondsSincePlaced))
                return;

            if (!AnyEnemyWithin(triggerRadius))
                return;

            triggerSent = true;
            photonView.RPC(nameof(RPC_Detonate), RpcTarget.AllViaServer, transform.position);
        }

        [PunRPC]
        private void RPC_Detonate(Vector3 at)
        {
            // Guards the one real explosion: the first RPC_Detonate any client receives, whether
            // that is this client's own trigger echoing back or another client's copy of this same
            // mine having triggered first - see MineDetonationState's own class comment. The same
            // flag is also what stops FixedUpdate's own trigger check from now on - nothing extra to
            // add there.
            if (detonation == null || !detonation.TryDetonate())
                return;

            int hitCount = ApplyBlast(at);
            LogDetonation(at, hitCount);

            // Every client hides the visual right away - a detonated mine must not keep looking
            // armed just because its object is still alive for a little longer. See the class
            // comment for why the object itself outlives this by Destroy Delay Seconds.
            HideVisual();

            // Only the owner ends this object's life, and only after the delay - see RequestDestroy
            // and the class comment for why an immediate destroy here is the actual bug being fixed.
            if (IsOwnerClient)
                StartCoroutine(DestroyAfterDetonation());
        }

        private IEnumerator DestroyAfterDetonation()
        {
            if (destroyDelaySeconds > 0f)
                yield return new WaitForSeconds(destroyDelaySeconds);

            RequestDestroy();
        }

        private void HideVisual()
        {
            if (visual != null)
                visual.gameObject.SetActive(false);
        }

        private bool AnyEnemyWithin(float radius)
        {
            List<IDamageable> candidates = OverlapDamageables(transform.position, radius);
            return MineTargeting.SelectTargets(candidates, OwnerActor, OwnerTeam).Count > 0;
        }

        private int ApplyBlast(Vector3 at)
        {
            List<IDamageable> candidates = OverlapDamageables(at, explosionRadius);
            List<IDamageable> targets = MineTargeting.SelectTargets(candidates, OwnerActor, OwnerTeam);

            var slow = new StatusEffectSpec { kind = StatusKind.Slow, duration = slowSeconds, magnitude = slowMagnitude, abilityId = AbilityId };

            foreach (IDamageable target in targets)
            {
                target.ApplyDamage(new DamageInfo(damage, OwnerActor, OwnerTeam, -1, DamageSource.Splash, false, at, AbilityId));
                (target as IStatusReceiver)?.ApplyStatus(slow, OwnerActor);
            }

            return targets.Count;
        }

        private List<IDamageable> OverlapDamageables(Vector3 at, float radius)
        {
            caught.Clear();
            int count = Physics.OverlapSphereNonAlloc(at, radius, overlapBuffer, detectionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                if (candidate != null)
                    caught.Add(candidate);
            }

            return new List<IDamageable>(caught);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDetonation(Vector3 at, int hitCount)
        {
            Debug.Log($"[MINE] detonated at {at} - {hitCount} enemy(ies) hit for {damage:0.#} + slow {slowMagnitude:0.00} for {slowSeconds:0.#}s");
        }

        // ---- per-owner registry, so MineAbility can find and prune its own mines ------------------
        // Identical shape to Portal's own registry (see its class comment for why this is a plain
        // per-client static dictionary rather than networked state).

        private static readonly List<Mine> EmptyList = new List<Mine>();
        private static readonly Dictionary<int, List<Mine>> byOwner = new Dictionary<int, List<Mine>>();

        public static IReadOnlyList<Mine> ForOwner(int actorNumber) =>
            byOwner.TryGetValue(actorNumber, out List<Mine> list) ? list : EmptyList;

        private static void Register(Mine mine)
        {
            if (!byOwner.TryGetValue(mine.OwnerActor, out List<Mine> list))
            {
                list = new List<Mine>();
                byOwner.Add(mine.OwnerActor, list);
            }
            list.Add(mine);
        }

        private static void Unregister(Mine mine)
        {
            if (byOwner.TryGetValue(mine.OwnerActor, out List<Mine> list))
                list.Remove(mine);
        }
    }
}
