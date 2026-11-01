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
    /// Thinks every BotConfig.ThinkSeconds; aims, moves and holds keys every frame.
    /// </summary>
    public sealed class BotBrain : MonoBehaviour
    {
        private const float AimToleranceFloorDegrees = 3f;
        private const float BodyHalfWidth = 0.7f;
        private const float HalfFlameConeDegrees = 30f;

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
        private readonly Dictionary<int, PhotonView> enemyBodies = new Dictionary<int, PhotonView>();
        private readonly Dictionary<int, (Vector3 position, float time)> lastSeen = new Dictionary<int, (Vector3, float)>();
        private readonly List<HealthPackManager.PackStatus> packs = new List<HealthPackManager.PackStatus>();
        private readonly List<ConquestZone> conquestZones = new List<ConquestZone>();
        private readonly List<DominionZone> dominionZones = new List<DominionZone>();
        private readonly List<ShopOffer> offers = new List<ShopOffer>();

        private int target = -1;
        private float targetSeenSince;
        private float targetAimedSince;
        private Vector3 targetVelocity;
        private Vector3 targetLastPosition;
        private float lastEnemySeenAt = -99f;
        private BotStance stance = BotStance.Fight;
        private int strafeSide = 1;
        private float strafeSince;
        private float strafeSwitchAfter = 2f;
        private Vector3 aimDirection = Vector3.forward;
        private Vector3 aimOffset;
        private float aimOffsetAt;
        private Vector3? aimPointOverride;

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

        public BotPlayer Bot => bot;
        public int Target => target;
        public BotStance Stance => stance;
        public Vector3? Goal => goal;
        public string Doing { get; private set; } = "";

        public void Init(BotDirector botDirector, BotPlayer botPlayer)
        {
            director = botDirector;
            cfg = botDirector.Config;
            bot = botPlayer;
            build = cfg != null ? cfg.BuildAt(bot.Build) : null;
            plan = PlanOf(build);
            strafeSwitchAfter = NextStrafeTime();
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
                target = -1;
                goal = null;
                hasPath = false;
                Doing = "waiting to respawn";
                return;
            }

            See();
            PickTarget();
            bool cornered = NearestVisibleEnemyMetres() <= cfg.CorneredMetres;
            stance = BotHealthRules.Stance(stance, Health01, cfg.RetreatHealth, cfg.ReturnHealth, cornered);
            ChooseGoal();
            ChooseAbilities();
        }

        private void See()
        {
            visible.Clear();
            enemyBodies.Clear();
            int team = Team;
            if (team < 0 || PhotonNetwork.CurrentRoom == null) return;
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player.ActorNumber == view.OwnerActorNr || Teams.IsSpectator(player) || !Teams.TryGetTeam(player, out int theirs) || theirs == team) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                if (body == null) continue;
                PlayerHealth theirHealth = body.GetComponent<PlayerHealth>();
                if (!BotTargetRules.Targetable(theirHealth != null && theirHealth.IsAlive, RespawnShield.IsUpFor(player))) continue;
                enemyBodies[player.ActorNumber] = body;
                if (!TeamSightQuery.CanTeamSeeBody(team, body)) continue;
                lastSeen[player.ActorNumber] = (body.transform.position, Time.time);
                lastEnemySeenAt = Time.time;
                visible.Add((player.ActorNumber, Flat(body.transform.position, Position)));
            }
        }

        private void PickTarget()
        {
            float engage = EngageMetres();
            for (int i = visible.Count - 1; i >= 0; i--)
                if (visible[i].distance > engage) visible.RemoveAt(i);
            int picked = BotTargetRules.Pick(visible, target, cfg.TargetSwitchMetres);
            if (picked != target)
            {
                target = picked;
                targetSeenSince = Time.time;
                targetAimedSince = Time.time;
                if (target >= 0 && enemyBodies.TryGetValue(target, out PhotonView body)) targetLastPosition = body.transform.position;
                targetVelocity = Vector3.zero;
            }
        }

        private float EngageMetres()
        {
            float range = weapon != null && weapon.Weapon != null ? weapon.Weapon.MaxRange * Mathf.Max(0.01f, weapon.CurrentRangeMultiplier) : 15f;
            float preferred = build != null ? build.FightRangeMax : range;
            return Mathf.Max(range, preferred) + 6f;
        }

        private void ChooseGoal()
        {
            wantedPack = -1;
            if (!MatchIsOn())
            {
                goal = null;
                Doing = "waiting for the match";
                return;
            }

            if (SuddenDeathGoal()) return;

            if (stance == BotStance.Retreat)
            {
                if (TryPackGoal(ultimate: false, cfg.HealthPackMetres, true)) { Doing = "backing off to a health pack"; return; }
                goal = HomePoint();
                Doing = "backing off home";
                return;
            }

            if (target >= 0 && enemyBodies.TryGetValue(target, out PhotonView targetBody))
            {
                goal = FightPoint(targetBody.transform.position);
                Doing = "fighting " + NameOf(target);
                return;
            }

            if (RecentMemory(out Vector3 lastPlace))
            {
                goal = lastPlace;
                Doing = "looking where an enemy was";
                return;
            }

            if (BotPackRules.WantsHealthPack(Health01, cfg.HealthPackHealth, visible.Count > 0, 0f, cfg.HealthPackMetres)
                && TryPackGoal(ultimate: false, cfg.HealthPackMetres, false)) { Doing = "taking a health pack"; return; }
            if (ultimate != null && !ultimate.IsFull && TryPackGoal(ultimate: true, cfg.UltimatePackMetres, false)) { Doing = "taking an ultimate pack"; return; }

            if (DominionMode.IsActive()) DominionGoal();
            else ConquestGoal();
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

        private bool SuddenDeathGoal()
        {
            SuddenDeathZone circle = SuddenDeathZone.Instance;
            if (circle == null || !circle.IsSuddenDeath) return false;
            Vector2 here = new Vector2(Position.x, Position.z);
            if (!DominionGoalRules.InsideCircle(here, circle.Centre, circle.CurrentRadius, cfg.CircleMarginMetres))
            {
                goal = new Vector3(circle.Centre.x, Position.y, circle.Centre.y);
                Doing = "getting back inside the circle";
                return true;
            }
            if (target >= 0 && enemyBodies.TryGetValue(target, out PhotonView targetBody))
            {
                Vector3 fight = FightPoint(targetBody.transform.position);
                Vector2 fight2 = new Vector2(fight.x, fight.z);
                goal = DominionGoalRules.InsideCircle(fight2, circle.Centre, circle.CurrentRadius, cfg.CircleMarginMetres)
                    ? fight
                    : new Vector3(circle.Centre.x, Position.y, circle.Centre.y);
                Doing = "sudden death: fighting " + NameOf(target);
                return true;
            }
            goal = new Vector3(circle.Centre.x, Position.y, circle.Centre.y);
            Doing = "sudden death: holding the circle";
            return true;
        }

        /// <summary>A spot at the build's range from the target, on the bot's side of it, shifted to the strafe side.</summary>
        private Vector3 FightPoint(Vector3 enemy)
        {
            float distance = Flat(enemy, Position);
            float min = build != null ? build.FightRangeMin : 6f;
            float max = build != null ? build.FightRangeMax : 12f;
            Vector3 away = Position - enemy;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            float keep;
            switch (BotRangeRules.Decide(distance, min, max))
            {
                case RangeMove.BackOff: keep = min + 0.5f; break;
                case RangeMove.Approach: keep = max - 0.5f; break;
                default: keep = distance; break;
            }
            if (Time.time - strafeSince > strafeSwitchAfter)
            {
                strafeSide = BotRangeRules.StrafeSide(strafeSide, Time.time - strafeSince, strafeSwitchAfter);
                strafeSince = Time.time;
                strafeSwitchAfter = NextStrafeTime();
            }
            Vector3 side = Vector3.Cross(Vector3.up, away) * strafeSide;
            return enemy + away * keep + side * 2.5f;
        }

        private float NextStrafeTime() => cfg != null ? Random.Range(cfg.StrafeSwitchMinSeconds, cfg.StrafeSwitchMaxSeconds) : 2f;

        private bool RecentMemory(out Vector3 place)
        {
            place = default;
            float best = float.MaxValue;
            foreach (var pair in lastSeen)
            {
                if (Time.time - pair.Value.time > cfg.MemorySeconds) continue;
                if (!enemyBodies.ContainsKey(pair.Key)) continue;
                float d = Flat(pair.Value.position, Position);
                if (d > EngageMetres() * 1.5f || d >= best) continue;
                best = d;
                place = pair.Value.position;
            }
            return best < float.MaxValue && best > 1.5f;
        }

        private bool TryPackGoal(bool ultimate, float maxMetres, bool anyDistance)
        {
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
                    : anyDistance ? d <= maxMetres : BotPackRules.WantsHealthPack(Health01, cfg.HealthPackHealth, visible.Count > 0, d, maxMetres);
                if (!wanted || d >= best) continue;
                best = d;
                chosen = pack;
            }
            if (best == float.MaxValue) return false;
            goal = chosen.Position;
            wantedPack = chosen.Id;
            return true;
        }

        private Vector3 HomePoint()
        {
            RoomManager room = director != null ? director.Room : null;
            Transform spawn = room != null && Team >= 0 ? room.SpawnPointFor(Team, Team, view.OwnerActorNr, false) : null;
            return spawn != null ? spawn.position : Position;
        }

        private void ConquestGoal()
        {
            BuildingManager zones = BuildingManager.Instance;
            MatchDirector match = MatchDirector.Instance;
            int team = Team;
            if (zones == null || zones.Current == null || team < 0) { goal = null; Doing = "no map yet"; return; }

            Vector3 from = Position;
            Vector3? human = NearestHumanTeammate(cfg.HumanTeammateMetres);
            if (human.HasValue) from = human.Value;

            int now = PhotonNetwork.ServerTimestamp;
            conquestZones.Clear();
            for (int z = 0; z < zones.ZoneCount; z++)
            {
                if (match != null && match.IsOutOfPlay(z)) continue;
                if (!zones.TryGetZoneCentre(z, out Vector3 centre)) continue;
                int owner = zones.Current.OwnerOf(z);
                int capitalTeam = zones.Map != null ? zones.Map.CapitalTeamOf(z) : TerritoryMapNeutral;
                bool ownCapital = capitalTeam == team;
                bool enemyCapital = capitalTeam >= 0 && capitalTeam != team && owner != team;
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
                    OwnCapitalThreatened = ownCapital && ZonePresenceTracker.Instance != null && ZonePresenceTracker.Instance.IsUnderAttack(z),
                    IsEnemyCapital = enemyCapital,
                    TeammatesNear = enemyCapital ? TeammatesNear(centre, cfg.PushCapitalMetres) : 0,
                    IsCentre = zones.BaseTierOf(z) == DominionRules.CentreTier,
                });
            }

            bool centreUrgent = match != null && match.CutTeam >= 0 && CentreScan.Instance != null
                && CentreScan.Instance.CountdownSeconds >= 0 && CentreScan.Instance.CountdownSeconds <= cfg.CentreBeforeScanSeconds;
            var settings = new ConquestGoalRules.Settings
            {
                TierThreeBonusMetres = cfg.TierThreeBonusMetres,
                BountyBonusMetres = cfg.BountyBonusMetres,
                PushCapitalTeammates = cfg.PushCapitalTeammates,
            };
            int zone = ConquestGoalRules.Choose(conquestZones, from, centreUrgent, settings);
            if (zone < 0)
            {
                goal = human ?? (Vector3?)null;
                Doing = human.HasValue ? "staying with a teammate" : "holding";
                return;
            }
            goal = SpotIn(zone);
            Doing = "taking zone " + zone;
        }

        private const int TerritoryMapNeutral = -1;

        private void DominionGoal()
        {
            BuildingManager zones = BuildingManager.Instance;
            DominionDirector director = DominionDirector.Instance;
            int team = Team;
            if (zones == null || zones.Current == null || team < 0) { goal = null; Doing = "no map yet"; return; }

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
                });
            }
            bool centreSoon = director != null && centreZone >= 0 && director.SecondsToCentrePayout > 0f
                && director.SecondsToCentrePayout <= cfg.CentreBeforePayoutSeconds;
            int zone = DominionGoalRules.Choose(dominionZones, Position, centreSoon);
            goal = zone >= 0 ? SpotIn(zone) : (Vector3?)null;
            Doing = zone >= 0 ? (zone == centreZone ? "going for the centre" : "taking zone " + zone) : "holding";
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

        // ---- abilities ----

        private void ChooseAbilities()
        {
            attachmentHold = false;
            mobilityHold = false;
            if (build == null || abilities == null) return;

            bool hasTarget = target >= 0 && enemyBodies.TryGetValue(target, out PhotonView targetBody);
            enemyBodies.TryGetValue(target, out targetBody);
            float distance = hasTarget ? Flat(targetBody.transform.position, Position) : float.MaxValue;
            bool inFight = hasTarget;
            int attachmentId = build.Attachment != null ? build.Attachment.Id : 0;
            int movementId = build.Movement != null ? build.Movement.Id : 0;
            int ultimateId = build.Ultimate != null ? build.Ultimate.Id : 0;

            switch (bot.Build)
            {
                case BotConfig.Shotgun:
                    if (hasTarget && Ready(AbilitySlot.Attachment, attachmentId)
                        && BotAbilityRules.Flame(true, distance, AngleTo(targetBody.transform.position), cfg.FlameMetres, HalfFlameConeDegrees))
                        Press(PressSlot.Attachment, null);
                    else if (hasTarget && Ready(AbilitySlot.Mobility, movementId)
                        && BotAbilityRules.Zip(true, distance, cfg.ZipMinMetres, cfg.ZipMaxMetres, Health01, cfg.ZipMinHealth, ClearShot(targetBody.transform.position)))
                        Press(PressSlot.Mobility, targetBody.transform.position);
                    if (UltimateReady(ultimateId) && BotAbilityRules.Fence(EnemiesWithin(cfg.FenceMetres), EnemiesWithin(cfg.FenceTwoEnemiesMetres)))
                        Press(PressSlot.Ultimate, null);
                    break;

                case BotConfig.Burst:
                    if (hasTarget && Ready(AbilitySlot.Attachment, attachmentId)
                        && BotAbilityRules.Cover(inFight, Health01, cfg.CoverHealth, distance, cfg.CoverMinMetres))
                        Press(PressSlot.Attachment, targetBody.transform.position);
                    mobilityHold = abilities.EquippedId(AbilitySlot.Mobility) == movementId
                        && BotAbilityRules.Sprint(goal.HasValue && !hasTarget, Time.time - lastEnemySeenAt, cfg.SprintQuietSeconds,
                            overheat != null ? overheat.Normalised : 0f, cfg.SprintMaxHeat);
                    if (UltimateReady(ultimateId) && BotAbilityRules.HealZone(inFight || stance == BotStance.Retreat, Health01,
                            LowestTeammateHealthNear(cfg.HealZoneTeammateMetres), cfg.HealZoneHealth))
                        Press(PressSlot.Ultimate, null);
                    break;

                case BotConfig.Laser:
                    attachmentHold = abilities.EquippedId(AbilitySlot.Attachment) == attachmentId && BotAbilityRules.Scope(hasTarget, distance, cfg.ScopeMinMetres);
                    float nearest = NearestVisibleEnemyMetres();
                    if (nearest < float.MaxValue && Ready(AbilitySlot.Mobility, movementId) && BotAbilityRules.Blink(Health01, cfg.BlinkHealth, nearest, cfg.BlinkEnemyMetres))
                        Press(PressSlot.Mobility, BotAbilityRules.AwayFrom(Position, NearestVisibleEnemyPosition(), 9f));
                    if (hasTarget && UltimateReady(ultimateId)
                        && BotAbilityRules.Raybeam(true, distance, cfg.RaybeamMetres, Time.time - targetSeenSince, cfg.RaybeamSeenSeconds))
                        Press(PressSlot.Ultimate, null);
                    break;
            }
        }

        private bool Ready(AbilitySlot slot, int wantedId) =>
            wantedId != 0 && abilities.EquippedId(slot) == wantedId && abilities.BlockFor(slot) == CastBlock.None && !abilities.CasterLocked && pending.Slot == PressSlot.None;

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
            if (pending.Stage > 3) pending = default;
        }

        private void AimAndFire()
        {
            primaryHeld = false;
            Vector3 desired;
            float distance = 0f;
            float targetHealth01 = 1f;
            bool hasTarget = target >= 0 && enemyBodies.TryGetValue(target, out PhotonView body) && body != null;
            enemyBodies.TryGetValue(target, out body);

            if (pending.Slot != PressSlot.None && pending.AimAt.HasValue)
            {
                desired = pending.AimAt.Value - Position;
            }
            else if (hasTarget)
            {
                Vector3 targetPosition = body.transform.position;
                distance = Flat(targetPosition, Position);
                WeaponDefinition def = weapon != null ? weapon.Weapon : null;
                float speed = def != null && def.WindupSeconds <= 0f ? def.ProjectileSpeed : 0f;
                Vector3 lead = BotAimRules.Lead(targetPosition, targetVelocity, distance, speed, cfg.LeadFraction);
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
            float reach = Mathf.Max(3f, distance);
            aim.SetAimOverride(new Vector3(Position.x, Position.y, Position.z) + aimDirection * reach);

            if (!hasTarget || pending.Slot != PressSlot.None && pending.AimAt.HasValue) return;
            if (!TeamSightQuery.CanTeamSeeBody(Team, body)) return;

            float range = weapon != null && weapon.Weapon != null ? weapon.Weapon.MaxRange * Mathf.Max(0.01f, weapon.CurrentRangeMultiplier) : 0f;
            float error01 = BotAimRules.ErrorMetres(Time.time - targetAimedSince, cfg.AimErrorStartMetres, cfg.AimErrorMinMetres, cfg.AimSettleSeconds);
            float tolerance = Mathf.Max(AimToleranceFloorDegrees, Mathf.Atan2(BodyHalfWidth + error01, Mathf.Max(1f, distance)) * Mathf.Rad2Deg);
            bool aimed = BotAimRules.OnTarget(aimDirection, (body.transform.position - Position), tolerance);
            bool canAct = overheat == null || !overheat.IsSilenced;
            canAct &= abilities == null || !abilities.CasterLocked;
            primaryHeld = BotFireRules.ShouldFire(true, BotFireRules.Reacted(Time.time - targetSeenSince, cfg.ReactionSeconds), aimed, distance, range,
                overheat != null ? overheat.Normalised : 0f, cfg.StopFiringHeat, targetHealth01, cfg.FinishTargetHealth, canAct);
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
            router?.PressBotVent();
        }

        private void TakePackIfThere()
        {
            if (wantedPack < 0 || HealthPackManager.Instance == null || !goal.HasValue) return;
            if (Flat(goal.Value, Position) > HealthPackManager.Instance.PickupRadius) return;
            if (HealthPackManager.Instance.TryTakeForBot(view, wantedPack)) wantedPack = -1;
        }

        // ---- moving ----

        private Vector3 Steer()
        {
            if (!goal.HasValue || director == null || !director.NavMesh.Ready) return Vector3.zero;
            Vector3 to = goal.Value;
            if (Flat(to, Position) < 0.6f) { hasPath = false; return Vector3.zero; }

            if (BotPathRules.NeedsRepath(hasPath, Time.time - plannedAt, cfg.RepathSeconds, Flat(to, plannedFor)))
            {
                plannedAt = Time.time;
                plannedFor = to;
                hasPath = director.NavMesh.TryPath(Position, to, path);
                corners = hasPath ? path.corners : new Vector3[0];
            }

            if (Time.time - progressSince >= cfg.StuckSeconds)
            {
                bool stuck = BotPathRules.IsStuck(true, Flat(Position, progressFrom), Time.time - progressSince, cfg.StuckSeconds);
                progressFrom = Position;
                progressSince = Time.time;
                if (stuck)
                {
                    hasPath = false;
                    sidestep = Vector3.Cross(Vector3.up, (to - Position).normalized) * (Random.value < 0.5f ? -1f : 1f);
                    sidestepUntil = Time.time + 0.4f;
                }
            }
            if (Time.time < sidestepUntil) return sidestep;

            if (!hasPath) return BotPathRules.MoveToward(Position, to);
            int next = BotPathRules.NextCorner(corners, Position, 0.6f);
            return next < 0 ? Vector3.zero : BotPathRules.MoveToward(Position, corners[next]);
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
