using System.Collections.Generic;
using Overpower.Abilities;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Vision;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// The host's side of the bots: once the room's scene is up and the room has bots, it builds the walkable map (BotNavMesh) and gives
    /// every bot body this client controls a BotBrain and a scoreboard publisher. It rebuilds the map when the arena changes shape (the
    /// shrink). On any other client, and on a host with no bots, it does nothing. Added by RoomManager.
    /// </summary>
    public sealed class BotDirector : MonoBehaviour
    {
        private RoomManager roomManager;
        private BotConfig config;
        private GameplayConfig gameplay;
        private VisionConfig vision;

        private readonly BotNavMesh navMesh = new BotNavMesh();
        private readonly List<PhotonView> bodies = new List<PhotonView>();
        private int builtForCut = int.MinValue;
        private float rebuildAt = -1f;
        private float nextCheckAt;

        public BotNavMesh NavMesh => navMesh;
        public BotConfig Config => config;
        public GameplayConfig Gameplay => gameplay;
        public RoomManager Room => roomManager;

        public static BotDirector Instance { get; private set; }

        public void Init(RoomManager manager, BotConfig botConfig, GameplayConfig gameplayConfig, VisionConfig visionConfig)
        {
            roomManager = manager;
            config = botConfig;
            gameplay = gameplayConfig;
            vision = visionConfig;
        }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDestroy() => navMesh.Dispose();

        private void Update()
        {
            navMesh.Tick();
            if (Time.unscaledTime < nextCheckAt) return;
            nextCheckAt = Time.unscaledTime + 0.25f;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || roomManager == null || !roomManager.RoomSceneIsLoaded || config == null) return;
            if (!RoomHasBots()) return;

            int cut = MatchDirector.Instance != null ? MatchDirector.Instance.CutTeam : -1;
            if (cut != builtForCut)
            {
                // The shrink's wall goes up when the cut happens: the map is read again a moment later, once it stands.
                rebuildAt = builtForCut == int.MinValue ? Time.unscaledTime : Time.unscaledTime + 1f;
                builtForCut = cut;
            }
            if (rebuildAt >= 0f && Time.unscaledTime >= rebuildAt)
            {
                rebuildAt = -1f;
                ConfigureSight();
                navMesh.Build(config);
            }

            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (!(player is BotPlayer bot)) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(bot.ActorNumber);
                if (body == null || !body.IsMine || body.GetComponent<BotBrain>() != null) continue;
                body.GetComponent<PlayerInputRouter>()?.BeginBotDrive();
                if (gameplay != null) ScoreboardPublisher.Create(body.gameObject, gameplay);
                body.gameObject.AddComponent<BotBrain>().Init(this, bot);
                Debug.Log($"[BOTS] {bot.NickName} ({bot.ActorNumber}) is playing build {bot.Build}");
            }
        }

        private static bool RoomHasBots()
        {
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
                if (player is BotPlayer) return true;
            return false;
        }

        /// <summary>A bot sees by its team's sight, read from the same VisionConfig as players; a bot holding Scope by the Scope's own
        /// numbers, from the module prefab of the build that carries it.</summary>
        private void ConfigureSight()
        {
            if (vision == null) return;
            ScopeAbility scope = null;
            foreach (BotConfig.Build build in config.Builds)
            {
                GameObject module = build != null && build.Attachment != null ? build.Attachment.ModulePrefab : null;
                scope = module != null ? module.GetComponent<ScopeAbility>() : null;
                if (scope != null) break;
            }
            TeamSightQuery.Configure(vision, scope != null ? scope.SightConeAngleDegrees : 0f, scope != null ? scope.SightConeLength : 0f,
                scope != null ? scope.SightCircleChange : 0f);
        }
    }
}
