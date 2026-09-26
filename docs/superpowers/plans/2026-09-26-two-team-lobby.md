# The Two-Team Lobby: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Before a match, the host can set the room to "two teams, up to 6 players" (and back), so joiners fill two
teams (2v2, 3v3) instead of being spread over three; the host then starts the match, which plays on the cut map.

**Architecture:** One new Room Property `mMode` (2 or 3; absent = 3), written only by the master during the warm-up
with check-and-set on `mTeams` being absent, together with the room's `MaxPlayers` (6 or 9). Pure rules
(`MatchStartRules`) decide who may join which team, when the countdown starts, when the host may start and when the
switch is offered; `MatchDirector` wires them; each client moves ITS OWN player off the third team when the mode switches
to two. The host's panel gains one toggle button. Going live with teams [0, 1] reuses the existing host-start path (the
cut map from live).

**Tech Stack:** Unity 6000.0.70f1, C#, Photon PUN 2 (Room/Player Properties only; no RPC change), NUnit edit-mode tests
through the `unity` CLI.

**Tudor's answers (2026-09-26):** the host switches; rooms open with three teams; in two-team mode the host presses Start
(no auto-start); players on the third team move to the smaller of the two, and switching back is allowed until the
countdown (nobody moves); 7 or more in the lobby greys the option out, and a two-team room refuses a 7th player; white
(team 0) and violet (team 1) play, the full map shows in the warm-up, cyan's corner closes when the match goes live.

---

## Rules for every task

The "Rules for every task" section of `docs/superpowers/plans/2026-09-25-map-shrink.md` applies in full (the Editor lock
per task, the CLI rules, `list_open_scenes` clean, async tests in a foreground loop, red first, `git add` by path only,
`git commit -F` with the Co-Authored-By line, no push, no RPC added/renamed/removed - `[PunRPC]` stays 31, tests guard
rules never Tudor's numbers, new asset fields as hand-edited YAML lines, Tudor may be using the computer, never
`editor_focus`, never stop a process by name, never print the Photon App IDs).

## Decisions

| # | Decision |
|---|---|
| L1 | `MatchDirector.LobbyModeKey = "mMode"`: int, 2 or 3; absent reads as 3. Written only by the master, only while the teams aren't fixed, with check-and-set expecting `mTeams` absent (so a switch can never land after the countdown started, even across a master switch). The same write sets `PhotonNetwork.CurrentRoom.MaxPlayers` to mode × `RoomManager.TeamSize`. |
| L2 | In two-team mode only teams 0 and 1 are open before the countdown (`IsTeamOpen(mode, team)` = mode 3 or team < 2). After the countdown, joining follows the existing rule (in the match and not knocked out). |
| L3 | Two-team mode never starts the countdown by itself. The host may start when both teams 0 and 1 have a player, team 2 has none, and nobody is still without a team. Three-team mode is unchanged (auto-start at three teams; the host may start with exactly two teams). |
| L4 | The host may switch to two teams while the teams aren't fixed and at most 2 × TeamSize (6) players are in the room (the switch is shown greyed otherwise, with a line saying why); back to three while the teams aren't fixed. |
| L5 | On the switch to two teams, every client whose OWN player is on team 2 re-picks the smaller of teams 0 and 1 (the existing `RoomManager` pick, now honouring the mode) and moves its own player to that team's spawn point (warm-up: nothing counts). Switching back moves nobody. |
| L6 | `MatchDirector.LiveStateChanged` also fires on a mode change (the panel redraws). The minimap and the arena are unchanged in the warm-up; going live with teams [0, 1] closes cyan's corner through the existing host-start path. |
| L7 | New designer text lives in `UiTheme` (one home each): the two buttons' labels, the greyed reason, and three warm-up lines for two-team mode. |
| L8 | Telemetry: a marker line when the mode switches ("two-team lobby on/off"), through the existing `MatchTelemetry.DropMarker`. |

## File map

| File | Change |
|---|---|
| `Assets/scripts/Match/Rules/MatchStartRules.cs` | mode-aware rules (L2-L4) + warm-up messages for two-team mode |
| `Assets/Tests/MatchStartRulesTests.cs` | their tests |
| `Assets/scripts/Match/MatchDirector.Live.cs`, `MatchDirector.cs` | `mMode`, `LobbyMode`, `HostSetLobbyMode`, mode-aware `MayJoinTeam`/auto-start/`HostMayStartNow`, mode edge |
| `Assets/scripts/RoomManager.cs` | the re-pick + move on the switch to two; `CreateRoom` unchanged (9) |
| `Assets/scripts/Player/PlayerLifecycle.cs` | a public "move my player to this team's spawn" if none fits (reuse `TeleportToSpawnPoint`) |
| `Assets/scripts/UI/MatchStartPanel.cs`, `UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset` | the toggle button, the lines (L7) |

---

### Task 1: The rules (pure, tested)

**Files:** `MatchStartRules.cs`, `MatchStartRulesTests.cs`.

- [ ] **Step 1: Failing tests** (append to `MatchStartRulesTests`, same style as the file):
  - `ModeReadsTwoOnlyWhenStoredAsTwo`: `LobbyModeOf(2) == 2`, `LobbyModeOf(3) == 3`, `LobbyModeOf(null) == 3`,
    `LobbyModeOf("x") == 3`, `LobbyModeOf(5) == 3`.
  - `TwoTeamModeOpensOnlyTheFirstTwoTeamsBeforeTheCountdown`: `IsTeamOpen(2, 0/1/2)` = true/true/false;
    `IsTeamOpen(3, 2)` = true; `MayJoin(teamsFixed:false, inMatch:false, eliminated:false, teamOpen:false)` = false;
    after the countdown the existing rule wins (`MayJoin(true, true, false, false)` = true).
  - `TwoTeamModeNeverStartsItself`: `StartsCountdownAutomatically(false, {1,1,1}, mode: 2)` = false; mode 3 = true.
  - `TheHostStartsATwoTeamMatchWhenBothTeamsHaveSomeone`: mode 2: `{1,1,0}` → true; `{2,0,0}` → false; `{1,1,1}` →
    false (someone still on the third team); a player without a team → false; teams fixed → false. Mode 3 keeps the old
    answers (the existing test stays as it is).
  - `TheSwitchToTwoTeamsNeedsSixOrFewer`: `MaySwitchToTwoTeams(false, 6, teamSize: 3)` = true; 7 → false; teams fixed →
    false. `MaySwitchToThreeTeams(false)` = true; `(true)` = false.
  - `TheRoomHoldsModeTimesTeamSize`: `MaxPlayersFor(2, 3)` = 6; `MaxPlayersFor(3, 3)` = 9.
  - `TheWarmupLineSaysTwoTeams`: `WarmupMessageFor(Warmup, teamsWithPlayers: 2, isHost: true, mode: 2)` =
    `TwoTeamsHostMayStart`; guest → `TwoTeamsWaitingForHost`; with fewer than both teams filled → `TwoTeamsWaitingForPlayers`;
    counting down → `Countdown`; live → `None`. Mode 3: the old answers.
  Run: compile failure (record it).
- [ ] **Step 2: Implement** in `MatchStartRules` (keep every existing method and its callers working: add the mode as a
  parameter with overloads, or add new methods beside the old ones; don't break `MatchStartRulesTests`' existing tests):
  `public const int TwoTeams = 2;` `public const int ThreeTeams = TeamCount;` `LobbyModeOf(object raw)`,
  `IsTeamOpen(int mode, int team)`, `MayJoin(bool teamsFixed, bool inMatch, bool eliminated, bool teamOpen)`,
  `StartsCountdownAutomatically(bool, IReadOnlyList<int>, int mode)`, `HostMayStart(bool, IReadOnlyList<int>, int, int mode)`,
  `MaySwitchToTwoTeams(bool teamsFixed, int playersInRoom, int teamSize)`, `MaySwitchToThreeTeams(bool teamsFixed)`,
  `MaxPlayersFor(int mode, int teamSize)`, and three `WarmupMessage` values (`TwoTeamsWaitingForPlayers`,
  `TwoTeamsHostMayStart`, `TwoTeamsWaitingForHost`) with `WarmupMessageFor(..., int mode)`. Doc comments quote Tudor's
  answers (2026-09-26). Allocation-free where the old ones are (the panel polls every frame).
- [ ] **Step 3:** Recompile, all tests pass. **Step 4:** Commit `feat(match): the rules for a two-team lobby`.

### Task 2: The wiring (networking: opus review)

**Files:** `MatchDirector.Live.cs`, `MatchDirector.cs`, `RoomManager.cs`, `PlayerLifecycle.cs`.

- [ ] `LobbyModeKey = "mMode"`; `public int LobbyMode => PhotonNetwork.InRoom ? MatchStartRules.LobbyModeOf(room prop) : 3`
  (read the room directly, like `IsLive`).
- [ ] `public bool HostMaySwitchToTwoTeamsNow` / `HostMaySwitchToThreeTeamsNow` (master only; the player count is
  `PhotonNetwork.CurrentRoom.PlayerCount`) and `public void HostSetLobbyMode(int mode)`: refused (log) unless master and
  the rule allows; writes `{ mMode: mode }` with expected `{ mTeams: null }`, and on success sets `MaxPlayers =
  MaxPlayersFor(mode, RoomManager.TeamSize)` and drops the telemetry marker (L8). Idempotent (writing the current mode is a
  no-op).
- [ ] `MayJoinTeam(team)` passes `IsTeamOpen(LobbyMode, team)`; `Update`'s auto-start and `HostMayStartNow` pass
  `LobbyMode`; `CountdownShouldCancel` unchanged. `StartCountdown` keeps `MaxPlayers = teams.Length × TeamSize` (a
  two-team start is 6 either way).
- [ ] The mode edge: `OnRoomPropertiesUpdate` also reacts to `mMode` (NOT counted in `touchesElimination`; it's its own
  write, not the master's elimination triad - don't touch `writesAwaitingEcho` for it). `ReactToRoomState` records the
  previous mode with the other `lastApplied*` fields BEFORE reacting, raises `LiveStateChanged` when it changed, and on a
  change to two teams (not on `firstRead`) asks `RoomManager` to re-seat the local player (below). Reset the recorded mode
  in `OnLeftRoom`.
- [ ] `RoomManager`: `PickSmallestTeam` already skips teams `MayJoinTeam` refuses, so it honours the mode. Add a public
  `ReseatLocalPlayerIfTeamClosed()`: if the local player's team isn't open any more and the teams aren't fixed, pick the
  smaller open team (`PickSmallestTeam`), write the team property (`UpdateNetworkProperties`), and move the local player to
  that team's spawn point (`teamSpawnPoints[picked]`) through `PlayerLifecycle` (add a small public method that calls its
  existing private `TeleportToSpawnPoint` with that point - `PlayerDisplacement.TeleportTo`, never `transform.position`).
  A joiner who picked team 2 with the old mode a moment before the switch arrives is covered too: the same call on the
  mode edge re-seats them.
- [ ] No test can run Photon: say in the report which parts are covered by Task 1's rules and which by Task 4.
- [ ] Commit `feat(match): the two-team lobby mode in the room, team picks and the start rules`.

### Task 3: The panel (the host's switch)

**Files:** `MatchStartPanel.cs`, `UiTheme.cs`, `UiTheme.asset` (+YAML lines).

- [ ] `UiTheme` (next to the other warm-up strings, each with a plain tooltip): `lobbyTwoTeamsButtonText` = "Two teams (up
  to 6)", `lobbyThreeTeamsButtonText` = "Three teams (up to 9)", `lobbyTwoTeamsTooManyText` = "Two teams needs 6 players
  or fewer", `warmupTwoTeamsWaitingText` = "Warm-up (two teams): the host starts once both teams have a player.",
  `warmupTwoTeamsHostText` = "Warm-up (two teams): both teams are here. Start when you're ready.",
  `warmupTwoTeamsGuestText` = "Warm-up (two teams): waiting for the host to start." (+6 YAML lines in `UiTheme.asset`).
- [ ] `MatchStartPanel`: a second button on the same clickable canvas, shown to the host only during the warm-up (not
  counting down, not live): its label is the OTHER mode's text; clicking calls `MatchDirector.Instance.HostSetLobbyMode`
  with that mode; when switching to two isn't allowed (7+ players) the button is non-interactable and the warm-up line
  shows `lobbyTwoTeamsTooManyText` to the host. Place it beside the Start button (same row, left of it; reuse the Start
  button's size/colour tokens unless a new token is clearly needed - if so, add it to `UiTheme` with a tooltip). The
  warm-up line uses the new messages in two-team mode. Only write text when it changes (the file's own rule: no string
  built on a frame nothing moved).
- [ ] Commit `feat(ui): the host's two-team switch`.

### Task 4: Two clients (with a fresh development `Client2`)

Build `Client2` (development, the runtime server on for the build only), A = Editor (host), B = Client2, one fresh room by
name; recorders on B first. Check: (1) the room opens in three-team mode, `MaxPlayers` 9; (2) A switches to two: `mMode` 2
and `MaxPlayers` 6 on both, B's warm-up line is the two-team guest line; (3) with A forced onto team 2 before the switch
(its own team property, warm-up), the switch moves A to team 0 or 1 (the smaller) and to that spawn, and B sees A's new
team; (4) A switches back to three: `MaxPlayers` 9, nobody moves; (5) A switches to two again and presses Start: the
countdown, then live with teams [0, 1] and cyan's corner closed on both; (6) during the countdown the switch is gone and a
`HostSetLobbyMode` call is refused. The 7-player rule is covered by Task 1's tests (say so). Gates as in the map-shrink
Task 8; captures of A's warm-up panel (the switch) and B's line.

### Task 5: Docs

`progress.md`, `assumptions-for-tudor.md` (done lines), HANDOFF (the new room key `mMode`: every build in a room must
match; the lobby), `editor-queue.md`; push.
