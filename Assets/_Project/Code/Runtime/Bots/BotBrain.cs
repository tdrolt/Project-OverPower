using System.Collections.Generic;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
using Overpower.Weapons;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.AI;

namespace Overpower.Bots
{
    /// <summary>
    /// One bot, on its body, on the host. It plays through the same doors as a player: its team's sight (TeamSightQuery), the input
    /// router's bot seam (move, trigger, ability keys, Vent), the aim override, the shop's rules and the loadout. Every decision is a pure
    /// rule in Bots/Rules with BotConfig's numbers; this class only gathers what the rules need and presses the keys they choose.
    /// Objective first: each think it picks the zone it goes for, and an enemy turns it away from that only by BotFocusRules.
    /// Thinks every BotConfig.ThinkSeconds; aims, moves and holds keys every frame.
    /// </summary>
    public sealed class BotBrain : MonoBehaviour
    {
        private const float AimToleranceFloorDegrees = 3f;
        private const float BodyHalfWidth = 0.7f;
        private const float HalfFlameConeDegrees = 30f;
        private const float ArriveMetres = 0.6f;

        /// <summary>What a goal is for. The goal hold compares this and its key (zone, pack, target or teammate), never the point.</summary>
        private enum GoalKind { None, Home, Pack, Regroup, Fight, Memory, Objective }

        private BotDirector director;
        private BotConfig cfg;
        private BotConfig.Build build;
        private BuildPlan plan;
        private BotPlayer bot;

        private PhotonView view;
        private PlayerInputRouter router;
        private PlayerAim aim;
        private PlayerHealth health;
        private PlayerOverheat overheat;
        private AbilityRunner abilities;
        private WeaponFiring weapon;
        private UltimateCharge ultimate;
        private PlayerLoadout loadout;
        private GoldWallet wallet;
        private BotSeat seat;

        // Made in Awake: Unity refuses a NavMeshPath built during construction, and the refusal would leave every field after it unset.
        private NavMeshPath path;
        private Vector3[] corners = new Vector3[0];
        private bool hasPath;
        private Vector3? goal;
        private Vector3 plannedFor;
        private float plannedAt = -99f;
        private Vector3 progressFrom;
        private float progressSince;
        private float sidestepUntil;
        private Vector3 sidestep;

        private readonly List<(int actor, float distance)> visible = new List<(int, float)>();
        private readonly List<(int actor, float distance)> candidates = new List<(int, float)>();
        private readonly List<(int actor, Vector3 position)> mates = new List<(int, Vector3)>();
        private readonly Dictionary<int, PhotonView> enemyBodies = new Dictionary<int, PhotonView>();
        private readonly Dictionary<int, int> enemyTeams = new Dictionary<int, int>();
        private readonly Dictionary<int, (Vector3 position, float time)> lastSeen = new Dictionary<int, (Vector3, float)>();
        private readonly Dictionary<int, float> hitAt = new Dictionary<int, float>();
        private readonly List<HealthPackManager.PackStatus> packs = new List<HealthPackManager.PackStatus>();
        private readonly List<ConquestZone> conquestZones = new List<ConquestZone>();
        private readonly List<DominionZone> dominionZones = new List<DominionZone>();
        private readonly List<ShopOffer> offers = new List<ShopOffer>();

        private int target = -1;
        private bool helping;
        private bool engaged;
        private float targetSeenSince;
        private float targetAimedSince;
        private bool aimingAtTarget;
        private Vector3 targetVelocity;
        private Vector3 targetLastPosition;
        private float lastEnemySeenAt = -99f;
        private BotStance healthStance = BotStance.Fight;
        private BotStance stance = BotStance.Fight;
        private RangeMove rangeMove = RangeMove.Hold;
        private int strafeSide = 1;
        private bool strafeStill;
        private float strafeSince;
        private float strafeSwitchAfter = 2f;
        private Vector3 aimDirection = Vector3.forward;
        private Vector3 aimOffset;
        private float aimOffsetAt;

        private int objectiveZone = -1;
        private Vector3 objectiveCentre;
        private Vector3? objectivePoint;
        private bool objectiveUrgent;
        private bool pushingCapital;
        private string objectiveDoing = "";
        private string goalDoing = "";
        private GoalKind heldKind = GoalKind.None;
        private int heldKey;
        private float heldSince;

        private float nextPauseRollAt;
        private float pauseUntil;
        private float scoutUntil;
        private int scoutedZone = -1;

        private bool scanPrimed;
        private int scanStartSeen;
        private float scanRadiusSeen;

        private bool wasSilenced;
        private bool ventWillHit;
        private bool ventPressed;

        private bool primaryHeld;
        private bool attachmentHeld, ultimateHeld, mobilityHeld;
        private bool attachmentHold, mobilityHold;
        private PendingPress pending;
        private float nextThinkAt;
        private float nextShopAt;
        private int wantedPack = -1;

        private enum PressSlot { None, Attachment, Ultimate, Mobility }

        private struct PendingPress
        {
            public PressSlot Slot;
            public int Stage;
            public Vector3? AimAt;
        }

        /// <summary>What this bot has done so far, for the checks and Tudor's tuning: key presses per ability slot, trigger pulls, Vent
        /// presses, packs taken, purchases, times it backed off, fell back to regroup, and stopped to scout ahead.</summary>
        public struct Tally
        {
            public int Attachment, Movement, Ultimate, Pulls, Vents, Packs, Purchases, Retreats, Regroups, Scouts;
            public override string ToString() =>
                $"attach {Attachment} move {Movement} ult {Ultimate} pulls {Pulls} vents {Vents} packs {Packs} buys {Purchases} retreats {Retreats} regroups {Regroups} scouts {Scouts}";
        }

        private Tally tally;
        public Tally Done => tally;

        public BotPlayer Bot => bot;
        public int Target => target;
        public BotStance Stance => stance;
        public Vector3? Goal => goal;
        public int ObjectiveZone => objectiveZone;
        public bool Engaged => engaged;
        public string Doing { get; private set; } = "";

        public void Init(BotDirector botDirector, BotPlayer botPlayer)
        {
            director = botDirector;
            cfg = botDirector.Config;
            bot = botPlayer;
            build = cfg != null ? cfg.BuildAt(bot.Build) : null;
            plan = PlanOf(build);
            strafeSwitchAfter = NextStrafeTime();
            if (cfg != null) nextPauseRollAt = Time.time + Random.Range(cfg.IdlePauseEveryMinSeconds, cfg.IdlePauseEveryMaxSeconds);
        }

        private void Awake()
        {
            path = new NavMeshPath();
            view = GetComponent<PhotonView>();
            router = GetComponent<PlayerInputRouter>();
            aim = GetComponent<PlayerAim>();
            health = GetComponent<PlayerHealth>();
            overheat = GetComponent<PlayerOverheat>();
            abilities = GetComponent<AbilityRunner>();
            weapon = GetComponent<WeaponFiring>();
            ultimate = GetComponent<UltimateCharge>();
            loadout = GetComponent<PlayerLoadout>();
            wallet = GetComponent<GoldWallet>();
            seat = GetComponent<BotSeat>();
        }

        private void OnEnable()
        {
            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= OnDamaged;
        }

        // Raised on the body's owner (the host, for a bot): who hit it and when, so it fights back whatever it was doing.
        private void OnDamaged(DamageResult result, DamageInfo info)
        {
            if (info.SourceActorNumber > 0 && view != null && info.SourceActorNumber != view.OwnerActorNr) hitAt[info.SourceActorNumber] = Time.time;
        }

        private static BuildPlan PlanOf(BotConfig.Build b)
        {
            if (b == null) return default;
            WeaponDefinition family = b.FamilyWeapon;
            return new BuildPlan
            {
                BaselineId = family != null && family.Parent != null ? family.Parent.Id : 1,
                FamilyId = family != null ? family.Id : 0,
                UpgradeId = b.UpgradeWeapon != null ? b.UpgradeWeapon.Id : 0,
                AttachmentId = b.Attachment != null ? b.Attachment.Id : 0,
                MovementId = b.Movement != null ? b.Movement.Id : 0,
                UltimateId = b.Ultimate != null ? b.Ultimate.Id : 0,
                Absorb = b.AbsorbUpgrades,
                Recharge = b.RechargeUpgrades,
            };
        }

        private int Team => Teams.TryGetTeam(view.Owner, out int t) ? t : seat != null ? seat.Team : -1;
        private bool Alive => health != null && health.IsAlive;
        private float Health01 => health != null && health.MaxHealth > 0f ? health.Health / health.MaxHealth : 1f;
        private Vector3 Position => transform.position;

        private void Update()
        {
            if (view == null || !view.IsMine || cfg == null)
            {
                // The host left and another client took over this body until it is destroyed: nothing of this bot runs there.
                enabled = false;
                return;
            }

            if (Time.time >= nextThinkAt)
            {
                nextThinkAt = Time.time + cfg.ThinkSeconds;
                Think();
            }
            if (Time.time >= nextShopAt)
            {
                nextShopAt = Time.time + 0.5f;
                Shop();
            }
            Act();
        }

        // ---- deciding ----

        private void Think()
        {
            if (!Alive)
            {
                ReadScan();
                target = -1;
                engaged = false;
                goal = null;
                hasPath = false;
                heldKind = GoalKind.None;
                pauseUntil = scoutUntil = 0f;
                Doing = "waiting to respawn";
                return;
            }

            See();
            ReadScan();
            ChooseObjective();
            int before = target;
            PickTarget();
            bool cornered = NearestVisibleEnemyMetres() <= cfg.CorneredMetres;
            healthStance = BotHealthRules.Stance(healthStance, Health01, cfg.RetreatHealth, cfg.ReturnHealth, cornered);
            bool outnumbered = BotTeamRules.Outnumbered(EnemiesKnownNear(Position, cfg.OutnumberedRadiusMetres, cfg.TargetLostSeconds),
                TeammatesNear(Position, cfg.OutnumberedRadiusMetres), cfg.OutnumberedMargin);
            BotStance was = stance;
            stance = BotTeamRules.Stance(healthStance, outnumbered, cornered);
            if (stance == BotStance.Retreat && was != BotStance.Retreat) tally.Retreats++;
            if (stance == BotStance.Regroup && was != BotStance.Regroup) tally.Regroups++;
            ChooseGoal((before >= 0) != (target >= 0) || stance != was);
            ScoutAhead();
            PauseNowAndThen();
            Doing = Time.time < scoutUntil ? "scouting before " + objectiveDoing : Time.time < pauseUntil ? "standing still a moment" : goalDoing;
            ChooseAbilities();
        }

        private void See()
        {
            visible.Clear();
            enemyBodies.Clear();
            enemyTeams.Clear();
            mates.Clear();
            int team = Team;
            if (team < 0 || PhotonNetwork.CurrentRoom == null) return;
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player.ActorNumber == view.OwnerActorNr || Teams.IsSpectator(player) || !Teams.TryGetTeam(player, out int theirs)) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                if (body == null) continue;
                PlayerHealth theirHealth = body.GetComponent<PlayerHealth>();
                bool alive = theirHealth != null && theirHealth.IsAlive;
                if (theirs == team)
                {
                    if (alive) mates.Add((player.ActorNumber, body.transform.position));
                    continue;
                }
                if (!BotTargetRules.Targetable(alive, RespawnShield.IsUpFor(player))) continue;
                enemyBodies[player.ActorNumber] = body;
                enemyTeams[player.ActorNumber] = theirs;
                if (!TeamSightQuery.CanTeamSeeBody(team, body)) continue;
                lastSeen[player.ActorNumber] = (body.transform.position, Time.time);
                lastEnemySeenAt = Time.time;
                visible.Add((player.ActorNumber, Flat(body.transform.position, Position)));
            }
        }

        /// <summary>The centre scan's wave of the bot's team shows it every enemy the front sweeps, as for a player's red dot: the bot
        /// remembers where. The band swept is tracked here because the bot looks once a think, not every frame.</summary>
        private void ReadScan()
        {
            CentreScan scan = CentreScan.Instance;
            if (scan == null)
            {
                scanPrimed = false;
                return;
            }
            ScanFrame frame = scan.Frame;
            float sweptFrom = BotTeamRules.ScanSweptFrom(scanStartSeen, frame.ScanStartMs, scanRadiusSeen);
            bool primed = scanPrimed;
            scanPrimed = true;
            scanStartSeen = frame.ScanStartMs;
            scanRadiusSeen = frame.Radius;
            // The first look only notes where the wave is: a wave already half way out would otherwise show everyone inside it.
            if (!primed || !Alive || !BotTeamRules.ScanShowsUs(frame.Active && frame.Travelling, scan.HolderTeam, Team)) return;
            Vector3 centre = scan.CentrePosition;
            foreach (var pair in enemyBodies)
            {
                if (pair.Value == null) continue;
                Vector3 at = pair.Value.transform.position;
                if (CentreScanRules.FrontSwept(sweptFrom, frame.Radius, Flat(at, centre))) lastSeen[pair.Key] = (at, Time.time);
            }
        }

        private void PickTarget()
        {
            candidates.Clear();
            float engage = EngageMetres();
            foreach (var (actor, distance) in visible)
                if (distance <= engage && enemyBodies.TryGetValue(actor, out PhotonView body) && !LeftAlone(actor, body.transform.position))
                    candidates.Add((actor, distance));
            if (target >= 0 && !Sees(target) && enemyBodies.ContainsKey(target) && lastSeen.TryGetValue(target, out var seen)
                && Time.time - seen.time <= cfg.TargetLostSeconds && !LeftAlone(target, seen.position))
                candidates.Add((target, Flat(seen.position, Position)));
            int picked = BotTargetRules.Pick(candidates, target, cfg.TargetSwitchMetres);
            helping = false;
            if (picked < 0)
            {
                picked = TeammatesFightTarget();
                helping = picked >= 0;
            }
            if (picked != target)
            {
                target = picked;
                targetSeenSince = Time.time;
                targetAimedSince = Time.time;
                aimingAtTarget = false;
                rangeMove = RangeMove.Hold;
                if (target >= 0 && enemyBodies.TryGetValue(target, out PhotonView body)) targetLastPosition = body.transform.position;
                targetVelocity = Vector3.zero;
            }
        }

        /// <summary>The closest seen enemy that is in a fight with a teammate near the bot, for a bot with no target of its own; -1 when none.</summary>
        private int TeammatesFightTarget()
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            foreach (var (actor, distance) in visible)
            {
                if (distance >= bestDistance || !enemyBodies.TryGetValue(actor, out PhotonView body)) continue;
                Vector3 at = body.transform.position;
                if (LeftAlone(actor, at) || !TeammateFights(at)) continue;
                best = actor;
                bestDistance = distance;
            }
            return best;
        }

        private bool TeammateFights(Vector3 enemy)
        {
            foreach (var (_, at) in mates)
                if (BotTeamRules.FightingNearTeammate(Flat(at, Position), cfg.HelpTeammateMetres, Flat(enemy, at), cfg.TeammateFightMetres)) return true;
            return false;
        }

        private float EngageMetres()
        {
            float range = WeaponRange();
            if (range <= 0f) range = 15f;
            float preferred = build != null ? build.FightRangeMax : range;
            return Mathf.Max(range, preferred) + 6f;
        }

        private float WeaponRange() =>
            weapon != null && weapon.Weapon != null ? weapon.Weapon.MaxRange * Mathf.Max(0.01f, weapon.CurrentRangeMultiplier) : 0f;

        private bool Sees(int actor)
        {
            foreach (var (seen, _) in visible)
                if (seen == actor) return true;
            return false;
        }

        /// <summary>Where the target is as far as the team knows: where it stands while seen, else where it was last seen.</summary>
        private bool TryTargetPosition(out Vector3 at)
        {
            at = default;
            if (target < 0 || !enemyBodies.TryGetValue(target, out PhotonView body) || body == null) return false;
            at = body.transform.position;
            if (!Sees(target) && lastSeen.TryGetValue(target, out var seen)) at = seen.position;
            return true;
        }

        private bool HitMeLately(int actor) => hitAt.TryGetValue(actor, out float at) && Time.time - at <= cfg.HitMemorySeconds;

        // ---- the objective ----

        private void ChooseObjective()
        {
            objectiveZone = -1;
            objectivePoint = null;
            objectiveUrgent = false;
            pushingCapital = false;
            objectiveDoing = "holding";
            if (!MatchIsOn()) return;
            if (DominionMode.IsActive()) DominionGoal();
            else ConquestGoal();
            BuildingManager zones = BuildingManager.Instance;
            if (objectiveZone >= 0 && zones != null) zones.TryGetZoneCentre(objectiveZone, out objectiveCentre);
        }

        private bool MatchIsOn()
        {
            if (DominionMode.IsActive())
            {
                DominionStage stage = DominionDirector.Instance != null ? DominionDirector.Instance.Stage : DominionStage.None;
                return stage == DominionStage.Round || stage == DominionStage.Overtime || stage == DominionStage.SuddenDeath;
            }
            return MatchDirector.Instance != null && MatchDirector.Instance.IsLive;
        }

        private void ConquestGoal()
        {
            BuildingManager zones = BuildingManager.Instance;
            MatchDirector match = MatchDirector.Instance;
            int team = Team;
            if (zones == null || zones.Current == null || team < 0) { objectiveDoing = "no map yet"; return; }

            Vector3 from = Position;
            Vector3? human = NearestHumanTeammate(cfg.HumanTeammateMetres);
            if (human.HasValue) from = human.Value;

            int now = PhotonNetwork.ServerTimestamp;
            ZonePresenceTracker presence = ZonePresenceTracker.Instance;
            conquestZones.Clear();
            for (int z = 0; z < zones.ZoneCount; z++)
            {
                if (match != null && match.IsOutOfPlay(z)) continue;
                if (!zones.TryGetZoneCentre(z, out Vector3 centre)) continue;
                int owner = zones.Current.OwnerOf(z);
                int capitalTeam = zones.Map != null ? zones.Map.CapitalTeamOf(z) : TerritoryMap.Neutral;
                bool ownCapital = capitalTeam == team;
                bool enemyCapital = capitalTeam >= 0 && capitalTeam != team && owner != team;
                bool underAttack = presence != null && presence.IsUnderAttack(z);
                bool bounty = false;
                if (zones.TryGetZoneBounty(z, out int tierBounty, out float holdSeconds, out _))
                    bounty = BountyLabelRules.LabelAmount(owner, zones.Current.HeldSinceMs(z), zones.Current.LastOwnerOf(z), zones.Current.LastHeldMs(z), now,
                        (int)(holdSeconds * 1000f), tierBounty) > 0 && owner != team;
                conquestZones.Add(new ConquestZone
                {
                    Id = z,
                    Centre = centre,
                    Tier = zones.TierOf(z),
                    Bounty = bounty,
                    MayCapture = zones.MayTeamCapture(team, z),
                    IsOwnCapital = ownCapital,
                    OwnCapitalThreatened = ownCapital && underAttack,
                    IsEnemyCapital = enemyCapital,
                    TeammatesNear = enemyCapital ? TeammatesNear(centre, cfg.PushCapitalMetres) : 0,
                    IsCentre = zones.BaseTierOf(z) == DominionRules.CentreTier,
                    OursUnderAttack = !ownCapital && owner == team && underAttack,
                    EnemiesNear = EnemiesKnownNear(centre, cfg.ObjectiveGuardMetres, cfg.ZoneIntelSeconds),
                });
            }

            bool centreUrgent = match != null && match.CutTeam >= 0 && CentreScan.Instance != null
                && CentreScan.Instance.CountdownSeconds >= 0 && CentreScan.Instance.CountdownSeconds <= cfg.CentreBeforeScanSeconds;
            var settings = new ConquestGoalRules.Settings
            {
                TierThreeBonusMetres = cfg.TierThreeBonusMetres,
                BountyBonusMetres = cfg.BountyBonusMetres,
                PushCapitalTeammates = cfg.PushCapitalTeammates,
                EnemyNearPenaltyMetres = cfg.EnemyNearZonePenaltyMetres,
                DefendZoneMetres = cfg.DefendZoneMetres,
            };
            int zone = ConquestGoalRules.Choose(conquestZones, from, centreUrgent, settings);
            if (zone < 0)
            {
                objectivePoint = human;
                objectiveDoing = human.HasValue ? "staying with a teammate" : "holding";
                return;
            }
            objectiveZone = zone;
            objectivePoint = SpotIn(zone);
            foreach (ConquestZone z in conquestZones)
            {
                if (z.Id != zone) continue;
                objectiveUrgent = z.OwnCapitalThreatened || z.OursUnderAttack;
                pushingCapital = z.IsEnemyCapital;
            }
            objectiveDoing = (objectiveUrgent ? "defending zone " : pushingCapital ? "pushing the capital, zone " : "taking zone ") + zone;
        }

        private void DominionGoal()
        {
            BuildingManager zones = BuildingManager.Instance;
            DominionDirector director = DominionDirector.Instance;
            int team = Team;
            if (zones == null || zones.Current == null || team < 0) { objectiveDoing = "no map yet"; return; }

            int centreZone = director != null ? director.CentreZone : -1;
            int[] table = this.director != null && this.director.Room != null && this.director.Room.Dominion != null ? this.director.Room.Dominion.PointsPerZonePerSecond : null;
            int now = PhotonNetwork.ServerTimestamp;
            dominionZones.Clear();
            for (int z = 0; z < zones.ZoneCount; z++)
            {
                if (MatchDirector.Instance != null && MatchDirector.Instance.IsOutOfPlay(z)) continue;
                if (!zones.TryGetZoneCentre(z, out Vector3 centre)) continue;
                int owner = zones.Current.OwnerOf(z);
                CaptureProgress progress = zones.CaptureProgressOf(z);
                bool losing = owner == team && progress.Team >= 0 && progress.Team != team && progress.RatePerSecond01 > 0f && progress.Evaluate(now) > 0f;
                dominionZones.Add(new DominionZone
                {
                    Id = z,
                    Centre = centre,
                    PointsPerSecond = PointsPerSecond(z, team, zones, table),
                    Ours = owner == team,
                    BeingTakenFromUs = losing,
                    IsCentre = z == centreZone,
                    Capturable = zones.IsCapturableZone(z),
                    EnemiesNear = EnemiesKnownNear(centre, cfg.ObjectiveGuardMetres, cfg.ZoneIntelSeconds),
                });
            }
            bool centreSoon = director != null && centreZone >= 0 && director.SecondsToCentrePayout > 0f
                && director.SecondsToCentrePayout <= cfg.CentreBeforePayoutSeconds;
            int zone = DominionGoalRules.Choose(dominionZones, Position, centreSoon, cfg.EnemyNearZonePenaltyMetres);
            if (zone < 0) return;
            objectiveZone = zone;
            objectivePoint = SpotIn(zone);
            foreach (DominionZone z in dominionZones)
                if (z.Id == zone) objectiveUrgent = z.BeingTakenFromUs;
            objectiveDoing = zone == centreZone ? "going for the centre" : (objectiveUrgent ? "defending zone " : "taking zone ") + zone;
        }

        private readonly int[] oneOwner = new int[1], oneTier = new int[1];
        private readonly bool[] oneSpawn = new bool[1];

        /// <summary>What zone z pays per second to its holder, by the game's own points rule (capitals, spawns and the centre pay nothing per tick).</summary>
        private float PointsPerSecond(int z, int team, BuildingManager zones, int[] table)
        {
            if (table == null) return 0f;
            oneOwner[0] = team;
            oneTier[0] = zones.BaseTierOf(z);
            oneSpawn[0] = zones.IsSpawnZone(z);
            return DominionRules.PointsThisTick(oneOwner, oneTier, oneSpawn, team, table);
        }

        /// <summary>A spot inside the zone's capture circle, different for each bot so a team does not stack on one point.</summary>
        private Vector3 SpotIn(int zone)
        {
            BuildingManager zones = BuildingManager.Instance;
            zones.TryGetZoneCentre(zone, out Vector3 centre);
            float radius = zones.CaptureRadiusOf(zone);
            float angle = (view.OwnerActorNr * 137.5f) * Mathf.Deg2Rad;
            float r = Mathf.Min(2f, radius * 0.4f);
            return centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
        }

        // ---- the goal ----

        private void ChooseGoal(bool situationChanged)
        {
            wantedPack = -1;
            engaged = false;
            if (!MatchIsOn())
            {
                goal = null;
                goalDoing = "waiting for the match";
                heldKind = GoalKind.None;
                return;
            }
            if (SuddenDeathGoal())
            {
                heldKind = GoalKind.None;
                return;
            }

            bool hasTarget = TryTargetPosition(out Vector3 targetAt);
            if (hasTarget && stance == BotStance.Fight)
                engaged = BotFocusRules.ShouldEngage(objectiveZone >= 0, Flat(targetAt, Position), EngageLimit(),
                    objectiveZone >= 0 ? Flat(targetAt, objectiveCentre) : float.MaxValue, cfg.ObjectiveGuardMetres,
                    HitMeLately(target), helping || TeammateFights(targetAt));

            GoalKind kind;
            int key = 0;
            Vector3? point;
            string doing;
            if (stance == BotStance.Retreat)
            {
                if (TryPack(false, cfg.HealthPackMetres, true, out int pack, out Vector3 packAt))
                {
                    kind = GoalKind.Pack;
                    key = pack;
                    point = packAt;
                    doing = "backing off to a health pack";
                }
                else
                {
                    kind = GoalKind.Home;
                    point = HomePoint();
                    doing = "backing off home";
                }
            }
            else if (stance == BotStance.Regroup)
            {
                kind = GoalKind.Regroup;
                point = RegroupPoint(out key);
                doing = "falling back to regroup";
            }
            else if (engaged)
            {
                kind = GoalKind.Fight;
                key = target;
                Vector3 fight = FightPoint(targetAt);
                point = OffLimits(fight) ? (Vector3?)null : fight;
                doing = "fighting " + NameOf(target);
            }
            else if (RecentMemory(out Vector3 lastPlace))
            {
                kind = GoalKind.Memory;
                point = lastPlace;
                doing = "looking where an enemy was";
            }
            else if (BotPackRules.WantsHealthPack(Health01, cfg.HealthPackHealth, EnemiesWithin(EngageMetres()) > 0, 0f, cfg.HealthPackMetres)
                && TryPack(false, cfg.HealthPackMetres, false, out int healthPack, out Vector3 healthAt))
            {
                kind = GoalKind.Pack;
                key = healthPack;
                point = healthAt;
                doing = "taking a health pack";
            }
            else if (ultimate != null && !ultimate.IsFull && TryPack(true, cfg.UltimatePackMetres, false, out int ultimatePack, out Vector3 ultimateAt))
            {
                kind = GoalKind.Pack;
                key = ultimatePack;
                point = ultimateAt;
                doing = "taking an ultimate pack";
            }
            else
            {
                kind = GoalKind.Objective;
                key = objectiveZone;
                point = objectivePoint;
                doing = objectiveDoing;
            }

            // A fight's point moves with the strafe and a new target is its own fight: only a change of kind (or of zone, pack, teammate) is held.
            bool differs = kind != heldKind || (key != heldKey && kind != GoalKind.Fight);
            if (differs && heldKind != GoalKind.None
                && BotPaceRules.KeepGoal(Time.time - heldSince, cfg.GoalHoldSeconds, situationChanged, objectiveUrgent))
            {
                if (heldKind == GoalKind.Pack) wantedPack = heldKey;
                return;
            }
            if (differs)
            {
                heldKind = kind;
                heldSince = Time.time;
            }
            heldKey = key;
            goal = point;
            goalDoing = doing;
            if (kind == GoalKind.Pack) wantedPack = key;
        }

        private float EngageLimit() => (build != null ? build.FightRangeMax : 12f) + cfg.EngageExtraMetres;

        private bool SuddenDeathGoal()
        {
            SuddenDeathZone circle = SuddenDeathZone.Instance;
            if (circle == null || !circle.IsSuddenDeath) return false;
            Vector2 here = new Vector2(Position.x, Position.z);
            Vector3 middle = new Vector3(circle.Centre.x, Position.y, circle.Centre.y);
            if (!DominionGoalRules.InsideCircle(here, circle.Centre, circle.CurrentRadius, cfg.CircleMarginMetres))
            {
                goal = middle;
                goalDoing = "getting back inside the circle";
                return true;
            }
            if (TryTargetPosition(out Vector3 targetAt))
            {
                engaged = true;
                Vector3 fight = FightPoint(targetAt);
                Vector2 fight2 = new Vector2(fight.x, fight.z);
                goal = DominionGoalRules.InsideCircle(fight2, circle.Centre, circle.CurrentRadius, cfg.CircleMarginMetres) ? fight : middle;
                goalDoing = "sudden death: fighting " + NameOf(target);
                return true;
            }
            goal = middle;
            goalDoing = "sudden death: holding the circle";
            return true;
        }

        /// <summary>A spot at the build's range from the target, on the bot's side of it, shifted to the strafe side (or not, when this
        /// strafe it stands still).</summary>
        private Vector3 FightPoint(Vector3 enemy)
        {
            float distance = Flat(enemy, Position);
            float min = build != null ? build.FightRangeMin : 6f;
            float max = build != null ? build.FightRangeMax : 12f;
            float band = Mathf.Min(cfg.RangeDeadBandMetres, (max - min) * 0.5f);
            Vector3 away = Position - enemy;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            rangeMove = BotRangeRules.Decide(distance, min, max, rangeMove, band);
            float keep;
            switch (rangeMove)
            {
                case RangeMove.BackOff: keep = min + band; break;
                case RangeMove.Approach: keep = max - band; break;
                default: keep = distance; break;
            }
            if (Time.time - strafeSince > strafeSwitchAfter)
            {
                strafeSide = BotRangeRules.StrafeSide(strafeSide, Time.time - strafeSince, strafeSwitchAfter);
                strafeStill = BotPaceRules.Pause(true, Random.value, cfg.FightStandStillChance);
                strafeSince = Time.time;
                strafeSwitchAfter = NextStrafeTime();
            }
            Vector3 side = Vector3.Cross(Vector3.up, away) * (strafeStill ? 0 : strafeSide);
            return enemy + away * keep + side * 2.5f;
        }

        private float NextStrafeTime() => cfg != null ? Random.Range(cfg.StrafeSwitchMinSeconds, cfg.StrafeSwitchMaxSeconds) : 2f;

        /// <summary>The nearest spot where an enemy was seen lately (not seen now), worth a look: near the zone it goes for and outside every
        /// enemy spawn.</summary>
        private bool RecentMemory(out Vector3 place)
        {
            place = default;
            float best = float.MaxValue;
            float reach = EngageMetres() * 1.5f;
            foreach (var pair in lastSeen)
            {
                if (Time.time - pair.Value.time > cfg.MemorySeconds || !enemyBodies.ContainsKey(pair.Key) || Sees(pair.Key)) continue;
                Vector3 spot = pair.Value.position;
                float d = Flat(spot, Position);
                if (d > reach || d >= best || OffLimits(spot)) continue;
                if (!BotFocusRules.ChaseMemory(objectiveZone >= 0, objectiveZone >= 0 ? Flat(spot, objectiveCentre) : 0f, cfg.ObjectiveGuardMetres)) continue;
                best = d;
                place = spot;
            }
            return best < float.MaxValue && best > 1.5f;
        }

        private bool TryPack(bool ultimate, float maxMetres, bool anyDistance, out int packId, out Vector3 at)
        {
            packId = -1;
            at = default;
            HealthPackManager manager = HealthPackManager.Instance;
            if (manager == null) return false;
            manager.ListPacks(packs);
            float best = float.MaxValue;
            HealthPackManager.PackStatus chosen = default;
            foreach (HealthPackManager.PackStatus pack in packs)
            {
                if (pack.IsUltimate != ultimate || !pack.Available) continue;
                float d = Flat(pack.Position, Position);
                bool wanted = ultimate
                    ? BotPackRules.WantsUltimatePack(this.ultimate == null || this.ultimate.IsFull, d, maxMetres)
                    : anyDistance ? d <= maxMetres : BotPackRules.WantsHealthPack(Health01, cfg.HealthPackHealth, EnemiesWithin(EngageMetres()) > 0, d, maxMetres);
                if (!wanted || d >= best) continue;
                best = d;
                chosen = pack;
            }
            if (best == float.MaxValue) return false;
            packId = chosen.Id;
            at = chosen.Position;
            return true;
        }

        private Vector3 HomePoint()
        {
            RoomManager room = director != null ? director.Room : null;
            Transform spawn = room != null && Team >= 0 ? room.SpawnPointFor(Team, Team, view.OwnerActorNr, false) : null;
            return spawn != null ? spawn.position : Position;
        }

        /// <summary>Beside the nearest living teammate (a couple of bodies short of it, on the bot's side), or home with nobody left.</summary>
        private Vector3 RegroupPoint(out int mate)
        {
            mate = -1;
            float best = float.MaxValue;
            Vector3 at = default;
            foreach (var (actor, position) in mates)
            {
                float d = Flat(position, Position);
                if (d >= best) continue;
                best = d;
                at = position;
                mate = actor;
            }
            if (mate < 0) return HomePoint();
            Vector3 back = Position - at;
            back.y = 0f;
            return back.sqrMagnitude > 0.01f ? at + back.normalized * Mathf.Min(best, BodyHalfWidth * 4f) : at;
        }

        // ---- spawns, teammates and what the team knows ----

        /// <summary>Whether an enemy stands in its own spawn, where the bot leaves it alone.</summary>
        private bool LeftAlone(int actor, Vector3 at) => enemyTeams.TryGetValue(actor, out int theirs) && InSpawnOffLimits(theirs, at);

        /// <summary>Whether a point is inside any enemy team's spawn the bot keeps out of.</summary>
        private bool OffLimits(Vector3 point)
        {
            int team = Team;
            for (int t = 0; t < ZoneThreat.MaxTeams; t++)
                if (t != team && InSpawnOffLimits(t, point)) return true;
            return false;
        }

        /// <summary>Whether a point is inside enemyTeam's spawn (Dominion: its spawn area; Conquest: the capital it respawns at) and the bot
        /// is not pushing that capital.</summary>
        private bool InSpawnOffLimits(int enemyTeam, Vector3 point)
        {
            int spawnZone = TerritoryMap.Neutral;
            bool inside;
            if (DominionMode.IsActive())
            {
                inside = SpawnHealArea.InOwnSpawn(enemyTeam, point);
            }
            else
            {
                BuildingManager zones = BuildingManager.Instance;
                spawnZone = MatchDirector.Instance != null ? MatchDirector.Instance.RespawnCapitalOf(enemyTeam) : TerritoryMap.Neutral;
                inside = spawnZone != TerritoryMap.Neutral && zones != null && zones.TryGetZoneCentre(spawnZone, out Vector3 centre)
                    && Flat(point, centre) <= zones.CaptureRadiusOf(spawnZone);
            }
            return BotSpawnRules.OffLimits(inside, pushingCapital && spawnZone == objectiveZone);
        }

        /// <summary>How many living enemies the team saw (or the scan showed) within metres of a point in the last seconds.</summary>
        private int EnemiesKnownNear(Vector3 point, float metres, float seconds)
        {
            int count = 0;
            foreach (var pair in lastSeen)
                if (enemyBodies.ContainsKey(pair.Key) && Time.time - pair.Value.time <= seconds && Flat(pair.Value.position, point) <= metres) count++;
            return count;
        }

        private Vector3? NearestHumanTeammate(float maxMetres)
        {
            int team = Team;
            Vector3? best = null;
            float bestDistance = maxMetres;
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player is BotPlayer || Teams.IsSpectator(player) || !Teams.TryGetTeam(player, out int theirs) || theirs != team) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                PlayerHealth theirHealth = body != null ? body.GetComponent<PlayerHealth>() : null;
                if (theirHealth == null || !theirHealth.IsAlive) continue;
                float d = Flat(body.transform.position, Position);
                if (d > bestDistance) continue;
                bestDistance = d;
                best = body.transform.position;
            }
            return best;
        }

        /// <summary>How many living players of the bot's team (itself included) are within metres of a point.</summary>
        private int TeammatesNear(Vector3 point, float metres)
        {
            int team = Team, count = 0;
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (Teams.IsSpectator(player) || !Teams.TryGetTeam(player, out int theirs) || theirs != team) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                PlayerHealth theirHealth = body != null ? body.GetComponent<PlayerHealth>() : null;
                if (theirHealth != null && theirHealth.IsAlive && Flat(body.transform.position, point) <= metres) count++;
            }
            return count;
        }

        private float LowestTeammateHealthNear(float metres)
        {
            int team = Team;
            float lowest = 1f;
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player.ActorNumber == view.OwnerActorNr || Teams.IsSpectator(player) || !Teams.TryGetTeam(player, out int theirs) || theirs != team) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                PlayerHealth theirHealth = body != null ? body.GetComponent<PlayerHealth>() : null;
                if (theirHealth == null || !theirHealth.IsAlive || theirHealth.MaxHealth <= 0f || Flat(body.transform.position, Position) > metres) continue;
                lowest = Mathf.Min(lowest, theirHealth.Health / theirHealth.MaxHealth);
            }
            return lowest;
        }

        private float NearestVisibleEnemyMetres()
        {
            float best = float.MaxValue;
            foreach (var (_, distance) in visible) best = Mathf.Min(best, distance);
            return best;
        }

        private int EnemiesWithin(float metres)
        {
            int count = 0;
            foreach (var (_, distance) in visible)
                if (distance <= metres) count++;
            return count;
        }

        // ---- standing still and scouting ----

        /// <summary>Now and then, with no enemy to fight and nothing urgent, it stands still a moment.</summary>
        private void PauseNowAndThen()
        {
            bool idle = stance == BotStance.Fight && target < 0 && !objectiveUrgent && Time.time >= scoutUntil;
            if (!idle) pauseUntil = 0f;
            if (Time.time < nextPauseRollAt) return;
            nextPauseRollAt = Time.time + Random.Range(cfg.IdlePauseEveryMinSeconds, cfg.IdlePauseEveryMaxSeconds);
            if (BotPaceRules.Pause(idle, Random.value, cfg.IdlePauseChance))
                pauseUntil = Time.time + Random.Range(cfg.PauseMinSeconds, cfg.PauseMaxSeconds);
        }

        /// <summary>The Laser bot, on its way to a zone, stops once to look ahead through its Scope (ChooseAbilities holds it, AimAndFire faces the zone).</summary>
        private void ScoutAhead()
        {
            if (Time.time < scoutUntil)
            {
                if (target >= 0 || visible.Count > 0 || stance != BotStance.Fight) scoutUntil = 0f;
                return;
            }
            if (bot.Build != BotConfig.Laser || build == null || abilities == null || objectiveZone < 0
                || heldKind != GoalKind.Objective || heldKey != objectiveZone || target >= 0) return;
            bool hasScope = build.Attachment != null && abilities.EquippedId(AbilitySlot.Attachment) == build.Attachment.Id;
            if (!BotAbilityRules.Scout(hasScope, scoutedZone == objectiveZone, Flat(objectiveCentre, Position), cfg.ScoutMinMetres, cfg.ScoutMaxMetres,
                    visible.Count > 0)) return;
            scoutedZone = objectiveZone;
            scoutUntil = Time.time + cfg.ScoutSeconds;
            pauseUntil = 0f;
            tally.Scouts++;
        }

        private bool StandingStill() => Time.time < pauseUntil || Time.time < scoutUntil;

        // ---- abilities ----

        private void ChooseAbilities()
        {
            attachmentHold = false;
            mobilityHold = false;
            if (build == null || abilities == null) return;

            bool hasTarget = TryTargetPosition(out Vector3 targetAt);
            float distance = hasTarget ? Flat(targetAt, Position) : float.MaxValue;
            bool inFight = hasTarget;
            float nearest = NearestVisibleEnemyMetres();
            Vector3 nearestAt = NearestVisibleEnemyPosition();
            int attachmentId = build.Attachment != null ? build.Attachment.Id : 0;
            int movementId = build.Movement != null ? build.Movement.Id : 0;
            int ultimateId = build.Ultimate != null ? build.Ultimate.Id : 0;

            switch (bot.Build)
            {
                case BotConfig.Shotgun:
                    if (nearest < float.MaxValue && Ready(AbilitySlot.Mobility, movementId)
                        && BotAbilityRules.ZipEscape(Health01, cfg.ZipEscapeHealth, nearest, cfg.ZipEscapeEnemyMetres, WallBehind(nearestAt)))
                        Press(PressSlot.Mobility, BotAbilityRules.AwayFrom(Position, nearestAt, cfg.ZipMaxMetres));
                    else if (hasTarget && Ready(AbilitySlot.Attachment, attachmentId)
                        && BotAbilityRules.Flame(true, distance, AngleTo(targetAt), cfg.FlameMetres, HalfFlameConeDegrees))
                        Press(PressSlot.Attachment, null);
                    else if (hasTarget && Ready(AbilitySlot.Mobility, movementId)
                        && BotAbilityRules.Zip(true, distance, cfg.ZipMinMetres, cfg.ZipMaxMetres, Health01, cfg.ZipMinHealth, ClearShot(targetAt)))
                        Press(PressSlot.Mobility, targetAt);
                    if (UltimateReady(ultimateId) && BotAbilityRules.Fence(EnemiesWithin(cfg.FenceMetres), EnemiesWithin(cfg.FenceTwoEnemiesMetres)))
                        Press(PressSlot.Ultimate, null);
                    break;

                case BotConfig.Burst:
                    if (hasTarget && Ready(AbilitySlot.Attachment, attachmentId)
                        && BotAbilityRules.Cover(inFight, Health01, cfg.CoverHealth, distance, cfg.CoverMinMetres))
                        Press(PressSlot.Attachment, targetAt);
                    mobilityHold = abilities.EquippedId(AbilitySlot.Mobility) == movementId
                        && BotAbilityRules.Sprint(goal.HasValue && !hasTarget && !StandingStill(), Time.time - lastEnemySeenAt, cfg.SprintQuietSeconds,
                            overheat != null ? overheat.Normalised : 0f, cfg.SprintMaxHeat);
                    if (UltimateReady(ultimateId) && BotAbilityRules.HealZone(inFight || stance == BotStance.Retreat, Health01,
                            LowestTeammateHealthNear(cfg.HealZoneTeammateMetres), cfg.HealZoneHealth))
                        Press(PressSlot.Ultimate, null);
                    break;

                case BotConfig.Laser:
                    attachmentHold = abilities.EquippedId(AbilitySlot.Attachment) == attachmentId
                        && (BotAbilityRules.Scope(hasTarget, distance, cfg.ScopeMinMetres) || Time.time < scoutUntil);
                    if (nearest < float.MaxValue && Ready(AbilitySlot.Mobility, movementId) && BotAbilityRules.Blink(Health01, cfg.BlinkHealth, nearest, cfg.BlinkEnemyMetres))
                        Press(PressSlot.Mobility, BotAbilityRules.AwayFrom(Position, nearestAt, 9f));
                    if (hasTarget && UltimateReady(ultimateId)
                        && BotAbilityRules.Raybeam(true, distance, cfg.RaybeamMetres, Time.time - targetSeenSince, cfg.RaybeamSeenSeconds))
                        Press(PressSlot.Ultimate, null);
                    break;
            }
        }

        private bool Ready(AbilitySlot slot, int wantedId) =>
            wantedId != 0 && abilities.EquippedId(slot) == wantedId && abilities.BlockFor(slot) == CastBlock.None && !abilities.CasterLocked && pending.Slot == PressSlot.None
            && BotPressRules.MayPressAgain(Time.time - lastPressAt[(int)ToPress(slot)], cfg.PressRetrySeconds);

        private readonly float[] lastPressAt = { -99f, -99f, -99f, -99f };

        private static PressSlot ToPress(AbilitySlot slot) =>
            slot == AbilitySlot.Attachment ? PressSlot.Attachment : slot == AbilitySlot.Mobility ? PressSlot.Mobility : PressSlot.Ultimate;

        private bool UltimateReady(int ultimateId) =>
            ultimate != null && ultimate.IsFull && Ready(AbilitySlot.Ultimate, ultimateId);

        private void Press(PressSlot slot, Vector3? aimAt)
        {
            if (pending.Slot != PressSlot.None) return;
            pending = new PendingPress { Slot = slot, Stage = 0, AimAt = aimAt };
        }

        private Vector3 NearestVisibleEnemyPosition()
        {
            float best = float.MaxValue;
            Vector3 at = Position + transform.forward;
            foreach (var (actor, distance) in visible)
                if (distance < best && enemyBodies.TryGetValue(actor, out PhotonView body)) { best = distance; at = body.transform.position; }
            return at;
        }

        private bool ClearShot(Vector3 to)
        {
            Vector3 from = weapon != null ? weapon.SafeMuzzlePosition : Position + Vector3.up;
            Vector3 end = new Vector3(to.x, from.y, to.z);
            return !Physics.Linecast(from, end, LayerMask.GetMask("Building", "Barrier"), QueryTriggerInteraction.Ignore);
        }

        /// <summary>Whether a Zip fired straight away from the enemy would bite a wall within its reach (zipMaxMetres) to pull the bot to.</summary>
        private bool WallBehind(Vector3 enemy)
        {
            Vector3 from = weapon != null ? weapon.SafeMuzzlePosition : Position + Vector3.up;
            Vector3 away = BotAbilityRules.AwayFrom(Position, enemy, 1f) - Position;
            return Physics.Raycast(from, away, cfg.ZipMaxMetres, LayerMask.GetMask("Building", "Barrier"), QueryTriggerInteraction.Ignore);
        }

        private float AngleTo(Vector3 point)
        {
            Vector3 to = point - Position;
            to.y = 0f;
            Vector3 facing = aim != null ? aim.AimDirection : transform.forward;
            facing.y = 0f;
            return Vector3.Angle(facing, to);
        }

        // ---- acting every frame ----

        private void Act()
        {
            if (!Alive)
            {
                pending = default;
                primaryHeld = attachmentHeld = ultimateHeld = mobilityHeld = false;
                router?.SetBotInput(Vector3.zero, false, false, false, false);
                return;
            }

            TrackTarget();
            Vector3 move = Steer();
            AimAndFire();
            Vent();
            RunPendingPress();
            TakePackIfThere();

            attachmentHeld = attachmentHold || (pending.Slot == PressSlot.Attachment && pending.Stage == 2);
            mobilityHeld = mobilityHold || (pending.Slot == PressSlot.Mobility && pending.Stage == 2);
            ultimateHeld = pending.Slot == PressSlot.Ultimate && pending.Stage == 2;
            router?.SetBotInput(move, primaryHeld, attachmentHeld, ultimateHeld, mobilityHeld);
        }

        private void TrackTarget()
        {
            if (target < 0 || !enemyBodies.TryGetValue(target, out PhotonView body) || body == null) return;
            Vector3 now = body.transform.position;
            if (Time.deltaTime > 0f) targetVelocity = Vector3.Lerp(targetVelocity, (now - targetLastPosition) / Time.deltaTime, 0.3f);
            targetLastPosition = now;
        }

        /// <summary>Press stages: 0 aim where the ability goes (a frame for the aim to land), 1 nothing held, 2 the key down, 3 released.</summary>
        private void RunPendingPress()
        {
            if (pending.Slot == PressSlot.None) return;
            pending.Stage++;
            if (pending.Stage == 2)
            {
                lastPressAt[(int)pending.Slot] = Time.time;
                if (pending.Slot == PressSlot.Attachment) tally.Attachment++;
                else if (pending.Slot == PressSlot.Mobility) tally.Movement++;
                else tally.Ultimate++;
            }
            if (pending.Stage > 3) pending = default;
        }

        private void AimAndFire()
        {
            bool wasPulling = primaryHeld;
            primaryHeld = false;
            PhotonView body = null;
            bool hasTarget = target >= 0 && enemyBodies.TryGetValue(target, out body) && body != null;
            bool seen = hasTarget && TeamSightQuery.CanTeamSeeBody(Team, body);
            Vector3 targetPosition = Position;
            if (hasTarget)
            {
                targetPosition = body.transform.position;
                if (!seen && lastSeen.TryGetValue(target, out var last)) targetPosition = last.position;
            }
            float distance = hasTarget ? Flat(targetPosition, Position) : 0f;
            float range = WeaponRange();
            // Off a fight it watches its route and turns to an enemy only once it is in range (an opportunity): walking with its back to
            // its aim would slow it like a player walking backwards.
            bool onTarget = hasTarget && (engaged || distance <= range);
            if (onTarget && !aimingAtTarget) targetAimedSince = Time.time;
            aimingAtTarget = onTarget;

            Vector3 desired;
            float targetHealth01 = 1f;
            if (pending.Slot != PressSlot.None && pending.AimAt.HasValue)
            {
                desired = pending.AimAt.Value - Position;
            }
            else if (onTarget)
            {
                WeaponDefinition def = weapon != null ? weapon.Weapon : null;
                float speed = def != null && def.WindupSeconds <= 0f ? def.ProjectileSpeed : 0f;
                Vector3 lead = BotAimRules.Lead(targetPosition, seen ? targetVelocity : Vector3.zero, distance, speed, cfg.LeadFraction);
                if (Time.time - aimOffsetAt > cfg.AimWobbleSeconds)
                {
                    aimOffsetAt = Time.time;
                    float error = BotAimRules.ErrorMetres(Time.time - targetAimedSince, cfg.AimErrorStartMetres, cfg.AimErrorMinMetres, cfg.AimSettleSeconds);
                    Vector3 across = Vector3.Cross(Vector3.up, (targetPosition - Position).normalized);
                    aimOffset = across * Random.Range(-error, error);
                }
                desired = lead + aimOffset - Position;
                PlayerHealth theirs = body.GetComponent<PlayerHealth>();
                if (theirs != null && theirs.MaxHealth > 0f) targetHealth01 = theirs.Health / theirs.MaxHealth;
            }
            else if (Time.time < scoutUntil)
            {
                desired = objectiveCentre - Position;
            }
            else if (goal.HasValue && hasPath)
            {
                desired = (corners.Length > 1 ? corners[Mathf.Min(1, corners.Length - 1)] : goal.Value) - Position;
            }
            else
            {
                desired = aimDirection;
            }

            desired.y = 0f;
            if (desired.sqrMagnitude > 0.0001f)
                aimDirection = BotAimRules.TurnToward(aimDirection, desired, cfg.TurnDegreesPerSecond * Time.deltaTime);
            float reach = Mathf.Max(3f, onTarget ? distance : 0f);
            aim.SetAimOverride(new Vector3(Position.x, Position.y, Position.z) + aimDirection * reach);

            if (!onTarget || !seen || pending.Slot != PressSlot.None && pending.AimAt.HasValue) return;

            float error01 = BotAimRules.ErrorMetres(Time.time - targetAimedSince, cfg.AimErrorStartMetres, cfg.AimErrorMinMetres, cfg.AimSettleSeconds);
            float tolerance = Mathf.Max(AimToleranceFloorDegrees, Mathf.Atan2(BodyHalfWidth + error01, Mathf.Max(1f, distance)) * Mathf.Rad2Deg);
            bool aimed = BotAimRules.OnTarget(aimDirection, targetPosition - Position, tolerance);
            bool canAct = overheat == null || !overheat.IsSilenced;
            canAct &= abilities == null || !abilities.CasterLocked;
            primaryHeld = BotFireRules.ShouldFire(seen, BotFireRules.Reacted(Time.time - targetSeenSince, cfg.ReactionSeconds), aimed, distance, range,
                overheat != null ? overheat.Normalised : 0f, cfg.StopFiringHeat, targetHealth01, cfg.FinishTargetHealth, canAct);
            if (primaryHeld && !wasPulling) tally.Pulls++;
        }

        private void Vent()
        {
            if (overheat == null) return;
            bool silenced = overheat.IsSilenced;
            if (silenced && !wasSilenced)
            {
                ventWillHit = Random.value < cfg.VentSuccessChance;
                ventPressed = false;
            }
            wasSilenced = silenced;
            if (!BotVentRules.PressNow(silenced, overheat.IsVentWindowOpen, ventWillHit, ventPressed)) return;
            ventPressed = true;
            tally.Vents++;
            router?.PressBotVent();
        }

        private void TakePackIfThere()
        {
            if (wantedPack < 0 || HealthPackManager.Instance == null || !goal.HasValue) return;
            if (Flat(goal.Value, Position) > HealthPackManager.Instance.PickupRadius) return;
            if (!HealthPackManager.Instance.TryTakeForBot(view, wantedPack)) return;
            wantedPack = -1;
            tally.Packs++;
        }

        // ---- moving ----

        private Vector3 Steer()
        {
            if (!goal.HasValue || director == null || !director.NavMesh.Ready || StandingStill())
            {
                ResetProgress();
                return Vector3.zero;
            }
            Vector3 to = goal.Value;
            float toGoal = Flat(to, Position);
            if (toGoal < ArriveMetres)
            {
                hasPath = false;
                ResetProgress();
                return Vector3.zero;
            }

            if (BotPathRules.NeedsRepath(hasPath, Time.time - plannedAt, cfg.RepathSeconds, Flat(to, plannedFor)))
            {
                plannedAt = Time.time;
                plannedFor = to;
                hasPath = director.NavMesh.TryPath(Position, to, path);
                corners = hasPath ? path.corners : new Vector3[0];
            }
            // Never a straight walk without a route: that is what ran bots into walls.
            if (!hasPath)
            {
                ResetProgress();
                return Vector3.zero;
            }

            if (Time.time - progressSince >= cfg.StuckSeconds)
            {
                bool stuck = BotPathRules.IsStuck(toGoal > cfg.NoSidestepNearGoalMetres, Flat(Position, progressFrom), Time.time - progressSince, cfg.StuckSeconds);
                ResetProgress();
                if (stuck)
                {
                    hasPath = false;
                    sidestep = Vector3.Cross(Vector3.up, (to - Position).normalized) * (Random.value < 0.5f ? -1f : 1f);
                    sidestepUntil = Time.time + 0.4f;
                }
            }
            if (Time.time < sidestepUntil) return sidestep;

            int next = BotPathRules.NextCorner(corners, Position, cfg.CornerReachMetres);
            return next < 0 ? Vector3.zero : BotPathRules.MoveToward(Position, corners[next]);
        }

        // The stuck clock only runs while it is trying to walk: a bot that stood still on purpose is not stuck the moment it sets off.
        private void ResetProgress()
        {
            progressFrom = Position;
            progressSince = Time.time;
        }

        // ---- shopping ----

        private void Shop()
        {
            if (build == null || loadout == null || health == null) return;
            if (DominionMode.IsActive()) ShopDominion();
            else ShopConquest();
        }

        private BotLoadout Current()
        {
            int Slot(AbilitySlot s)
            {
                int id = abilities != null ? abilities.EquippedId(s) : LoadoutProperties.Empty;
                return id == LoadoutProperties.Empty ? 0 : id;
            }
            return new BotLoadout
            {
                WeaponId = weapon != null && weapon.Weapon != null ? weapon.Weapon.Id : plan.BaselineId,
                AttachmentId = Slot(AbilitySlot.Attachment),
                MovementId = Slot(AbilitySlot.Mobility),
                UltimateId = Slot(AbilitySlot.Ultimate),
                Absorb = health.AbsorbLevel,
                Recharge = health.RechargeLevel,
            };
        }

        private void ShopConquest()
        {
            ArmorConfig armour = health.ArmorSettings;
            GameplayConfig gameplay = director != null ? director.Gameplay : null;
            ShopContext ctx = ShopPricing.Build(gameplay, health, wallet, view.Owner, Position, health.IsAlive);
            int cap = armour != null ? armour.MaxArmorUpgrades : 0;
            BotLoadout now = Current();
            offers.Clear();
            foreach (ShopStep step in BotShopRules.Missing(plan, now, cap))
            {
                int list = ListPrice(step, now, armour);
                int price = ctx.IsFree ? 0 : PriceNow(step, now, armour, list);
                offers.Add(new ShopOffer(step, list, price));
            }
            int pick = BotShopRules.MostExpensiveAffordable(offers, ctx.IsFree ? int.MaxValue : ctx.Balance);
            if (pick < 0) return;
            ShopOffer offer = offers[pick];
            if (!ctx.IsFree && ctx.Check(offer.Price) != PurchaseBlock.None) return;
            if (offer.Price > 0 && (wallet == null || !wallet.TrySpend(offer.Price))) return;
            Apply(offer.Step, cap);
            tally.Purchases++;
            Debug.Log($"[BOTS] {bot.NickName} bought {offer.Step} for {offer.Price}");
        }

        private int ListPrice(ShopStep step, BotLoadout now, ArmorConfig armour)
        {
            switch (step.Kind)
            {
                case ShopStepKind.Weapon:
                    return step.Id == plan.FamilyId ? GoldOf(build.FamilyWeapon) : GoldOf(build.UpgradeWeapon);
                case ShopStepKind.Attachment: return build.Attachment != null ? build.Attachment.GoldCost : 0;
                case ShopStepKind.Movement: return build.Movement != null ? build.Movement.GoldCost : 0;
                case ShopStepKind.Ultimate: return build.Ultimate != null ? build.Ultimate.GoldCost : 0;
                default: return armour != null ? armour.CostFor(now.Absorb + now.Recharge) : 0;
            }
        }

        private int PriceNow(ShopStep step, BotLoadout now, ArmorConfig armour, int list)
        {
            switch (step.Kind)
            {
                case ShopStepKind.Attachment: return ShopRules.AbilityPrice(now.AttachmentId == 0, AbilitySlot.Attachment, list);
                case ShopStepKind.Movement: return ShopRules.AbilityPrice(now.MovementId == 0, AbilitySlot.Mobility, list);
                case ShopStepKind.Ultimate: return ShopRules.AbilityPrice(now.UltimateId == 0, AbilitySlot.Ultimate, list);
                default: return list;
            }
        }

        private static int GoldOf(WeaponDefinition def) => def != null ? def.GoldCost : 0;

        private void Apply(ShopStep step, int armourCap)
        {
            switch (step.Kind)
            {
                case ShopStepKind.Weapon: loadout.SetWeapon(step.Id); break;
                case ShopStepKind.Attachment: loadout.SetAbility(AbilitySlot.Attachment, step.Id); break;
                case ShopStepKind.Movement: loadout.SetAbility(AbilitySlot.Mobility, step.Id); break;
                case ShopStepKind.Ultimate: loadout.SetAbility(AbilitySlot.Ultimate, step.Id); break;
                case ShopStepKind.Absorb: ArmorLoadoutActions.TryUpgrade(health, loadout, health.ArmorSettings, true, armourCap); break;
                case ShopStepKind.Recharge: ArmorLoadoutActions.TryUpgrade(health, loadout, health.ArmorSettings, false, armourCap); break;
            }
        }

        /// <summary>Dominion picks are free and taken in the break: whatever the coming round opens on the way to the build, one change per
        /// shopping tick.</summary>
        private void ShopDominion()
        {
            DominionDirector director = DominionDirector.Instance;
            DominionConfig config = this.director != null && this.director.Room != null ? this.director.Room.Dominion : null;
            if (director == null || config == null || !DominionShopRules.MayPick(director.Stage)) return;
            int round = Mathf.Max(1, director.Round);
            int depth = Pick(config.WeaponDepthByRound, round);
            int armourCount = Pick(config.ArmorUpgradesByRound, round);
            BotLoadout want = BotShopRules.DominionTarget(plan, depth, armourCount);
            BotLoadout now = Current();
            ArmorConfig armour = health.ArmorSettings;
            int cap = Mathf.Min(armour != null ? armour.MaxArmorUpgrades : 0, armourCount);

            if (now.WeaponId != want.WeaponId && want.WeaponId != 0) loadout.SetWeapon(want.WeaponId);
            else if (now.AttachmentId != want.AttachmentId && want.AttachmentId != 0) loadout.SetAbility(AbilitySlot.Attachment, want.AttachmentId);
            else if (now.MovementId != want.MovementId && want.MovementId != 0) loadout.SetAbility(AbilitySlot.Mobility, want.MovementId);
            else if (now.UltimateId != want.UltimateId && want.UltimateId != 0) loadout.SetAbility(AbilitySlot.Ultimate, want.UltimateId);
            else if (now.Absorb < want.Absorb) ArmorLoadoutActions.TryUpgrade(health, loadout, armour, true, cap);
            else if (now.Recharge < want.Recharge) ArmorLoadoutActions.TryUpgrade(health, loadout, armour, false, cap);
        }

        private static int Pick(int[] byRound, int round) =>
            byRound == null || byRound.Length == 0 ? 0 : byRound[Mathf.Clamp(round - 1, 0, byRound.Length - 1)];

        // ---- helpers ----

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private static string NameOf(int actor)
        {
            Player p = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            return p != null ? p.NickName : actor.ToString();
        }
    }
}
