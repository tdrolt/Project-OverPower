using System;
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
    /// Dominion Task 9, the round HUD's host. Added at runtime by DominionDirector (no scene footprint), one per client including a spectator seat
    /// (a spectator has no body, so none of this can live on the player prefab). It owns one overlay canvas and draws, from the ROOM alone
    /// (DominionRoomState and the server clock), so every screen shows the same numbers:
    ///  - RoundHud: the round bar at the top centre during a round and sudden death;
    ///  - BreakCard: the card between rounds, plus the big last-seconds countdown;
    ///  - CentrePayoutLabel: the centre countdown under the minimap (3v3v3);
    ///  - SuddenDeathOverlay: the banner and the circle countdown under the minimap;
    ///  - DominionResultPanel: the match result, built on request from MatchUI (players) or SpectatorSeatView (spectators).
    /// Nothing here is drawn in Conquest, in the warm-up or on the lobby screens: the whole HUD is hidden until the match is live in a Dominion room.
    /// No new networking: it only reads Room Properties. The canvas sorts under the shop (-5) and over the player HUD and minimap, so the shop opens
    /// on top of the break card.
    /// </summary>
    public sealed class DominionHud : MonoBehaviourPunCallbacks
    {
        public static DominionHud Instance { get; private set; }

        /// <summary>Where the HUD canvas sorts: under the shop (-5) so the shop draws over the break card, over the player HUD (-10) and the minimap (-9).</summary>
        public const int SortingOrder = -6;

        private RoomManager rooms;
        private UiTheme theme;
        private LobbyUiKit kit;
        private Canvas canvas, resultCanvas;
        private RoundHud round;
        private BreakCard breakCard;
        private CentrePayoutLabel centre;
        private SuddenDeathOverlay sudden;
        private DominionResultPanel result;

        private DominionRoomState state;
        private DominionRoomState previous;
        private bool haveState;
        private bool dirty = true;
        private int[] teams = Array.Empty<int>();
        private Room teamsRoom;
        private int centreZone = -1;

        public RoundHud Round => round;
        public BreakCard Break => breakCard;
        public CentrePayoutLabel Centre => centre;
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
            switch (state.Stage)
            {
                case DominionStage.Round:
                    round.Refresh(teams, state.Round, config.MaxRounds, DominionHudText.SecondsLeft(state.EndMs, now), false, state.Points, state.Wins, config.RoundsToWin, names);
                    breakCard.SetVisible(false);
                    sudden.Hide();
                    RefreshCentre(config, now, names);
                    break;
                case DominionStage.SuddenDeath:
                    round.Refresh(teams, state.Round, config.MaxRounds, 0, true, state.Points, state.Wins, config.RoundsToWin, names);
                    breakCard.SetVisible(false);
                    centre.SetVisible(false);
                    RefreshSudden(now);
                    break;
                case DominionStage.Break:
                    round.SetVisible(false);
                    centre.SetVisible(false);
                    sudden.Hide();
                    breakCard.SetDotsToWin(config.RoundsToWin);
                    breakCard.Refresh(teams, state.Round, state.Round <= 1, state.Points, state.Wins, DominionHudText.SecondsLeft(state.EndMs, now),
                        Mathf.RoundToInt(config.BreakCountdownSeconds), config.WeaponDepthByRound, config.ArmorUpgradesByRound, names,
                        Teams.TryGetPlayingTeam(PhotonNetwork.LocalPlayer, out _), OpenTheShop);
                    break;
                default: // None (before round 1 is written) and Over (the result panel takes the screen)
                    HideBars();
                    break;
            }
        }

        private void ReadRoom(DominionConfig config)
        {
            previous = state;
            bool hadPrevious = haveState;
            state = DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties);
            haveState = true;
            dirty = false;
            if (teamsRoom != PhotonNetwork.CurrentRoom)
            {
                teams = DominionMode.TeamsOfCurrentRoom();
                teamsRoom = PhotonNetwork.CurrentRoom;
            }
            if (!hadPrevious || round == null) return;

            // The centre paid out: the next payout time moved and one team's points rose by the payout. A joiner's first read never flashes.
            int paid = DominionHudText.CentrePayoutTeam(previous.CentreMs, state.CentreMs, previous.Points, state.Points, config.CentrePayoutPoints);
            if (paid >= 0) round.Flash(paid, config.CentrePayoutPoints, theme.scoreboardTeamNames);
        }

        private void RefreshCentre(DominionConfig config, int now, string[] names)
        {
            bool show = teams.Length == 3 && state.CentreMs != 0 && now != 0;
            if (!show) { centre.SetVisible(false); return; }
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

        /// <summary>The match is over and this client has to show the result: the Dominion result card, with the table of points per round. Called by
        /// MatchUI (a player) and SpectatorSeatView (a spectator) in a Dominion room. False when the HUD cannot be built (no theme yet), so the caller
        /// falls back to its own panel. Safe to call twice.</summary>
        public bool ShowResult(int winner, Action onBack)
        {
            if (!EnsureBuilt()) return false;
            if (result.IsShowing) return true;
            DominionConfig config = DominionMode.Config();
            if (config == null) return false;
            ReadRoom(config);
            HideBars();
            result.Show(winner, teams, state.Wins, state.History, config.RoundsToWin, theme.scoreboardTeamNames, onBack);
            Debug.Log($"[DOMINION] result: {result.HeadlineText} | {string.Join(" / ", result.TableRows)}");
            return true;
        }

        // ---------------------------------------------------------------- building

        private bool EnsureBuilt()
        {
            if (kit != null) return true;
            if (rooms == null) rooms = FindFirstObjectByType<RoomManager>();
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
