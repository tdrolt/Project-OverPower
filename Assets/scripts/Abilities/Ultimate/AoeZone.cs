using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// The zone itself - Tudor's ultimate spec (2026-09-13): "self-cast radius lasting 6 seconds,
    /// dealing damage every second. Follows the caster." AoeZoneAbility only decides WHEN it goes
    /// down (at the caster's own feet, spending the ultimate meter); everything about what the zone
    /// does once it exists lives here, on the prefab.
    ///
    /// TICK ON EVERY CLIENT, DESTROY ON THE OWNER ONLY - exactly the FireField pattern
    /// (Weapons/FireField.cs), and for the identical reason: the plan's "tick on the owner only"
    /// would deal zero damage to everyone else, because ApplyDamage on a remote copy of THEIR
    /// PlayerHealth returns default() the instant it is called from a machine that is not theirs.
    /// Every client runs its own ZoneTickSchedule (Combat/ZoneTickSchedule.cs) against the SAME
    /// clock - seconds since this object was actually placed (Age, growing by real time elapsed on
    /// THIS client - the identical secondsSincePlaced pattern Mine.cs uses for its own arm delay) -
    /// so a late joiner's client, however many ticks already passed by the time it hears about this
    /// zone, pays out exactly the ticks it is owed and never one early or one extra. NO PER-TICK
    /// LOGGING (Task 1.11 plan): the AoE ability this replaces logged every tick and froze a 9-player
    /// playtest doing it.
    ///
    /// NOT USING NetworkedDeployable's OWN Lifetime Seconds. That field means "destroy on a fixed
    /// wall-clock timer, unrelated to anything else" (Portal leaves it at 0 for the same reason: its
    /// own logic decides when it goes away). Here, DURATION AND TICK COUNT ARE THE SAME NUMBER SEEN
    /// TWO WAYS - 6 seconds at 1 tick/second IS 6 ticks - so Duration Seconds and Tick Seconds below
    /// are the zone's only real duration; the schedule finishing is what ends its life, and this
    /// class calls RequestDestroy itself once it does. A second, always-0, "Lifetime Seconds" field
    /// sitting in the Inspector unused would be the exact "one number, two homes" mistake this
    /// project's own coding standards call out.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public sealed class AoeZone : NetworkedDeployable
    {
        [Header("Zone")]
        [SerializeField, Tooltip("How far the zone reaches from its centre, in metres. Claude's " +
                 "number: 5.")]
        private float radius = 5f;

        [SerializeField, Tooltip("Damage dealt to every enemy standing inside on each tick. Claude's " +
                 "number: 14 - 84 over the full 6-tick duration.")]
        private float damagePerTick = 14f;

        [SerializeField, Tooltip("How long the zone lasts, in seconds, before it disappears on its " +
                 "own. Tudor's spec: 6.")]
        private float durationSeconds = 6f;

        [SerializeField, Tooltip("Seconds between damage ticks - tick k lands at k * Tick Seconds " +
                 "after the zone was placed. Tudor's spec: 1.0, which with a 6s duration gives " +
                 "exactly 6 ticks.")]
        private float tickSeconds = 1f;

        [Header("Anchor")]
        [SerializeField, Tooltip("If checked, the zone follows the caster instead of staying where " +
                 "it was cast. Tudor's spec: the zone follows, so this defaults ON.")]
        private bool followsCaster = true;

        [SerializeField, Tooltip("Which layers this zone can hit. Default is where living players " +
                 "and practice dummies are; nothing on any other layer has an IDamageable to find, so " +
                 "widening this only costs performance.")]
        private LayerMask detectionMask = ~0;

        [SerializeField, Tooltip("The zone's visible mesh, if any. Resized sideways to match Radius " +
                 "the moment the zone is placed, so one prefab covers every size of zone. Left empty " +
                 "skips resizing anything.")]
        private Transform visual;

        // Not a design tunable: how many colliders one tick considers - matches FireField's own
        // MaxBurningColliders reasoning for a similarly small arena radius.
        private const int MaxOverlapColliders = 32;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Scratch set for one tick's overlap pass, so a target made of several colliders (a
        // player's own body) is only counted once - the same trick FireField.burning uses.
        private readonly HashSet<IDamageable> seenThisTick = new HashSet<IDamageable>();
        private readonly List<IDamageable> candidateBuffer = new List<IDamageable>();

        private ZoneTickSchedule schedule;
        private CasterFollower follower;

        // The real Time.time this zone's OnPlaced ran on THIS client, turning Age (a one-time
        // snapshot) into a number that keeps growing - the identical secondsSincePlaced pattern
        // Mine.cs documents on its own localPlacedRealTime field.
        private float localPlacedRealTime;

        private void OnValidate()
        {
            radius = Mathf.Max(0.1f, radius);
            damagePerTick = Mathf.Max(0f, damagePerTick);
            durationSeconds = Mathf.Max(0.01f, durationSeconds);
            tickSeconds = Mathf.Max(0.01f, tickSeconds);
        }

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            int totalTicks = Mathf.Max(1, Mathf.RoundToInt(durationSeconds / tickSeconds));
            schedule = new ZoneTickSchedule(tickSeconds, totalTicks);
            follower = new CasterFollower(followsCaster, OwnerActor);
            localPlacedRealTime = Time.time;

            if (visual != null)
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);
        }

        private void FixedUpdate()
        {
            follower?.Tick(transform);

            if (schedule == null)
                return;

            float secondsSincePlaced = (float)Age + (Time.time - localPlacedRealTime);
            int dueTicks = schedule.ConsumeDueTicks(secondsSincePlaced);
            for (int i = 0; i < dueTicks; i++)
                ApplyTick();

            // Every client stops ticking the instant its own schedule completes; only the owner
            // ever deletes the object - the same split FireField.Burn ends on.
            if (schedule.IsComplete && IsOwnerClient)
                RequestDestroy();
        }

        private void ApplyTick()
        {
            if (radius <= 0f)
                return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer,
                                                       detectionMask, QueryTriggerInteraction.Ignore);

            seenThisTick.Clear();
            candidateBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                if (candidate == null || !seenThisTick.Add(candidate))
                    continue;

                candidateBuffer.Add(candidate);
            }

            // Not a teammate, not the caster, not a structure, and locally authoritative - the same
            // rule Mine.cs's own trigger uses (MineTargeting.SelectTargets' own class comment).
            List<IDamageable> targets = MineTargeting.SelectTargets(candidateBuffer, OwnerActor, OwnerTeam);
            foreach (IDamageable target in targets)
            {
                target.ApplyDamage(new DamageInfo(damagePerTick, OwnerActor, OwnerTeam, -1,
                                                   DamageSource.Zone, false, transform.position));
            }
        }
    }
}
