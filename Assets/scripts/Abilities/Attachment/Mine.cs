using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// A proximity mine dropped at the caster's feet. This class is the networked object and its own trigger/blast;
    /// MineAbility (which holds the charges) decides when one gets placed and prunes the oldest past Max Active Mines.
    /// EVERY BLAST NUMBER LIVES HERE, ON THE PREFAB: damage, slow, radii and arm delay are read by EVERY client's trigger
    /// and detonation, and the prefab is the same committed asset on every machine, so unlike Portal's diameter nothing
    /// is duplicated into instantiationData. Persistence is NetworkedDeployable's Lifetime Seconds (Persist Seconds is
    /// that field under another name). Only Seq (this owner's Nth mine, for the oldest-first prune, see
    /// DeployablePruning) and the ability id travel through instantiationData.
    /// TRIGGER IS VICTIM-SIDE, EVERY CLIENT. FixedUpdate runs on every machine once armed (Age plus real time on THIS
    /// client, see SecondsSincePlaced, is at least Arm Delay Seconds) and looks for an IDamageable this client has
    /// authority over that is an enemy of OwnerTeam (MineTargeting.SelectTargets): its own player or local practice
    /// dummies, never a remote player, whose ApplyDamage would no-op here. The FIRST client to see a target sends
    /// RPC_Detonate AllViaServer so everyone agrees on one blast; MineDetonationState.TryDetonate makes it run once even
    /// if a client sees both its own trigger and the RPC.
    /// THE OWNER DOES NOT DESTROY THIS THE INSTANT IT DETONATES: PUN silently drops an RPC addressed to a PhotonView that
    /// no longer exists and relay order is not guaranteed, so a bystander's in-flight RPC_Detonate would be lost and they
    /// would take no damage. RPC_Detonate only hides the visual (the 'detonated' flag stops the trigger on every client
    /// at once); the OWNER alone schedules the PhotonNetwork.Destroy (via RequestDestroy) Destroy Delay Seconds later.
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

        [SerializeField, Tooltip("While the mine is still arming (Arm Delay Seconds above) its model blinks: " +
                 "this many seconds shown, then this many hidden, over and over, so anyone who can see it " +
                 "knows it cannot go off yet. Once it is armed it stops blinking and stays shown. " +
                 "Smaller is a faster flicker.")]
        private float blinkPeriodSeconds = 0.15f;

        [SerializeField, Tooltip("Which layers this mine's trigger and explosion can catch. Default " +
                 "is everything with health - a player or a practice dummy; nothing on any other " +
                 "layer has an IDamageable to find, so widening this only costs performance.")]
        private LayerMask detectionMask = ~0;

        [Header("Visibility")]
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

        // Not a design tunable: colliders one overlap considers; comfortably every player plus every practice dummy
        // (see ExplodeOnImpact.MaxSplashColliders).
        private const int MaxOverlapColliders = 16;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // One blast, one hit per victim - a player is several colliders to Physics, same reasoning
        // as ExplodeOnImpact.caught and FireField.burning.
        private readonly HashSet<IDamageable> caught = new HashSet<IDamageable>();

        // Owner-independent: every client (the owner's own included) ticks and detonates its own
        // copy of this state - see the class comment.
        private MineDetonationState detonation;

        // The real Time.time OnPlaced ran on THIS client: Age is a one-time snapshot from OnPhotonInstantiate, so
        // SecondsSincePlaced = Age + (Time.time - this) keeps growing. Without it IsArmed would never become true for a
        // normal placer, whose Age is ~0 when set.
        private float localPlacedRealTime;

        // Set once the trigger RPC has actually been sent, so a target still standing in range on
        // the next FixedUpdate does not fire a second RPC while the first is still in flight.
        private bool triggerSent;

        public int Seq { get; private set; }

        /// <summary>Trigger Radius, read-only - MineView draws the owner team's trigger ring from this one number.</summary>
        public float TriggerRadius => triggerRadius;

        /// <summary>Explosion Radius, read-only - MineView flashes the blast ring at this size.</summary>
        public float ExplosionRadius => explosionRadius;

        /// <summary>Damage, Slow Magnitude and Slow Seconds, read-only - the shop's pop-up shows them.</summary>
        public float Damage => damage;
        public float SlowMagnitude => slowMagnitude;
        public float SlowSeconds => slowSeconds;

        /// <summary>Invisible After Seconds, read-only - MineView (A5) switches every viewer's MineVisibility at
        /// this age.</summary>
        public float InvisibleAfterSeconds => invisibleAfterSeconds;

        /// <summary>Seconds since this mine was ACTUALLY placed, continuously updated on THIS client: Age plus real time
        /// since OnPlaced ran here. FixedUpdate's arm-delay check and MineView's visibility check (A5) read this one
        /// number, so they can never disagree about how old the mine is (see localPlacedRealTime).</summary>
        public float SecondsSincePlaced => (float)Age + (Time.time - localPlacedRealTime);

        /// <summary>True once RPC_Detonate has run on THIS client: read-only mirror of MineDetonationState.Detonated, the
        /// single source of truth (the Visual's activeSelf cannot tell a detonation from the IsExpired hide). False
        /// before OnPlaced.</summary>
        public bool Detonated => detonation != null && detonation.Detonated;

        /// <summary>True once this copy has read itself as already past Lifetime Seconds (NetworkedDeployable's own
        /// IsExpired, exposed read-only) - the defensive backstop for a late-join/cache-removal race, not a real
        /// detonation. MineView (A5) must not fade an expired mine back in for the owner's own team once
        /// NetworkedDeployable has already hidden its renderers.</summary>
        public bool HasExpired => IsExpired;

        /// <summary>The id of the MineAbility that placed this, threaded through instantiationData (MineAbility.PlaceMine
        /// appends Definition.Id right after Seq) since this deployable is a separate prefab with no AbilityDefinition
        /// of its own. -1 if the data is missing (same defensive fallback as Seq).</summary>
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

        private void Update()
        {
            // Local only, from this mine's own placed time - no RPC. Never fights the hiding: once detonated
            // or expired the model stays hidden, and enemies' own MineView renderer hiding is independent of
            // the model's active state, so a mine hidden from you stays hidden whatever the blink says.
            if (visual == null || detonation == null || detonation.Detonated || IsExpired)
                return;

            bool shown = MineArmingBlinkRule.IsShown(SecondsSincePlaced, detonation.IsArmed(SecondsSincePlaced), blinkPeriodSeconds);
            if (visual.gameObject.activeSelf != shown)
                visual.gameObject.SetActive(shown);
        }

        private void FixedUpdate()
        {
            // Backstop (FAIL #15): a copy that arrived already past Lifetime Seconds must never trigger. IsExpired hid the
            // visual and collider, but not the OverlapSphere below, which looks at OTHER colliders - hence the check.
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
            // The one real explosion: the first RPC_Detonate any client receives (its own trigger echoing back, or another
            // client's) - see MineDetonationState. The same flag stops FixedUpdate's trigger from now on.
            if (detonation == null || !detonation.TryDetonate())
                return;

            int hitCount = ApplyBlast(at);
            LogDetonation(at, hitCount);

            // Every client hides the visual at once; the object outlives this by Destroy Delay Seconds (class comment).
            HideVisual();

            // Only the owner ends this object's life, after the delay: an immediate destroy drops bystanders' in-flight RPCs.
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

            // The slow remembers when this mine was laid: the owner's own copy of the victim hears of it too, and an old mine must not end the owner's new respawn bubble (A26).
            StatusEffectSpec slow = PlacedEffects.PlacedSlow(slowSeconds, slowMagnitude, AbilityId, PlacedServerTimestampMs);

            foreach (IDamageable target in targets)
            {
                target.ApplyDamage(PlacedEffects.MineBlast(damage, OwnerActor, OwnerTeam, at, AbilityId, PlacedServerTimestampMs));
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
