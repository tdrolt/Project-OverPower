using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Weapons;
using Overpower.Match;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// A cast time (RaybeamWindup: a red hexagon shrinking at the gun), then ONE beam channelled from the gun along the caster's aim for Channel
    /// Seconds (RaybeamChannel): it damages the first enemy it touches and gives that enemy a vulnerability stack as it touches, then one per second.
    /// Walls and placed cover stop it, the short barriers never do (HitMasks). The meter is spent as the channel starts; a stun, a knockback or
    /// death ends it early, an overheat does not. RUNS ON EVERY CLIENT from the channel phase, re-reading the caster's gun and facing each frame
    /// (PlayerAim turns the body, PlayerNetSync replicates it), as the Flamethrower does; damage and status are victim-side (only the target's
    /// own client acts on ApplyDamage/ApplyStatus). Only the owner slows the caster and locks the gun and the other abilities (LocksCaster).
    /// </summary>
    public sealed class RaybeamAbility : AbilityModule
    {
        [Header("Beam")]
        [SerializeField, Tooltip("Damage per second the beam deals to the enemy it touches, before that enemy's vulnerability (which the beam " +
                 "itself raises), through the normal damage funnel: armour, shields and immunity apply as for any damage.")]
        private float damagePerSecond = 5f;

        [SerializeField, Tooltip("How long the beam lasts once the cast time is over, in seconds. A stun, a knockback or death ends it sooner.")]
        private float channelSeconds = 3f;

        [SerializeField, Tooltip("Extra damage taken per vulnerability stack, as a fraction: 0.2 means +20%. The total is capped by GameplayConfig's " +
                 "Vulnerability Cap (0.6 = three stacks).")]
        private float vulnerabilityPerStack = 0.2f;

        [SerializeField, Tooltip("The first vulnerability stack lands as the beam touches an enemy, then one more each time they have spent " +
                 "this many more seconds in it (1 = stacks at 0, 1 and 2 s, so a whole 3 s beam reaches +60%). The time adds up over one " +
                 "channel, so a target the beam slips off and finds again keeps what it had.")]
        private float secondsPerStack = 1f;

        [SerializeField, Tooltip("How long the vulnerability lasts, in seconds. Every new stack sets the time of all of them back to this.")]
        private float vulnerabilitySeconds = 4f;

        [SerializeField, Tooltip("How far the beam reaches from the gun, in metres, when no enemy, wall or cover stops it first.")]
        private float beamRange = 28.8f;

        [SerializeField, Tooltip("Width of the beam, in metres - BOTH what it hits (the SphereCast radius is half of it) AND what you see: the " +
                 "drawn beam is exactly this wide, and its white core is a share of this width. One number, one home.")]
        private float beamWidth = 0.6825f;

        [SerializeField, Tooltip("Which layers the beam can hit. Default is where living players and dummies sit; Building is the walls it " +
                 "stops at. Deployable cover is found as an IStructure on whatever collider it actually sits on, whichever layer that is.")]
        private LayerMask hitMask = ~0;

        [Header("Cast time and channel")]
        [SerializeField, Tooltip("Seconds from pressing the Raybeam to the beam starting (the cast time). Meanwhile a red hexagon shrinks at " +
                 "your gun for everyone who can see you, and you can still move and aim. Being stunned or dying during it cancels the Raybeam " +
                 "and your ultimate meter stays full; overheating does not. 0 starts the beam at the press.")]
        private float windupSeconds = 0.6f;

        [SerializeField, Tooltip("How fast the caster walks while the beam is on, as a share of their speed: 0.5 = half speed. They cannot " +
                 "shoot or use another ability meanwhile.")]
        private float channelMoveMultiplier = 0.5f;

        [Header("Visuals")]
        [SerializeField, Tooltip("The visible beam: Raybeam Beam (one red sheath, one white-hot core, one team-colour outline), kept for the " +
                 "whole channel and laid along the beam every frame. Any other prefab with a Line Renderer draws a plain line. Leave empty for " +
                 "an invisible beam.")]
        private GameObject beamVfx;

        [SerializeField, Tooltip("The scorch that flashes where the beam rests on a wall or a placed cover. Shown where your team can see that " +
                 "spot, like a gun's impact. Leave empty for no scorch.")]
        private GameObject scorchVfx;

        [SerializeField, Tooltip("How often the scorch flashes again while the beam rests on a wall, in seconds.")]
        private float scorchEverySeconds = 0.25f;

        // Not a tuning value, matching Hitscan's MaxContacts: nine players with a couple of colliders each plus the walls along a beam fit.
        private const int MaxContacts = 32;
        private const byte PhaseWindup = 0, PhaseChannel = 1, PhaseCancel = 2; // 0 is always the press (CastEvent.Phase)
        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxContacts];
        private readonly List<BeamContact> contactBuffer = new List<BeamContact>(MaxContacts);
        private readonly List<Vector3> normalBuffer = new List<Vector3>(MaxContacts); // alongside contactBuffer: where a scorch faces

        private StatusEffectSpec vulnerabilitySpec;

        private readonly Vector3[] lineFrom = new Vector3[1], lineTo = new Vector3[1];
        private bool windingUp; // owner only
        private float windupStartedAt;
        private bool channelling; // owner only: the lock and the slow
        private float channelStartedAt;
        private RaybeamChargeView charge;
        private bool chargeBuilt;

        // Every client: the beam being drawn and who is in it.
        private Coroutine beamRoutine;
        private GameObject beam;
        private int beamCasterActor, beamCasterTeam;
        private float nextScorchAt;
        private readonly Dictionary<IDamageable, float> secondsInBeam = new Dictionary<IDamageable, float>();
        private System.Func<Vector3, Vector3, bool> beamSeenAlong;

        protected override void OnValidate()
        {
            base.OnValidate();
            damagePerSecond = Mathf.Max(0f, damagePerSecond);
            channelSeconds = Mathf.Max(0f, channelSeconds);
            vulnerabilityPerStack = Mathf.Max(0f, vulnerabilityPerStack);
            secondsPerStack = Mathf.Max(0.01f, secondsPerStack);
            vulnerabilitySeconds = Mathf.Max(0f, vulnerabilitySeconds);
            beamRange = Mathf.Max(0.1f, beamRange);
            beamWidth = Mathf.Max(0.01f, beamWidth);
            windupSeconds = Mathf.Max(0f, windupSeconds);
            channelMoveMultiplier = Mathf.Clamp01(channelMoveMultiplier);
            scorchEverySeconds = Mathf.Max(0.02f, scorchEverySeconds);
            RebuildSpec();
        }

        private void Awake() => RebuildSpec();

        // The meter alone gates the press (IsReady): the runner must never refuse a press after TryBuildCast has started the wind-up.
        protected override bool SpendsChargeOnCast => false;

        private void OnDestroy()
        {
            if (charge != null)
                Destroy(charge.gameObject);
            HideBeam();
            if (channelling && Owner?.Motor != null)
                Owner.Motor.RemoveSpeedMultiplier(this);
        }

        /// <summary>Rebuilt once more here: Awake/OnValidate run before Bind assigns Definition (AbilityModule: "do not
        /// read it in Awake"), so the Awake spec always has abilityId -1. OnEquip is the first point Definition is safe.</summary>
        public override void OnEquip() => RebuildSpec();

        private void RebuildSpec()
        {
            vulnerabilitySpec = new StatusEffectSpec
            {
                kind = StatusKind.Vulnerability,
                duration = vulnerabilitySeconds,
                magnitude = vulnerabilityPerStack,
                // Definition is null here when OnValidate runs on the raw prefab asset (before Bind ever assigns it).
                abilityId = Definition != null ? Definition.Id : -1
            };
        }

        // ---- owner only ---------------------------------------------------------------------------

        /// <summary>Full meter only, like the other ultimates, and not while a cast or a beam is under way: the base class's 1 charge and
        /// 0 s cooldown recover instantly, so without this override that pool would never refuse a cast.</summary>
        public override bool IsReady => RaybeamWindup.IsReady(Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull, windingUp || channelling);

        public override bool LocksCaster => RaybeamChannel.LocksCaster(channelling);

        /// <summary>The press only starts the cast time: the payload tells every client where the gun was, for the hexagon. The meter is
        /// spent when the beam starts (EndWindup), so a cancelled cast leaves it full.</summary>
        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;
            if (!IsReady)
                return false;

            windingUp = true;
            windupStartedAt = Time.time;
            payload = new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection };
            return true;
        }

        public override void OwnerTick(float deltaTime, bool held, CastBlock actorBlock)
        {
            if (windingUp)
            {
                RaybeamWindupStep step = RaybeamWindup.Step(Time.time - windupStartedAt, windupSeconds, actorBlock);
                if (step != RaybeamWindupStep.Waiting)
                    EndWindup(step);
                return;
            }
            if (channelling)
                TickChannel(actorBlock);
        }

        private void TickChannel(CastBlock actorBlock)
        {
            bool knockedBack = Owner.Displacement is PlayerDisplacement displacement && displacement.IsKnockedBack;
            RaybeamChannelStep step = RaybeamChannel.Step(Time.time - channelStartedAt, channelSeconds, actorBlock, knockedBack);
            if (step == RaybeamChannelStep.Broken)
                EndChannel(sendCancel: true);
            else if (step == RaybeamChannelStep.Finished)
                EndChannel(sendCancel: false); // every client ends its own beam after Channel Seconds
        }

        /// <summary>A death, a stun or an unequip ends the cast time or the beam; a silence does not (RaybeamWindup.InterruptCancels). Died and
        /// Unequipped reach every client, Stunned only the owner, whose cancel phase ends the beam everywhere else.</summary>
        public override void Interrupt(InterruptReason reason)
        {
            if (!RaybeamWindup.InterruptCancels(reason))
                return;
            if (windingUp)
                EndWindup(RaybeamWindupStep.Cancel);
            if (channelling)
                EndChannel(sendCancel: true);
            HideBeam();
            if (charge != null)
                charge.End();
        }

        private void EndWindup(RaybeamWindupStep end)
        {
            windingUp = false;
            if (RaybeamWindup.SpendsUltimate(end) && Owner.UltimateCharge != null && Owner.UltimateCharge.Spend())
            {
                // Set before the send: with RpcTarget.All the caster's own ExecuteCast runs inside it.
                channelling = true;
                channelStartedAt = Time.time;
                Owner.Motor?.AddSpeedMultiplier(this, channelMoveMultiplier);
                CastContext ctx = CurrentContext();
                SendPhase(PhaseChannel, new CastPayload { Origin = ctx.Muzzle, Direction = ctx.AimDirection, Point = ctx.TargetPoint });
            }
            else
                SendPhase(PhaseCancel, default);
        }

        private void EndChannel(bool sendCancel)
        {
            channelling = false;
            Owner.Motor?.RemoveSpeedMultiplier(this);
            if (sendCancel)
                SendPhase(PhaseCancel, default);
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            switch (cast.Phase)
            {
                case PhaseWindup:
                    ShowCharge(cast);
                    return;
                case PhaseChannel:
                    if (charge != null)
                        charge.End();
                    StartBeam(cast);
                    return;
                case PhaseCancel:
                    if (charge != null)
                        charge.End();
                    HideBeam();
                    return;
            }
        }

        private void ShowCharge(in CastEvent cast)
        {
            if (!chargeBuilt)
            {
                chargeBuilt = true; // one try: a theme without its look warns once, not on every press
                UiTheme theme = Owner != null && Owner.Health != null ? Owner.Health.Theme : null;
                charge = RaybeamChargeView.TryCreate(theme, Definition.Id, Owner?.PhotonView, Owner?.Weapon, Owner?.Root.transform);
            }
            if (charge != null)
                charge.Begin(Time.time - cast.SecondsLate, windupSeconds, cast.CasterTeam);
        }

        private void StartBeam(in CastEvent cast)
        {
            HideBeam();
            beamCasterActor = cast.CasterActor;
            beamCasterTeam = cast.CasterTeam;
            secondsInBeam.Clear(); // the time in the beam counts per channel
            nextScorchAt = 0f;
            beamRoutine = StartCoroutine(BeamRoutine(cast.SecondsLate));
        }

        private IEnumerator BeamRoutine(float secondsLate)
        {
            float elapsed = Mathf.Max(0f, secondsLate);
            while (elapsed < channelSeconds)
            {
                BeamTick(Time.deltaTime);
                yield return null;
                elapsed += Time.deltaTime;
            }
            beamRoutine = null;
            HideBeam();
        }

        /// <summary>One frame of beam, on every client: from the caster's gun along their facing, to the first enemy, wall or cover. Damage and
        /// stacks are offered to whatever it touches; only that target's own client takes them.</summary>
        private void BeamTick(float deltaTime)
        {
            Transform body = Owner.Root.transform;
            Vector3 origin = Owner.Weapon != null ? Owner.Weapon.SafeMuzzlePosition : body.position;
            Vector3 aim = body.forward;
            aim.y = 0f;
            aim = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;

            int count = Physics.SphereCastNonAlloc(origin, beamWidth * 0.5f, aim, hitBuffer, beamRange, BuildMask(), QueryTriggerInteraction.Ignore);
            contactBuffer.Clear();
            normalBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null)
                    continue;
                IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
                contactBuffer.Add(new BeamContact(hit.distance, target, hit.point, target is IStructure));
                normalBuffer.Add(hit.normal);
            }

            BeamResult result = BeamResolver.Resolve(contactBuffer, beamRange, RaybeamChannel.TargetsHit, beamCasterActor, beamCasterTeam);
            if (result.Struck.Count > 0)
            {
                BeamContact struck = result.Struck[0];
                secondsInBeam.TryGetValue(struck.Target, out float before);
                float after = before + deltaTime;
                secondsInBeam[struck.Target] = after;
                struck.Target.ApplyDamage(new DamageInfo(RaybeamChannel.TickDamage(damagePerSecond, deltaTime), beamCasterActor, beamCasterTeam, -1,
                                                         DamageSource.Burn, false, struck.Point, vulnerabilitySpec.abilityId));
                int stacks = RaybeamChannel.StacksEarned(before, after, secondsPerStack);
                IStatusReceiver receiver = stacks > 0 ? (struck.Target as Component)?.GetComponentInParent<IStatusReceiver>() : null;
                for (int k = 0; k < stacks && receiver != null; k++)
                    receiver.ApplyStatus(vulnerabilitySpec, beamCasterActor);
            }

            Vector3 end = origin + aim * result.Length;
            if (Time.time >= nextScorchAt && RaybeamLook.EndsOnSurface(result))
            {
                RaybeamLook.ScorchSpot(contactBuffer, normalBuffer, result, end, aim, out Vector3 scorchAt, out Quaternion scorchFacing);
                if (RaybeamLook.ShowsScorch(true, TeamSight.ShotShownAt(beamCasterTeam, scorchAt)) && scorchVfx != null && VfxManager.Instance != null)
                {
                    VfxManager.Instance.PlayVFX(scorchVfx, scorchAt, scorchFacing);
                    nextScorchAt = Time.time + scorchEverySeconds;
                }
            }
            DrawBeam(origin, aim, end);
        }

        /// <summary>Hitscan.BuildMask's identical reasoning: the designer's layers minus the three
        /// invariants (Bullet, DeadPlayer, Barrier) that are never negotiable for any hit-detecting
        /// shot - Raybeam always passes a jersey barrier (GDD p.29), whatever its own hitMask ticks.</summary>
        private int BuildMask() => HitMasks.StripNonNegotiableLayers(hitMask);

        /// <summary>A local effect on each client, never a networked object: every client draws its own copy, hidden where its team cannot
        /// see the beam.</summary>
        private void DrawBeam(Vector3 origin, Vector3 aim, Vector3 end)
        {
            if (beamVfx == null)
                return;
            lineFrom[0] = origin;
            lineTo[0] = end;
            if (beam == null)
                beam = Instantiate(beamVfx, origin, Quaternion.identity);
            if (beamSeenAlong == null)
                beamSeenAlong = (a, b) => TeamSight.ShotShownAlong(beamCasterTeam, a, b); // made once, not every frame
            if (!RaybeamLook.BeamShown(lineFrom, lineTo, beamSeenAlong))
            {
                if (beam.activeSelf)
                    beam.SetActive(false);
                return;
            }
            if (!beam.activeSelf)
                beam.SetActive(true);
            ShowBeam(beam, origin, aim, lineFrom, lineTo, beamCasterTeam);
        }

        private void HideBeam()
        {
            if (beamRoutine != null)
            {
                StopCoroutine(beamRoutine);
                beamRoutine = null;
            }
            if (beam != null)
                Destroy(beam);
            beam = null;
        }

        /// <summary>Lays the drawn beam (an instantiated clone, never the prefab asset) over the hit lines, each exactly as wide as the
        /// line that hits (beamWidth is the one home). A beam prefab without the Raybeam's own view gets a plain centre line.</summary>
        internal void ShowBeam(GameObject beamObject, Vector3 origin, Vector3 aim, Vector3[] from, Vector3[] to, int casterTeam)
        {
            if (beamObject.TryGetComponent(out RaybeamBeamView view))
            {
                view.Draw(origin, aim, from, to, beamWidth, casterTeam);
                return;
            }

            LineRenderer line = beamObject.GetComponentInChildren<LineRenderer>();
            if (line == null)
                return;
            int middle = from.Length / 2;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = beamWidth;
            line.positionCount = 2;
            line.SetPosition(0, from[middle]);
            line.SetPosition(1, to[middle]);
        }

        public override string ShopStatsText() =>
            ShopNumberFormat.Lines($"{ShopNumberFormat.Compact(damagePerSecond)} damage per second for {ShopNumberFormat.Compact(channelSeconds)}s",
                                   $"+{ShopNumberFormat.Compact(vulnerabilityPerStack * 100f)}% damage taken per second in the beam, for {ShopNumberFormat.Compact(vulnerabilitySeconds)}s",
                                   $"Range {ShopNumberFormat.Compact(beamRange)}m");
    }
}
