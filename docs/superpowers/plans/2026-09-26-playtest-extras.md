# Playtest Extras (bug reporting, the log zip, the Escape pop-up): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For the friends playtests: every player's console goes into their match log; Ctrl+B marks "a bug just
happened" (with a screenshot) and the tester's next chat message becomes the note; the report gets a "Bug reports"
section and a per-player "Console" section; each player's log is zipped at the end of a match (and on quit) and the game
shows where the file is; Escape asks "Close the game?".

**Architecture:** Everything rides the existing telemetry (`Assets/scripts/Telemetry/`: each client writes its own
`.jsonl` through `MatchTelemetry.Log`; the Editor report `TelemetryMenu.BuildReport` merges a folder's files by match
time). Three new line types (`console`, `bug`, `chat`), pure rules for what gets written (tested), the report's two new
sections following the existing "Markers" pattern, a small zip step, and one runtime pop-up. No gameplay change, no
network message: telemetry stays local per client, as its design says.

**Tech Stack:** Unity 6000.0.70f1, C#, Input System (`Keyboard.current` for tool keys - the project's convention for F1
and Escape), NUnit edit-mode tests, the existing Newtonsoft-based report writer.

**Tudor's answers (2026-09-26):** yes to Ctrl+B with a screenshot and the chat note; yes to the report sections; yes to
the zip ("so each tester sends one file"); Escape → "Close the game?" Yes / No ("we will make a menu later on but this is
good for now"). Playtest at 17:00 the same day; Tudor makes the build himself.

---

## Rules for every task

The "Rules for every task" section of `docs/superpowers/plans/2026-09-25-map-shrink.md` applies in full. Plus:
**never write the Photon App IDs into any log** (the console listener scrubs them - see P1).

## Decisions (the survey behind them: facts with file:line in the controller's notes, 2026-09-26)

| # | Decision |
|---|---|
| P1 | **Console → log.** A new `ConsoleTelemetry` listens to `Application.logMessageReceived` (main thread; not the threaded variant - the writer isn't thread-safe) and writes a `console` line through `MatchTelemetry.Log` (queued until the file opens, as today): level (`log`/`warning`/`error`/`exception`/`assert`), the message cut to `TelemetryConfig.consoleMessageMaxChars` (500), the stack for errors/exceptions cut to 1000 chars, and a repeat count - the same message repeating within 1 s is folded into one line with `n`. At most `consoleMaxLinesPerSecond` (50) lines a second; the rest are counted into one `console` line saying how many were dropped. **Scrub:** every occurrence of the Photon App IDs (read once from `PhotonNetwork.PhotonServerSettings.AppSettings`: the Realtime and the Chat id) is replaced by `<app id>` before anything is written. A guard stops the listener reacting to anything it logs itself. New `TelemetryConfig` fields (tooltips): `recordConsole` (on), `consoleMaxLinesPerSecond` 50, `consoleMessageMaxChars` 500. |
| P2 | **Ctrl+B.** A tool key (the project's convention: `Keyboard.current` polled in `Update`, not the Input Actions asset - see F1 and the loadout's Escape): Ctrl held + B pressed this frame, ignored while typing in chat (the existing `PlayerInputRouter.IsTypingInChat` check - make it `public static` or share it) or while the quit pop-up is open, at most once every 2 s. It writes a `bug` line: actor, match time, position (x, z), team, alive, the zone stood in (if any), the loadout ids (weapon, equipment, mobility, ultimate - from the player's own properties), and the screenshot file name; saves `ScreenCapture.CaptureScreenshot(<absolute path in MatchTelemetry.CurrentFolder>)` as `bug_<actor>_<t>.png` (a relative path would land next to the exe); shows `PlayerHud.ShowToast(theme.bugMarkedText)` on the local HUD ("Bug marked - type what happened in chat"). Lives in an always-on component (NOT the F1 test range panel, which can be switched off). |
| P3 | **Chat note.** `PhotonChat.SubmitPublicChatOnClick` (`Assets/scripts/chat/chatmanager.cs`) writes a `chat` line (actor, text cut to 300 chars) right before it publishes - the sender's own text, not the receive callback. |
| P4 | **The report.** `console`, `bug` and `chat` join `TelemetryLog.KnownEventNames`. A **"Bug reports"** section at the top of the whole-match tab: one card per `bug` line - who (nick from the session line), when (match time, phase), where (position, zone), team, loadout, the screenshot (an `<img>` with a path relative to the report, which is written in the match folder - check where `BuildReport` writes it and link accordingly), the reporter's `chat` lines from 0-60 s after the mark (their note), every client's `console` lines from 20 s before to 5 s after (merged, sorted by time, labelled by player), and the existing 30 s gameplay-event window the Markers section already shows. A **"Console"** section per player: every error, exception and warning, grouped by message, with the count and the first/last time (plain log lines only appear inside bug windows). CSVs: `bugs.csv`, `console.csv` (errors + warnings), whole-match only. |
| P5 | **The zip.** When this client's match result shows (the win/lose panel) and again on quit (if not already done since the last flush): flush the log (a new `MatchTelemetry.FlushNow()`), then zip THIS client's own files only - its `.jsonl` and its `bug_<actor>_*.png` - into `<Telemetry folder>/OverPower-log_<yyyy-MM-dd_HHmm>_<nick>.zip` (overwriting its own earlier zip of the same match). Other clients' files in the same folder (several clients on one PC) are left out: each tester sends their own. Then show, on a small clickable overlay with the result: "Your match log is saved: <path> - send this file to Tudor." and an "Open folder" button (`Application.OpenURL` of the folder). Use `System.IO.Compression` (`ZipFile`/`ZipArchive`) - **smoke-test that it compiles for this project first** (API level `NET_Standard`); if it doesn't, stop and report (the controller decides a fallback). Every step exception-safe: a failed zip logs one warning and changes nothing else. |
| P6 | **Escape → "Close the game?".** A runtime component (a clickable overlay canvas with a `GraphicRaycaster`, built like `MatchStartPanel`'s button canvas) polls `Keyboard.current.escapeKey`: if the loadout screen or the chat was open this frame OR the previous frame (both close themselves on Escape - `LoadoutScreen.cs:390-394`, `chatmanager.cs:152-160` - and the order of `Update`s is not fixed), it does nothing; otherwise it opens the pop-up: the question, Yes, No. While open it claims `PlayerInputRouter.SetToolFocus` (no moving or shooting while deciding; the match keeps running - it's multiplayer). Escape again or No closes it. Yes: the zip (P5) if needed, then the existing quit path, extracted from `QuitButton.Quit` into one shared static (`PhotonNetwork.Disconnect()`, then `Application.Quit()`, or stopping Play Mode in the Editor) so both use it. Texts in `UiTheme`: `quitPromptText` "Close the game?", `quitYesText` "Yes", `quitNoText` "No", plus P2's `bugMarkedText` and P5's `matchLogSavedText` ("Your match log is saved: {0} - send this file to Tudor.") and `openLogFolderText` ("Open folder") (+ YAML lines). |
| P7 | **Tests guard rules:** the console line rule (cutting, folding repeats, the per-second cap and its dropped count, the scrub - with a fake id, never a real one), the bug window selection (which console/chat lines a mark gets), the zip's file selection (own files only) and name, the "Escape belongs to the shop/chat this frame" rule. Report tests follow the existing report tests' style (synthetic `.jsonl` lines). |

## File map

| File | Change |
|---|---|
| `Assets/scripts/Telemetry/TelemetryKeys.cs` | `console`, `bug`, `chat` event names + field keys |
| `Assets/scripts/Telemetry/ConsoleLineRule.cs` (new, pure) + `ConsoleTelemetry.cs` (new) | P1 |
| `Assets/scripts/Telemetry/MatchTelemetry.cs` | `LogBug(...)`, `LogChat(...)`, `FlushNow()`; the console listener hooked on start |
| `Assets/scripts/Telemetry/BugMarkerKey.cs` (new) | P2 (the key, the screenshot, the toast) |
| `Assets/scripts/chat/chatmanager.cs` | P3 (one call) |
| `Assets/scripts/Data/TelemetryConfig.cs` + `Assets/Gameplay/Config/TelemetryConfig.asset` | P1 fields (+YAML lines) |
| `Assets/scripts/Editor/Telemetry/TelemetryLog.cs`, `TelemetryAggregator.cs`, `ReportTables.cs`, `HtmlReportWriter.cs`, `CsvReportWriter.cs` | P4 |
| `Assets/scripts/Telemetry/MatchLogZip.cs` (new, with a pure file-selection rule) | P5 |
| `Assets/scripts/UI/QuitConfirmPanel.cs` (new), `Assets/scripts/QuitButton.cs`, a shared `GameQuit` static | P6 |
| `Assets/scripts/UI/UiTheme.cs` + `UiTheme.asset` | the texts (+YAML lines) |
| `Assets/Tests/*` | P7 |

---

### Task 1: Recording (console, bug marks, chat notes)

- [ ] Tests first (red): `ConsoleLineRuleTests` - a long message is cut to the limit; a repeat within 1 s folds (count
  goes up, no new line); a different message starts a new line; past the per-second cap, lines are counted as dropped and
  one summary line carries the count; the scrub replaces a fake id `TEST-APP-ID-1234` everywhere and never emits it;
  error/exception keep a (cut) stack, log/warning don't. Plus a `BugMarkerKey` rule test if you extract one (e.g. "a mark
  is refused within 2 s of the last" / "refused while typing").
- [ ] Implement P1 (`ConsoleLineRule` pure; `ConsoleTelemetry` subscribes on enable, unsubscribes on disable/quit;
  started from wherever `MatchTelemetry` sets itself up; respects `TelemetryConfig.Enabled` and `recordConsole`), P2, P3,
  `TelemetryConfig` fields (+3 YAML lines), `UiTheme.bugMarkedText` (+1 YAML line), `TelemetryKeys`.
- [ ] Recompile; all tests pass. Commit `feat(telemetry): console lines, Ctrl+B bug marks and chat notes in the match log`.

### Task 2: The report sections

- [ ] Tests first (red), following the existing report tests (find them: `TelemetryAggregator`/`ReportTables` tests):
  from synthetic lines of two clients, a bug card gets the reporter's chat within 60 s after, every client's console
  lines from 20 s before to 5 s after, sorted by time and labelled; the Console section groups repeats with counts and
  first/last time and leaves plain `log` lines out; `console`/`bug`/`chat` are no longer counted as unknown.
- [ ] Implement P4 (data → `ScopePayload` → HTML/JS render functions following `renderMarkers`; the two CSVs).
- [ ] Build a report from a real folder (the latest in the Telemetry folder, or one Task 4 produces) and open the HTML
  file: the two sections render without a JavaScript error (the report has a `safeRun` wrapper - check its error list).
- [ ] Commit `feat(report): bug reports and each player's console`.

### Task 3: The zip and the Escape pop-up

- [ ] Smoke test first: a scratch script (outside `Assets/`: compile it through a throwaway file under `Assets/` that you
  delete in the same step, or check the assembly list) proving `System.IO.Compression.ZipFile.CreateFromDirectory` or
  `ZipArchive` compiles in this project. If neither does, stop and report.
- [ ] Tests first (red): the zip's file selection (from a folder listing: only `<actor>_*.jsonl` and `bug_<actor>_*.png`
  of this actor), its name; the Escape rule ("the shop or chat was open this frame or last frame → not mine").
- [ ] Implement P5 and P6 (+ the `UiTheme` texts: +5 YAML lines; the shared `GameQuit`, `QuitButton` calling it).
- [ ] Commit `feat(ui): the match log zip and the Escape pop-up`.

### Task 4: Check it (combined with the two-team lobby's two-client check)

One fresh development `Client2` build covers the lobby and these extras. In addition to the lobby's Task 4 checks: on
both A (Editor) and B (Client2), in a two-team match: (1) a `Debug.LogError("playtest-extras check")` by eval appears as
a `console` line in that client's `.jsonl`; (2) a bug mark from each (drive the key through `InputSystem.QueueStateEvent`
inside the game process, or call the component's public method and say which) writes a `bug` line and a `bug_*.png`
next to the log, and the toast shows (capture); (3) a chat message sent by B right after its mark appears as B's `chat`
line; (4) end the match (or force the result panel as the 2.7b checks did) → each client's zip exists with only its own
files, and the saved-log line shows (capture); (5) the report built from the shared folder shows both bug cards with the
screenshots, the notes, and both clients' console lines in the windows; (6) Escape on A with nothing open → the pop-up
(capture); No closes it; Escape with the shop open → the shop closes and no pop-up. Yes is checked last, on B only (it
quits B; confirm B's process ended and A saw B leave) - never Yes on the Editor (it would stop the check). Gates as usual.

### Task 5: Docs

`progress.md`, `assumptions-for-tudor.md` (a "For the 5 pm playtest" block at the top: how testers report a bug, where
their zip is, how to build the report), HANDOFF, `editor-queue.md`; push.
