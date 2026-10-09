using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;

namespace Overpower.Weapons
{
    /// <summary>
    /// A patch of burning ground that hurts enemies standing in it, then goes out. Left behind by the cursor rocket (DetonateAtCursor).
    /// A real NETWORKED object, unlike projectiles: it lives long enough that somebody joining mid-fire must see it, which PhotonNetwork.Instantiate
    /// gives and a local spawn does not.
    /// Its numbers arrive as instantiationData, NOT field assignments: Instantiate returns only the CALLER's copy, so a field written to it is dead
    /// on every other machine (an ability here once did that, and the damage that landed was the victim's own prefab value). Spawn() packs the
    /// numbers, OnPhotonInstantiate unpacks them, so all machines burn for the same amount.
    /// Damage is victim-side like every other path: each client ticks its own copy and calls ApplyDamage, only the victim's PlayerHealth acts.
    /// Destruction is NOT: only the owner may PhotonNetwork.Destroy, or each of nine clients tries and eight log an error.
    /// Never log per tick: each console write hits the disk before returning, and per-tick logging in an earlier area-of-effect froze builds.
    /// One line when the field lights up is the whole budget.
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

        [Header("Look (visual only - ability visuals step 6 amendment, 2026-09-18)")]
        [SerializeField, Tooltip("The bright ring drawn at the fire's true edge - Assets/Gameplay/UI/AimConeLine.mat. " +
                 "This carries most of the 'don't stand here' read alongside the filled disc's own team colour.")]
        private LineRenderer rim;

        [SerializeField, Tooltip("Team colours - Assets/Gameplay/Config/UiTheme.asset (Shot Color For). Tudor's " +
                 "call: the disc is the SHOOTER's team colour, not a fixed fire palette - a first attempt at a " +
                 "hot pale orange still blended into this arena's own tan/orange ground (the same trap team 0's " +
                 "near-white and the flamethrower's orange both hit), and a fixed colour cannot tell a player " +
                 "whose fire they are standing in anyway. Matches MineView/PortalView/FenceCageView/SplashShell, " +
                 "which all read the shooter/owner's team the same way.")]
        private UiTheme theme;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the filled disc. Kept modest on purpose so the ground " +
                 "reads as a translucent hazard, not a solid slab - the rim below carries the strong edge.")]
        private float fillOpacity = 0.55f;

        [SerializeField, Range(0f, 1f), Tooltip("Opacity of the rim outline - higher than the fill, since the rim " +
                 "carries most of the 'don't stand here' read.")]
        private float rimOpacity = 0.95f;

        [SerializeField, Tooltip("Points on the rim circle. 48 reads as round at the game camera's distance.")]
        private int rimSegments = 48;

        private MaterialPropertyBlock block;

        // Not a tuning value: how many colliders one tick considers, matching ExplodeOnImpact.
        private const int MaxBurningColliders = 32;
        private static readonly Collider[] OverlapBuffer = new Collider[MaxBurningColliders];

        // Not a design tunable: the burn area is an upright cylinder standing on the field's own floor position, so "standing in the drawn circle" is
        // exactly "being burned" whatever a player's height. 2 m comfortably covers a standing player's whole body (ankles to well over head).
        private const float BurnHeightMetres = 2f;

        // One tick, one hit per victim - a player is several colliders to Physics. Same reasoning
        // as ExplodeOnImpact.caught, and reused across ticks rather than reallocated per tick.
        private readonly HashSet<IDamageable> burning = new HashSet<IDamageable>();

        private int weaponId = -1;
        private int sourceActorNumber = -1;
        private int placedServerTimestampMs;
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
        /// <param name="damageMultiplier">
        /// The shot's combined damage multiplier (ProjectileContext.DamageMultiplier * FireTimeDamageMultiplier, the same product Damage and
        /// ExplodeOnImpact.SplashDamageAt apply) - 1 for a shot nothing has boosted. Multiplies damagePerSecond BEFORE it is packed into
        /// instantiationData, so every client, remote copies included (they only ever read the packed value), burns for the same already-scaled
        /// number. Clamped to >= 0 like WeaponFiring.SetStatMultipliers.
        /// </param>
        public static void Spawn(GameObject prefab, Vector3 position, int weaponId, float damageMultiplier = 1f)
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
                template.damagePerSecond * Mathf.Max(0f, damageMultiplier),
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

            // The owner of the object is whoever fired the shot that left it, so kill credit, the no-friendly-fire rule and the TEAM COLOUR below all come
            // from there with no new RPC or instantiationData slot (MineView/PortalView/FenceCageView read an owner's team the same way).
            sourceActorNumber = info.Sender != null ? info.Sender.ActorNumber : -1;
            Teams.TryGetTeam(info.Sender, out sourceTeamId);
            // When this field was set up, so its burn can say whether it predates its owner's respawn. A late joiner's replay of an old field reads a later
            // time here; fields are short-lived and the worst case is one tick counted as new. (A26)
            placedServerTimestampMs = info.SentServerTimestamp;

            Color teamColor = theme != null ? theme.ShotColorFor(sourceTeamId) : Color.white;

            if (visual != null)
            {
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);

                if (block == null)
                    block = new MaterialPropertyBlock();
                VisualTint.SetMeshColor(visual.GetComponent<Renderer>(), block, VisualTint.WithAlpha(teamColor, fillOpacity));
            }

            if (rim != null)
            {
                VisualTint.FillFlatCircle(rim, radius, rimSegments);
                VisualTint.SetLineColor(rim, VisualTint.WithAlpha(teamColor, rimOpacity));
            }

            // Vision: shown when any part of the disc is in sight, or it reaches my team.
            VisibleWhenSeen gate = GetComponent<VisibleWhenSeen>();
            if (gate != null)
                gate.SetSeenRadius(radius);

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

            // An upright capsule over the drawn disc, not a sphere at this field's own centre - see OverlapBurnZone. Still runs on every client; damage
            // stays victim-side, so this only changes WHICH colliders the query considers, never who applies the result.
            int count = OverlapBurnZone(Physics.defaultPhysicsScene, transform.position, radius, OverlapBuffer, burnMask);

            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null || IsFriendly(target) || !burning.Add(target))
                    continue;

                // abilityId -1: FireField's DoT is always a weapon's cursor leaf (the rocket), so the weapon id stays.
                target.ApplyDamage(PlacedEffects.FireFieldBurn(amount, sourceActorNumber, sourceTeamId, weaponId, transform.position, placedServerTimestampMs));
            }
        }

        /// <summary>
        /// The upright capsule a standing player's whole body sits in, over the field's own drawn disc. <paramref name="floorCentre"/> is this field's
        /// transform.position, which sits on the floor (DetonateAtCursor.OnExpired), so the capsule's base is on the ground it burns.
        /// Static with an explicit PhysicsScene (the seam GroundSnap.TryFindGroundY uses) so FireFieldBurnZoneTests can query real colliders in an
        /// edit-mode preview scene; BurnEveryoneInside calls it with Physics.defaultPhysicsScene.
        /// </summary>
        public static int OverlapBurnZone(PhysicsScene physics, Vector3 floorCentre, float radius, Collider[] results, int mask) =>
            physics.OverlapCapsule(floorCentre, floorCentre + Vector3.up * BurnHeightMetres, radius, results, mask,
                                    QueryTriggerInteraction.Ignore);

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
