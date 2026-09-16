# Capital under attack: respawn at T2, and no capturing through a zone under attack — design

**Date:** 2026-09-16 · **Branch:** `limit-testing` · **Status:** approved by Tudor in brainstorming, 2026-09-16

Tudor:
- "If the T1 is under attack, the players that would respawn in that T1 zone would respawn at the T2 instead (even if
  it's not owned by them)."
- "They must not be able to capture any adjacent zones if the previous zone owned by them is under attack or
  contested", so players who spawn in the T2 can't capture it while their capital is under attack.

The same session also resized the capital pockets: **the pocket walls wrap the 10 m capital capture circle** [T]
(arena plan, Task A4).

Markers: [T] Tudor decided · [G] GDD · [C] Claude's starting value (Inspector-tunable).

---

## Decisions taken in brainstorming (do not re-litigate)

| Topic | Decision |
|---|---|
| "Under attack" | **An enemy player (alive, any team other than the owner) standing inside the zone's capture circle**, whether or not defenders are there. It stays under attack for **3 s after the last enemy leaves** (tunable) so it doesn't flicker at the edge [T] |
| Only owned zones | A neutral zone is never "under attack" (there is nothing to defend) [C, follows from the definition] |
| Respawn | When a player's respawn timer ends and **their capital is under attack**, they respawn at **their capital's T2** (capital 6 → T2 0, 7 → 1, 8 → 2), **whoever owns that T2** [T] |
| Capture block | **Per link** [T]: a team may capture a zone only through an adjacent zone it owns **that is not under attack**. Another safe owned neighbour still allows it. A team's own capital stays capturable by that team (unchanged rule) |
| Pocket | Walls wrap the capture circle; radius stays 10 m [T] |

## What exists (verified in code 2026-09-16)

- **Adjacency** is explicit per tower. Capitals 6/7/8 are adjacent only to T2 0/1/2. Each T2 is adjacent to its capital
  and two T3s (0: 6,3,5 · 1: 7,3,4 · 2: 8,4,5). Each T3 is adjacent to two T2s and T4 (3: 0,1,9 · 4: 1,2,9 · 5: 0,2,9).
  T4 9 is adjacent to 3, 4 and 5.
- **`TerritoryMap.MayCapture(team, zone, owners)`**: not already owned; own capital → yes; otherwise any adjacent zone
  owned by the team.
- **It is only checked in `BuildingCapture.OnTriggerEnter`, on the entering client**, which then RPCs the master to add
  the player to `playersInZone`. So:
  - A player already inside a zone is never re-checked. A capture in progress wouldn't stop when its link comes under
    attack.
  - **Only players allowed to capture are ever in `playersInZone`.** An enemy with no link, and every defender in their
    own zone (`MayCapture` returns false for an owned zone), are invisible to the capture logic. The code in
    `HandleCapturedState` clearly intends "enemy present and no team member present → drain", but a defender can never
    be "present". **Suspected existing bug: defenders can't stop a drain by standing in their zone.** This must be
    measured before it is fixed.
- **Master's capture tick:**
  - A neutral capture advances only with capturers and nobody else. When the capturers leave, progress **holds**.
  - An owned zone drains while an enemy is alone in it. When the drain stops and restarts, progress resets to full.
- **Respawn** (`PlayerLifecycle.RespawnPlayer`, owner client) teleports to `RoomManager.teamSpawnPoints[team]` after the
  delay.
- `BuildingManager.TryGetZoneAt(position)` finds the zone whose capture circle contains a point.
- `BuildingManager` is already ~700 lines.

## Part 1 — Zone presence (who is standing where)

A new **`ZonePresenceTracker`** (scene component next to `BuildingManager`, `MonoBehaviourPunCallbacks`) keeps
presence separate from capture eligibility, and out of `BuildingManager`.

- **Master only, every 0.2 s:**
  - For each player in the room with a known team (Player Property), alive (`alive` Player Property) and a registered
    view (`PlayerLookup`), take its view's replicated position and find the zone it stands in with
    `BuildingManager.TryGetZoneAt`.
  - Build a **team bitmask per zone**.
- **Published to Room Properties, only when a mask changes**, in its own `SetCustomProperties` call:
  - `zPres` (int[zones]): the bitmask of teams present now.
  - `zSeen` (int[zones × 3]): the server ms when each team was last present in each zone, written when that team's
    bit clears.
  - Same echo-window write basis as `PublishCaptureProgress`. Late joiners and a new master read both.
- **Pure `ZoneThreat.IsUnderAttack(owner, presentMask, lastSeenMs[], nowMs, lingerMs)`:**
  - owner < 0 → false;
  - any team t ≠ owner whose bit is set → true;
  - or whose last-seen is within `lingerMs` of now → true.
  - A 0 server clock (trap 13) → only the mask counts.
- **`ZonePresenceTracker.IsUnderAttack(zone)`** (every client) combines the replicated arrays, the replicated owner and
  `TerritoryConfig.UnderAttackLingerSeconds` (**3 s** [T], tooltip). `IsTeamPresent(zone, team)` is exposed for the
  defender fix.

## Part 2 — Capture block per link

- **`TerritoryMap.MayCapture(team, zone, owners, Func<int, bool> isUnderAttack)`:** an owned neighbour counts only if
  `!isUnderAttack(neighbour)`. The existing 3-argument overload keeps its behaviour (nothing is under attack). Own
  capital unchanged.
- **Entry stays on the plain adjacency rule** (corrected 2026-09-16 after B2). Checking the threat on entry would
  refuse a player who walks in during an attack, and they would never be counted once the link turned safe without
  stepping out and back in. The continuous check below does the blocking.
- **Continuous (master):**
  - `CalculateCaptureProgress` counts capturers only while `MayCapture(capturingID, …, IsUnderAttack)` holds. When it
    stops holding, progress **holds**, exactly as if the capturers had stepped out.
  - `HandleCapturedState` counts a draining enemy team only while that team `MayCapture` holds. If not, no drain.
  - When the link is safe again, capture/drain resumes without anyone re-entering.
- **Defenders (measured first):** if the measurement confirms that a defender standing in their own zone doesn't stop
  a drain, `HandleCapturedState`'s "team member present" reads `ZonePresenceTracker.IsTeamPresent(zone, owner)` instead
  of `playersInZone`. This is a fix to the evident intent of the existing code [C]. If the measurement shows it already
  works, nothing changes.

## Part 3 — Respawn at T2

- **Spawn points:** `RoomManager.capitalUnderAttackSpawnPoints` (Transform[], index = team, with a tooltip). There are
  three new spawn points, each **5 m from its T2 tower toward its capital** [C], inside the T2 capture circle and just
  outside the capital pocket. They are added to `ArenaSymmetry` as a snapped triplet, so the Team 2 one is placed and
  the other two follow.
- **`PlayerLifecycle.RespawnPlayer`** (owner) decides **when the timer ends**: if
  `IsUnderAttack(Map.CapitalOf(team))` and a spawn point exists, it uses that point. Otherwise it uses the normal one. It
  logs which one it used.
- **Player feedback:**
  - While waiting, the respawn panel shows "Your capital is under attack — you will respawn at your T2" whenever that
    is currently true.
  - After respawning at T2, the HUD shows a short toast: "Respawned at T2: capital under attack".
  - The text and the toast duration live on `UiTheme`.
- Health regen and the shop keep their own rules, so an enemy-owned T2 gives no regen and no shop.

## Verification

- **Edit-mode tests:**
  - `ZoneThreat`: present, left 2.9 s ago, left 3.1 s ago, neutral, owner-only, attacker now owns it, zero clock.
  - Presence mask building from (team, zone, alive) tuples.
  - `MayCapture`: link under attack blocks; a second safe link allows; own capital always allowed; the null predicate
    keeps the old behaviour.
- **Two clients** (Editor = team A, Player = team B):
  1. B walks into A's capital. A reads `IsUnderAttack(capital)` true. B leaves; it stays true until ~3 s later
     (game-time stamps), then false.
  2. With B inside A's capital, A dies. A respawns at A's T2 spawn point (`Rigidbody.position` measured), the panel
     line is visible (capture), and so is the toast. B leaves, 4 s pass, A dies again and respawns at the capital.
  3. A stands in A's neutral T2 while B is in A's capital: A's capture progress stays Idle. B leaves: capture starts
     ~3 s later **without A re-entering**, and completes in the tier's capture time.
  4. Defender check before and after any fix: B drains A's T2 (owned by A); A steps in; report whether the drain stops.
  5. Regression: a solo T2 capture with nobody attacking still takes 15 s.
- `RpcList` unchanged (no RPCs added). `NetworkPrefabObservablesTests` passes. The scene test (arena Validate) passes
  with the new triplet.

## Out of scope

Spawn protection, choosing among several T2s, "under attack" for neutral zones, UI markers on the map, and any change
to how neutral captures are contested.
