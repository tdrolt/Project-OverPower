using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Net;

namespace Overpower.Weapons
{
    /// <summary>
    /// A patch of burning ground that hurts enemies standing in it, then goes out. Left behind by
    /// the cursor rocket; nothing else spawns one yet.
    ///
    /// UNLIKE A PROJECTILE, THIS IS A REAL NETWORKED OBJECT. Projectiles are local objects rebuilt
    /// from a fire RPC on each client, because they live for half a second and spawning nine
    /// networked objects a second per player would be absurd. A fire field lives for three
    /// seconds, and somebody joining the match in the middle of those three seconds has to see it
    /// standing there - which is what PhotonNetwork.Instantiate gives and a local spawn does not.
    ///
    /// ITS NUMBERS ARRIVE AS instantiationData, NOT AS FIELD ASSIGNMENTS. PhotonNetwork.Instantiate
    /// hands back only the CALLER's copy of the object; every other client builds its own from the
    /// prefab and never sees anything written to the returned reference. This project already has
    /// that bug: an ability assigned its damage to the returned object, the field was dead on
    /// every other machine, and the damage that actually landed was whatever the victim's own copy
    /// of the prefab happened to say. Spawn() below packs the numbers into instantiationData and
    /// OnPhotonInstantiate unpacks them, so all nine machines burn for the same amount.
    ///
    /// Damage is victim-side, like every other damage path here: each client ticks its own copy of
    /// the field, calls ApplyDamage on everything inside, and only the victim's own PlayerHealth
    /// acts on it. Destruction is NOT victim-side - only the owner may PhotonNetwork.Destroy, or
    /// each of nine clients tries and eight of them log an error.
    ///
    /// It deliberately does not log per tick. A previous area-of-effect logged every tick, which
    /// at ten ticks a second across seven seconds was seventy console lines per cast on every
    /// client, and froze builds outright because each console write hits the disk before
    /// returning. One line when the field lights up is the whole budget.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class FireField : MonoBehaviourPun, IPunInstantiateMagicCallback
    {
        [SerializeField, Tooltip("How long the ground burns, in seconds, before the field goes " +
                 "out and deletes itself.")]
        private float duration = 3f;

        [SerializeField, Tooltip("Damage an enemy standing in the fire takes per second. What " +
                 "lands on any one tick is this multiplied by Tick Interval, so changing the " +
                 "tick rate changes how the damage is parcelled up but not how much of it there " +
                 "is in total.")]
        private float damagePerSecond = 12f;

        [SerializeField, Tooltip("How far the fire reaches from its centre, in metres. The visual " +
                 "is resized to match this, so the burning patch is always exactly as big as it " +
                 "looks.")]
        private float radius = 2.5f;

        [SerializeField, Tooltip("Seconds between damage ticks. Bigger means chunkier, more " +
                 "readable hits that are easier to react to; smaller means a smooth burn that " +
                 "punishes clipping the edge. It does not change total damage - see Damage Per " +
                 "Second.")]
        private float tickInterval = 1f;

        [SerializeField, Tooltip("Which layers can catch fire. Default is where living players " +
                 "and practice dummies are; nothing on any other layer has health to lose.")]
        private LayerMask burnMask = ~0;

        [SerializeField, Tooltip("The flames. Scaled sideways to match Radius when the field " +
                 "lights up, so one prefab covers every size of fire; its height is left alone " +
                 "and is yours to author here.")]
        private Transform visual;

        // Not a tuning value: how many colliders one tick considers, matching ExplodeOnImpact.
        private const int MaxBurningColliders = 32;
        private static readonly Collider[] OverlapBuffer = new Collider[MaxBurningColliders];

        // One tick, one hit per victim - a player is several colliders to Physics. Same reasoning
        // as ExplodeOnImpact.caught, and reused across ticks rather than reallocated per tick.
        private readonly HashSet<IDamageable> burning = new HashSet<IDamageable>();

        private int weaponId = -1;
        private int sourceActorNumber = -1;
        private int sourceTeamId = -1;

        /// <summary>
        /// Puts a fire field on the ground for every client. Call it on the ONE client that
        /// decided the fire should exist - PhotonNetwork.Instantiate is not a broadcast you can
        /// safely repeat, and nine callers means nine fires.
        ///
        /// The numbers come off the prefab's own FireField and are then sent explicitly over the
        /// wire, which looks like a round trip and is not: it keeps a single tuning home (the
        /// prefab) while making the transfer the kind that actually reaches other machines. A
        /// caller that later wants a bigger or shorter-lived fire overrides them here instead of
        /// needing a second prefab.
        /// </summary>
        /// <param name="prefab">The fire field prefab. It must live under a Resources folder -
        /// PhotonNetwork.Instantiate resolves prefabs by name through PUN's default pool, which
        /// loads them with Resources.Load.</param>
        public static void Spawn(GameObject prefab, Vector3 position, int weaponId)
        {
            if (prefab == null)
                return;

            FireField template = prefab.GetComponent<FireField>();
            if (template == null)
            {
                Debug.LogError($"[FireField] prefab '{prefab.name}' has no FireField component - " +
                                "nothing was spawned.");
                return;
            }

            // Networked spawns need a room. Outside one this would throw from deep inside PUN
            // with nothing pointing back here, so say so plainly instead.
            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[FireField] not in a Photon room, so no fire field was spawned. " +
                                  "Join a match before firing a weapon that leaves one.");
                return;
            }

            object[] data =
            {
                template.radius,
                template.damagePerSecond,
                template.duration,
                weaponId,
            };

            PhotonNetwork.Instantiate(prefab.name, position, Quaternion.identity, 0, data);
        }

        /// <summary>Runs on every client, including the one that called Spawn, once the object
        /// exists there. This is the only place the field's numbers are set - see the class
        /// comment on why assigning them to the object Spawn returned would not work.</summary>
        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            object[] data = info.photonView.InstantiationData;
            if (data != null && data.Length >= 4)
            {
                radius = (float)data[0];
                damagePerSecond = (float)data[1];
                duration = (float)data[2];
                weaponId = (int)data[3];
            }
            else
            {
                // Falling back to the prefab's own values keeps the fire burning rather than
                // silently doing nothing, but it means this client may disagree with the others -
                // which is the exact failure this data is here to prevent, so it is loud.
                Debug.LogError($"[FireField] {name} was spawned without its instantiation data, so " +
                                "it is falling back to the prefab's numbers and may not match the " +
                                "other clients.");
            }

            // The owner of the object is whoever fired the shot that left it, so kill credit and
            // the no-friendly-fire rule both come from there rather than being sent again.
            sourceActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            Teams.TryGetTeam(info.Sender, out sourceTeamId);

            if (visual != null)
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);

            Debug.Log($"[FireField] lit at {transform.position} - {radius:0.##}m, " +
                       $"{damagePerSecond:0.#} dmg/s for {duration:0.##}s");

            StartCoroutine(Burn());
        }

        /// <summary>
        /// Burns for Duration, dealing Damage Per Second in Tick Interval helpings, then puts
        /// itself out.
        ///
        /// The last helping is shortened rather than skipped, so a three second fire on a two
        /// second tick deals its full three seconds of damage instead of quietly losing the
        /// remainder. Every client runs this; only the owner deletes the object at the end.
        /// </summary>
        private IEnumerator Burn()
        {
            if (tickInterval <= 0f)
            {
                // A zero interval would spin this loop forever without ever advancing. One tick at
                // the end is the least surprising reading of "no interval".
                Debug.LogError($"[FireField] {name} has a Tick Interval of {tickInterval}, which " +
                                "cannot advance - burning as a single tick at the end instead.");
                tickInterval = duration;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float step = Mathf.Min(tickInterval, duration - elapsed);
                yield return new WaitForSeconds(step);
                elapsed += step;

                BurnEveryoneInside(damagePerSecond * step);
            }

            // Owner only. Nine clients each calling this is the bug that produced eight errors per
            // cast in the ability this rule was learned from.
            if (photonView.IsMine)
                PhotonNetwork.Destroy(gameObject);
        }

        private void BurnEveryoneInside(float amount)
        {
            if (amount <= 0f || radius <= 0f)
                return;

            burning.Clear();

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, OverlapBuffer,
                                                       burnMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null || IsFriendly(target) || !burning.Add(target))
                    continue;

                target.ApplyDamage(new DamageInfo(amount, sourceActorNumber, sourceTeamId, weaponId,
                                                   DamageSource.Burn, false, transform.position));
            }
        }

        /// <summary>The same no-friendly-fire rule as the rest of the damage paths, so you cannot
        /// set your own team on fire. Unknown teams fail OPEN and stay valid targets, matching
        /// ProjectileMotor.FliesThrough and Teams.AreSameTeam.</summary>
        private bool IsFriendly(IDamageable target)
        {
            if (target.ActorNumber == sourceActorNumber)
                return true;

            return sourceTeamId >= 0 && target.TeamId == sourceTeamId;
        }
    }
}
