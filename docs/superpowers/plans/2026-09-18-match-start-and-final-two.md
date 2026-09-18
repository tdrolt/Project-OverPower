# Match Start and the Final Two (2.7b): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to carry out this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal (Tudor, 2026-09-18 midday):**
- **A warm-up comes before every match.** Players can fight and capture, but nothing counts. The shop is a free sandbox.
- **A 5-second countdown starts** by itself once all three teams have a player. With exactly two teams, the host presses Start instead. "Match starts in 5" shows on every screen, then the match goes live.
- **Going live is a fresh start** for everyone: back to the empty starter kit, and the real economy begins.
- **A host-started match begins in the two-team phase.** The third capital is out of play.
- **The final two play to the end, with no draw.**
  - A team without a capital is out at once while the other team still holds one.
  - If both teams have lost their capitals, it becomes last man standing.
  - A team with no capital that takes any capital respawns there.
- **Shield hits count as combat.** Being shot while the Invulnerability shield is up keeps you in combat.

**Architecture:**
- **Pure rules, tested in edit mode:**
  - `MatchPhaseRules` is rewritten: warm-up, teams in the match, having any capital, last man standing, the no-draw rule, the respawn capital and the territory winner.
  - New `MatchStartRules`: when the countdown starts, when it is cancelled, when the match goes live, what the host may do, what is out of play, and where joiners may go.
  - New `HitVerdictRule`: the order of checks in the damage funnel.
  - `ShopRules.IsFree`: the one answer to "is the shop free right now?" (Free Loadout, or the warm-up sandbox).
  - Additions to `TerritoryMap`, `TerritorySnapshot`, `CaptureRingState`, `UltimateChargeState`, `PurchaseLedger` and `PhaseTimeline`.
- **Adapters:**
  - `MatchDirector` writes and reads the countdown and going-live state.
  - `BuildingManager` and `BuildingCapture` reset the territory.
  - Each owner resets their own player, run by `PlayerLifecycle.ResetForMatchStart`.
  - `RoomManager` places joiners.
  - `ShopPricing`, `LoadoutScreen` and `PlayerLoadout` ask `ShopRules.IsFree`, so the warm-up is a sandbox shop.
  - `MinimapView` and `CaptureRingView` grey out an out-of-play capital.
  - A new `MatchStartPanel` shows the warm-up line, the countdown and the host's button.
- **Networking:**
  - Two new Room Properties, `mTeams` and `mLiveAt`, written together by the master with check-and-set when the countdown starts. `mPhase` (existing) appearing is the moment the match is live.
  - One new Player Property, `lastStandAt`, written by the owner in the same call as `lastStand`: the server time of a last-stand death.
  - **No RPC added, renamed or removed.** No new `IPunObservable`.

**Tech stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, NUnit edit-mode tests, the `unity` CLI, and the two-client harness.

**Naming rule:** tasks are numbered 0 to 11, plus 5b. Reports, commits and `progress.md` call them **"2.7b step N"**.

**Provenance:**
- Written by a read-only opus planner. Every `file:line` was read at HEAD **`15a9125`**.
- The cleanup batch landed during planning:
  - `PlayerHealth` gained 22 lines, so `ApplyDamage` now runs from `:281` to `:346`.
  - `PlayerLifecycle` gained 8 lines after `:651`.
  - `Building capture.cs:693` now passes `CurrentOwners`.
- **Re-read before editing.** Line numbers drift.
- **Amended 2026-09-18 afternoon** with Tudor's answers below. Steps 0-2 are unchanged. Changed: the header, "What exists" E, Decisions 1-7, 10, 13, 15-18 and 20, new Decisions 21-23, the Open list, rule 13, the file map, steps 3, 4, 5, 8, 9, 10 and 11, a new step 5b, the risks and the totals.

---

## Tudor's answers to "Open for Tudor" (2026-09-18 afternoon) — these OVERRIDE the defaults below

1. **Three-team adoption: YES.** Grabbing an enemy's capital saves a team in the three-team part too (the default;
   `CountsAsHavingACapital` stays "any capital in play" in both phases).
2. **The fresh start resets Mobility and Equipment too: "back to empty".** Everyone returns to the empty starter kit
   at going live (Decision 6's "Kept" list loses the Mobility/Equipment picks). **Plus a new ask:** *"if you can also
   keep it so before the match start people can reset their upgrades and buy unlimited amounts so testers can
   experiment"* → **the warm-up is a sandbox shop:** before the match goes live, buying is free and unlimited and
   upgrades can be reset (as `GameplayConfig › Free Loadout` does today, but only during the warm-up); going live
   resets everything to the starter kit and the real economy starts. Plan it as its own small step: the shop reads
   "free" from `freeLoadout || !MatchDirector.IsLive`.
3. **A 5-second countdown before going live** (not instant). "Match starts in 5" on every screen, then the fresh
   start. The countdown's length is a designer value (`GameplayConfig` or `UiTheme`, tooltipped). Needs a
   replicated start time (the master writes the go-live moment as a server timestamp; every client counts down to
   it) — no RPC. The host's Start and the automatic three-team trigger both start the countdown; decide what happens
   if a team empties during the countdown (default: cancel it and return to the warm-up).
4. **The same-instant wipe: the last to die wins.** Replaces Decision 13's "most zones". Needs each player's death
   time (server timestamp) readable by the master — e.g. a death-time player property set by the owner, or the
   `lastStand` write carrying it. A true tie on the same server millisecond: lower team number.
5. Waiting-panel wording: not answered — leave the default (unchanged text).

---

## Tudor's words [T] (2026-09-18 midday; recorded at the top of `assumptions-for-tudor.md`)

1. **Going live:**
   - The match goes live **automatically once three teams have at least one player.**
   - With only two teams, **the host** gets a **Start** button. The match then starts **in the two-team phase**, with **the third capital out of play**: nobody can capture it, and it is greyed out on the minimap.
   - Going live is a **fresh start**: zones return to their starting owners, gold goes to 0, everyone goes back to their spawn, and respawn timers reset.
   - **Before going live:** fights and captures happen, but **nothing counts**.
2. **A team's last player leaves:**
   - **With three teams left,** that team is out only once its capital falls.
   - **With two teams left,** it stays in while it holds its capital, and is out at once if the capital is (or already was) captured.
   - Once live, an emptied team is **still in the match**.
3. **The final two:**
   - A team with no capital while the other team holds one is **out at once**.
   - If both teams have lost their capitals, **when the phase becomes two teams or at any later point**, it is **last man standing, with no draw**:
     - The dead can't respawn.
     - A team whose members are all dead loses, even if the winners hold no capital.
   - A capital-less team that captures **any** capital respawns there. The other team, still without one, is then out at once.
   - **A team holding a second capital still respawns at its own capital.**
   - "Having a capital" means holding **any capital in play**.
4. **The Invulnerability shield counts as combat.**
   - Hits the armed trap nullifies, and hits blocked during the 4 s immunity, **reset the victim's out-of-combat clock**.
   - A self-hit or a teammate's hit never does.

**Leftovers from the last 2.7 review, folded in:**
- A pure test for the knockout cascade.
- `OnLeftRoom` removes the gold key instead of writing 0.
- The "lone player can't be knocked out" wording.
- `RoomManager`'s wrong reason for keeping the loadout.
- The stale already-neutral comment in `MatchDirector`.
- A test that really exercises the already-neutral guard.
- The old QUESTION lines in the assumptions file.

---

## What exists (verified at `15a9125`)

### A. The rules: `Assets/scripts/Match/Rules/MatchPhaseRules.cs`
- **`MatchPhase` (`:5-12`)** has three values: `ThreeTeams = 1`, `TwoTeams = 2`, `Over = 3`. There is **no warm-up**.
- **`TeamStatus` (`:14-23`)** has `Members`, `MembersOutForLastStand` and `HoldsItsCapital`, the team's **own** capital only.
- **`Recompute` (`:54-86`)** is a fixpoint that skips `Members <= 0` teams. A team counts as "in" only while someone is connected.
- **`PhaseFor` (`:88-99`)** returns ThreeTeams while there are no eliminations. Otherwise it counts teams that have members.
  - As a result, a two-team match can never *start* in TwoTeams, and an emptied team drops out of the count.
- **`IsLastStandDeath(bool)` (`:105`)** and **`TerritoryWinCounts(int)` (`:111`)**, the "lone player" guard.
- **The class comment (`:39-50`)** still says "a lone player can never win" and "adoption was cut".
- **The 2.7 fixpoint produces a draw.** When both remaining teams are capital-less at the first knockout, both are swept. The assumptions file lists this as a QUESTION.

### B. The director: `Assets/scripts/Match/MatchDirector.cs`
- **Keys (`:36-40`):** `mPhase`, `mElim`, `mWin`. `IsEliminated` reads the room directly (`:73-74`) because `RoomManager.OnJoinedRoom` runs before this class's own callback.
- **`CapitalOf` (`:81-83`)** is the static capital, with a comment calling it "the adoption hook".
- **Triggers (`:107-129`):** ownership change, the `lastStand`/`teamID` Player Properties, a player leaving, a master switch.
- **`OnRoomPropertiesUpdate` (`:131-145`).**
- **`OnLeftRoom` reset (`:156-166`).**
- **`MasterRecompute` (`:223-276`):**
  - It is refused once Over.
  - It moves the Tier-3 zones on `ThreeTeams → TwoTeams` (`:270-271`).
  - It closes the room at Over (`:272-273`).
- **`NeutraliseTierThreeZones` (`:278-291`).** The comment at `:282-284` says "Already a no-op on an already-neutral zone". **That is stale:** since round 2 it also wipes history on an already-neutral zone.
- **`LogTelemetry` (`:307-326`)** logs `teamsRemaining` for `Members > 0` teams.
- **`BuildTeamStatuses` (`:328-356`).**
- **`ReactToRoomState` (`:370-406`)** handles the lost panel, the three-to-two edge (home plus banner) and the result panel.
- **Creation:** `BuildingManager.Awake` creates the director at runtime (`BuildingManager.cs:323-324`). It has **no serialized fields**, so any look value must come from a component that has the theme.

### C. The player lifecycle: `Assets/scripts/Player/PlayerLifecycle.cs`
- **`PlayerDied` (`:243-292`):**
  - `capitalHeld` checks the **own** capital (`:254`).
  - Last stand leads to the waiting panel, `lastStand=true` and the retired master RPC (`:256-275`).
  - The countdown runs only `if (capitalHeld && !respawnStarted)` (`:277`).
- **`CheckForCathedralCapture` (`:298-340`)** respawns a waiting player only when the **own** capital is back (`:322`).
- **`TryGetOwnCathedral` (`:348-361`)** goes through `MatchDirector.CapitalOf`.
- **`NextRespawnDelay` / `deathCount` (`:369-378`).**
- **`RespawnPlayer` (`:380-447`):**
  - `ChooseSpawnPoint` runs at `:405-409`.
  - The coroutine is started from two places, and **no handle is kept**, so today nothing can stop it.
- **`ReturnToSpawn`, `ReturnToSpawnForPhaseChange` and `MoveToSpawnPoint` (`:452-478`)** use `teamSpawnPoints[pt.teamID]`.
- **`ChooseSpawnPoint` (`:484-513`)** uses the static `Map.CapitalOf` and `capitalUnderAttackSpawnPoints[teamID]`.
- **`UpdateRespawnNote` (`:522-538`).**
- **`SetAlive` / `SetLastStandOut` (`:593-612`).**

### D. Territory and towers
- **`TerritoryMap.cs`:** `CapitalOf` (`:59-64`). `MayCapture` (`:76-89`), where **your own capital is always capturable** (`:82-83`). A cut capital must be refused *before* that exception.
- **`TerritorySnapshot.cs`:** `WithCapture`, `WithNeutral` and `WithNeutralReset` (`:57-97`). There is no "starting snapshot" builder.
- **`BuildingManager.cs`:**
  - `SetNeutralWithoutBountyHistory` has its guard at `:618`.
  - `Write` (`:646-660`) is followed by `lastWritten`/`writesAwaitingEcho`.
  - `WriteInitialSnapshotWhenClockIsReady` builds the start inline (`:697-699`).
  - `OnMasterClientSwitched` resets every tower (`:437-442`).
  - `CheckTerritoryWin` (`:848-882`) has the "lone player" comment and `TerritoryWinCounts` at `:869-873`.
  - `CountTeamsWithPlayers` (`:884-895`).
- **`Building capture.cs`:**
  - `RefreshRingView` (`:231-243`).
  - `RepublishProgressNow` (`:309-320`).
  - `ZoneUnderAttack` is a cached delegate (`:324-325`).
  - `TeamMayCaptureNow` (`:344-358`).
  - `ResetToOwner` is private (`:644-654`).
  - `OnTriggerEnter` checks `MayCapture` at `:693`.
- **`ZonePresenceTracker.IsUnderAttack` (`:93-102`)** is **always false for a neutral zone.** A neutral cut capital therefore needs no change there.

### E. Economy and loadout
- **`GoldWallet.Start` (`:111-152`)** reads an **existing** `gold` key as the balance (`:121-124`).
  - `RoomManager.OnLeftRoom` writes `gold = 0` (`RoomManager.cs:91`), and a written 0 beats `TerritoryConfig.StartingGold`. That is the leftover to fix.
  - The comment's reason for keeping the loadout (`:81-84`) is wrong: `PlayerLoadout.Start` republishes the whole starting kit (`PlayerLoadout.cs:80-111`).
- **`TerritoryConfig.startingGold = 0`** (`TerritoryConfig.cs:57`, asset line 34). This is the one home for "gold at the start".
- **`PlayerLoadout.Start` (`:74-119`)** publishes the starting kit. The ultimate starts empty unless Free Loadout is on (`:93`).
- **`PurchaseLedger` (`ShopRules.cs:53`)** is the owner-local refund memory held by `LoadoutScreen` (`LoadoutScreen.cs:84`).
- **`UltimateChargeState`** has no reset.
- **`AbilityRunner.ResetCooldowns` is private** (`:512`). The death and respawn cleanup is in `HandleAliveChanged` (`:473-484`).
- **`NetworkedDeployable`** (`Abilities/Core/NetworkedDeployable.cs`) has `IsOwnerClient` (`:107`) and `RequestDestroy` (`:126`). Subclasses: mines, cover walls, portals, fences, AoE zones.
- **Every place `GameplayConfig › Free Loadout` is read** (`GameplayConfig.cs:125-126`, asset `freeLoadout: 1`):
  - `ShopPricing.Build` (`UI/ShopPricing.cs:83`) folds it into `ShopContext.FreeLoadout`. **Every shop decision reads that one field:** the header (`LoadoutScreen.cs:579-591`), weapon buy (`:673`), weapon reset (`:704`), armour buy (`:812`), armour reset (`:850`), ability buy (`:928`), and `ShopContext.Check`/`StatusText` (`ShopPricing.cs:43-45`, `:71`).
  - While free, a purchase spends no gold, records nothing in the ledger, and ignores the territory and out-of-combat gates. A reset refunds nothing.
  - `PlayerLoadout.Start` (`:93`) decides whether the prefab's starting ultimate is handed out.
  - **`ShopRules.AbilityPrice` (the free first pick into an empty Mobility/Equipment slot) does not read it.** That is a separate real-economy rule and stays as it is.
  - Telemetry only reads the tuning snapshot's `freeLoadout` and each `purchase` line's `free` flag (`TelemetryAggregator.cs:263`, `:291`). The purchase check is already window-scoped, so warm-up purchases never reach the match tables.
- **The shop's existing "reset" buttons:** Reset Weapon (`OnResetWeaponClicked`, back to the tree root) and Reset Armor (`OnResetArmorClicked`, levels 0/0). Abilities have no sell-back; picking another card replaces the slot.
- **`GameplayConfig.asset`** lists fields in declaration order (`respawnMaxSeconds: 10` at line 19). The director has no Inspector fields; the master's own player's `PlayerLifecycle` holds a `GameplayConfig` reference (`:47`).

### F. What players see
- **`MinimapView`:**
  - `SetZoneShown` (`:217-227`) **hides** a bubble and its links. It has no caller.
  - `RecolourOwnership` (`:584-594`), `ApplyLinkStyle` (`:600-632`), `UpdateZones` (`:634+`).
  - **The neutral fill is already grey** (`UiTheme.minimapNeutralColor`, `:503`). A "greyed" out-of-play capital therefore needs a **different** look.
- **`CaptureRingState.From` (`:49-81`)** is pure. `CaptureRingView.Refresh` picks the edge colour at `:103`.
- **`PlayerHud`:**
  - `ShowToast` (`:342`) and `ShowTwoTeamsLeftBanner` (`:355`).
  - The toast sits **top-centre**, unscaled (`:918-938`).
  - The HUD canvas has **no GraphicRaycaster**. Clickable buttons live on their own canvas, as the recipe in `LoadoutScreen.Builder.cs:502-548` shows.

### G. The shield and the damage funnel: `PlayerHealth.ApplyDamage` (`:281-346`)
The funnel checks, in order:
1. `IsMine`/`isDead` (`:285`).
2. **`IsInvulnerable` returns early (`:289-290`), before the self and friendly checks.**
3. Self (`:295-303`).
4. Friendly (`:307-315`).
5. The armed-trap veto (`:323-324`).
6. Resolve.
7. **`secondsSinceCombat = 0f` (`:331`)**, reached only by hits that land.

The review was right: moving one line is not enough.

### H. Telemetry
- **`MatchTelemetry`** logs a **phase 1 anchor** when the master claims the match identity (`:430`). `LogElimination` and `LogPhase` are master-only (`:605-627`).
- **`PhaseTimeline.From` (`Editor/Telemetry/PhaseTimeline.cs:65-102`)** splits Phase 1 `[0, t)` and Phase 2 at the first `phase ≥ 2`, with an elimination fallback.
- **`HtmlReportWriter.cs:122-126`** assumes **Phase 1 starts at 0**: it takes its length as the transition time.
- **`TelemetryLog.KnownEventNames` (`:96-110`)** means a new event type must be registered there.

---

## Decisions [C]

1. **Three states, three Room Property facts** (Tudor answer 3):
   - **Warm-up:** no `mTeams`.
   - **Counting down:** `mTeams` (`int[]`, the teams in the match, ascending) and `mLiveAt` (`int`, the server ms the match goes live) are present, `mPhase` is not.
     - The master writes both **in one call, with Photon's check-and-set on the absent `mTeams`**, as `MatchTelemetry`'s identity claim does. Two masters racing across a switch cannot both start a countdown.
     - `mLiveAt = now + GameplayConfig › Match Start Countdown Seconds`, computed wrap-safe.
     - A late joiner reads both with the room, and every client counts down to the same server time. **No RPC.**
   - **Live:** `mPhase` is present. The master writes it, with `mTeams`, `mElim = []` and `mWin = -1`, **when its own server clock reaches `mLiveAt`**, with check-and-set on the absent `mPhase`, so the match can go live once only.
   - `mTeams` can be removed only by a cancel while counting down (Decision 22). Once live, it never changes. A decided match closes its room, so nothing is ever rewritten after that.
   - It all lives on the room, so it survives a master switch: the new master sees the timestamp and carries on.
2. **`MatchPhase.Warmup = 0` is derived, not stored.** `ReadPhase` returns `Warmup` whenever `mPhase` is absent, **countdown included**. `MatchStartRules.StartStateFor` gives `Warmup`/`CountingDown`/`Live` for the UI and the joiners.
3. **Nothing counts before live, and the countdown is still warm-up.**
   - `MasterRecompute` and `CheckTerritoryWin` gate on the room's **echoed** `mPhase`, never on a pending write or on a client's own clock.
   - A warm-up death (countdown included) is always an ordinary respawn, with no waiting panel. The last stand exists only to decide a knockout.
   - The shop stays a free sandbox until live (Decision 21).
4. **The teams in the match are fixed when the countdown starts:** the teams with players at that moment.
   - A team that later empties **during the countdown** cancels it (Decision 22). One that empties **after live** stays in (rule 2).
   - The third team of a host start is never in.
   - **A player who joins during the countdown** goes only to a team in `mTeams`. For a three-team countdown that is any team; for a host start it is never the left-out team (`MatchStartRules.MayJoin(teamsFixed, …)`). They get the same fresh start as everyone at live, which changes nothing for someone who just spawned.
   - After live, joiners go only to teams in the match and not knocked out.
5. **The fresh start and the knockout gate take effect at the timestamp, not at the countdown write**, in this order (`MatchDirector.GoLive`; the master does it all in one frame, the frame its clock reaches `mLiveAt`):
   1. `BuildingManager.ResetForMatchStart(teams)` writes a **whole new snapshot, built from scratch rather than from the write basis.** Every tower's local capture, drain and cooldown goes back to its new owner, and its progress is republished.
      - A warm-up capture already in flight, even one whose own write is still echoing, is thrown away.
      - No warm-up progress can complete after this line.
   2. The `mPhase`/`mTeams`/`mElim`/`mWin` write.

   Photon delivers one client's writes in order, so **every client applies the territory reset before it sees the match go live.** On that second echo, each client resets its own player (`PlayerLifecycle.ResetForMatchStart`).
   - **Why the master's write, and not each client's own clock, is the moment:** a client resetting itself when its own clock crossed `mLiveAt` could run before a last-moment cancel or a still-echoing warm-up capture (and its bounty) reached it. The master's write keeps "territory first, then live" in one order for everyone. The countdown on screen reaches 1 on each client's own synced clock; the reset follows within one network trip.
   - **Gold:** the wallet reset runs after the reset territory is already applied, so no warm-up income or bounty lands afterwards. The start snapshot pays a bounty of 0 on every zone.
   - **A player mid-respawn:** the respawn coroutine is stopped before the teleport, so it cannot move the player again later.
   - **Callback order:** the director registers before `BuildingManager`, because it is added in `Awake`. Nothing here relies on the order of two classes within **one** event.
6. **The fresh start resets and keeps** the following. The list has one home, the doc comment on `PlayerLifecycle.ResetForMatchStart`.

   **Reset:**
   - Territory, towers and capture progress (the master).
   - Gold goes to `TerritoryConfig.StartingGold`, which is 0 today. Tudor's "gold to 0" is that value.
   - The purchase ledger, so "Reset weapon" cannot refund warm-up spending.
   - **The whole loadout goes back to the starter kit** (Tudor answer 2): the starting weapon, and **all three ability slots** (Mobility, Equipment, Ultimate) to `PlayerLoadout.StartingAbilityId`. The prefab ships all three empty, so this is "back to empty". The first pick into an empty Mobility or Equipment slot is then free again, as for any new player.
   - Armour upgrade levels go to 0/0.
   - The ultimate meter goes to 0.
   - Overheat goes to 0.
   - Ability cooldowns are ready and channels are interrupted.
   - Full health and armour; status effects cleared, including an armed shield; the combat clock goes to 0.
   - `deathCount` goes to 0.
   - A respawn countdown or wait is cancelled; the player is alive with `lastStand = false`.
   - Any move in flight is cancelled and the player is placed at their own team's spawn.
   - The player's own deployables are destroyed.
   - The loadout screen closes.

   **Kept:**
   - The team and the name.
   - The Photon score (nothing reads it).
   - Fire fields and projectiles already in flight. They last for seconds.
7. **Fixing the teams only restricts joiners for a host start.** With two teams, the master also lowers the room's `MaxPlayers` to 6 when it starts the countdown (and puts it back to 9 on a cancel). A seventh random joiner then makes a fresh room instead of being turned away by `PickSmallestTeam`.
8. **The cut capital:**
   - It is **neutral with no history** in the start snapshot.
   - `TerritoryMap.MayCapture` refuses it for **everyone**, checked before the own-capital exception, in both `BuildingCapture` call sites.
   - It pays nothing: it earns no income because it is neutral, and pays no bounty because nobody can capture it.
   - It is never "under attack", because it is neutral.
   - On the ground, its ring edge and its minimap bubble use one new `UiTheme.outOfPlayZoneColor`: dark and see-through, so it doesn't read as ordinary neutral grey.
   - Its minimap links are **hidden**. No route leads into it, and a grey line would read as a way in.
   - `SetZoneShown` stays as it is, an unused hook that hides a zone entirely.
9. **A knocked-out team's capital stays in play.** Tudor names "the eliminated team's" capital as one you can adopt. This answers the 2.7 QUESTION ([T], implied).
10. **"Having a capital" means holding any capital in play, in both phases.** [T] Tudor answer 1: yes, adoption saves a team in the three-team part too (GDD p.20's last-stand adoption). One switch controls it: `MatchPhaseRules.CountsAsHavingACapital`.
11. **The respawn capital:**
    - Your own capital while you hold it.
    - Otherwise, the in-play capital your team has **held longest**, measured with wrap-safe `HeldSinceMs`. That is the one it adopted first.
    - This is derived from the snapshot, so it needs **no new key**. A new master and a late joiner compute the same answer.
    - The spawn point is `teamSpawnPoints[Map.CapitalTeamOf(capital)]`, and the under-attack rule is applied to that capital.
12. **When a respawn countdown ends while the team has no capital:**
    - **Three teams:** the player still comes back at home (unchanged). GDD p.20's last stand counts only deaths after the fall.
    - **Two teams (last man standing):** the player waits instead. Tudor: "the dead can't respawn".
    - A knocked-out team never respawns. This also fixes a latent 2.7 case: a player on a countdown when their team was knocked out in the final two used to respawn frozen.
13. **No draw: the last team to die wins a same-instant wipe** ([T] Tudor answer 4). If every team still in would go out in the same recompute, the team whose last player went out **latest** stays in and wins.
    - The time is each team's latest `lastStandAt` stamp (Decision 23), compared wrap-safe.
    - A team with no stamp (it emptied; nobody died) counts as out before any team that has one.
    - A true tie on the same server millisecond goes to the lower team number.
    - This makes the rule total: no stuck state and no winner-less end.
    - In practice the master usually hears the two deaths one event apart and decides on the first one, which is the same "first to die loses" answer. The stamps decide when one recompute sees both: two deaths in the same network batch, or a new master reading a room where both teams are already wiped.
14. **The territory win is replaced by `TerritoryWinner(live, owners of in-play capitals)`.**
    - The old "two teams with players" guard would wrongly stop a survivor whose opponents left from winning. Rule 2 keeps emptied teams in the match.
    - Once live, two teams were in by construction.
15. **The host is the Photon master.**
    - The Start button exists only on the master's screen.
    - Its `onClick` calls `MatchDirector.HostStartMatch()` **locally**, which starts the countdown. The presser *is* the master, so no RPC is needed.
    - If mastership moved between drawing the button and the click, the guard refuses and the button hides on the next frame.
    - If the master leaves during the warm-up, the next master gets the button.
16. **The master polls instead of relying only on callbacks:** every 0.5 s in the warm-up (to start a countdown), and **every frame while counting down** (to cancel, or to go live on the frame its clock reaches `mLiveAt`).
    - 0.5 s is not a gameplay value, like `ZonePresenceTracker.MeasureIntervalSeconds`.
    - A missed callback, an unsynced clock or an unread snapshot could otherwise strand a full room in the warm-up.
    - A new master mid-countdown needs nothing extra: its own poll sees `mLiveAt` and carries on. If the moment passed during the switch, it goes live on its first frame.
17. **The joiner race is closed on both sides:**
    - The host cannot start while anyone in the room has no team yet: they might be the third team.
    - A joiner the server placed before the countdown write, but whose team is not in `mTeams`, re-picks **as soon as it sees the teams fixed** (`RoomManager.EnsureLocalTeamInMatch`, called again on the live edge, idempotent). It plays the countdown on its real team.
18. **Telemetry:**
    - `phase 0` is the new warm-up anchor. The countdown is part of the warm-up.
    - Going live logs `phase 1` (three teams) or `phase 2` (a host start), in the master's live write at the timestamp.
    - A new `adopt` event is logged.
    - The report's windows start at going live, the warm-up is left out, and the header shows how long the warm-up lasted.
    - Legacy logs, which carry a phase-1 anchor, are read exactly as today.
19. **Shield combat order** (`HitVerdictRule`): self, then teammate, then already immune, then the armed trap, then the hit lands.
    - `Shielded` and `Lands` reset the clock. Self and teammate hits never do, and never ask the trap.
    - The attacker's clock is unchanged. A shot into a shield credits no damage, the same as any miss.
20. **Assets:** no scene change and no prefab change. Two assets change, each field with a tooltip:
    - `UiTheme`: 12 new fields.
    - `GameplayConfig`: 1 new field, `matchStartCountdownSeconds = 5`.
    - Both are added to their `.asset` **by a targeted YAML edit, one line per field**, never `SetDirty`+save (HANDOFF trap 8c: a save back-fills unrelated stale lines).
21. **The warm-up is a sandbox shop** ([T] Tudor answer 2).
    - **One home for "is the shop free right now?":** `ShopRules.IsFree(freeLoadout, matchLive) => freeLoadout || !matchLive`, pure and tested.
    - One runtime gatherer, `ShopPricing.IsFreeNow(GameplayConfig)`, feeds it the setting (missing config fails open to free, as today) and `MatchDirector.Instance.IsLive` (a missing director reads as warm-up, so also free).
    - Its two readers:
      - `ShopPricing.Build` puts it in `ShopContext`, whose field is **renamed `FreeLoadout` → `IsFree`** so the name stops lying. Every shop decision already reads that one field, so every purchase, reset and header follows with no per-handler change.
      - `PlayerLoadout.StartingAbilityId`: whether the prefab's starting ultimate is handed out.
    - **"Buy unlimited amounts"** is what a free shop already does: no gold spent, no gate (anywhere, in combat), nothing recorded in the ledger.
    - **"Reset their upgrades"** maps to the existing **Reset Weapon** (back to the tree root) and **Reset Armor** (levels 0/0) buttons. While the shop is free they work anywhere, with no gate and no refund. Abilities have no sell-back; picking another card swaps the slot.
    - The header reads "Free (warm-up)" in the warm-up and "Free (test mode)" with Free Loadout on. The reason joins the header's change check so it re-draws when the match goes live.
    - `ShopRules.AbilityPrice`'s free first pick is untouched: it is the real economy's rule and never read Free Loadout.
    - At live, the fresh start empties the loadout (Decision 6) and the real economy begins. Free Loadout on still makes the whole match free, as today.
22. **The countdown** ([T] Tudor answer 3).
    - **Length:** `GameplayConfig › Match Start Countdown Seconds`, default 5. It is gameplay timing, so it belongs with the respawn numbers.
      - Only the master reads it, when it starts the countdown. The director has no Inspector fields, so it asks the master's own player (`PlayerLifecycle.Config`, a new getter over the reference it already holds). If that player isn't spawned yet, the start waits for the next poll.
      - 0 means "no countdown": live on the master's next frame.
    - **Both triggers start it:** three teams present, or the host's Start.
    - **What shows:** "Match starts in N" on every screen, from each client's own synced server clock (`MatchStartRules.CountdownSecondsShown`). The Start button hides. The shop stays free.
    - **A team in `mTeams` empties during the countdown:** the master cancels it (`mTeams` and `mLiveAt` removed, with check-and-set expecting `mPhase` still absent) and the room returns to the warm-up. A host start then shows its button again if two teams remain.
    - **The host leaves during the countdown:** if their team still has a player, the countdown **continues**, because the new master sees `mLiveAt`. If the host was their team's only player, that team is now empty and the cancel rule applies.
    - **A third team arriving during a host-start countdown** doesn't restart it as a three-team match: the teams were fixed, so the joiner goes to team 0 or 1.
23. **The death time for the no-draw rule is a new Player Property, `PlayerLifecycle.LastStandAtKey = "lastStandAt"`.**
    - The owner writes it **in the same call as `lastStand = true`**: `PhotonNetwork.ServerTimestamp` at the last-stand death. The same call clears it (null) with `lastStand = false`.
    - A sibling key rather than changing `lastStand`'s type: every existing reader keeps working unchanged. A missed reader of a retyped `lastStand` would silently stop every knockout.
    - The master reads each team's latest stamp into `TeamStatus.LastOutAtMs`. A new master and every client read the same properties, so they reach the same answer.
    - `RoomManager.OnLeftRoom` clears it with the other match keys.

---

## Where the code or the GDD differ from the brief

1. **"The host" is the Photon master.** The assumptions file says "the first player in the room". That is the master until they leave; then the next master inherits the button.
2. **"Greyed out on the minimap" can't reuse the existing hook.** `SetZoneShown` *hides* the bubble, and neutral is already grey. The plan adds a distinct "out of play" look and hides the capital's links (Decision 8).
3. **The shield isn't a one-line change**, as the review said. The `IsInvulnerable` return precedes both the self and friendly checks and the clock reset. The new order keeps self and teammate hits out.
4. **In the GDD, adoption belongs to the three-team last stand** (p.20). Tudor's words put it in the final two. [T] It is built in both.
5. **Tudor's adoption rule settles a 2.7 question.** "The eliminated team's capital" can be adopted, which answers the QUESTION "does a knocked-out team's lane leave play?": it stays in play.

---

## Open for Tudor

Items 1-4 were answered on 2026-09-18 afternoon (see the answers block at the top) and are built as Decisions 10, 6 + 21, 22 and 13.

5. **Waiting-panel wording** (unanswered, so the default stands). In last man standing, the waiting panel still says to wait for your capital to be retaken, but now *any* capital works.
   - **Default:** leave the text. It's a prefab label.
   - Say if you want new wording.
6. **New, from the countdown (default built, low stakes):**
   - A third team's player arriving during a **host-start** countdown joins team 0 or 1; the match stays two-team. The alternative, cancelling and restarting as three-team, would let one late joiner reset a countdown the host already started.
   - The host leaving during the countdown cancels it only if they were their team's only player (Decision 22).

---

## Rules for every task

These are the same as `2026-09-18-invulnerability-and-raybeam.md` "Rules for every task", with these changes:

1. **Before starting:** read `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\HANDOFF.md` §5.
2. **Branch:** `limit-testing` only.
3. **Editor:** `unity command editor_status` must answer first. **There is one Editor**: if another agent is driving it, stop and ask the controller. Never `editor_focus`.
4. **Recompiling and tests:** never edit `.cs` files, recompile, run tests or build in Play Mode.
   - After `unity command recompile`, poll `recompile_status`; it is the only compile truth.
   - Run tests async only: `run_tests -- --mode editor --async_tests true`, then poll `test_status`.
5. **The dirty-scene check:** read `SceneManager.GetActiveScene().isDirty` on its own, not chained with `&&`. Continue only on `False`.
6. **After Play Mode:** run `git diff --stat -- "Assets/Scenes/Game Scene.unity"` and expect it empty.
7. **Moving and aiming:** move players only with `PlayerDisplacement.TeleportTo`. Aim only with `PlayerAim.SetAimOverride`.
8. **Before any measurement,** list the room's actors.
9. **SCRATCH** means the session scratchpad, subfolder `match-start`. All scripts, recordings and captures go there, never under `Assets/`.
10. **Designer values:** every designer-facing value lives on an asset with a plain `[Tooltip]`, in exactly one home. Comments say *why*, for a designer reader.
11. **Commits:**
    - Stage only the files the task lists, and check `git status --short` first. Other agents commit on this branch.
    - Use the session attribution line.
    - Put no unmeasured number in a message.
12. **Assumptions:**
    - Append `[C]` lines under a new `## Match start and the final two (2.7b, 2026-09-18)` heading at the end of `assumptions-for-tudor.md`. The file is outside the repo: never commit it.
    - Then list its `## ` headings to confirm none was lost.
13. **`UiTheme.asset` and `GameplayConfig.asset`:** add only the new fields, one line each, by a YAML edit (trap 8c), at the position of the field's declaration. Quote strings that contain `:`. `git diff` on each asset must show exactly the added lines.
    - **In Play Mode, a `ScriptableObject` changed by reflection stays changed in the Editor** (and can be saved to disk by a later asset save). Any check that flips `Free Loadout` by reflection restores it before leaving Play Mode, then confirms `git diff -- Assets/Gameplay/Config/GameplayConfig.asset` is empty.
14. **Networking:**
    - `git diff BASE -- Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` must show **no RpcList change** in any task.
    - `PlayerNetSync` stays the only `IPunObservable`.
15. **Not playtest-ready until the end.** Between steps 3 and 11 the game is not ready for Tudor; the interim rules are noted per task. Build no playtest until step 11 passes.

---

## File map

| File | Responsibility | Step |
|---|---|---|
| `Assets/scripts/Combat/HitVerdictRule.cs` (create) | `IArmedShield`, `HitVerdict`, `Classify`, `CountsAsCombat` | 1 |
| `Assets/Tests/HitVerdictRuleTests.cs` (create) | The funnel order and the combat clock | 1 |
| `Assets/scripts/Player/PlayerHealth.cs` | `ApplyDamage` goes through `HitVerdictRule` | 1 |
| `Assets/scripts/Player/PlayerStatusEffects.cs` | Implements `IArmedShield` | 1 |
| `Assets/scripts/RoomManager.cs` | Gold key removed on leave; true comment | 2 |
| `Assets/scripts/RoomManager.cs` | `lastStandAt` cleared on leave | 3 |
| `Assets/scripts/RoomManager.cs` | `MayJoinTeam` in `PickSmallestTeam`; `EnsureLocalTeamInMatch` | 5 |
| `Assets/scripts/Match/Rules/TerritorySnapshot.cs` | `NeedsNeutralReset` | 2 |
| `Assets/scripts/Match/Rules/TerritorySnapshot.cs` | `Starting` | 3 |
| `Assets/scripts/BuildingManager.cs` | Guard uses `NeedsNeutralReset` | 2 |
| `Assets/scripts/BuildingManager.cs` | `Starting` for the initial write; interim territory win | 3 |
| `Assets/scripts/BuildingManager.cs` | `ResetForMatchStart`; live-gated `TerritoryWinner` | 5 |
| `Assets/scripts/Match/MatchDirector.cs` (plus `MatchDirector.Live.cs` partial, create) | Step 2: comment | 2 |
| same | Step 3: interim statuses | 3 |
| same | Step 5: keys, countdown (start, cancel), going live at the timestamp, gates, react, queries | 5 |
| same | Step 7: respawn queries | 7 |
| same | Step 8: live toast | 8 |
| same | Step 9: telemetry calls | 9 |
| `Assets/Tests/TerritorySnapshotTests.cs` | +3 tests in step 2, +2 in step 3 | 2, 3 |
| `Assets/scripts/Match/Rules/MatchPhaseRules.cs` (rewrite) | Warm-up, teams in the match, any capital, last man standing, no draw (last to die), respawn capital, territory winner, adoption | 3 |
| `Assets/Tests/MatchPhaseRulesTests.cs` (rewrite) | 34 tests | 3 |
| `Assets/scripts/Match/Rules/MatchStartRules.cs` (create) | Countdown start, cancel, moment, display; host start; out of play; joiners; warm-up message | 3 |
| `Assets/Tests/MatchStartRulesTests.cs` (create) | 11 tests | 3 |
| `Assets/scripts/Match/Rules/TerritoryMap.cs` | `CapitalTeamOf`, `Capitals`, the out-of-play `MayCapture` overload | 3 |
| `Assets/Tests/TerritoryMapTests.cs` | +4 tests | 3 |
| `Assets/scripts/Player/PlayerLifecycle.cs` | Step 3: interim `IsLastStandDeath`; `lastStandAt` stamp in `SetLastStandOut` | 3 |
| same | Step 4: `ResetForMatchStart`, respawn handle | 4 |
| same | Step 5: warm-up death rule; `Config` getter | 5 |
| same | Step 7: respawn capital, adoption, wait conversion | 7 |
| `Assets/scripts/Player/PlayerLoadout.cs`, `Match/GoldWallet.cs`, `Player/UltimateCharge.cs`, `Combat/UltimateChargeState.cs`, `Match/Rules/ShopRules.cs`, `UI/LoadoutScreen.cs`, `Player/AbilityRunner.cs`, `Abilities/Core/NetworkedDeployable.cs` | Owner-side `ResetForMatchStart` pieces | 4 |
| `Assets/Tests/UltimateChargeStateTests.cs`, `Assets/Tests/ShopRulesTests.cs` | +1 test each | 4 |
| `Assets/scripts/Data/GameplayConfig.cs`, `Assets/Gameplay/Config/GameplayConfig.asset` | `matchStartCountdownSeconds` (one YAML line) | 5 |
| `Assets/scripts/Player/Building capture.cs` | Step 5: `ResetForMatchStart` | 5 |
| `Assets/scripts/Match/Rules/ShopRules.cs`, `UI/ShopPricing.cs`, `UI/LoadoutScreen.cs`, `Player/PlayerLoadout.cs` | The warm-up sandbox shop: `IsFree`, `IsFreeNow`, `ShopContext.IsFree` rename, header reason, starting ultimate | 5b |
| `Assets/Tests/ShopRulesTests.cs` | +1 test | 5b |
| same | Step 6: out-of-play checks and ring | 6 |
| `Assets/scripts/Match/Rules/CaptureRingState.cs`, `Match/CaptureRingView.cs`, `UI/MinimapView.cs` | The out-of-play look | 6 |
| `Assets/Tests/CaptureRingStateTests.cs` | +1 test | 6 |
| `Assets/scripts/UI/UiTheme.cs`, `Assets/.../UiTheme.asset` | 1 field (step 6), then 11 fields (step 8) | 6, 8 |
| `Assets/scripts/UI/MatchStartPanel.cs` (create), `UI/PlayerHud.cs` | Warm-up line, countdown, host Start button, live toast | 8 |
| `Assets/scripts/Telemetry/TelemetryKeys.cs`, `Telemetry/MatchTelemetry.cs`, `Editor/Telemetry/TelemetryLog.cs`, `Editor/Telemetry/PhaseTimeline.cs`, `Editor/Telemetry/TelemetryAggregator.cs`, `Editor/Telemetry/ReportTables.cs`, `Editor/Telemetry/HtmlReportWriter.cs` | Warm-up anchor, live and `adopt` lines, report windows | 9 |
| `Assets/Tests/PhaseTimelineTests.cs`, `Assets/Tests/TelemetryPhaseKeysTests.cs`, one aggregator test | +5, +1 (and one reshaped), +1 | 9 |
| `docs/superpowers/specs/2026-09-16-telemetry-design.md` | Part 3: a warm-up note | 9 |
| `Resources/loops/Limit Test/assumptions-for-tudor.md`, `progress.md` (not committed) | Answered QUESTIONs; progress row | 10 |

---

### Step 0: start state

- [ ] **1. Check the Editor and the tree.**
  - `editor_status` answers with `playMode: "stopped"` and `compiling: false`. If another agent is driving the Editor, **stop and ask**.
  - The dirty-scene check reads `False`.
  - `git status --short` must be clean. If it isn't, stop and report every file.
- [ ] **2. Record the baselines.**
  - `git rev-parse HEAD` is **BASE**.
  - Save `git show BASE:Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset | grep -A400 RpcList` to `SCRATCH/rpclist-base.txt`.
  - Run the tests and record the passing count as **BASE TESTS** (974 at `68c25a1`, plus the cleanup batch's). If anything fails, stop.

---

### Step 1: shield hits count as combat (Tudor rule 4)

**Files:** `HitVerdictRule.cs` (create), `HitVerdictRuleTests.cs` (create), `PlayerHealth.cs`, `PlayerStatusEffects.cs`.

- [ ] **1. Red.** Write the tests. They must fail to compile with `CS0246 ... 'IArmedShield'`. Report that exact message.

```csharp
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Tudor, 2026-09-18: being shot while the Invulnerability shield is up keeps you in combat. The ORDER is
    /// the point: your own and your teammates' hits are thrown away before the shield is ever asked, so they can
    /// neither spring the trap nor count as combat.</summary>
    public class HitVerdictRuleTests
    {
        private sealed class FakeShield : IArmedShield
        {
            public bool Armed;
            public int Asked;
            public bool TryConsume(float damageAmount) { Asked++; bool was = Armed; Armed = false; return was; }
        }

        [Test]
        public void YourOwnHitIsIgnoredAndNeverAsksTheShield()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.IgnoredSelf, HitVerdictRule.Classify(true, false, false, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void ATeammatesHitIsIgnoredAndNeverSpringsTheTrap()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.IgnoredTeammate, HitVerdictRule.Classify(false, true, false, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void SelfWinsOverTeammateBecauseYouAreOnYourOwnTeam()
        {
            // Teams.AreSameTeam(a, a) is true - the caller may pass both.
            Assert.AreEqual(HitVerdict.IgnoredSelf, HitVerdictRule.Classify(true, true, false, null, 10f));
        }

        [Test]
        public void AnEnemyHitDuringTheImmunityIsShieldedAndLeavesANewArmAlone()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, true, shield, 10f));
            Assert.AreEqual(0, shield.Asked);
            Assert.IsTrue(shield.Armed);
        }

        [Test]
        public void TheFirstEnemyHitOnAnArmedShieldIsShieldedAndDisarmsIt()
        {
            var shield = new FakeShield { Armed = true };
            Assert.AreEqual(HitVerdict.Shielded, HitVerdictRule.Classify(false, false, false, shield, 10f));
            Assert.AreEqual(1, shield.Asked);
            Assert.IsFalse(shield.Armed);
        }

        [Test]
        public void AnEnemyHitWithNoArmLands()
        {
            var shield = new FakeShield();
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, false, shield, 10f));
            Assert.AreEqual(1, shield.Asked);
        }

        [Test]
        public void NoShieldComponentStillLetsTheHitLand()
        {
            Assert.AreEqual(HitVerdict.Lands, HitVerdictRule.Classify(false, false, false, null, 10f));
        }

        [Test]
        public void ShieldedAndLandedHitsAreCombatIgnoredOnesAreNot()
        {
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(HitVerdict.Shielded));
            Assert.IsTrue(HitVerdictRule.CountsAsCombat(HitVerdict.Lands));
            Assert.IsFalse(HitVerdictRule.CountsAsCombat(HitVerdict.IgnoredSelf));
            Assert.IsFalse(HitVerdictRule.CountsAsCombat(HitVerdict.IgnoredTeammate));
        }
    }
}
```

- [ ] **2. The rule.**

```csharp
namespace Overpower.Combat
{
    /// <summary>The one question the damage funnel asks the Invulnerability trap. PlayerStatusEffects implements it; the
    /// tests use a fake. An interface rather than a delegate so the funnel allocates nothing per hit (burn ticks call it
    /// every frame).</summary>
    public interface IArmedShield
    {
        /// <summary>True exactly once, for the first qualifying hit while armed - and disarms as it says so.</summary>
        bool TryConsume(float damageAmount);
    }

    public enum HitVerdict
    {
        /// <summary>Your own shot or splash: no damage, not combat.</summary>
        IgnoredSelf,
        /// <summary>A teammate's: no damage, not combat.</summary>
        IgnoredTeammate,
        /// <summary>A real enemy hit the shield stopped (the armed trap, or the immunity after it): no damage, but it IS
        /// combat - Tudor, 2026-09-18: no armour recharge, no shop, no regen while being shot.</summary>
        Shielded,
        /// <summary>A real enemy hit that goes on to DamageResolver.</summary>
        Lands,
    }

    /// <summary>
    /// The order of PlayerHealth.ApplyDamage's early exits, pulled out so it is tested (2.7b). Self first, then
    /// teammate, then the shield - so a friendly or self hit can neither burn an armed trap nor keep anyone "in
    /// combat". An immunity already running is checked before the trap, so a hit during it never consumes a second arm.
    /// </summary>
    public static class HitVerdictRule
    {
        public static HitVerdict Classify(bool fromSelf, bool fromTeammate, bool alreadyInvulnerable,
                                          IArmedShield armedShield, float damageAmount)
        {
            if (fromSelf) return HitVerdict.IgnoredSelf;
            if (fromTeammate) return HitVerdict.IgnoredTeammate;
            if (alreadyInvulnerable) return HitVerdict.Shielded;
            if (armedShield != null && armedShield.TryConsume(damageAmount)) return HitVerdict.Shielded;
            return HitVerdict.Lands;
        }

        public static bool CountsAsCombat(HitVerdict verdict) =>
            verdict == HitVerdict.Shielded || verdict == HitVerdict.Lands;
    }
}
```

- [ ] **3. Wire it in.**
  - `PlayerStatusEffects : MonoBehaviour, IStatusReceiver, IArmedShield`, with `bool IArmedShield.TryConsume(float d) => TryConsumeReactiveInvulnerability(d);`.
  - In `PlayerHealth.ApplyDamage`, replace `:288-324` with one path:
    - `sourcePlayer` as now.
    - `fromSelf = sourcePlayer != null && sourcePlayer == photonView.Owner`.
    - `fromTeammate = !fromSelf && Teams.AreSameTeam(sourcePlayer, photonView.Owner)`.
    - `verdict = HitVerdictRule.Classify(fromSelf, fromTeammate, statusEffects != null && statusEffects.IsInvulnerable, statusEffects, info.Amount)`.
    - `if (HitVerdictRule.CountsAsCombat(verdict)) secondsSinceCombat = 0f;`
    - Then return `default` for every verdict except `Lands`. Keep both "reported once per match" logs.
  - Delete the later `secondsSinceCombat = 0f` at `:331`, so there is one home.
  - Keep the rework's order comment (`:317-322`) in shorter form, and add Tudor's 2026-09-18 rule.
  - The burn-tick re-entrancy note in `PlayerStatusEffects.Update` stays true: the trap is still consumed from inside the funnel.
- [ ] **4. Green.** Recompile, then run the tests. **Expect BASE TESTS + 8.**
- [ ] **5. Single-client Play Mode check.**
  - Alone, list the actors first. Equip id 25 in Ultimate, call `UltimateCharge.Fill()`, and cast through `AbilityRunner.TryCast` by reflection.
  - From an in-process coroutine, call `ApplyDamage` with an enemy `DamageInfo`: 10 damage, from an actor number **not in the room**. `AreSameTeam` fails open, so it counts as an enemy.
    - Hit once inside the armed window, then every 0.5 s for 4 s.
    - Record `SecondsSinceCombat`, `Armor`, `Health`, `IsInvulnerable` and `IsReactiveInvulnerabilityArmed` per frame.
  - **Expected:**
    - Health never moves.
    - `SecondsSinceCombat` never exceeds about 0.5 s during the shield.
    - Armour never ticks up.
  - Then a hit with **your own actor number** during the immunity: `SecondsSinceCombat` keeps climbing.
- [ ] **6. Commit** the four files. The body notes: a self or teammate hit during the immunity now logs the once-per-match "blocked" line (log only).

---

### Step 2: the small 2.7 leftovers

**Files:** `RoomManager.cs`, `MatchDirector.cs` (comment only), `TerritorySnapshot.cs`, `BuildingManager.cs`, `TerritorySnapshotTests.cs`.

- [ ] **1. Red.** Add these tests. They fail to compile until `NeedsNeutralReset` exists.

```csharp
        [Test]
        public void AnOwnedZoneNeedsANeutralReset()
        {
            Assert.IsTrue(new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).NeedsNeutralReset(3));
        }

        [Test]
        public void AZoneDrainedToNeutralStillNeedsItsHistoryWiped()
        {
            // The exact case BuildingManager.SetNeutralWithoutBountyHistory's guard exists for (2.7 review round 2):
            // a flank drained to neutral just before the Tier-3 reset still carries a payable hold.
            var s = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutral(3, nowMs: 305000);
            Assert.IsTrue(s.NeedsNeutralReset(3));
        }

        [Test]
        public void ANeutralZoneWithNoHistoryIsLeftAlone()
        {
            Assert.IsFalse(new TerritorySnapshot(10).NeedsNeutralReset(3));
            var reset = new TerritorySnapshot(10).WithCapture(3, 1, 5000, 0).WithNeutralReset(3, nowMs: 305000);
            Assert.IsFalse(reset.NeedsNeutralReset(3), "a second reset in a row writes nothing");
        }
```

- [ ] **2. Implement.** Add `public bool NeedsNeutralReset(int zone) => OwnerOf(zone) != TerritoryMap.Neutral || LastOwnerOf(zone) != TerritoryMap.Neutral;` to `TerritorySnapshot`, with a doc comment.
  - `BuildingManager.cs:618` becomes `if (!basis.NeedsNeutralReset(zone)) return;`, with its comment kept. The test now exercises the real guard.
- [ ] **3. Fix the stale `MatchDirector.cs:282-284`** comment. New text: the reset is master-only through `WriteBasis`. It is safe to loop every zone and to run twice: a zone already neutral with no hold history is skipped (`NeedsNeutralReset`), and one that drained to neutral but still carries history is wiped, which is the point.
- [ ] **4. `RoomManager.OnLeftRoom` (`:85-96`).**
  - Replace `{ GoldWallet.GoldKey, 0 }` with `{ GoldWallet.GoldKey, null }`. Photon strips null-valued keys on the local player.
  - Rewrite the comment:
    - **Why remove, not 0:** `GoldWallet.Start` reads an existing key as the balance. A written 0 would beat a future non-zero `TerritoryConfig › Starting Gold`, the one home for starting gold.
    - **Kept:** the weapon, equipment, ultimate and mobility picks, and `teamID`. `PlayerLoadout.Start` republishes the whole starting kit for every newly spawned player, and `PickSmallestTeam` overwrites `teamID` on the next join. Nothing needs clearing.
    - **Armour levels:** `PlayerLoadout.Start` republishes them too. They are reset here as well so that a player turned away before spawning doesn't carry them.
- [ ] **5. Green.** Expect **+3**.
- [ ] **6. Play Mode check.** In one process: join, leave, then read `PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("gold")`. **Expected `False`.** Rejoin: the local `GoldWallet.Balance` equals `StartingGold`.
- [ ] **7. Commit** the five files.

---

### Step 3: the pure rules (red first) and interim wiring

**Files:**
- Rules: `MatchPhaseRules.cs` (rewrite) and `MatchStartRules.cs` (create).
- Map and snapshot: `TerritoryMap.cs` and `TerritorySnapshot.cs`.
- Tests: `MatchPhaseRulesTests.cs` (rewrite), `MatchStartRulesTests.cs` (create), `TerritoryMapTests.cs` and `TerritorySnapshotTests.cs`.
- Interim adapters: `MatchDirector.cs`, `PlayerLifecycle.cs`, `BuildingManager.cs` and `RoomManager.cs` (the `lastStandAt` clear only).

- [ ] **1. Red: `MatchPhaseRulesTests.cs`, rewritten wholesale.** `TeamStatus` changed shape, and three 2.7 behaviours are reversed on purpose. The build must fail on the new members.

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>2.7b (Tudor, 2026-09-18): who is out and which phase the match is in once it is live. Rewritten from
    /// 2.7: a host-started match is TwoTeams from the start, an emptied team stays in, and there is no draw.</summary>
    public class MatchPhaseRulesTests
    {
        private static TeamStatus Holds(int id, int members = 1, int outForLastStand = 0) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = true, HoldsAnyCapitalInPlay = true };

        /// <summary>Holds no capital at all. lastOutAt: the server ms its latest member went out (null = nobody did).</summary>
        private static TeamStatus Lost(int id, int members = 1, int outForLastStand = 0, int? lastOutAt = null) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = false, HoldsAnyCapitalInPlay = false, LastOutAtMs = lastOutAt };

        /// <summary>Lost its own capital but holds another team's.</summary>
        private static TeamStatus Adopted(int id, int members = 1, int outForLastStand = 0) =>
            new TeamStatus { TeamId = id, InMatch = true, Members = members, MembersOutForLastStand = outForLastStand,
                             HoldsOwnCapital = false, HoldsAnyCapitalInPlay = true };

        /// <summary>The third team of a host-started two-team match.</summary>
        private static TeamStatus NotInMatch(int id) => new TeamStatus { TeamId = id, InMatch = false };

        private static MatchPhaseResult Run(params TeamStatus[] teams) => MatchPhaseRules.Recompute(new List<int>(), teams);
        private static MatchPhaseResult Run(int[] alreadyOut, params TeamStatus[] teams) => MatchPhaseRules.Recompute(alreadyOut, teams);

        // ---- the phase

        [Test]
        public void ThreeTeamsInTheMatchIsPhaseOne()
        {
            var r = Run(Holds(0, 3), Holds(1, 3), Holds(2, 3));
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(-1, r.Winner);
        }

        [Test]
        public void AHostStartedMatchIsTheTwoTeamPhaseFromTheStart()
        {
            var r = Run(Holds(0), Holds(1), NotInMatch(2));
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        [Test]
        public void PlayersLeavingNeverMoveThePhase()
        {
            // Telemetry spec Part 3: the phase changes only on a knockout. Two emptied teams still holding capitals are in.
            var r = Run(Holds(0, 3), Holds(1, members: 0), Holds(2, members: 0));
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
            Assert.IsEmpty(r.Eliminated);
        }

        // ---- three teams: the last stand

        [Test]
        public void WithThreeTeamsALostCapitalWithSurvivorsIsALastStand()
        {
            var r = Run(Lost(0, 3, 2), Holds(1, 3), Holds(2, 3));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        [Test]
        public void WithThreeTeamsNoCapitalAndEveryoneOutKnocksTheTeamOut()
        {
            var r = Run(Lost(0, 3, 3), Holds(1, 3), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void EveryoneOutButStillHoldingTheCapitalIsNotOut()
        {
            Assert.IsEmpty(Run(Holds(0, 3, 3), Holds(1, 3), Holds(2, 3)).Eliminated);
        }

        [Test]
        public void WithThreeTeamsAnEmptiedTeamStaysInUntilItsCapitalFalls()
        {
            // Tudor rule 2: nobody is left to make a last stand, so the capital falling IS the knockout.
            Assert.IsEmpty(Run(Holds(0, members: 0), Holds(1, 3), Holds(2, 3)).Eliminated);
            CollectionAssert.AreEqual(new[] { 0 }, Run(Lost(0, members: 0), Holds(1, 3), Holds(2, 3)).Eliminated);
        }

        [Test]
        public void WithThreeTeamsAnotherTeamsCapitalKeepsYouIn()
        {
            // Tudor, 2026-09-18 afternoon: yes - adoption counts in the three-team phase too (GDD p.20).
            var r = Run(Adopted(0, 3, 3), Holds(1, 3), Lost(2, 3, 1));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.ThreeTeams, r.Phase);
        }

        // ---- two teams

        [Test]
        public void WithTwoTeamsLosingYourCapitalWhileTheOtherHoldsOneIsInstant()
        {
            var r = Run(new[] { 2 }, Lost(0, 3, 0), Holds(1, 3), Lost(2, 3, 3));
            CollectionAssert.AreEqual(new[] { 2, 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void WithTwoTeamsAnEmptiedTeamStaysInWhileItHoldsItsCapital()
        {
            var r = Run(Holds(0), Holds(1, members: 0), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void WithTwoTeamsAnEmptiedTeamIsOutTheMomentItsCapitalFalls()
        {
            var r = Run(Holds(0), Lost(1, members: 0), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void BothWithoutACapitalIsLastManStandingNotAKnockout()
        {
            var r = Run(Lost(0, 2, 1), Lost(1, 2, 1), NotInMatch(2));
            Assert.IsEmpty(r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void LastManStandingTheWipedTeamLosesEvenIfTheWinnerHoldsNothing()
        {
            var r = Run(Lost(0, 2, 2), Lost(1, 2, 1), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void LastManStandingAnEmptiedTeamLoses()
        {
            var r = Run(Lost(0, members: 0), Lost(1, 1, 0), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void TakingAnyCapitalInLastManStandingKnocksOutTheOtherTeam()
        {
            var r = Run(Adopted(0, 2, 1), Lost(1, 2, 0), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(0, r.Winner);
        }

        // ---- no draw: the last team to die wins (Tudor, 2026-09-18 afternoon)

        [Test]
        public void NoDrawTheTeamWhoseLastPlayerDiedLastWins()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 1000), Lost(1, 1, 1, lastOutAt: 1200), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void NoDrawATrueTieOnTheSameMillisecondGoesToTheLowerTeamNumber()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 1000), Lost(1, 1, 1, lastOutAt: 1000), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 1 }, r.Eliminated);
            Assert.AreEqual(0, r.Winner);
        }

        [Test]
        public void NoDrawTheLastDeathIsReadAcrossTheServerClockWrap()
        {
            // PhotonNetwork.ServerTimestamp wraps: int.MinValue + 10 is 21 ms AFTER int.MaxValue - 10.
            var r = Run(Lost(0, 1, 1, lastOutAt: int.MaxValue - 10), Lost(1, 1, 1, lastOutAt: int.MinValue + 10), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void NoDrawATeamThatEmptiedLosesToATeamThatDied()
        {
            // Nobody on team 0 died - its players left - so it has no stamp and counts as out first.
            var r = Run(Lost(0, members: 0), Lost(1, 1, 1, lastOutAt: 1000), NotInMatch(2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(1, r.Winner);
        }

        [Test]
        public void EveryTeamWipedAtOnceStillEndsWithAWinner()
        {
            var r = Run(Lost(0, 1, 1, lastOutAt: 300), Lost(1, 1, 1, lastOutAt: 100), Lost(2, 1, 1, lastOutAt: 200));
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(0, r.Winner);
        }

        // ---- across the three-to-two transition

        [Test]
        public void TheKnockoutCascadeEndsTheMatchInOneRecompute()
        {
            // 2.7 review leftover, re-checked against last man standing. A is wiped with no capital; B has lost its
            // capital but has players left; C holds its own. A goes out (last stand); now two teams remain and C HOLDS a
            // capital, so B is out at once - not last man standing, which needs BOTH sides capital-less. One call must
            // see both, or the match would sit one tick in a phase that is already decided.
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 0), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(2, r.Winner);
        }

        [Test]
        public void AKnockoutThatLeavesTwoCapitallessTeamsStartsLastManStanding()
        {
            // Replaces 2.7's draw ("a match can end with nobody winning"): both survivors hold nothing, so neither goes out.
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 1), Lost(2, 3, 2));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
            Assert.AreEqual(-1, r.Winner);
        }

        [Test]
        public void TwoTeamsWipedInTheSameThreeTeamRecomputeBothGoAndTheThirdWins()
        {
            var r = Run(Lost(0, 3, 3), Lost(1, 3, 3), Lost(2, 3, 1));
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, r.Eliminated);
            Assert.AreEqual(2, r.Winner);
        }

        // ---- permanence

        [Test]
        public void AKnockoutIsPermanent()
        {
            var r = Run(new[] { 0 }, Holds(0, 3), Holds(1, 3), Holds(2, 3));
            CollectionAssert.AreEqual(new[] { 0 }, r.Eliminated);
            Assert.AreEqual(MatchPhase.TwoTeams, r.Phase);
        }

        [Test]
        public void TheLastTeamLeftWins()
        {
            var r = Run(new[] { 0, 2 }, Lost(0, 3, 3), Holds(1, 3), Lost(2, 3, 3));
            Assert.AreEqual(MatchPhase.Over, r.Phase);
            Assert.AreEqual(1, r.Winner);
        }

        // ---- the small rules the adapters ask

        [Test]
        public void HavingACapitalMeansAnyCapitalInPlayInEveryPhase()
        {
            // Tudor's answer 1 pins here (any capital in play, both phases). Were it ever reversed for the three-team
            // phase: `phase == ThreeTeams ? holdsOwnCapital : holdsAnyCapitalInPlay`, and flip the first assert and
            // WithThreeTeamsAnotherTeamsCapitalKeepsYouIn.
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.ThreeTeams, holdsOwnCapital: false, holdsAnyCapitalInPlay: true));
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.TwoTeams, false, true));
            Assert.IsTrue(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.TwoTeams, true, true));
            Assert.IsFalse(MatchPhaseRules.CountsAsHavingACapital(MatchPhase.ThreeTeams, false, false));
        }

        [Test]
        public void OnlyALiveDeathWithNoCapitalIsALastStandDeath()
        {
            Assert.IsFalse(MatchPhaseRules.IsLastStandDeath(live: false, teamHasACapital: false), "warm-up: always an ordinary respawn");
            Assert.IsFalse(MatchPhaseRules.IsLastStandDeath(live: true, teamHasACapital: true));
            Assert.IsTrue(MatchPhaseRules.IsLastStandDeath(live: true, teamHasACapital: false));
        }

        private static MatchPhaseRules.CapitalHold Hold(int zone, int owner, int since) =>
            new MatchPhaseRules.CapitalHold { Zone = zone, Owner = owner, HeldSinceMs = since };

        [Test]
        public void YouRespawnAtYourOwnCapitalWhileYouHoldIt()
        {
            // "A team holding a second capital still respawns at its own" - even one it took earlier.
            Assert.AreEqual(6, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(7, 0, 100), Hold(6, 0, 5000), Hold(8, 1, 0) }));
        }

        [Test]
        public void WithoutYourOwnYouRespawnAtTheCapitalHeldLongest()
        {
            Assert.AreEqual(8, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(6, 1, 0), Hold(7, 0, 2000), Hold(8, 0, 1000) }));
        }

        [Test]
        public void HeldLongestSurvivesTheServerClockWrapping()
        {
            // PhotonNetwork.ServerTimestamp is an int that wraps: the stamp just before the wrap is the OLDER hold.
            Assert.AreEqual(7, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(8, 0, int.MinValue + 100), Hold(7, 0, int.MaxValue - 100) }));
        }

        [Test]
        public void NoCapitalMeansNowhereToRespawn()
        {
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.RespawnCapital(0, 6, new[] { Hold(6, 1, 0), Hold(7, 1, 0) }));
        }

        [Test]
        public void WhereAnEndedCountdownPutsYou()
        {
            const int own = 6, adopted = 7, none = TerritoryMap.Neutral;
            Assert.AreEqual(own, MatchPhaseRules.SpawnCapitalFor(MatchPhase.Warmup, false, own, none), "warm-up: always home");
            Assert.AreEqual(adopted, MatchPhaseRules.SpawnCapitalFor(MatchPhase.TwoTeams, false, own, adopted));
            Assert.AreEqual(own, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, false, own, none), "a countdown that began before the fall still ends at home");
            Assert.AreEqual(none, MatchPhaseRules.SpawnCapitalFor(MatchPhase.TwoTeams, false, own, none), "last man standing: the dead wait");
            Assert.AreEqual(none, MatchPhaseRules.SpawnCapitalFor(MatchPhase.ThreeTeams, true, own, own), "a knocked-out team never respawns");
        }

        [Test]
        public void ATerritoryWinCountsOnlyOnceLiveAndOnlyForEveryCapitalInPlay()
        {
            // Replaces 2.7's "two teams with players" guard: going live already needed two teams, and Tudor's rule 2
            // keeps an emptied team in - a survivor who takes its capital has won it.
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(live: false, new[] { 0, 0, 0 }));
            Assert.AreEqual(0, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, 0 }));
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, 1 }));
            Assert.AreEqual(TerritoryMap.Neutral, MatchPhaseRules.TerritoryWinner(true, new[] { 0, 0, TerritoryMap.Neutral }));
            Assert.AreEqual(1, MatchPhaseRules.TerritoryWinner(true, new[] { 1, 1 }), "a host-started match has two capitals in play");
        }

        [Test]
        public void AnAdoptionIsACapitallessTeamTakingSomeoneElsesCapital()
        {
            Assert.IsTrue(MatchPhaseRules.IsAdoption(newOwner: 0, capitalTeamOfZone: 1, otherCapitalsInPlayHeldByNewOwner: 0));
            Assert.IsFalse(MatchPhaseRules.IsAdoption(0, 0, 0), "retaking your own capital is a recapture");
            Assert.IsFalse(MatchPhaseRules.IsAdoption(0, 1, 1), "a team that already had a capital just took a second");
            Assert.IsFalse(MatchPhaseRules.IsAdoption(TerritoryMap.Neutral, 1, 0));
        }
    }
}
```

- [ ] **2. Red: `MatchStartRulesTests.cs`.**

```csharp
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchStartRulesTests
    {
        [Test]
        public void TheCountdownStartsTheMomentAllThreeTeamsHaveAPlayer()
        {
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(teamsFixed: false, new[] { 1, 1, 0 }));
            Assert.IsTrue(MatchStartRules.StartsCountdownAutomatically(false, new[] { 1, 1, 1 }));
            Assert.IsTrue(MatchStartRules.StartsCountdownAutomatically(false, new[] { 3, 2, 1 }));
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(true, new[] { 1, 1, 1 }), "a countdown or a live match already fixed the teams");
        }

        [Test]
        public void TheHostMayStartOnlyWithExactlyTwoTeams()
        {
            Assert.IsTrue(MatchStartRules.HostMayStart(teamsFixed: false, new[] { 1, 0, 2 }, playersWithoutATeam: 0));
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 2, 0, 0 }, 0), "one team: nobody to play");
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 1 }, 0), "three teams start on their own");
            Assert.IsFalse(MatchStartRules.HostMayStart(true, new[] { 1, 1, 0 }, 0), "already counting down or live");
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 0 }, 1), "someone still joining may be the third team");
        }

        [Test]
        public void TheCountdownEndsItsLengthAfterItStartsEvenAcrossTheClockWrap()
        {
            Assert.AreEqual(15000, MatchStartRules.CountdownEndsAt(nowMs: 10000, countdownSeconds: 5f));
            Assert.AreEqual(int.MinValue + 4999, MatchStartRules.CountdownEndsAt(int.MaxValue, 5f));
            Assert.AreEqual(10000, MatchStartRules.CountdownEndsAt(10000, 0f), "0 s: live on the master's next frame");
            Assert.AreEqual(10000, MatchStartRules.CountdownEndsAt(10000, -3f), "a negative length is treated as 0");
        }

        [Test]
        public void TheMasterGoesLiveOnceItsClockReachesTheMoment()
        {
            Assert.IsFalse(MatchStartRules.HasReached(nowMs: 14999, momentMs: 15000));
            Assert.IsTrue(MatchStartRules.HasReached(15000, 15000));
            Assert.IsTrue(MatchStartRules.HasReached(15001, 15000));
            Assert.IsTrue(MatchStartRules.HasReached(int.MinValue + 10, int.MaxValue - 10), "across the wrap");
        }

        [Test]
        public void TheCountdownShowsWholeSecondsAndNeverZeroBeforeLive()
        {
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(nowMs: 10000, liveAtMs: 15000));
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(10001, 15000));
            Assert.AreEqual(1, MatchStartRules.CountdownSecondsShown(14999, 15000));
            Assert.AreEqual(1, MatchStartRules.CountdownSecondsShown(15500, 15000), "past the moment, live write not arrived yet: hold at 1");
        }

        [Test]
        public void ACountdownIsCancelledTheMomentATeamInItEmpties()
        {
            Assert.IsFalse(MatchStartRules.CountdownShouldCancel(new[] { 0, 1, 2 }, new[] { 1, 2, 1 }));
            Assert.IsTrue(MatchStartRules.CountdownShouldCancel(new[] { 0, 1, 2 }, new[] { 1, 0, 1 }));
            Assert.IsFalse(MatchStartRules.CountdownShouldCancel(new[] { 0, 1 }, new[] { 1, 1, 0 }), "the left-out team being empty is expected");
            Assert.IsTrue(MatchStartRules.CountdownShouldCancel(new[] { 0, 1 }, new[] { 0, 1, 3 }), "players on the left-out team never save a host start whose team left");
        }

        [Test]
        public void TheStartStateFollowsWhatTheRoomHolds()
        {
            Assert.AreEqual(StartState.Warmup, MatchStartRules.StartStateFor(teamsFixed: false, phaseWritten: false));
            Assert.AreEqual(StartState.CountingDown, MatchStartRules.StartStateFor(true, false));
            Assert.AreEqual(StartState.Live, MatchStartRules.StartStateFor(true, true));
        }

        [Test]
        public void TheTeamsInTheMatchAreTheTeamsWithPlayersAtThatMoment()
        {
            CollectionAssert.AreEqual(new[] { 0, 2 }, MatchStartRules.TeamsWithPlayers(new[] { 2, 0, 1 }));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, MatchStartRules.TeamsWithPlayers(new[] { 1, 1, 1 }));
        }

        [Test]
        public void OnlyTheCapitalOfATeamLeftOutOfALiveMatchIsOutOfPlay()
        {
            var inMatch = new[] { 0, 1 };
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(live: false, capitalTeam: 2, inMatch), "warm-up: every capital plays");
            Assert.IsTrue(MatchStartRules.IsCapitalOutOfPlay(true, 2, inMatch));
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(true, 0, inMatch));
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(true, TerritoryMap.Neutral, inMatch), "not a capital");
        }

        [Test]
        public void JoinersOnlyGoToTeamsStillInTheMatch()
        {
            Assert.IsTrue(MatchStartRules.MayJoin(teamsFixed: false, inMatch: false, eliminated: false), "warm-up: any team");
            Assert.IsTrue(MatchStartRules.MayJoin(true, true, false), "during the countdown or live: a team in the match");
            Assert.IsFalse(MatchStartRules.MayJoin(true, false, false), "the left-out team of a host start, from its countdown on");
            Assert.IsFalse(MatchStartRules.MayJoin(true, true, true), "knocked out");
        }

        [Test]
        public void TheWarmupLineMatchesTheStateWhoIsHereAndWhoIsHost()
        {
            Assert.AreEqual(WarmupMessage.None, MatchStartRules.WarmupMessageFor(StartState.Live, teamsWithPlayers: 2, isHost: true));
            Assert.AreEqual(WarmupMessage.Countdown, MatchStartRules.WarmupMessageFor(StartState.CountingDown, 2, true));
            Assert.AreEqual(WarmupMessage.WaitingForTeams, MatchStartRules.WarmupMessageFor(StartState.Warmup, 1, true));
            Assert.AreEqual(WarmupMessage.HostMayStart, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, true));
            Assert.AreEqual(WarmupMessage.WaitingForHost, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, false));
        }
    }
}
```

- [ ] **3. Red: add to `TerritoryMapTests.cs` and `TerritorySnapshotTests.cs`.**

```csharp
        // TerritoryMapTests
        [Test]
        public void AnOutOfPlayCapitalIsNeverCapturableNotEvenByItsOwnTeam()
        {
            // The own-capital exception must not reopen the third capital of a host-started match.
            Assert.IsFalse(RealMap().MayCapture(2, 8, new Dictionary<int, int> { { 6, 0 }, { 7, 1 } }, null, zone => zone == 8));
        }

        [Test]
        public void AnOutOfPlayCapitalRefusesATeamNextToIt()
        {
            var owners = new Dictionary<int, int> { { 6, 0 }, { 7, 1 }, { 2, 0 } };
            Assert.IsTrue(RealMap().MayCapture(0, 8, owners), "without the check, zone 2 is a way in");
            Assert.IsFalse(RealMap().MayCapture(0, 8, owners, null, zone => zone == 8));
        }

        [Test]
        public void TheOutOfPlayCheckLeavesEveryOtherZoneAlone()
        {
            Assert.IsTrue(RealMap().MayCapture(0, 0, StartOwners(), null, zone => zone == 8));
        }

        [Test]
        public void CapitalTeamOfNamesWhoseCapitalAZoneIs()
        {
            Assert.AreEqual(2, RealMap().CapitalTeamOf(8));
            Assert.AreEqual(TerritoryMap.Neutral, RealMap().CapitalTeamOf(3));
        }

        // TerritorySnapshotTests
        private static readonly (int, int)[] Capitals = { (6, 0), (7, 1), (8, 2) };

        [Test]
        public void TheStartingSnapshotGivesEveryTeamInTheMatchItsCapitalAndNothingElse()
        {
            var s = TerritorySnapshot.Starting(10, Capitals, new[] { 0, 1 }, nowMs: 5000);
            Assert.AreEqual(0, s.OwnerOf(6));
            Assert.AreEqual(1, s.OwnerOf(7));
            Assert.AreEqual(5000, s.HeldSinceMs(6));
            Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(8), "the left-out team's capital is owned by nobody");
            for (int zone = 0; zone < 10; zone++)
            {
                if (zone != 6 && zone != 7) Assert.AreEqual(TerritoryMap.Neutral, s.OwnerOf(zone));
                Assert.AreEqual(0, s.BountyPaidOnLastCapture(zone), "a fresh start pays no bounty");
                Assert.AreEqual(TerritoryMap.Neutral, s.LastOwnerOf(zone), "and leaves no hold to pay one later");
            }
        }

        [Test]
        public void WithEveryTeamTheStartingSnapshotIsTheWarmupStart()
        {
            Assert.AreEqual(2, TerritorySnapshot.Starting(10, Capitals, teamsInMatch: null, nowMs: 5000).OwnerOf(8));
        }
```

- [ ] **4. Report the red** compile errors verbatim.
- [ ] **5. Implement `MatchPhaseRules.cs`.**

```csharp
using System.Collections.Generic;

namespace Overpower.Match
{
    public enum MatchPhase
    {
        /// <summary>Before the match goes live (2.7b, Tudor 2026-09-18): fights and captures happen, nothing counts. Never
        /// stored - MatchDirector reads it from mTeams being absent.</summary>
        Warmup = 0,
        /// <summary>Three teams still in: a team with no capital is out only once all its members are out (last stand).</summary>
        ThreeTeams = 1,
        /// <summary>Two teams still in - after the first knockout, or from the start of a host-started match.</summary>
        TwoTeams = 2,
        Over = 3,
    }

    public struct TeamStatus
    {
        public int TeamId;
        /// <summary>Fixed when the match went live (MatchDirector.TeamsInMatchKey). A team that later empties stays in
        /// (Tudor, rule 2); the third team of a host start was never in and is ignored here entirely.</summary>
        public bool InMatch;
        public int Members;
        /// <summary>Members who died while the team had no capital ("lastStand" Player Property). An ordinary respawn
        /// countdown never counts.</summary>
        public int MembersOutForLastStand;
        /// <summary>Owns its own starting capital right now.</summary>
        public bool HoldsOwnCapital;
        /// <summary>Owns at least one capital in play: its own, an enemy's, or a knocked-out team's. The third capital of a
        /// host-started match is never in play.</summary>
        public bool HoldsAnyCapitalInPlay;
        /// <summary>The server ms this team's latest member went out for the last stand - the latest "lastStandAt"
        /// Player Property among its members who are out. Null when none has a stamp (nobody died; e.g. it emptied).
        /// Read only by the no-draw rule (Tudor: the last team to die wins a same-instant wipe).</summary>
        public int? LastOutAtMs;
    }

    public sealed class MatchPhaseResult
    {
        public MatchPhase Phase;
        public List<int> Eliminated = new List<int>();
        /// <summary>The winning team when Phase is Over, else -1.</summary>
        public int Winner = -1;
    }

    /// <summary>
    /// Who is out and which phase a LIVE match is in, recomputed from facts every client has (the teams fixed at going
    /// live, capital owners, who has died since their team lost its capitals, team sizes) - so a new master, or a second
    /// match, reaches the same answer. MatchDirector never calls this during the warm-up: nothing counts then.
    ///
    /// The phase moves ONLY on a knockout (telemetry spec Part 3): it is the number of teams still in, and "in" is fixed
    /// at going live - nobody joining or leaving can move it. What each phase MEANS (Tudor, 2026-09-18, and GDD p.20-21):
    /// - three teams: a team with no capital is out once every member is out (its last stand); an emptied team is "all
    ///   out", so its capital falling is its knockout;
    /// - two teams: with no capital while the other team holds one, you are out at once; with none on either side it is
    ///   last man standing - only a wiped team goes out, and the other wins even holding nothing;
    /// - "having a capital" is CountsAsHavingACapital (adoption);
    /// - there is never a draw: if every team still in would go out at the same instant, the team whose last player
    ///   went out latest stays in and wins (NoDrawSurvivorIndex).
    /// A fixpoint: a knockout can narrow the match to two teams, and the two-team rule then applies at once, not next tick.
    /// </summary>
    public static class MatchPhaseRules
    {
        public static MatchPhaseResult Recompute(IReadOnlyCollection<int> alreadyEliminated, IReadOnlyList<TeamStatus> teams)
        {
            var result = new MatchPhaseResult();
            result.Eliminated.AddRange(alreadyEliminated);
            var outNow = new List<TeamStatus>();

            while (true)
            {
                MatchPhase phase = PhaseFor(teams, result.Eliminated);
                if (phase == MatchPhase.Over)
                    break;

                outNow.Clear();
                foreach (TeamStatus team in teams)
                    if (IsStillIn(team, result.Eliminated) && IsOutNow(team, phase, teams, result.Eliminated))
                        outNow.Add(team);
                if (outNow.Count == 0)
                    break;

                // No draw (Tudor): never knock out every team still in at once.
                if (outNow.Count == CountStillIn(teams, result.Eliminated))
                    outNow.RemoveAt(NoDrawSurvivorIndex(outNow));

                foreach (TeamStatus team in outNow)
                    result.Eliminated.Add(team.TeamId);
            }

            result.Phase = PhaseFor(teams, result.Eliminated);
            if (result.Phase == MatchPhase.Over)
                foreach (TeamStatus team in teams)
                    if (IsStillIn(team, result.Eliminated))
                        result.Winner = team.TeamId;
            return result;
        }

        private static bool IsOutNow(TeamStatus team, MatchPhase phase, IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            if (HasACapital(team, phase))
                return false;

            bool everyoneOut = team.MembersOutForLastStand >= team.Members; // an emptied team: 0 >= 0
            if (phase == MatchPhase.ThreeTeams)
                return everyoneOut;

            foreach (TeamStatus other in teams)
                if (other.TeamId != team.TeamId && IsStillIn(other, eliminated) && HasACapital(other, phase))
                    return true;   // GDD p.21: the other team holds a capital - instant
            return everyoneOut;    // last man standing
        }

        /// <summary>Capital adoption (Tudor, 2026-09-18): holding ANY capital in play counts - a team that lost its own
        /// but took another's is safe and respawns there. Open for Tudor #1: default applies it in both phases (GDD p.20
        /// describes it for the three-team last stand); the phase parameter is here so the alternative is one line.</summary>
        public static bool CountsAsHavingACapital(MatchPhase phase, bool holdsOwnCapital, bool holdsAnyCapitalInPlay) =>
            holdsAnyCapitalInPlay;

        private static bool HasACapital(TeamStatus team, MatchPhase phase) =>
            CountsAsHavingACapital(phase, team.HoldsOwnCapital, team.HoldsAnyCapitalInPlay);

        private static bool IsStillIn(TeamStatus team, List<int> eliminated) => team.InMatch && !eliminated.Contains(team.TeamId);

        private static int CountStillIn(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int count = 0;
            foreach (TeamStatus team in teams)
                if (IsStillIn(team, eliminated)) count++;
            return count;
        }

        private static MatchPhase PhaseFor(IReadOnlyList<TeamStatus> teams, List<int> eliminated)
        {
            int stillIn = CountStillIn(teams, eliminated);
            return stillIn >= 3 ? MatchPhase.ThreeTeams : stillIn == 2 ? MatchPhase.TwoTeams : MatchPhase.Over;
        }

        /// <summary>Tudor, 2026-09-18 afternoon: in a same-instant wipe the team whose last player went out LAST wins. A
        /// team with no stamp (it emptied - nobody died) counts as out before any stamped team; a true tie on the same
        /// server millisecond goes to the lower team number.</summary>
        private static int NoDrawSurvivorIndex(List<TeamStatus> candidates)
        {
            int best = 0;
            for (int i = 1; i < candidates.Count; i++)
                if (WentOutLater(candidates[i], candidates[best]))
                    best = i;
            return best;
        }

        private static bool WentOutLater(TeamStatus a, TeamStatus b)
        {
            if (a.LastOutAtMs.HasValue != b.LastOutAtMs.HasValue)
                return a.LastOutAtMs.HasValue; // a death stamp beats none
            // Server-clock stamps wrap: compare by difference, never by value.
            int later = a.LastOutAtMs.HasValue ? unchecked(a.LastOutAtMs.Value - b.LastOutAtMs.Value) : 0;
            return later > 0 || (later == 0 && a.TeamId < b.TeamId);
        }

        /// <summary>The branch PlayerLifecycle.PlayerDied takes: a death is a last-stand death (wait for a capital,
        /// counts toward a knockout) only in a live match, with no capital. Warm-up deaths always respawn - the last
        /// stand exists only to decide a knockout, and nothing counts before live.</summary>
        public static bool IsLastStandDeath(bool live, bool teamHasACapital) => live && !teamHasACapital;

        public struct CapitalHold { public int Zone; public int Owner; public int HeldSinceMs; }

        /// <summary>Where a team respawns: its own capital while it holds it ("a team holding a second capital still
        /// respawns at its own"), else the in-play capital it has held longest - the one it adopted first. Derived from
        /// the replicated hold stamps, so a new master and a late joiner get the same answer with no extra state.
        /// Neutral when it holds none.</summary>
        public static int RespawnCapital(int team, int ownCapital, IReadOnlyList<CapitalHold> capitalsInPlay)
        {
            int best = TerritoryMap.Neutral, bestSince = 0;
            foreach (CapitalHold hold in capitalsInPlay)
            {
                if (hold.Owner != team) continue;
                if (hold.Zone == ownCapital) return ownCapital;
                // Server-clock stamps wrap: compare by difference, never by value (same as TerritorySnapshot's hold maths).
                if (best == TerritoryMap.Neutral || unchecked(hold.HeldSinceMs - bestSince) < 0)
                {
                    best = hold.Zone;
                    bestSince = hold.HeldSinceMs;
                }
            }
            return best;
        }

        /// <summary>Where an ended respawn countdown puts a player, as a capital zone; Neutral = don't respawn, wait.</summary>
        public static int SpawnCapitalFor(MatchPhase phase, bool teamEliminated, int ownCapital, int respawnCapital)
        {
            if (teamEliminated || phase == MatchPhase.Over) return TerritoryMap.Neutral;
            if (phase == MatchPhase.Warmup) return ownCapital;
            if (respawnCapital != TerritoryMap.Neutral) return respawnCapital;
            // No capital. Three teams: a countdown that began before the fall still ends at home (GDD p.20's last stand
            // counts only deaths AFTER the fall - unchanged from 2.7). Two teams: last man standing, the dead can't respawn.
            return phase == MatchPhase.ThreeTeams ? ownCapital : TerritoryMap.Neutral;
        }

        /// <summary>BuildingManager's second win condition: one team holds every capital in play - only once live.</summary>
        public static int TerritoryWinner(bool live, IReadOnlyList<int> ownersOfCapitalsInPlay)
        {
            if (!live || ownersOfCapitalsInPlay == null || ownersOfCapitalsInPlay.Count == 0) return TerritoryMap.Neutral;
            int owner = ownersOfCapitalsInPlay[0];
            if (owner < 0) return TerritoryMap.Neutral;
            foreach (int o in ownersOfCapitalsInPlay)
                if (o != owner) return TerritoryMap.Neutral;
            return owner;
        }

        /// <summary>The telemetry `adopt` line: a team holding no other capital in play just took one that isn't its own.</summary>
        public static bool IsAdoption(int newOwner, int capitalTeamOfZone, int otherCapitalsInPlayHeldByNewOwner) =>
            newOwner >= 0 && capitalTeamOfZone >= 0 && capitalTeamOfZone != newOwner && otherCapitalsInPlayHeldByNewOwner == 0;
    }
}
```

- [ ] **6. Implement `MatchStartRules.cs`.** Same namespace; pure C#, no `UnityEngine` (use `System.Math`). Two enums: `public enum StartState { Warmup, CountingDown, Live }` and `public enum WarmupMessage { None, Countdown, WaitingForTeams, HostMayStart, WaitingForHost }`.
  - `public const int TeamCount = 3;`
  - `CountTeamsWithPlayers(IReadOnlyList<int>)`: allocation-free, used by the per-frame UI.
  - `TeamsWithPlayers(IReadOnlyList<int>) → int[]`.
  - `StartStateFor(bool teamsFixed, bool phaseWritten)`: `Live` if the phase is written, else `CountingDown` if the teams are fixed, else `Warmup`.
  - `StartsCountdownAutomatically(teamsFixed, members) => !teamsFixed && CountTeamsWithPlayers(members) >= TeamCount`.
  - `HostMayStart(teamsFixed, members, playersWithoutATeam) => !teamsFixed && playersWithoutATeam == 0 && CountTeamsWithPlayers(members) == 2`.
  - `CountdownEndsAt(int nowMs, float countdownSeconds) => unchecked(nowMs + (int)Math.Round(Math.Max(0, countdownSeconds) * 1000))`.
  - `HasReached(int nowMs, int momentMs) => unchecked(nowMs - momentMs) >= 0`, wrap-safe.
  - `CountdownSecondsShown(int nowMs, int liveAtMs)`: `Math.Max(1, ceil(unchecked(liveAtMs - nowMs) / 1000.0))`. It never shows 0: "1" holds until the master's live write arrives.
  - `CountdownShouldCancel(IReadOnlyList<int> teamsInMatch, IReadOnlyList<int> membersPerTeam)`: true if any team in `teamsInMatch` has 0 members.
  - `IsCapitalOutOfPlay(live, capitalTeam, IReadOnlyList<int> teamsInMatch) => live && capitalTeam >= 0 && teamsInMatch != null && !Contains(teamsInMatch, capitalTeam)`. Out of play from **live**, not from the countdown: the countdown is still warm-up, and the cut capital only goes neutral in the live reset.
  - `MayJoin(teamsFixed, inMatch, eliminated) => !teamsFixed || (inMatch && !eliminated)`.
  - `WarmupMessageFor(StartState state, teamsWithPlayers, isHost)`: None when `Live`, `Countdown` when `CountingDown`; in the warm-up, WaitingForTeams unless exactly 2, otherwise host or guest.
  - `Contains(IReadOnlyList<int>, int)` as a plain loop.
  - The class comment quotes Tudor's rule 1 and answer 3.
- [ ] **7. `TerritoryMap`:**
  - `CapitalTeamOf(int zone)`.
  - `public IEnumerable<KeyValuePair<int, int>> Capitals => capitalOwnerByZone;`
  - A **separate** 5-argument overload, `MayCapture(team, zone, owners, isUnderAttack, isOutOfPlay)`. It refuses `isOutOfPlay(zone)` first, then defers to the 4-argument version. It is not an optional parameter, so existing 4-argument calls with `null` stay unambiguous.
  - The comment says why the check sits before the own-capital exception.
- [ ] **8. `TerritorySnapshot.Starting(int zoneCount, IEnumerable<(int zone, int team)> capitals, IReadOnlyList<int> teamsInMatch, int nowMs)`:** `null` means every team. It is a loop of `WithCapture(..., bountyPaid: 0)`, with a comment.
- [ ] **9. Interim adapters.** These keep the build compiling; each is replaced in the step named.
  - **`MatchDirector.BuildTeamStatuses`** fills the new fields:
    - `InMatch = true` for all three teams **[interim, replaced in step 5]**;
    - `HoldsOwnCapital` as today;
    - `HoldsAnyCapitalInPlay` = owns any of the three capitals;
    - `LastOutAtMs` = the latest `lastStandAt` stamp (compared wrap-safe) among the team's members whose `lastStand` is true, or null. This is final, not interim.
  - **`LogTelemetry` (`:312`)** counts `teamsRemaining` from `InMatch && !eliminated`.
  - **`PlayerLifecycle.PlayerDied` (`:256`)** uses `MatchPhaseRules.IsLastStandDeath(live: true, teamHasACapital: capitalHeld)` **[interim, step 5]**.
  - **`PlayerLifecycle`** gains `public const string LastStandAtKey = "lastStandAt";`, with a comment (Decision 23). `SetLastStandOut(bool)` writes **both keys in one Hashtable**: `{ LastStandKey, out }` and `{ LastStandAtKey, out ? (object)PhotonNetwork.ServerTimestamp : null }`. Final, not interim. `MatchDirector.OnPlayerPropertiesUpdate` already recomputes on `LastStandKey`, which is in the same change set.
  - **`RoomManager.OnLeftRoom`** adds `{ PlayerLifecycle.LastStandAtKey, null }` beside `LastStandKey`. Final.
  - **`BuildingManager.CheckTerritoryWin`:**
    - `int winner = MatchPhaseRules.TerritoryWinner(live: CountTeamsWithPlayers() >= 2, <owners of the three capitals from Current>)` **[interim, step 5]**.
    - Rewrite the "lone player" comment at `:869-871`. Say the live flag replaces it in step 5.
  - **`WriteInitialSnapshotWhenClockIsReady`** uses `TerritorySnapshot.Starting(ZoneCount, <CathedralBuildingIDs as pairs>, null, now)`.
  - **Rewrite the `MatchPhaseRules` class comment** as above. That removes "a lone player can never win" and "adoption was cut".
- [ ] **10. Green.** Expect **+35**: MatchPhaseRulesTests goes from 16 to 34, MatchStartRulesTests adds 11, TerritoryMapTests 4, TerritorySnapshotTests 2.
- [ ] **11. Commit** the twelve files. The body says the interim build has no warm-up yet and treats every team as in the match; it is not for playtests. It names the new `lastStandAt` Player Property: every client must rebuild.

---

### Step 4: the owner-side fresh start (nothing calls it yet)

**Files:** `PlayerLifecycle.cs`, `PlayerLoadout.cs`, `GoldWallet.cs`, `UltimateCharge.cs`, `UltimateChargeState.cs`, `ShopRules.cs`, `LoadoutScreen.cs`, `AbilityRunner.cs`, `NetworkedDeployable.cs`, `UltimateChargeStateTests.cs`, `ShopRulesTests.cs`.

- [ ] **1. Red.**
  - `UltimateChargeStateTests.ClearEmptiesTheMeterForAFreshStart`: fill, clear, then `Current == 0` and `!IsFull`.
  - `ShopRulesTests.ClearForgetsEverythingSpent`: record weapon 600 and armour 1400, clear, then both sells refund 0.
- [ ] **2. Pure pieces.** `UltimateChargeState.Clear() => Current = 0f;` and `PurchaseLedger.Clear()` (both totals to 0). Each gets a "2.7b fresh start" comment.
- [ ] **3. Owner-only `ResetForMatchStart()` on each component.** Each returns at once when `!photonView.IsMine`.
  - **`PlayerLoadout`:**
    - Extract `StartingAbilityId(slot)` from `Start` (`:93-99`) so there is one definition of the free starting kit. (Step 5b changes its ultimate test from Free Loadout to `ShopPricing.IsFreeNow`.)
    - Put the weapon back to `startingWeaponId`, **every** ability slot (Mobility, Equipment, Ultimate) to `StartingAbilityId(slot)`, and the armour to `SetArmorLevels(0, 0)`. The prefab ships all three slots empty, so this is Tudor's "back to empty" (answer 2).
    - Publish all in **one** Hashtable.
  - **`GoldWallet`:** `accrual = new GoldAccrual(StartingGold)`, which also drops the fractional carry. Then `IncomePerSecond = 0` and `PublishBalance()`.
  - **`UltimateCharge`:** `state.Clear()`.
  - **`LoadoutScreen`:** `ledger.Clear()`; `Close()` if open.
  - **`AbilityRunner`:**
    - `ForEachModule(m => m.Interrupt(InterruptReason.Died)); ResetCooldowns(); ForEachModule(m => m.OnRespawned());`
    - The comment says this is the same cleanup a death and respawn run, without publishing a death.
  - **`NetworkedDeployable`:** `public static void DestroyAllPlacedByLocalPlayer()` loops `FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None)`, and for each with `IsOwnerClient` calls `RequestDestroy()`, the single-destroyer path. The comment says fire fields are deliberately left: they last seconds.
- [ ] **4. `PlayerLifecycle`.**
  - Keep a `respawnRoutine` handle: both `StartCoroutine(RespawnPlayer(...))` sites assign it, and `RespawnPlayer` nulls it at its end.
  - Add `public void ResetForMatchStart(int team)`, owner only. Its doc comment is **the one home** for Decision 6's reset and kept lists. In this order:
    1. Stop `respawnRoutine`. Set `death = respawnStarted = false` and `deathCount = 0`. Hide the respawn and waiting panels and clear the note, **before** any move, or the coroutine could teleport the player again.
    2. Call `AbilityRunner.ResetForMatchStart()`, then `playerDisplacement.Cancel()`, then `NetworkedDeployable.DestroyAllPlacedByLocalPlayer()`.
    3. Call `PlayerLoadout.ResetForMatchStart()` **before** `playerHealth.ResetForRespawn()`, because armour capacity must drop to level 0 before the refill. In between, call `GoldWallet`, `UltimateCharge`, `PlayerOverheat.Clear()` (children, `true`) and `LoadoutScreen`.
    4. `TeleportToSpawnPoint(roomManager.teamSpawnPoints[team])`, guarded like `MoveToSpawnPoint`.
    5. `if (!isAlive) SetAlive(true);` and then `SetLastStandOut(false)`. An alive player gets **no** `AliveChanged`, which avoids a telemetry `respawn` line for everyone at the live instant.
    6. Log one `[MATCH] fresh start` line with team, position, gold and deathCount.
- [ ] **5. Green.** Expect **+2**.
- [ ] **6. Single-client Play Mode check.** List the actors first.
  - Set up:
    - F1 "+1000 Gold".
    - `SetWeapon` to a non-starting id.
    - `SetArmorLevels(1, 0)`.
    - `SetAbility(Equipment, <a mine id>)` and `SetAbility(Ultimate, 25)`.
    - `UltimateCharge.Fill()`.
    - Place a mine.
    - Die: a lethal enemy `DamageInfo` from a non-room actor, as in step 1.
  - While the countdown runs, call `ResetForMatchStart(ownTeam)` by eval.
  - **Expected:**
    - Gold = StartingGold.
    - The starting weapon; **all three ability slots empty** (Equipment included); armour 0/0.
    - Ultimate meter 0; `deathCount` 0.
    - Alive at `teamSpawnPoints[team]` (read `rb.position` after 0.5 s).
    - The mine is gone.
    - **No second teleport** when the original countdown would have ended.
- [ ] **7. Commit** the eleven files.

---

### Step 5: the countdown and going live (master), gates, reactions and joiners

**Files:** `MatchDirector.cs` (plus a new partial `MatchDirector.Live.cs` holding the countdown and going-live half, so the file stays readable), `BuildingManager.cs`, `Building capture.cs`, `RoomManager.cs`, `PlayerLifecycle.cs`, `GameplayConfig.cs`, `GameplayConfig.asset`.

- [ ] **1. The countdown's length** (Decision 22). In `GameplayConfig.cs`, directly after `killHeight` and before `[Header("Combat state")]`:

```csharp
        [Header("Match start (2.7b)")]
        [Tooltip("Seconds between the match being started - by the third team's first player arriving, or by the " +
                 "host's Start button - and it going live. 'Match starts in N' shows on every screen meanwhile, and " +
                 "nothing counts yet. Going live is a fresh start: zones, gold, loadouts and respawn timers reset. " +
                 "0 = no countdown.")]
        [SerializeField, Min(0f)] private float matchStartCountdownSeconds = 5f;
        public float MatchStartCountdownSeconds => matchStartCountdownSeconds;
```

  - Add the one line `  matchStartCountdownSeconds: 5` to `GameplayConfig.asset`, directly after `  killHeight: -10`. `git diff` on the asset shows exactly that line.
  - `PlayerLifecycle` gains `public GameplayConfig Config => gameplayConfig;`. The comment says the director reads the countdown length through the master's own player, because it has no Inspector fields.
- [ ] **2. Keys and readers** (`MatchDirector.Live.cs`). Each reads **the room directly**, like `IsEliminated`, because `RoomManager.OnJoinedRoom` runs first.
  - `public const string TeamsInMatchKey = "mTeams";` and `public const string LiveAtKey = "mLiveAt";`, each with a comment giving its lifetime (Decision 1).
  - `State` (`MatchStartRules.StartStateFor(mTeams present, mPhase present)`), `IsLive` (`mPhase` present), `IsCountingDown`, `TeamsFixed` (`mTeams` present).
  - `LiveAtMs`, and `CountdownSecondsShown` (`MatchStartRules.CountdownSecondsShown(PhotonNetwork.ServerTimestamp, LiveAtMs)`).
  - `TeamsInMatch`, `IsInMatch(team)`.
  - `IsOutOfPlay(zone)`, via `Map.CapitalTeamOf` and `MatchStartRules.IsCapitalOutOfPlay(IsLive, …)`.
  - `MayJoinTeam(team)`, via `MatchStartRules.MayJoin(TeamsFixed, …)`.
  - `TeamsWithPlayersNow` and `HostMayStartNow` (`PhotonNetwork.IsMasterClient && MatchStartRules.HostMayStart(TeamsFixed, members, teamless)`). Count through `PhotonNetwork.CurrentRoom.Players` (a struct enumerator, no allocation, as `MinimapView.UpdatePlayers` does), because the UI polls these every frame.
  - `public event System.Action LiveStateChanged;` covers the countdown starting, being cancelled, and the match going live.
  - `ReadPhase` returns `Warmup` when `mPhase` is absent. `lastWrittenPhase` and `lastAppliedPhase` start at `Warmup`.
- [ ] **3. Master: start, cancel, go live.**

```csharp
        // Not gameplay values. The master re-checks every 0.5 s in the warm-up (a callback alone can be missed - clock not
        // synced, snapshot not read yet - and would strand a full room there) and every frame while counting down (to go
        // live on the frame its clock reaches mLiveAt). After sending a countdown or cancel write it waits for the room
        // to echo it - or 1 s, in case a check-and-set was refused - so one decision is never sent twice.
        private const float StartCheckIntervalSeconds = 0.5f;
        private const float EchoWaitSeconds = 1f;
        private float nextStartCheck;
        private float waitForEchoUntil = -1f; // cleared by OnRoomPropertiesUpdate when mTeams/mLiveAt/mPhase arrive
        private bool liveWritten;             // master: the live write was sent

        private void Update()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || liveWritten || IsLive
                || Time.unscaledTime < waitForEchoUntil)
                return;

            if (IsCountingDown)
            {
                if (MatchStartRules.CountdownShouldCancel(TeamsInMatch, CountMembers()))
                    CancelCountdown();
                else if (MatchStartRules.HasReached(PhotonNetwork.ServerTimestamp, LiveAtMs))
                    GoLive(TeamsInMatch);
                return;
            }

            if (Time.unscaledTime < nextStartCheck)
                return;
            nextStartCheck = Time.unscaledTime + StartCheckIntervalSeconds;
            int[] members = CountMembers();
            if (MatchStartRules.StartsCountdownAutomatically(TeamsFixed, members))
                StartCountdown(MatchStartRules.TeamsWithPlayers(members));
        }

        /// <summary>The host's Start button calls this directly - the host IS the master, so this is a local call and no
        /// RPC is needed. Starts the countdown; refused (and the button hides next frame) if mastership moved or the rule
        /// no longer holds.</summary>
        public void HostStartMatch()
        {
            if (!HostMayStartNow) { Debug.LogWarning("[MATCH] Start refused: not the master, already started, or not exactly two teams."); return; }
            StartCountdown(MatchStartRules.TeamsWithPlayers(CountMembers()));
        }

        private void StartCountdown(int[] teams)
        {
            GameplayConfig config = LocalPlayerConfig(); // the master's own PlayerLifecycle.Config
            if (config == null || PhotonNetwork.ServerTimestamp == 0 || teams.Length < 2)
                return; // player not spawned / clock not synced yet - the next poll tries again

            int liveAt = MatchStartRules.CountdownEndsAt(PhotonNetwork.ServerTimestamp, config.MatchStartCountdownSeconds);
            var props = new Hashtable { { TeamsInMatchKey, teams }, { LiveAtKey, liveAt } };
            // Check-and-set on the ABSENT key (as MatchTelemetry's identity claim): two masters racing across a switch
            // can't both start a countdown, and the teams can't be fixed twice.
            var expectedAbsent = new Hashtable { { TeamsInMatchKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent))
                return;

            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            // A host-started match holds six, not nine (Decision 7).
            PhotonNetwork.CurrentRoom.MaxPlayers = teams.Length * RoomManager.TeamSize; // cast as RoomManager does
            Debug.Log($"[MATCH] countdown: teams [{string.Join(",", teams)}], live at {liveAt}");
        }

        /// <summary>Tudor, 2026-09-18: a team in the countdown emptied - back to the warm-up. Check-and-set expecting
        /// mPhase still absent: a countdown that already went live is never cancelled.</summary>
        private void CancelCountdown()
        {
            var props = new Hashtable { { TeamsInMatchKey, null }, { LiveAtKey, null } };
            var expected = new Hashtable { { PhaseKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expected))
                return;
            waitForEchoUntil = Time.unscaledTime + EchoWaitSeconds;
            PhotonNetwork.CurrentRoom.MaxPlayers = MatchStartRules.TeamCount * RoomManager.TeamSize;
            Debug.Log("[MATCH] countdown cancelled: a team in it emptied - back to the warm-up");
        }

        private void GoLive(int[] teams)
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || liveWritten || IsLive)
                return;

            // Territory FIRST, match keys SECOND, same frame - the frame this master's clock reached mLiveAt. Photon
            // delivers one client's writes in order, so every client has applied the reset before it sees the match go
            // live - which is what its own fresh start (ReactToRoomState) and every knockout check (MasterRecompute,
            // gated on the echoed mPhase) rely on. Why not each client's own clock: see Decision 5.
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null || !buildings.ResetForMatchStart(teams))
                return; // snapshot not read / clock not synced yet - the next frame tries again

            MatchPhase phase = teams.Length >= MatchStartRules.TeamCount ? MatchPhase.ThreeTeams : MatchPhase.TwoTeams;
            var props = new Hashtable { { TeamsInMatchKey, teams }, { PhaseKey, (int)phase },
                                        { EliminatedKey, new int[0] }, { WinnerKey, -1 } };
            // Check-and-set on the ABSENT mPhase: the match goes live once only, whichever master gets there.
            var expectedAbsent = new Hashtable { { PhaseKey, null } };
            if (!PhotonNetwork.CurrentRoom.SetCustomProperties(props, expectedAbsent))
                return;

            liveWritten = true;
            lastWrittenEliminated = new List<int>();
            lastWrittenPhase = phase;
            lastWrittenWinner = -1;
            writesAwaitingEcho++;
            Debug.Log($"[MATCH] live: teams [{string.Join(",", teams)}], {phase}");
        }
```

  - `GoLive(int[] teams)` also writes `mTeams` (the same value the countdown fixed). That keeps it callable by reflection in single-client checks that skip the countdown.
  - `OnRoomPropertiesUpdate` sets `waitForEchoUntil = -1f` whenever `mTeams`, `mLiveAt` or `mPhase` is in the change set.
  - Reset `waitForEchoUntil` and `liveWritten` in `OnLeftRoom` and `OnMasterClientSwitched`. A new master mid-countdown needs nothing else: its own `Update` sees `IsCountingDown` and carries on (Decision 22).
- [ ] **4. `BuildingManager.ResetForMatchStart(IReadOnlyList<int> teamsInMatch) → bool`.** Master only. It returns false if `current == null` or `ServerTimestamp == 0`.
  - `start = TerritorySnapshot.Starting(ZoneCount, <capitals>, teamsInMatch, ServerNowMs())`.
  - `Write(start)` from scratch, **not** `WriteBasis`. The comment says a warm-up capture still echoing is deliberately thrown away.
  - For each registered capture: `capture.ResetForMatchStart(start.OwnerOf(zone))`.
  - `territoryWinAnnounced = false`.
  - **`BuildingCapture.ResetForMatchStart(int owner)`** is master only: `ResetToOwner(owner); RepublishProgressNow();`. The comment says no warm-up progress can complete after this.
- [ ] **5. Gates.**
  - **`MasterRecompute`** opens with `if (!IsLiveIn(room props)) return;` (`mPhase` present) and a comment: the room's echoed flag, never `liveWritten` and never a client's own clock. The countdown is still warm-up.
  - **`CheckTerritoryWin`** uses `MatchPhaseRules.TerritoryWinner(director.IsLive, owners of capitals where !director.IsOutOfPlay(capital))`. Delete `CountTeamsWithPlayers`. The final comment replaces the lone-player wording: "before the match is live nothing counts; once live, at least two teams were in it".
  - **`BuildTeamStatuses`:**
    - `InMatch` comes from `mTeams`.
    - `HoldsAnyCapitalInPlay` = owns a capital whose team is in `mTeams`.
    - `LogTelemetry`'s `teamsRemaining` counts the teams in the match.
- [ ] **6. Every client: `ReactToRoomState`.**
  - Add `TeamsInMatchKey` and `LiveAtKey` to `touchesMatchState`.
  - On the **teams-fixed edge** (the countdown write arrives, `!firstRead`): `FindFirstObjectByType<RoomManager>()?.EnsureLocalTeamInMatch()`, so a joiner who raced into the left-out team plays the countdown on a real team.
  - On the **live edge** (`!firstRead && live && !lastAppliedLive`): find the local `PlayerLifecycle`, then `int team = FindFirstObjectByType<RoomManager>()?.EnsureLocalTeamInMatch() ?? myTeam;` then `lifecycle.ResetForMatchStart(team)`.
  - A player who joined during the countdown is not `firstRead` at the live edge, so they get the same fresh start. It changes nothing for someone who just spawned.
  - Raise `LiveStateChanged` on `firstRead`, and whenever `TeamsFixed`, `LiveAtMs` or `IsLive` changes.
  - The existing `ThreeTeams → TwoTeams` edge is untouched. The live edge is `Warmup → …`, so a host start never triggers the Tier-3 reset or the "two teams left" banner.
  - `OnLeftRoom` resets `lastAppliedLive`, the teams-fixed memory, `waitForEchoUntil`, `liveWritten` and the phases to `Warmup`.
- [ ] **7. `RoomManager`.**
  - In `PickSmallestTeam`, `if (director != null && !director.MayJoinTeam(i)) continue;` replaces the `IsEliminated` skip. Update both warning texts.
  - Add `public int EnsureLocalTeamInMatch()`:
    - If the teams are fixed and the local team isn't among them: `PickSmallestTeam()`, then `UpdateNetworkProperties(picked)`, log `[TEAM] re-picked ... (joined the left-out team as the host started)`, and return `picked`.
    - Otherwise return the current team. Idempotent.
- [ ] **8. `PlayerLifecycle.PlayerDied`:**
  - `IsLastStandDeath(MatchDirector.Instance != null && MatchDirector.Instance.IsLive, capitalHeld)`. A countdown death is a warm-up death.
  - The countdown branch becomes `else if (!respawnStarted)`, because a warm-up death with the capital lost must count down.
- [ ] **9. Green.** Expect **+0** (the countdown's pure rules landed in step 3).
- [ ] **10. Single-client Play Mode check.** List the actors first.
  - **Alone:**
    - `IsLive` false and `Phase == Warmup`.
    - `SetCaptured(6, 1, 0, 0)` by eval, then a lethal enemy hit: **respawn panel, not waiting**. `lastStand` absent or false; `mElim` absent.
  - **The cancel path:** call `StartCountdown(new[] { 0, 1 })` by reflection. `mTeams [0,1]` and `mLiveAt` (≈ now + 5000) appear, and `MaxPlayers` goes to 6. Team 1 is empty, so the next frame **cancels**: both keys disappear, `MaxPlayers` goes back to 9, the cancel line is logged, and **nothing was reset** (gold, position and loadout unchanged).
  - **The live path, skipping the countdown:** call `GoLive(new[] { 0, 1 })` by reflection:
    - Room: `mTeams [0,1]`, `mPhase 2`; `tOwn[6]=0`, `tOwn[7]=1`, `tOwn[8]=-1`.
    - Own player fresh-started as in step 4.
    - `IsOutOfPlay(8)` true.
    - Log order: `[TOWER] 8 -> neutral` before `[MATCH] fresh start`.
  - Call `GoLive` again: refused, and nothing is written. Read `writesAwaitingEcho` and the telemetry.
  - The countdown reaching the moment is checked with two clients in step 11 (A3): alone, the empty second team always cancels it.
- [ ] **11. Commit** the eight files. The body gives the order argument (territory first, then keys, at the timestamp) in two sentences.

---

### Step 5b: the warm-up sandbox shop (Tudor answer 2)

**Files:** `Match/Rules/ShopRules.cs`, `UI/ShopPricing.cs`, `UI/LoadoutScreen.cs`, `Player/PlayerLoadout.cs`, `Assets/Tests/ShopRulesTests.cs`.

- [ ] **1. Red.** Add to `ShopRulesTests.cs`; it fails to compile until `IsFree` exists.

```csharp
        [Test]
        public void TheShopIsFreeInTheWarmupOrWithFreeLoadout()
        {
            // Tudor, 2026-09-18: before the match goes live the shop is a sandbox - free, unlimited, resettable.
            Assert.IsTrue(ShopRules.IsFree(freeLoadout: false, matchLive: false), "the warm-up, countdown included");
            Assert.IsFalse(ShopRules.IsFree(false, true), "the real economy once live");
            Assert.IsTrue(ShopRules.IsFree(true, true), "Free Loadout keeps the whole match free, as before");
            Assert.IsTrue(ShopRules.IsFree(true, false));
        }
```

- [ ] **2. The one home.** `ShopRules.IsFree(bool freeLoadout, bool matchLive) => freeLoadout || !matchLive;`. Its doc comment says: this is the only place "is the shop free right now?" is decided; the warm-up is a sandbox so testers can experiment (Tudor); going live empties every loadout (`PlayerLifecycle.ResetForMatchStart`), so nothing bought free survives into the match.
- [ ] **3. The one gatherer.** `ShopPricing.IsFreeNow(GameplayConfig config) => ShopRules.IsFree(config == null || config.FreeLoadout, MatchDirector.Instance != null && MatchDirector.Instance.IsLive);`.
  - A missing config fails open to free, as today.
  - A missing director reads as the warm-up, so also free (a test scene without one).
- [ ] **4. `ShopContext`.**
  - Rename the field **`FreeLoadout` → `IsFree`**, and the constructor parameter with it, so the name stops lying. Update every reader in `LoadoutScreen` (`:579-591`, `:673`, `:704`, `:812`, `:850`, `:928`) and `ShopPricing` (`:43-45`, `:71`). The purchase and reset handlers need no other change: they already buy without gold or gate, record nothing, and reset without refund while it is true.
  - Add `public readonly bool IsWarmupSandbox`: free because the match isn't live, and Free Loadout is off.
  - `StatusText()` becomes `IsFree ? (IsWarmupSandbox ? "Free (warm-up)" : "Free (test mode)") : ReasonText(Check(0), 0)`. It sits beside the existing hardcoded shop strings, which live here, not on `UiTheme`.
  - `ShopPricing.Build` sets both from `IsFreeNow(config)` and `MatchDirector.Instance?.IsLive`.
- [ ] **5. `LoadoutScreen`'s header change check** (`:582`) also compares `IsWarmupSandbox`, so the header re-draws the moment the match goes live even with Free Loadout on. Rename `lastDisplayedFreeLoadout` to match.
- [ ] **6. `PlayerLoadout.StartingAbilityId`** (extracted in step 4): the ultimate's "starts empty" test becomes `!ShopPricing.IsFreeNow(gameplayConfig)` instead of `!gameplayConfig.FreeLoadout`. At spawn in the warm-up, the prefab's starting ultimate is handed out, as a free shop would. At the live reset the shop is no longer free, so it starts empty. The prefab ships it empty either way today.
- [ ] **7. Green.** Expect **+1**.
- [ ] **8. Single-client Play Mode check.** Set `GameplayConfig › Free Loadout` **off by reflection** for this check, restore it before leaving Play Mode, then confirm `git diff -- Assets/Gameplay/Config/GameplayConfig.asset` is empty (Rule 13).
  - **In the warm-up**, with 0 gold, standing outside your territory:
    - Buy an ultimate, two armour upgrades and a weapon-tree node: all succeed, gold stays 0, and no `Spent` event fires.
    - Reset Armor and Reset Weapon both work, with no refund (gold stays 0).
    - The header reads "Free (warm-up)".
  - Then `GoLive(new[] { 0, 1 })` by reflection:
    - The loadout is empty and the armour is 0/0 (step 4).
    - The header shows the real gate ("Go to a zone your team owns", or the out-of-combat countdown).
    - A purchase now costs gold: with 0 gold, the ultimate is refused with "Need N more gold".
- [ ] **9. Commit** the five files.

---

### Step 6: out of play (the cut capital)

**Files:** `Building capture.cs`, `CaptureRingState.cs`, `CaptureRingView.cs`, `MinimapView.cs`, `UiTheme.cs`, `UiTheme.asset`, `CaptureRingStateTests.cs`.

- [ ] **1. Red.** `CaptureRingStateTests.AnOutOfPlayZoneShowsNoArcWhateverTheRoomSays`: a capturing progress for team 0 with `outOfPlay: true` gives `Phase == Idle`, `!ShowsArc`, `OutOfPlay`, `OutlineTeam == Neutral`, and `!UnderAttack`.
- [ ] **2. `CaptureRingState`.**
  - Add a `public readonly bool OutOfPlay`, with the constructor gaining a final `bool outOfPlay = false`.
  - Add `From(..., int nowMs, bool outOfPlay = false)`: out of play returns an Idle state with `OutOfPlay` set, before any other branch.
- [ ] **3. `UiTheme`.** Add a `[Header("Match start (2.7b)")]` section with one field:

```csharp
        [Tooltip("A capital nobody is playing for - the third capital when the host starts a two-team match. Its ring on the " +
                 "ground and its minimap bubble take this colour. Keep it darker and more see-through than the neutral grey, " +
                 "so it reads as closed, not as ground you can take.")]
        public Color outOfPlayZoneColor = new Color(0.12f, 0.12f, 0.12f, 0.45f);
```

  Add the matching line to `UiTheme.asset` by YAML edit: `outOfPlayZoneColor: {r: 0.12, g: 0.12, b: 0.12, a: 0.45}`.
- [ ] **4. `BuildingCapture`.**
  - A cached `private static readonly System.Func<int, bool> ZoneOutOfPlay = zone => MatchDirector.Instance != null && MatchDirector.Instance.IsOutOfPlay(zone);`
  - Use it in `TeamMayCaptureNow` (5-argument `MayCapture`), in `OnTriggerEnter` (`MayCapture(team, id, CurrentOwners, null, ZoneOutOfPlay)`), and in `RefreshRingView` (`outOfPlay: ZoneOutOfPlay(buildingID)`).
  - Drains go through `TeamMayCaptureNow`, so they are covered.
- [ ] **5. `CaptureRingView.Refresh`.** `state.OutOfPlay` gives `edgeColor = theme.outOfPlayZoneColor`, with no pulse.
- [ ] **6. `MinimapView`.**
  - `ZoneUi.OutOfPlay`.
  - `RecolourOwnership` sets each zone's flag from `MatchDirector.Instance.IsOutOfPlay(zone)` and its fill from `outOfPlayZoneColor`.
  - `ApplyLinkStyle` shows a link only if neither end is out of play.
  - `UpdateZones` passes `outOfPlay` to `CaptureRingState.From`.
  - Subscribe to `MatchDirector.LiveStateChanged`, setting `ownershipDirty = true`, in `TryBuild`; unsubscribe in `OnDestroy`.
  - Rewrite the class comment's PHASES paragraph (`:58-59`).
- [ ] **7. Green.** Expect **+1**.
- [ ] **8. Single-client Play Mode check.** After `GoLive([0,1])` by reflection:
  - Stand in zone 8 for 25 s. Owner stays -1, and **no** `PublishCaptureProgress` for zone 8. Diff `CaptureProgressPublishCount` and read the room's `cTeam[8]`.
  - Zone 8's ring `edge.startColor` equals the theme colour.
  - The minimap zone-8 `Fill.color` equals the theme colour; every `Link 8-*` / `Link *-8` half is inactive.
  - Zone 0 is still capturable.
  - Capture the minimap corner at 616×576 to SCRATCH. **The controller looks at it.**
- [ ] **9. Commit** the seven files.

---

### Step 7: the final two in play (respawn capital, adoption, waiting)

**Files:** `MatchDirector.cs`, `PlayerLifecycle.cs`.

- [ ] **1. Director queries.** Each reads `Current` plus the in-play capitals.
  - `TeamHasACapital(team)`, through `CountsAsHavingACapital(Phase, own, any)`.
  - `RespawnCapitalOf(team)` builds `CapitalHold[]` for the in-play capitals from `Current.OwnerOf` and `HeldSinceMs`, then calls `MatchPhaseRules.RespawnCapital`.
  - `SpawnCapitalFor(team)` returns `MatchPhaseRules.SpawnCapitalFor(Phase, IsEliminated(team), Map.CapitalOf(team), RespawnCapitalOf(team))`.
  - **Delete `CapitalOf`** and its "adoption hook" comment. Update the class comment: adoption is built.
- [ ] **2. `PlayerLifecycle`.**
  - **`PlayerDied`** uses `hasCapital = director.TeamHasACapital(teamID)`. The waiting branch is unchanged in content.
  - **`RespawnPlayer`**, after the wait: `int capital = director.SpawnCapitalFor(teamID); if (capital == TerritoryMap.Neutral)`:
    - Convert to waiting: `SetRespawnPanelVisible(false)`, `ShowWaitingPanel()`, `SetLastStandOut(true)`, `respawnStarted = false`, `respawnRoutine = null`, `yield break`. `death` stays true.
    - Comment: last man standing, the dead can't respawn; a knocked-out team never does.
  - **`ChooseSpawnPoint(roomManager, teamID, capital, out atUnderAttack)`** uses `spawnIndex = Map.CapitalTeamOf(capital)`: `teamSpawnPoints[spawnIndex]`, and the under-attack spawn `[spawnIndex]` when `Current.OwnerOf(capital) == teamID && IsUnderAttack(capital)`.
  - **`UpdateRespawnNote`** uses the same capital.
  - **`CheckForCathedralCapture`** respawns when `director.RespawnCapitalOf(teamID) != Neutral && !director.IsEliminated(teamID) && !respawnStarted`: any capital in play. Rewrite its comment.
  - **`MoveToSpawnPoint`** (a fall, or the three-to-two trip home) uses `CapitalTeamOf(SpawnCapitalFor(team))`, falling back to the own team when Neutral.
  - **Delete `TryGetOwnCathedral`.**
- [ ] **3. Green.** Expect **+0**.
- [ ] **4. Single-client Play Mode check.** Run `GoLive([0,1,2])` by reflection: teams 1 and 2 are empty and in the match, holding their capitals.
  1. `SetCaptured(6, 1, 0, 0)`: team 0 is in its last stand. `Phase` stays ThreeTeams.
  2. `SetCaptured(7, 0, 0, 0)` adopts team 1's capital:
     - `RespawnCapitalOf(0) == 7`.
     - Team 1 is empty with no capital, so it is **out** (`mElim [1]`, `TwoTeams`, Tier-3 reset).
  3. A lethal enemy hit on the local player gives a **countdown**, not waiting, because team 0 holds 7. They respawn at `teamSpawnPoints[1]`: record `rb.position`.
  4. `SetCaptured(7, 2, 0, 0)`: team 0 has no capital and team 2 holds 8, so team 0 is **out at once**. Over, winner 2.
- [ ] **5. Commit** the two files.

---

### Step 8: the warm-up line, the countdown, the host's Start button, the live toast

**Files:** `UiTheme.cs`, `UiTheme.asset`, `PlayerHud.cs`, `MatchStartPanel.cs` (create), `MatchDirector.cs`.

- [ ] **1. `UiTheme`, under "Match start (2.7b)".** Every field has a plain tooltip; add each to the asset by YAML edit (11 lines).

  | Field | Default | Tooltip gist |
  |---|---|---|
  | `warmupWaitingText` | `Warm-up: nothing counts yet and the shop is free. The match starts when all three teams have a player.` | Shown to everyone during the warm-up, fewer than two teams |
  | `warmupHostText` | `Warm-up: two teams are here. Start now with two teams, or wait for a third.` | The host, exactly two teams |
  | `warmupGuestText` | `Warm-up: waiting for the host to start, or for a third team.` | Everyone else, exactly two teams |
  | `matchCountdownText` | `Match starts in {0}` | Shown to everyone during the countdown; `{0}` is the whole seconds left and must stay in the text. The countdown's length is `GameplayConfig › Match Start Countdown Seconds` |
  | `matchStartButtonText` | `Start match (2 teams)` | The host's button |
  | `matchStartButtonColor` | `(0.16, 0.45, 0.25, 0.95)` | Its fill; it must stand out from the grey Loadout button |
  | `matchStartButtonSize` | `(260, 52)` | Canvas units |
  | `warmupTopOffset` | `80` | Top edge to the warm-up line; clears the toast above it |
  | `warmupLineSize` | `(900, 64)` | The line's box; text wraps inside it; the button sits right below it |
  | `matchLiveToastText` | `The match is live! Zones, gold, loadouts and respawn timers are reset.` | Toast on going live, three teams |
  | `matchLiveTwoTeamsToastText` | `The match is live with two teams: lose your capital and you're out.` | Toast on a host start |

- [ ] **2. `PlayerHud`** (local only, in `BuildUi`):
  - A warm-up label built with its own `AddLabel`, so it matches the HUD's font, shadow and outline. Anchor it top-centre at `-warmupTopOffset`, size `warmupLineSize`, word wrap on, full size like the toast.
  - `MatchStartPanel.Create(transform, theme, label)`.
  - `public void ShowMatchLiveToast(bool twoTeams) => ShowToast(twoTeams ? theme.matchLiveTwoTeamsToastText : theme.matchLiveToastText);`
- [ ] **3. `MatchStartPanel`** (namespace `Overpower.UI`):
  - Builds its own button canvas, copying `LoadoutScreen.Builder.BuildToggleButtonCanvas` (`:502-548`): overlay, `overrideSorting` at order -10, a `CanvasScaler` from the theme, a **`GraphicRaycaster`** so `PlayerInputRouter.pointerOverUi` swallows the click instead of firing a shot, `TMP_DefaultControls.CreateButton`, and `navigation = None`.
  - The button sits under the line box, coloured `matchStartButtonColor`, with body-size text.
  - `Update` computes `MatchStartRules.WarmupMessageFor(d.State, d.TeamsWithPlayersNow, PhotonNetwork.IsMasterClient)`. For `Countdown` the text is `string.Format(theme.matchCountdownText, d.CountdownSecondsShown)`: it counts on each client's own synced server clock, so every screen shows the same number. The label's text and active state change **only when the message or the number changes**. The button is active only while `d.HostMayStartNow`, so it hides for the countdown.
  - `onClick` calls `MatchDirector.Instance?.HostStartMatch()`, which starts the countdown.
  - The class comment says: the host is the master, the click is a local call, so there is no RPC; the countdown is a server time in the room, so every client counts down to the same moment without one either.
- [ ] **4. `MatchDirector.ReactToRoomState`.** At the live edge, after `ResetForMatchStart`: `localView?.GetComponent<PlayerHud>()?.ShowMatchLiveToast(phase == MatchPhase.TwoTeams)`.
- [ ] **5. Green.** Expect **+0**.
- [ ] **6. Single-client check.**
  - Alone: the label reads `warmupWaitingText` and there is no button. Capture the top of the screen at 616×576 to SCRATCH.
  - `StartCountdown([0,1])` by reflection, with an in-process coroutine recording the label's text every frame. Between the countdown's echo and the cancel's echo (about one network round trip), it reads "Match starts in 5". The cancel then puts back `warmupWaitingText`.
  - `GoLive([0,1])` by reflection: the label hides and the two-team toast shows. Capture it.
  - **The controller looks at both captures.** The button's look and a full 5-4-3-2-1 countdown are captured in step 11 (A1, A3), with the Editor as host.
- [ ] **7. Commit** the five files.

---

### Step 9: telemetry (the warm-up, going live, adoption) and the report

**Files:** `TelemetryKeys.cs`, `MatchTelemetry.cs`, `MatchDirector.cs`, `TelemetryLog.cs`, `PhaseTimeline.cs`, `TelemetryAggregator.cs`, `ReportTables.cs`, `HtmlReportWriter.cs`, `PhaseTimelineTests.cs`, `TelemetryPhaseKeysTests.cs`, one aggregator test, and `docs/superpowers/specs/2026-09-16-telemetry-design.md`.

**What the report will see.** The countdown is part of the warm-up: "live" is the master's `phase 1`/`phase 2` line, logged in its live write at `mLiveAt`, not when the countdown starts.

| Match | Raw `phase`/`elimination` lines | Warm-up | Phase 1 | Phase 2 |
|---|---|---|---|---|
| Three teams, auto-live | `0` (anchor), `1` at live `remain [0,1,2]`, `elimination` + `2` at the first knockout, `3` at the end | `[0, tLive)`, left out | `[tLive, tKO)` | `[tKO, end]` |
| Host start | `0`, `2` at live `remain [0,1]`, `elimination` + `3` | `[0, tLive)`, left out | empty `[tLive, tLive)` | `[tLive, end]` |
| Never went live | `0` only | the whole log | empty | none (the header warns) |
| Legacy log (phase-1 anchor) | unchanged | none | as today | as today |

- [ ] **1. Red: `PhaseTimelineTests` (+5).** Use the file's own `Session`/`NewTempFolder` helpers and the `remain` key.
  - `AWarmupThenAThreeTeamStartOpensPhase1AtLive`:
    - Lines: anchor `num 0` at -1, `num 1` at 60, `num 2` at 200, a sample at 300.
    - Expect `WentLive`, `LiveSeconds 60`, `Warmup [0,60)`, `WholeMatch [60,300]`, `Phase1 [60,200)`, `Phase2 [200,300]`.
  - `AHostStartedTwoTeamMatchIsAllPhase2`:
    - Lines: `0` at -1, `2` at 45, a sample at 200.
    - Expect `LiveSeconds 45`, `TransitionSeconds 45`, `Phase1.Contains(100) == false`, `Phase2 [45,200]`, `WholeMatch.Start 45`.
  - `ASessionThatNeverWentLiveIsAllWarmup`: the anchor only, a sample at 80. Expect `!WentLive`, `Warmup [0,80]`, `!HasPhase2`, and `Phase1.Contains(10) == false`.
  - `AWarmupEliminationIsNeverTheTransition`: `0`, an elimination at 20, `1` at 60, a sample at 100. Expect `TransitionSeconds == null`.
  - `ALateDuplicateWarmupAnchorDoesNotMoveLive`: `0` at -1, `1` at 30, `0` at 50, a sample at 90. Expect `LiveSeconds 30`.
- [ ] **2. Red: `TelemetryPhaseKeysTests`.**
  - `PhaseOneAnchorLineShape` becomes **`WarmupAnchorLineShape`** (`num 0`).
  - Add `AdoptLineShape` (+1): `{"e":"adopt","t":..,"tm":0,"zone":7}`.
- [ ] **3. Red: one aggregator test** (+1), `AWarmupDeathIsNotCountedInTheMatch`. Use the death-line shape from `TelemetryAggregatorReviewFixesTests` (~`:222`): a death at 20 and one at 80, with live at 60. `BuildSet(...).WholeMatch.Deaths.Count == 1`, and the header's `WarmupSeconds == 60`.
- [ ] **4. Runtime.**
  - `TelemetryKeys.Adopt = "adopt"`, added to `TelemetryLog.KnownEventNames`.
  - `MatchTelemetry` logs the anchor as **`LogPhase(0, …)`** (`:430`). Update the comments at `:423-429` and `:595-627`: phase 0 is the warm-up, 1 or 2 is going live.
  - `LogAdoption(int team, int zone)` is master-only.
  - `MatchDirector.GoLive` (the live write at `mLiveAt`, not `StartCountdown`) calls `MatchTelemetry.Instance?.LogPhase((int)phase, teams)` right after the write. A countdown start or cancel logs a `marker` line with the note `countdown start` / `countdown cancelled` (the existing F1 marker shape; no new event type), so the report's Markers section shows them.
  - `MatchDirector.HandleOwnershipChanged`: on the master, when live, if `MatchPhaseRules.IsAdoption(newOwner, Map.CapitalTeamOf(zone), <other in-play capitals newOwner holds>)`, call `LogAdoption`.
- [ ] **5. `PhaseTimeline.From`, two passes.**
  - **Legacy** means no `phase` event with `num == 0`: `LiveSeconds = 0` and `WentLive = true`. **Behaviour identical to today.**
  - **New-style logs:** live is the first `phase` event with `num >= 1`; a `t < 0` is clamped to 0. The transition is the first `num >= 2` at `t >= LiveSeconds`. The elimination fallback also needs `t >= LiveSeconds`.
  - Windows:
    - `Warmup = LiveSeconds > 0 ? [0, live) : null`.
    - `WholeMatch = [live, end]`.
    - `Phase1 = [live, transition)`, or `[live, end]`.
    - If never live: `Warmup = WholeMatch = [0, end]` and `Phase1 = [end, end)`.
  - Add `LiveSeconds`, `WentLive`, `HasWarmup` and `Warmup`, and update the class comment.
- [ ] **6. Consumers.**
  - `ReportSet` gains `TransitionSeconds`, `LiveSeconds` and `WentLive`, set in `BuildSet`.
  - `ReportHeader` gains `WarmupSeconds` and `NeverWentLive`, set in `Build` like `EliminationFallbackUsed`.
  - `HtmlReportWriter:122-126` must take `transitionSeconds` from `ReportSet.TransitionSeconds`. Phase 1 no longer starts at 0.
  - The header prints "Warm-up: N s before the match went live (left out)", or a red "never went live" warning.
  - `ExtractPositions` (`:80`) skips `t < LiveSeconds` when `WentLive`.
  - `CsvReportWriter` needs no change: an empty Phase 1 window is already handled (the TimeWindow item-9 fixes).
  - The warm-up sandbox's free purchases carry `free: true`. The Free Loadout warning (`TelemetryAggregator.cs:291`) is already window-scoped, so they never reach a match that went live. A never-live report shows them under its own red warning; the implementer checks the two warnings read sensibly together.
- [ ] **7. The spec.** Add a short "Warm-up (2.7b)" paragraph to Part 3 of `2026-09-16-telemetry-design.md`, stating the table's rule.
- [ ] **8. Green.** Expect **+7**. **Every existing telemetry test must pass unchanged**; a failure means the legacy detection is wrong. Stop and report it rather than editing an old test.
- [ ] **9. Commit** the twelve files.

---

### Step 10: the assumptions file and progress (outside the repo; never committed)

- [ ] **1. Replace the answered QUESTIONs.** Find each by its opening words; the line numbers are hints from the read. Each becomes one `[T]` line naming the step's commit.
  - **`## Questions for you` › Territory and match** (~`:113-117`), the bullet starting *"**Superseded, Task 2.7 review:** a 2-player test does **not** start in the two-team phase"*. New line: two players now get a warm-up, and the host's Start begins the two-team phase.
  - **`## Phase 2 — match loop`** (~`:310-314`), *"**QUESTION for you:** when a team is knocked out, should its capital and lane leave play"*. New line: it stays in play, and a knocked-out team's capital can be adopted.
  - The same section (~`:416-434`), *"**Task 2.7 review, 2026-09-18 - two QUESTIONS for you**"*. This covers going live, and a leaver. Its option (c) holds the **"a lone player can neither be knocked out"** overstatement, which goes with it.
  - The same section (~`:435-441`), *"**Whole-phase review, 2026-09-18 - one more QUESTION**"*: the flank-reset bounty. [T] No, as built.
  - The same section (~`:495-500`), *"**QUESTION for you (play terms), from the last 2.7 review: a match can end with nobody winning.**"* [T] No draw; last man standing.
  - **`## Invulnerability and raybeam rework`** (~`:2247-2256`), *"**QUESTION for you (play terms): should being shot at while your Invulnerability shield is up count**"*. [T] Yes, step 1.
- [ ] **2. Under the new 2.7b heading,** add every `[C]` from this plan's Decisions, in play terms. That includes:
  - the countdown's cancel and host-leaving behaviour;
  - teams fixed at the countdown's start;
  - joiners during the countdown;
  - the shop's "Free (warm-up)" header;
  - "the last to die" read from death stamps;
  - Open #5 and #6.

  Tudor's four afternoon answers are `[T]` lines naming the steps that built them (answer 1 → step 3; 2 → steps 4 and 5b; 3 → steps 5 and 8; 4 → step 3).
- [ ] **3. Add commit hashes** to the top block, "Match start and the final two (your answers…)".
- [ ] **4. Add a `progress.md` row.** List the `## ` headings to confirm none was lost.

---

### Step 11: multi-client verification

**Before building:**
- Run `git status --short`. **It must be clean** (Rule 14).
- Harness §2:
  1. Switch the runtime server **on only for the build**: `set_runtime_pipeline_settings --settings '{"enableInBuilds":true}'`.
  2. `set_build_settings developmentBuild:true`.
  3. Build `Builds/Client2` (§3).
  4. **Switch the server back off at once.**
  5. `git status --short` must be clean again. Revert anything the build settings dirtied. **Never commit it on.**
- **Client3** is a folder copy (§8), not a rebuild. Delete its stale `.unity-pipeline-runtime-port`.

**Recording:**
- Install per-frame recorders on **every receiving client before acting** (CODING-STANDARDS §6). Use `two-client-eval/sync_recorder_tpl.cs`, extended to read `mTeams`, `mLiveAt`, `mPhase`, `mElim`, `tOwn`, own gold, `rb.position`, alive, `lastStand`, `lastStandAt`, `deathCount`, ultimate meter, armour levels, the loadout ids, the warm-up label's text, and `PhotonNetwork.ServerTimestamp`.
- List the actors before each check.
- A match that ends closes its room: leave and rejoin a fresh room between checks. The leave also re-checks step 2: `gold` is absent after leaving.

**Part A: two clients.** The Editor (actor 1) is the host on team 0; Client2 is on team 1.

| # | Check | Expected |
|---|---|---|
| A1 | Warm-up | Both `IsLive` false and `Phase Warmup`. The Editor's `HostMayStartNow` is true, Client2's false. Client2's label is `warmupGuestText` (read by reflection). **Capture the Editor's warm-up line and Start button** to SCRATCH. |
| A2 | Nothing counts | `SetCaptured(7,0,0,0)`, then kill Client2 with a real shot or a lethal hit from actor 1 on Client2's own client. Client2 gets a **respawn countdown, not waiting**. No `mElim`, not Over. |
| A3 | Host start: countdown, then live | Invoke the button's `onClick` on the Editor. **Countdown:** `mTeams [0,1]` and `mLiveAt` (≈ press + 5000 ms) arrive on both; `MaxPlayers` 6; the button hides; both labels read 5, 4, 3, 2, 1 on their own clocks (the number changes within one frame of each other at every whole second, in `ServerTimestamp` terms); **nothing resets during it**. **Live:** the master writes at its first frame with `ServerTimestamp ≥ mLiveAt`. On **both** recorders, the `tOwn` reset frame (6→0, 7→1, 8→-1) comes **at or before** the `mPhase` frame, and that frame is at most one round trip after `mLiveAt`. Then gold 0, own spawn, `deathCount` 0, ultimate 0, armour 0/0, starting weapon, **all three ability slots empty**. The label hides and the two-team toast shows (capture it). The master's file has `phase 0`, a `countdown start` marker, then `phase 2 remain [0,1]` at the live moment. |
| A3b | A team empties mid-countdown | Fresh room. Host start; 2 s in, Client2 leaves. `mTeams` and `mLiveAt` disappear (cancelled) before `mLiveAt`; `mPhase` never appears; the Editor's gold, position and loadout are untouched; `MaxPlayers` is back to 9; the label reads `warmupWaitingText`; a `countdown cancelled` marker is logged. |
| A3c | Master switch mid-countdown | Fresh room. Host start; 2 s in, `SetMasterClient(Client2)`. The countdown **continues**: Client2 (the new master) writes the live write at `mLiveAt`; both clients fresh-start once. The old master writes nothing more (read its `liveWritten`). |
| A3d | The warm-up sandbox shop | Before A3's start, with `Free Loadout` switched **off by reflection on both clients** (restored before leaving Play Mode; Rule 13): each player buys an ultimate and two armour upgrades with 0 gold, outside their territory. All succeed, gold stays 0, and Reset Armor and Reset Weapon work with no refund. The header reads "Free (warm-up)" and still does during the countdown. After live: the loadouts are empty and a purchase costs gold. |
| A4 | Out of play | The Editor stands in zone 8 for 25 s: owner -1 and no zone-8 progress on either client. Client2 reads `IsOutOfPlay(8)` true; its minimap zone-8 fill equals the theme colour and its links are inactive (reflection). Capture the Editor's minimap. |
| A5 | Instant, the GDD rule | `SetCaptured(7,0,0,0)`: `mElim [1]`, Over, winner 0, room closed. Client2 shows "you lost". Telemetry has an `elimination` and `phase 3`. |
| A6 | Last man standing (crafted) | Fresh room, host start. By reflection, one private `Write(Current.WithNeutral(6,now).WithNeutral(7,now))`, so **both** capitals go neutral in one write. `Phase` stays TwoTeams with nobody out. Kill Client2: **waiting panel**, `lastStand` true with a `lastStandAt` stamp within one round trip of the death (in `ServerTimestamp` terms), then team 1 is out. Over, **winner 0 while holding no capital**. |
| A7 | Adoption ends it | Fresh room, the same crafted state, then `SetCaptured(7,0,0,0)`. Team 1 is out at once, winner 0. The master's file has `adopt tm 0 zone 7`. |
| A8 | Leaver, two teams | Fresh room, host start. Client2 leaves while team 1 holds 7: `mElim` unchanged, TwoTeams, not Over, `IsInMatch(1)` true. Then `SetCaptured(7,0,0,0)`: team 1 out, winner 0. |
| A9 | Master switch | In the warm-up, `SetMasterClient(Client2)`: the Editor's button hides and Client2's `HostMayStartNow` turns true. Client2 calls `HostStartMatch` (reflection): the countdown, then live on both. `SetMasterClient` back to the Editor: `mTeams` intact, and A5's knockout is still decided by the new master. |
| A10 | Shield = combat | Live match. The Editor arms Invulnerability; Client2 fires every 0.5 s for 6 s (`fire_driver_tpl`). The Editor's recorder shows every blocked hit drops `SecondsSinceCombat` to about 0, armour never recharges, and health is unchanged. In a **warm-up room**, set Client2's `teamID` to 0 (same team): a teammate's hit during the immunity leaves the clock climbing and the trap armed. |

**Part B: three clients, adding Client3.** These checks cannot be done with two clients. **Fallback** if Client3 can't run: `GoLive([0,1,2])` by reflection with empty teams (step 7's recipe). That covers only the rule-2 paths; say so in the report. The pure tests carry the rest.

| # | Check | Expected |
|---|---|---|
| B1 | Auto countdown, then live | Editor and Client2 are in the warm-up; Client3 joins team 2. Within about 0.5 s: `mTeams [0,1,2]` and `mLiveAt` (≈ + 5000 ms); all three labels count down; no button ever showed on the Editor after Client3's team arrived. At `mLiveAt`: `mPhase 1`, `phase 1 remain [0,1,2]`, and everyone fresh-started. |
| B2 | Three-team adoption respawn | `SetCaptured(6,1)`, then kill the Editor: **waiting**. Then `SetCaptured(7,0)`: the Editor respawns after `NextRespawnDelay` at `teamSpawnPoints[1]`; `adopt` is logged. Team 1 (Client2 alive) is in its last stand, not out. |
| B3 | Transition into last man standing (replaces the draw) | Fresh room, three teams. One crafted write puts 6, 7 and 8 neutral. Kill Client2: team 1 out, `TwoTeams`, Tier-3 reset, **nobody else out**. Kill Client3: team 2 out, **winner 0 holding nothing**. On the master, read `BuildTeamStatuses` by reflection just before the second kill: team 1's `LastOutAtMs` equals Client2's `lastStandAt`. A true same-instant wipe can't be staged across processes; the tie-break itself is carried by the pure tests (`NoDraw…`). |
| B4 | Three-team leaver | Client3 leaves: team 2 is still in (`IsInMatch`, not eliminated, ThreeTeams). `SetCaptured(8,0)`: team 2 out at once, TwoTeams. |
| B5 | Late joiner after a host start | Editor and Client2 host-start; Client3 joins team 0 or 1 (**never 2**). It sees `IsOutOfPlay(8)` and the greyed bubble. **No fresh-start reset on joining** (its gold is StartingGold, published once). |
| B6 | The joiner race (direct call) | In B5's match, set Client3's `teamID` to 2 by eval, then call `RoomManager.EnsureLocalTeamInMatch()`. It returns 0 or 1 and publishes, and `PlayerTeamAppearance` re-applies. |
| B7 | The host leaves mid-countdown, with a teammate | Fresh room. In the warm-up, set Client3's `teamID` to 0 by eval (the Editor's team), so two teams are present. Host start; 2 s in, the Editor **leaves**. Team 0 still has Client3, so the countdown **continues**: the new master goes live at `mLiveAt`, and Client2 and Client3 fresh-start. |
| B8 | A joiner mid-countdown | Fresh room: Editor and Client2 host-start. Client3 joins 2 s into the countdown: it lands on team 0 or 1 (**never 2**), sees the countdown, and gets the fresh start at live along with everyone (gold StartingGold, empty loadout). |

**Part C: the report.** Build the reports for A3's and B1's folders (`TelemetryMenu.BuildReport(folder, false)`). Read `PhaseTimeline` by eval against the table in step 9: the warm-up window runs to the live moment, not to the countdown's start. The A2 warm-up death is **not** in the whole-match deaths, and A3d's free purchases raise no Free Loadout warning. The header shows the warm-up seconds.

**Part D: the final report.**
- The final test count and each step's delta from BASE TESTS.
- `git diff --stat BASE..HEAD`, explaining any file not in the file map.
- The RpcList matches `SCRATCH/rpclist-base.txt`.
- `Game Scene.unity`, `Multiplayer Player.prefab` and `TerritoryConfig.asset` are byte-identical to BASE. `GameplayConfig.asset` differs by exactly the one `matchStartCountdownSeconds: 5` line, and `UiTheme.asset` by exactly its 12 new lines.
- `RuntimePipelineConfig.json` is off.
- The assumptions file's headings are intact.
- Every A/B row with its recorder evidence and capture paths.

---

## Risks

- **R1: callback order.**
  - The director registers before `BuildingManager`, because it is added in `Awake`.
  - Nothing relies on the order of two classes within **one** event. Live is always a separate, later event than the territory reset, and the countdown write is a separate, earlier one.
  - `MasterRecompute` must **never** be triggered from `MatchDirector.OnRoomPropertiesUpdate`. Say so in its comment.
- **R2: a master switch mid-countdown or mid-GoLive.** Check-and-set means only one countdown can start (absent `mTeams`) and the match goes live only once (absent `mPhase`). Two masters racing the live moment can each write a territory reset; the second is identical and harmless. A refused check-and-set on the live write leaves `writesAwaitingEcho` at 1 until the winning master's echo decrements it; it heals itself, and the comment says so. A refused countdown or cancel write is retried after the 1 s echo wait.
- **R3: the joiner race** is closed by `playersWithoutATeam` on the master and `EnsureLocalTeamInMatch` on the joiner. Appearance follows the `teamID` echo a moment later.
- **R4: interim commits** (steps 3 to 8) are not playtest-ready. See Rule 15.
- **R5: accepted carry-overs across the reset:**
  - warm-up projectiles landing milliseconds after live;
  - the 3 s under-attack linger;
  - fire fields (seconds);
  - a channel's visual on *remote* screens. The interrupt runs owner-only; the visual ends on its own.
- **R6: the report windows now start at going live.** That touches the aggregator's review-hardened edge handling. The legacy path must stay byte-for-byte (step 9, sub-step 8).
- **R7: `UiTheme.asset` is stale** (trap 8c), and `GameplayConfig.asset` may be too. YAML edits only: exactly 12 new lines in `UiTheme.asset` (steps 6 and 8) and 1 in `GameplayConfig.asset` (step 5).
- **R8: the waiting panel's text** still says to retake your capital (Open #5; prefab label, not touched).
- **R9: the funnel reorder.** A self or teammate hit during the immunity now logs the once-per-match "blocked" line. `DummyTarget` has its own funnel and is unchanged (test range only).
- **R10: `MaxPlayers` 6** on a host start is a room-level write by the master, set at the countdown's start and put back on a cancel. If a playtest wants a seventh person to sit out rather than make a new room, remove those two lines.
- **R11: line drift.** Other agents commit on this branch. Every step re-reads its `file:line` references before editing.
- **R12: countdown clocks.** Each label counts on its own client's synced `PhotonNetwork.ServerTimestamp` (Photon's estimate, usually within tens of ms), so screens can differ by a frame or two. The reset is the master's write and lands up to one round trip after a label reaches 1. The label holds at 1 until then (never shows 0), so nobody sees "0" with nothing happening.
- **R13: a cancel racing the moment.** Cancel and go-live are both decisions of the one master, taken in order in one `Update`. A team emptying in the frame after the live write simply stays in the match (rule 2). The cancel's check-and-set expects `mPhase` absent, so a live match is never cancelled.
- **R14: "the last to die" vs arrival order.** Usually the master hears two final deaths one event apart and decides on the first to arrive. Network jitter can then give a death that happened a few tens of ms *earlier* the later arrival, so the stamp rule only decides when one recompute sees both. Holding every final knockout for a grace window would be exact, but it would delay every normal win. Not built; flagged for the playtest.
- **R15: the countdown's length comes from the master's own player's `GameplayConfig`.** It is the same asset every player component points at, read the moment the countdown starts. If the master's player isn't spawned yet, the start waits for the next poll.
- **R16: flipping Free Loadout by reflection in Play Mode** changes the Editor's in-memory asset, and a later asset save would write it to disk. Every check that does it restores the value and confirms `git diff` on the asset is empty (Rule 13).

---

## Order and commits

| Step | What | Tests | Commit |
|---|---|---|---|
| 0 | Start state | BASE | none |
| 1 | Shield hits count as combat | +8 | yes |
| 2 | 2.7 leftovers (gold key, comments, the already-neutral guard) | +3 | yes |
| 3 | Pure rules (incl. countdown, last to die); `lastStandAt`; interim wiring | +35 | yes |
| 4 | Owner-side fresh start | +2 | yes |
| 5 | Countdown and going live, gates, reactions, joiners | 0 | yes |
| 5b | The warm-up sandbox shop | +1 | yes |
| 6 | Out of play | +1 | yes |
| 7 | Final two in play (respawn capital, adoption, waiting) | 0 | yes |
| 8 | Warm-up line, countdown, Start button, live toast | 0 | yes |
| 9 | Telemetry and the report | +7 | yes |
| 10 | Assumptions and progress (outside the repo) | none | no |
| 11 | Two- and three-client verification | none | no |

**Total: BASE TESTS + 57.** Steps 1 and 2 are independent of the rest and may land first. Steps 3 to 9 (with 5b after 5) must follow in order.
