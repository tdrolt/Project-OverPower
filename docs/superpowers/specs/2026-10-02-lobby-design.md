# Lobbies: several games at once, seats, spectators, the new menus and How to play: design

**Date:** 2026-10-02. **Branch:** `lobby` (from `main` `ceab5de`). **Source:** Tudor's request (session of 2026-10-01
evening / 10-02), answered in several Ask rounds; the GDD v2.1 (`Resources/POP GDD - 2026-10-01.docx`) sections Team
Structure, Match Start, After Elimination, Territory. Mockups and Tudor's picks: https://claude.ai/artifact/8zDa3iexpnsFYnUH8cZ5Pi
(boards 1A, 2B, 3A, 4, 5A, 6, 7A, 8, 9, P1-P6; the 2v2 map board belongs to the Dominion spec). Working notes with
every answer: `Resources/loops/Limit Test/lobby-dominion-design-notes.md`. The second game mode, **Dominion**, has its
own spec and plan after this one; this spec only leaves room for it.

## What the player sees

**1. Name screen** (board 6). Centred: "PROJECT" over **OVERPOWER** (off-white with a medium-thin orange outline), a
name field (4 to 10 letters or numbers), **Find a lobby**, and **Rejoin your match** when a match still holds your
place. No tips panel.

**2. Lobby list** (board 1A). A table of open lobbies: name, mode ("Conquest 3v3v3"), players ("5 / 9 +1 spec"), status
(In lobby / Warm-up / In match; Dominion later adds "Round N"), host, and a **Join** button (**Spectate** when only
spectator seats are free; greyed **Full** when nothing is). Order: joinable first, then in a match, then full; newest
first inside each group. The list updates by itself. **Create lobby** top right; a cyan **How to play** button bottom
left. No game-mode info here (only the mode's name).

**3. Create lobby** (board 2B). Lobby name (prefilled "<name>'s lobby", at most 24 characters; two lobbies may share a
name). Mode: **Conquest** / **Dominion**, then teams: Conquest **3v3v3** or **3v3**; Dominion **2v2** or **3v3v3**.
Dominion is greyed "coming soon" until the Dominion build lands. The mode can't be changed after creating (make a new
lobby instead).

**4. Lobby room** (board 3A). The lobby's name, then the mode as a cyan outlined button ("Conquest 3v3v3 · ⓘ") that opens
the **game mode info**, then the host. One column per team (White, Purple, Cyan; Conquest 3v3 shows White and Purple)
with one seat per player; a row of **3 spectator seats**; on the side, **No role** with everyone who hasn't picked.
Click an empty seat to take it (moving from your old one); **Leave my seat** puts you back in No role. Bottom left:
**Leave lobby** and the cyan **How to play**; bottom right, host only: **Start game** ("Everyone spawns into the
warm-up"); others read "Waiting for <host> to start the game". The lobby chat sits bottom left.

**5. Start game.** Refused until every team of the mode will have a player once No role is filled (3-team modes need all
three teams). On Start, everyone in No role is placed: each on the emptiest team (ties: the left-most), then in
spectator seats. Seats lock: no team changes after this. Players spawn into today's **warm-up** (free shop, nothing
counts); spectators get no body.

**6. Warm-up bar** (board 4). Top of the screen: "WARM-UP · Free shop · nothing counts". The host has **End warm-up**;
everyone else reads "<host> ends the warm-up". End warm-up starts today's 5 s countdown ("MATCH STARTS IN 3",
"Everything resets when it goes live") and today's go-live. It needs at least one player present on every team of the
mode.

**7. Spectators** (board 8). A spectator seat has no body, no gold and no shop. They see the **whole map with no fog**
(both teams, every zone, every effect), from a whole-map view or following any player. A bar at the bottom: who they
watch and the keys **Q** previous, **E** next, **Space** whole map, **mouse wheel** zoom, plus **Leave**. Their chat
lines read "[SPEC] Kim: ...". A player knocked out of the match keeps today's Spectate (follows the team that knocked
them out, with that team's fog).

**8. Joining a lobby whose game is running.** You get a free seat on a team that is in the match (the emptiest), else a
spectator seat; the list shows Spectate when only those are left. You spawn as today's late joiner does.

**9. Drops and rejoin.** Before Start game a dropped player's seat is freed at once. After it, today's 2-minute rejoin
holds the seat (same team, gold, loadout). Quitting through the menu frees it at once.

**10. The host.** Whoever created the lobby. When the host leaves, the player who has been in the lobby longest becomes
host (Photon's own hand-over) and gets the host's buttons; the list shows the new host's name. A lobby closes when
everyone has left (a crashed last player's place keeps it in the list for up to the rejoin window).

**11. After the match.** The result screen's button goes back to the **lobby list** (not the name screen). The finished
lobby closes.

**12. Chat** (board 7A). Bottom left: a dark panel (lighter than before, alpha about 0.4), bigger text (18), the
sender's name in their team colour, "[SPEC]" for spectators, "Enter to type, Escape to close". **Each lobby has its own
chat** (today one Photon Chat channel is shared by every player in the region, so lobbies would hear each other). No
zone-capture lines. The chat also works in the lobby room.

**13. How to play** (board 5A, pictures P1-P6). A big panel over everything with a page list on the left (wiki style),
the page title, a picture, the text, previous/next buttons naming the neighbouring pages, and **✕** always top right.
Six pages: Moving and aiming, Shooting and overheat, Ability slots, Vision, The shop, Capturing zones. Texts verbatim
from the working notes ("Approved texts"). Opened from the lobby list and the lobby room only, never during a match.

**14. Game mode info** (board 9). The ⓘ on the mode button opens one page of cards with ✕ top right. Conquest: Goal,
Zones, Gold and upgrades (+ "Your first attachment and movement ability are free."), Last stand, The map shrinks, Centre
scan. Conquest 3v3 adds that the third corner is closed from the start. Dominion's cards come with the Dominion spec.

**15. Same name twice in a lobby.** The second player shows as "Tudor 2" (then 3...), so nobody can pass for someone
else.

**16. Capture speed with teammates (Tudor, 10-02).** No longer one more share per player: 1 player = 1×, 2 = 1.5×,
3 = 1.75× (a list in `TerritoryConfig`). The same list for an enemy draining your zone. Applies in every mode. Why
(Tudor): helping a teammate capture should pay, but three players stacking on a zone shouldn't speed-run captures.
The numbers are never shown to players; How to play keeps "Bring teammates and it goes faster".

**17. Match logs.** One folder per match named date, time, mode, team size and lobby name, e.g.
`2026-10-02_2130_Conquest-3v3v3_Tudors-lobby` (a second match in the same minute gets " (2)"); the match id stays inside
the files so the report still merges every client's files of one match. Lobby events (created, seat taken, start game,
host change) go in the log as markers.

## Design values (one home each)

| Value | Home |
|---|---|
| Seats per team and spectator seats per mode; which teams each mode uses | `GameModeDefinition` assets (below), one per mode |
| Mode display name, info cards (title + text + accent), "coming soon" | the same `GameModeDefinition` |
| Lobby name max length, name length min/max, list refresh, duplicate-name format | `LobbyConfig` (new) |
| Colours and sizes of the new screens, chat panel alpha and text size, How to play button colour | `UiTheme` (new sections) |
| How to play pages (title, text, picture) | `HowToPlayPages` asset (new) |
| Capture speed by number of players (1, 1.5, 1.75) | `TerritoryConfig › Capture Speed By Players` |
| Rejoin window, countdown | unchanged (`GameplayConfig`) |

## How it works

**Modes are data (Tudor P1: "make modes swappable if it's cheap").** A `GameModeDefinition` ScriptableObject per mode
(Conquest 3v3v3, Conquest 3v3, Dominion 2v2, Dominion 3v3v3) holds: id, display name, family (Conquest/Dominion), team ids,
seats per team, spectator seats, the scene/map to play on (Conquest: Game Scene), the info cards, and "available".
A `GameModeCatalogue` lists them; the create screen, the lobby room, the seat rules and the start rules read only the
definition. Adding Dominion later = new assets + its own rules component, no lobby change. (Suggestion for the Dominion
plan: Conquest's match flow in `MatchDirector` stays as it is; Dominion adds a sibling director that the definition
names, rather than branching inside MatchDirector.)

**One Photon room per lobby; no new RPCs.** Shared state is Room and Player Properties only (the RpcList stays 45,
byte-identical):
- Created with a unique room name (a short random id; the display name is a property), `MaxPlayers` = team seats +
  spectator seats, today's `PlayerTtl`, visible. Room Properties shown in the list (`CustomRoomPropertiesForLobby`):
  `lN` display name, `lM` mode id, `lS` stage (0 lobby, 1 warm-up, 2 in match), `lH` host name, `lF` filled seats as
  "team players / spectators" counts. The list comes from `OnRoomListUpdate` (kept in a cache, since Photon sends
  changes only); joining is `JoinRoom(name)`. `JoinRandomRoom` and the random `Room_####` go away.
- **Seats** are Room Properties `sT<team><index>` and `sS<index>` holding an actor number. Taking a seat is one
  check-and-set write: the new seat (expected empty) and the old one (expected you) in the same write, so two players
  clicking the same seat can't both get it, and a move is atomic. The master clears the seats of a player who left
  (before Start) or quit; after Start, a dropped player's seats stay theirs for the rejoin window.
- **Start game** (master): the pure auto-fill rule turns No role players into seat writes; one check-and-set write sets
  them and `lS` = 1 (expected 0 and the seats it computed from), retried next frame if refused. Every client, on the
  `lS` 0 → 1 edge, reads its own seat: a team seat writes `teamID` and spawns as today (a 3v3 lobby is today's
  two-team room, `mMode` = 2, written at creation); a spectator seat writes `spec` = true and starts the spectator view.
  Today's "land on the smallest team", the two/three-team switch and `ReseatLocalPlayerIfTeamClosed` are replaced by
  the seats.
- **End warm-up** is today's `HostStartMatch` with the rule "every team of the mode has a present player"; going live
  also writes `lS` = 2. `StartCountdown`/`CancelCountdown` stop shrinking `MaxPlayers` (spectator seats must stay
  joinable).
- **Late joiners** (`lS` ≥ 1) run the same placement rule against the free seats and take one by check-and-set; then
  spawn or spectate as above. A rejoining actor already owns its seat.
- **Host**: Photon's master client; a new master writes `lH`. Every master-only write already exists in the
  "new master carries on" form (MatchDirector, BuildingManager); the lobby adds only `lH` and the seat clean-up.
- **Spectators** (`spec` = true, no `teamID`, no body): every place that assumes each player has a team and a body
  (the code survey found about 15: team counts, `PlayerList` loops, telemetry, minimap, scoreboard, OverPower) skips
  them. The spectator camera reuses the follow camera; fog off for them (`TeamSight` treats a spectator as seeing
  everything).
- **Chat**: Photon Chat channel = the room name; subscribe on joining, unsubscribe on leaving; the "[SPEC]" prefix is
  added by the sender. (Photon Chat has its own connection count, separate from the 20 of the game.)
- **Back to the list**: `ReturnToNameScreen` becomes "return to the lobby list": the same leave + scene rebuild, then the
  name screen skips straight to the list (the name is kept).
- **UI** is built in code like today's panels (uGUI + TextMeshPro), the new screens in Oswald + Public Sans (TMP font
  assets made from the static font files in `gdd-build`), colours from `UiTheme`. Pictures P1-P6 are rendered to PNG
  with the house figure kit (`gdd-build/charts`) and imported as sprites.
- **Capture speed**: `Building capture.cs:762` (capture) and `CaptureFadeRule.NeutralFadeRate` (drain) take the speed
  from one pure rule reading `TerritoryConfig › Capture Speed By Players` (players beyond the list use its last entry).

**Pure rules, edit-mode tested:** `LobbySeatRules` (take/leave/move, the auto-fill order, Start allowed, End warm-up
allowed, late-joiner placement, seat counts from a definition), `LobbyListRules` (status text, sort order, Join /
Spectate / Full), `PlayerNameRules` (length, duplicate numbering), `MatchFolderName`, `CaptureSpeedRule`.

## Checks

Every task's first brief carries a two- or three-client check (F30) with recorders on every client, fresh processes for
joins and rejoins (F27), the room's actors listed first. **Two lobbies side by side** (two rooms, at most 4 clients in
all) for the list, the chat and the logs. Things that can't be run here go on a list for Tudor (he offered).

## Out of scope

Dominion and its 2v2 map (own spec); switching mode inside a lobby; passwords and private lobbies; kicking; team
changes after Start; a rematch in the same lobby; How to play during a match; matchmaking.
