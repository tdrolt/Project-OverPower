using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.UI
{
    /// <summary>
    /// The round HUD's host. Added at runtime by DominionDirector, one per client including a spectator seat (a spectator has no body, so none of
    /// this can live on the player prefab). Owns one overlay canvas and draws RoundHud, BreakCard, CentrePayoutLabel (3v3v3), ScoreBars (spectators
    /// too), SuddenDeathOverlay and, built on request from MatchUI (players) or SpectatorSeatView (spectators), DominionResultPanel. It reads the ROOM
    /// alone (DominionRoomState and the server clock), so every screen shows the same numbers, and only Room Properties (no networking of its own).
    /// Hidden until the match is live in a Dominion room: nothing in Conquest, the warm-up or the lobby screens. The canvas sorts under the shop (-5)
    /// and over the player HUD and minimap, so the shop opens on top of the break card.
    /// </summary>
    public sealed class DominionHud : MonoBehaviourPunCallbacks
    {
        public static DominionHud Instance { get; private set; }

        /// <summary>Under the shop (-5), over the player HUD (-10) and the minimap (-9).</summary>
        public const int SortingOrder = -6;

        private RoomManager rooms;
        private UiTheme theme;
        private LobbyUiKit kit;
        private Canvas canvas, resultCanvas;
        private RoundHud round;
        private BreakCard breakCard;
        private CentrePayoutLabel centre;
        private ScoreBars scoreBars;
        private SuddenDeathOverlay sudden;
        private DominionResultPanel result;

        private DominionRoomState state;
        private int[] finishedWinners = Array.Empty<int>(); // who won the round the break follows, as the room recorded it (read when the room changes, not every frame)
        private DominionRoomState previous;
        private bool haveState;
        private bool dirty = true;
        private int[] teams = Array.Empty<int>();
        private Room teamsRoom;
        private int centreZone = -1;
        private readonly int[] playersPerTeam = new int[DominionKeys.TeamSlots];
        private readonly Dictionary<int, float> inactiveSince = new Dictionary<int, float>(); // when this client first saw a player drop

        public RoundHud Round => round;
        public BreakCard Break => breakCard;
        public CentrePayoutLabel Centre => centre;
        public ScoreBars Bars => scoreBars;
        public SuddenDeathOverlay Sudden => sudden;
        public DominionResultPanel Result => result;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            round?.Destroy();
            breakCard?.Destroy();
            centre?.Destroy();
            scoreBars?.Destroy();
            sudden?.Destroy();
            result?.Destroy();
            kit?.Dispose();
            if (canvas != null) Destroy(canvas.gameObject);
            if (resultCanvas != null) Destroy(resultCanvas.gameObject);
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed) => dirty = true;
        public override void OnJoinedRoom() { dirty = true; haveState = false; }
        public override void OnLeftRoom()
        {
            // Leaving a room (the game reloads the scene on the way back to the list, but a scripted leave does not): nothing of the old match may stay up.
            haveState = false;
            dirty = true;
            teamsRoom = null;
            result?.Destroy();
            HideBars();
        }

        // ---------------------------------------------------------------- the frame

        private void Update()
        {
            bool live = PhotonNetwork.InRoom && DominionMode.IsActive() && MatchDirector.Instance != null && MatchDirector.Instance.IsLive;
            if (!live)
            {
                HideAll();
                return;
            }
            if (!EnsureBuilt()) return;
            DominionConfig config = DominionMode.Config();
            if (config == null) return;

            if (dirty || !haveState) ReadRoom(config);

            int now = PhotonNetwork.ServerTimestamp;
            string[] names = theme.scoreboardTeamNames;
            // The score bars follow the stage rule alone: a round or its overtime shows them, every other stage (the break, sudden death, the result) hides them.
            int[] shownTeams = DominionRules.TeamsShownIn(state.Stage, teams, state.Stage == DominionStage.Overtime ? CountPlayers() : null); // in overtime a team that left the game is hidden
            if (DominionScoreBarRules.ShownIn(state.Stage)) scoreBars.Refresh(shownTeams, state.Points);
            else scoreBars.SetVisible(false);
            switch (state.Stage)
            {
                case DominionStage.Round:
                case DominionStage.Overtime: // the same bar, its heading says OVERTIME and the clock is the overtime's own minute
                    round.Refresh(shownTeams, state.Round, config.MaxRounds, DominionHudText.SecondsLeft(state.EndMs, now), false, state.Stage == DominionStage.Overtime,
                        state.Wins, config.RoundsToWin, names);
                    breakCard.SetVisible(false);
                    sudden.Hide();
                    RefreshCentre(config, now, names);
                    break;
                case DominionStage.SuddenDeath:
                    round.Refresh(teams, state.Round, config.MaxRounds, 0, true, false, state.Wins, config.RoundsToWin, names);
                    breakCard.SetVisible(false);
                    centre.SetVisible(false);
                    RefreshSudden(now);
                    break;
                case DominionStage.Break:
                    round.SetVisible(false);
                    centre.SetVisible(false);
                    sudden.Hide();
                    breakCard.SetDotsToWin(config.RoundsToWin);
                    breakCard.Refresh(teams, state.Round, state.Round <= 1, state.Points, finishedWinners, state.Wins, DominionHudText.SecondsLeft(state.EndMs, now),
                        Mathf.RoundToInt(config.BreakCountdownSeconds), config.WeaponDepthByRound, config.ArmorUpgradesByRound, names,
                        Teams.TryGetPlayingTeam(PhotonNetwork.LocalPlayer, out _), OpenTheShop);
                    break;
                default: // None (before round 1 is written) and Over (the result panel takes the screen)
                    HideBars();
                    break;
            }
        }

        /// <summary>Players per team id, counted the way the master counts them for the last-team and overtime drop-out rules: a dropped player still counts
        /// for the dropped grace from the moment this client first saw the drop. Null (show every team) when the grace cannot be read.</summary>
        private int[] CountPlayers()
        {
            if (rooms == null || rooms.Config == null) return null;
            float grace = rooms.Config.DroppedGraceSeconds;
            Array.Clear(playersPerTeam, 0, playersPerTeam.Length);
            foreach (KeyValuePair<int, Player> pair in PhotonNetwork.CurrentRoom.Players)
            {
                Player p = pair.Value;
                if (p.IsInactive) { if (!inactiveSince.ContainsKey(pair.Key)) inactiveSince[pair.Key] = Time.unscaledTime; }
                else inactiveSince.Remove(pair.Key);
                if (!Teams.TryGetPlayingTeam(p, out int team) || team < 0 || team >= playersPerTeam.Length) continue;
                bool timed = inactiveSince.TryGetValue(pair.Key, out float since);
                if (DominionRules.CountsAsPresent(p.IsInactive, timed, since, Time.unscaledTime, grace)) playersPerTeam[team]++;
            }
            return playersPerTeam;
        }

        private void ReadRoom(DominionConfig config)
        {
            previous = state;
            bool hadPrevious = haveState;
            state = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            finishedWinners = DominionHistory.WinnersOfFinishedRound(state); // the break after round N names round N's winners
            haveState = true;
            dirty = false;
            if (teamsRoom != PhotonNetwork.CurrentRoom)
            {
                teams = DominionMode.TeamsOfCurrentRoom();
                teamsRoom = PhotonNetwork.CurrentRoom;
            }
            if (!hadPrevious || round == null) return;

            // The centre paid out: the next payout time moved and one team's points rose by the payout. A joiner's first read never flashes.
            int paid = DominionHudText.CentrePayoutTeam(previous.CentreMs, state.CentreMs, previous.Points, state.Points, config.CentrePayoutPoints, CentreHolder());
            if (paid >= 0) round.Flash(paid, config.CentrePayoutPoints, theme.scoreboardTeamNames);
        }

        private void RefreshCentre(DominionConfig config, int now, string[] names)
        {
            if (!DominionHudText.CentreLabelShown(teams.Length == 3, state.Stage, state.CentreMs, state.EndMs, now)) { centre.SetVisible(false); return; }
            centre.Refresh(config.CentrePayoutPoints, DominionHudText.SecondsLeft(state.CentreMs, now), CentreHolder(), names);
        }

        private void RefreshSudden(int now)
        {
            DominionDirector director = DominionDirector.Instance;
            int start = director != null ? director.SuddenDeathStartMs : state.SuddenDeathMs;
            if (start == 0 || now == 0) { sudden.Hide(); return; }
            SuddenDeathZone zone = SuddenDeathZone.Instance;
            sudden.Refresh(unchecked(now - start), zone != null && zone.IsSuddenDeath ? zone.SecondsUntilStopped : 0f);
        }

        /// <summary>The team holding the centre zone on this client's copy of the territory, or -1 when nobody does.</summary>
        private int CentreHolder()
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null || buildings.CurrentOwners == null) return -1;
            if (centreZone < 0 || buildings.BaseTierOf(centreZone) != DominionRules.CentreTier)
            {
                centreZone = -1;
                for (int zone = 0; zone < buildings.ZoneCount; zone++)
                    if (buildings.BaseTierOf(zone) == DominionRules.CentreTier) { centreZone = zone; break; }
            }
            if (centreZone < 0) return -1;
            return buildings.CurrentOwners.TryGetValue(centreZone, out int owner) && owner >= 0 && owner < DominionKeys.TeamSlots ? owner : -1;
        }

        private void HideBars()
        {
            round?.SetVisible(false);
            breakCard?.SetVisible(false);
            centre?.SetVisible(false);
            scoreBars?.SetVisible(false);
            sudden?.Hide();
        }

        private void HideAll() => HideBars();

        /// <summary>The break card's PICK YOUR BUILD button: opens the shop the way P does (a spectator has none, and no button).</summary>
        private static void OpenTheShop()
        {
            if (PhotonNetwork.LocalPlayer == null) return;
            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            LoadoutScreen shop = view != null ? view.GetComponent<LoadoutScreen>() : null;
            if (shop != null) shop.Toggle();
        }

        // ---------------------------------------------------------------- the result

        /// <summary>Takes the result card down (a spectator who leaves the room: the card is DominionHud's, so the spectator view cannot destroy it itself).</summary>
        public void HideResult() => result?.Destroy();

        /// <summary>Shows the Dominion result card. Called by MatchUI (a player) and SpectatorSeatView (a spectator). False when the HUD cannot be built
        /// (no theme yet), so the caller falls back to its own panel. Safe to call twice.</summary>
        public bool ShowResult(int winner, Action onBack)
        {
            if (!EnsureBuilt()) return false;
            if (result.IsShowing) return true;
            DominionConfig config = DominionMode.Config();
            if (config == null) return false;
            ReadRoom(config);
            HideBars();
            result.Show(winner, teams, state.Wins, state.History, state.HistoryWinners, state.SuddenDeathMs, theme.scoreboardTeamNames, onBack);
            Debug.Log($"[DOMINION] result: {result.HeadlineText} | {string.Join(" / ", result.TableRows)}");
            return true;
        }

        // ---------------------------------------------------------------- building

        private bool EnsureBuilt()
        {
            // A canvas that was destroyed under a live kit (a scene change took the HUD's children but not this object) is rebuilt, not drawn into.
            if (kit != null && canvas != null) return true;
            if (kit != null) { kit.Dispose(); kit = null; if (resultCanvas != null) Destroy(resultCanvas.gameObject); }
            rooms = rooms != null ? rooms : FindFirstObjectByType<RoomManager>();
            if (rooms == null || rooms.Theme == null || rooms.Theme.lobbyDisplayFont == null) return false;
            theme = rooms.Theme;
            kit = new LobbyUiKit(theme);
            canvas = kit.CreateCanvas(transform, "Dominion HUD Canvas");
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            resultCanvas = kit.CreateCanvas(transform, "Dominion Result Canvas");
            resultCanvas.overrideSorting = true;
            resultCanvas.sortingOrder = theme.dominionResultSortingOrder; // over the spectator bar (-10), under the saved-log box (10)
            round = new RoundHud(kit, canvas.transform);
            breakCard = new BreakCard(kit, canvas.transform);
            centre = new CentrePayoutLabel(kit, canvas.transform);
            scoreBars = new ScoreBars(kit, canvas.transform);
            sudden = new SuddenDeathOverlay(kit, canvas.transform);
            result = new DominionResultPanel(kit, resultCanvas.transform);
            return true;
        }

        /// <summary>Places a line under the corner minimap: top right, as wide as the minimap, below it by Under Minimap Gap. The centre countdown
        /// and the circle countdown share this spot (they never show at the same time).</summary>
        public static void PlaceUnderMinimap(RectTransform rect, UiTheme theme, float height)
        {
            float inset = theme.minimapCornerMargin + theme.minimapFrameWidth;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(theme.minimapCornerSize, height);
            rect.anchoredPosition = new Vector2(-inset, -(inset + theme.minimapCornerSize + theme.dominionUnderMinimapGap));
        }
    }
}
