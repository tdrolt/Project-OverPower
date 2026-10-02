# Dominion: a round-based mode for 2v2 (a new lane map) and 3v3v3 (today's arena): design

**Date:** 2026-10-02. **Branch:** `lobby` (after the lobby plan's tasks). **Source:** Tudor's request (session of 1-2
October: "a new round-based game mode, Dominion"), two rule rounds and one look round answered by Tudor, the 2v2 map drawn
by him and redrawn on the canvas. Mockups and picks: https://claude.ai/artifact/8zDa3iexpnsFYnUH8cZ5Pi (map board; D1 A,
D2 A, D3 A, D4 with the red outside, D5, D6; P6 four scenarios). Every answer:
`Resources/loops/Limit Test/lobby-dominion-design-notes.md` ("Dominion rules" rounds 1-2, "Dominion looks", the info
cards). Defaults Claude chose where the design left something open: `assumptions-for-tudor.md`, "While you were away
(lobby + Dominion build...)". Approved by Claude on Tudor's word ("write the spec for everything and approve it").
Depends on the lobby spec (`2026-10-02-lobby-design.md`): modes are `GameModeDefinition` data; Dominion's two modes are
already in the catalogue, greyed until this lands.

## What the player sees

**The match.** Best of three rounds. Each round lasts a fixed time (default 3 minutes). The team with the most points at
the end wins the round; a round that ends level counts for nobody. The first team to win two rounds wins the match (round
3 may not be played). If after round 3 no team has two wins, **sudden death** decides it (in 3v3v3 between the teams tied
for the most round wins; the others are out).

**Points.** Every zone your team owns gives points every second (default 5), also while enemies are attacking it until it
turns. Neutral zones give nothing. **Spawns never give points** (2v2 spawns and 3v3v3 capitals alike). **Bounties:** a
zone held without interruption for a while (default 60 s) builds a bounty (default 150 points) that the team taking it
collects. **The centre (3v3v3 only)** gives nothing per second; instead it pays **200 points every 30 s** to whoever holds
it at that moment (first payout 30 s into a round); "CENTRE +200 IN 12" and who holds it sit under the minimap (D6), and
"+200 CYAN" flashes on the scores. **No map scan in Dominion.**

**The round HUD (D1 A).** Top centre: the teams' points on either side (3v3v3: three team blocks), the round clock in the
middle ("ROUND 2 OF 3 · 1:42"), round wins as dots under each team.

**Between rounds (D2 A).** A **20 s break**: a result card ("ROUND 1 · PURPLE WINS · 540 / 620", the round-win dots),
what the next round opens ("Round 2 opens: a weapon family and one armor upgrade"), **Pick your build (P)**, and "ROUND 2
STARTS IN 14". Everyone is back at their spawn with a clean build, zones go neutral, bounties and the ultimate meter start
from zero. The last 5 s read "Round 2 starts in 5…", then the round clock starts.

**Your build each round.** Picks are free and only possible in the break (the shop opens with "Free" prices; a tier not
open yet is greyed with "Round 2" / "Round 3"):
- Round 1: the Baseline pistol; one movement ability, one attachment, one ultimate.
- Round 2: one weapon family (Rocket, Burst, SMG, Laser) + one armor upgrade (either path).
- Round 3: any family + one of its two upgrades (switching family allowed) + two armor upgrades.
A player joining mid-round picks once before spawning. A rejoining player keeps the round's picks.

**Respawns.** A fixed respawn time per size (default 6 s for both). The spawn heals: out of combat at the capital's rate
(10 HP/s after 6 s), in combat at 4 HP/s. 2v2: the whole pocket behind the boxes; 3v3v3: the capital's circle. Spawns can't
be captured.

**The respawn shield (D3 A).** After every respawn a **blue bubble, bigger than the Invulnerability ring**, seen by
everyone. While it's up you take no damage and "BLOCKED" pops over you whenever a hit is stopped; you can't capture, and a
zone ignores you (you neither capture nor block an enemy capture). It ends after its time (default 10 s) or the moment you
damage an enemy; casting an ability that hits nobody keeps it.

**Sudden death (D4).** Everyone starts alive at their spawn, no shield, no respawns, points stop. "SUDDEN DEATH · No
respawns · stay inside the circle · last team standing wins". A red ring centred between the two zones (2v2) or on the
centre zone (3v3v3) shrinks over 60 s to a small final circle (2v2 4 m, 3v3v3 8 m radius); **everything outside it is
tinted red** in the world and on the minimap, and standing outside costs 10 HP per second. The last team with a living
player wins the match.

**The result (D5).** "PURPLE WINS 2–1", a table of points per round with each round's winner in bold, and **Back to the
lobby list**.

**Everything else in Dominion:** fog of war as in every mode; no gold, no bounty gold, no map shrink, no knockouts; in
3v3v3 the adjacency rule stays (you capture next to a zone you hold; your capital counts as held), health packs and the
OverPower buff stay. 2v2 has no health packs. Teams in 2v2: White and Purple.

**The 2v2 map** (Tudor's drawing, canvas map board): built from the triangle map's pieces. A spawn pocket at each end
(27 × 21 m, healing area behind the boxes, the spawn tower in its middle as cover, players spawn around it), five boxes
(2.5 m, a row of 3 and a row of 2, every gap ≥ 2.75 m so you can walk round each box), a middle hall (24 × 32 m) sticking
out above and below with **two Tier 3-sized zones** top and bottom, both starting neutral, Tier 3 capture time, and in the
centre a jersey-barrier plus joined by big-wall arms to two big walls (an H: no walking straight between the zones; shots
cross only over the plus; Dash, Zip, Blink and Portals cross jersey barriers as everywhere). **56 m spawn to spawn.**
Mirror-symmetric.

**The info page** (the lobby's ⓘ): Dominion's cards, verbatim from the notes file (Rounds, Zones and points (3v3v3
variant), The centre (3v3v3 only), Bounties, Your build each round, Respawns, Sudden death).

## Design values (one home each)

`DominionConfig` (new): rounds to win, round seconds, break seconds, break countdown seconds, points per zone per second
(per tier, 3v3v3), centre payout points and interval and first delay, bounty hold seconds and points, respawn seconds per
size, shield seconds, shield bubble scale (relative to the Invulnerability ring), spawn heal out/in combat per second,
sudden death shrink seconds, final radius per map, damage per second outside, weapon tier and armor upgrades open per
round. The 2v2 map's layout: an `ArenaLayout`-style asset (`DominionLaneLayout`) for walls, boxes, zones, spawns. Colours:
`UiTheme` (shield blue `#4FA3FF`, sudden-death red `#E5484D` reused). Capture radius/time: the Tier 3 row of
`TerritoryConfig`.

## How it works

**A sibling director.** Conquest's `MatchDirector` flow stays as it is. `DominionDirector` (added at runtime like
MatchDirector, active when the room's mode family is Dominion) owns the round flow. The lobby's Start game and End warm-up
stay the same; going live starts round 1's clock instead of Conquest's knockout flow.

**Shared state is Room/Player Properties only; no RPC added, renamed or removed** (RpcList byte-identical, 45):
- Room: `dRnd` round number, `dStg` stage (break / round / sudden / over), `dEnd` server ms when the current stage ends,
  `dPts` int[] points per team, `dWins` int[] round wins, `dCtr` next centre payout ms, `dSd` sudden-death start ms,
  `dWin` match winner. All master-written with check-and-set like `mPhase`; a new master carries on from the room's own
  values. Points accrue on the master once per second from the territory snapshot (spawns excluded) and are written once
  per second (a low rate; every client shows the room's value).
- Player: `dShd` shield end time (server ms; the owner writes it on respawn and clears it when it deals damage, learnt from
  the existing `RPC_DamageCredit` the victim already sends), `dBlk` last blocked-hit stamp (the shielded player writes it
  when its own client stops a hit, so everyone shows "BLOCKED").
- The sudden-death radius is a pure function of `dSd` and the server clock; each client applies its own outside-circle
  damage (damage is victim-side, as everywhere).

**Reused, not rebuilt:** the per-round reset is `PlayerLifecycle.ResetForMatchStart` + `BuildingManager.ResetForMatchStart`
(all zones neutral) on each round edge; the shop's `ShopRules.IsFree` and `WeaponUpgradeTree` get a "tier open this round"
input; respawn healing reuses the capital regeneration path with an in-combat rate; zone presence skips shielded players;
the minimap and fog are unchanged; bounty bookkeeping reuses `TerritorySnapshot`'s hold timers.

**The 2v2 map is a second scene** (`Dominion 2v2`). The room's shared systems that sit in Game Scene today (RoomManager,
BuildingManager, UI roots, telemetry) become one prefab both scenes use. The lobby's Start game loads the mode's scene for
everyone (`PhotonNetwork.AutomaticallySyncScene` + the master's `LoadLevel`) when it differs from the current one; the
lobby list always lives in Game Scene. The scene's arena is built from primitives by an Editor tool reading
`DominionLaneLayout`, its minimap picture baked like the triangle's.

**Pure rules, edit-mode tested:** `DominionRules` (stage machine and end times, round winner and ties, match winner and
the sudden-death trigger incl. 3v3v3 ties, points accrual per tick, centre payout times, bounty), `RespawnShieldRules`
(ends on time / on dealing damage; blocks damage; excluded from capture), `SuddenDeathRules` (radius over time, outside
test), `DominionShopRules` (what each round opens, armor upgrades allowed).

## Checks

Every task's first brief carries a two- or three-client check with recorders on every client; rounds are timed by
recorders, not by eye; for speed the checks run with short round/break settings set in memory and set back (trap 15).
Dominion 3v3v3 is built and checked first on today's arena; the 2v2 scene comes after, so a slow map task never blocks the
rules. One end-to-end check per size at the end.

## Out of scope

Rematch in the same lobby, kill feed, new sounds, a separate scoreboard for rounds (Tab keeps today's), AI players,
changing Conquest.
