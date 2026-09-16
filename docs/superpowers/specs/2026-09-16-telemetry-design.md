# Match telemetry and balance reports — design

**Date:** 2026-09-16 · **Branch:** `limit-testing` · **Status:** approved by Tudor in brainstorming, 2026-09-16

Tudor wants to understand and balance future iterations of the prototype. His examples: gold per player at given
times, how much each zone generates, how much each team generates from its zones, and how much damage each gun upgrade
does. He asked for more metrics beyond those. The economy doesn't need to match the GDD 1:1, since the GDD's numbers
are assumptions themselves.

Markers: [T] Tudor decided · [G] GDD · [C] Claude's starting value (Inspector-tunable).

---

## Decisions taken in brainstorming (do not re-litigate)

| Topic | Decision |
|---|---|
| Collection | **Each client writes a local log of the events it owns, merged afterwards** [T approved approach]. No new RPCs, nothing sent over the network, no cloud service |
| Outputs | **Both an HTML report and CSV tables** [T]. The HTML is the curated view; the CSVs are for Excel and questions the report didn't anticipate. Both come from one aggregation, so their numbers always agree |
| Not built | Unity Cloud Analytics; a live in-game stats panel |
| Order | After 2.6 OverPower and 2.7 phases; before 2.8, whose two-client run produces the first real report [T]. So OverPower and phase events are in scope here |

## Principles

1. **Telemetry never changes gameplay.**
   - Gameplay code only raises C# events, most of which already exist. Everything that listens and writes lives in
     `Assets/scripts/Telemetry/`, so deleting that folder breaks nothing.
   - Every write is exception-safe: an IO error disables telemetry for the session and logs once.
2. **Every fact is logged once, by the client that owns it**, matching the ownership rules the game already follows:

   | Fact | Logged by |
   |---|---|
   | A hit | the victim |
   | Gold, purchases | the owner |
   | Shots, casts | the shooter/caster |
   | Territory, capture, phases | the master |

   Merging every client's file therefore double-counts nothing. Where a master change could log a territory event twice,
   the merge de-duplicates on a natural key.
3. **Every log is self-describing.** Its first line snapshots the tuning in force: prices, incomes, capture times,
   every weapon's and ability's stats, the build commit. A report from an old balance pass still reads correctly after
   Tudor retunes.
4. **Designer-friendly.** Every setting is on an asset with a plain tooltip. The report is one menu item. No code to
   read.

## Part 1 — Recording

**`TelemetryConfig` asset** (`Assets/Gameplay/Config/TelemetryConfig.asset`):
- Enabled (default on).
- Sample interval (5 s) [C].
- Record positions (on).
- Flush interval (2 s) [C].
- Folder name (`Telemetry`).

**Match identity.** The first master writes `mId` (a GUID) and `mStart` (`PhotonNetwork.ServerTimestamp`, guarded
against the 0-right-after-connect trap) into Room Properties if they're absent. Late joiners read them. Every event
carries `t`, match seconds from `mStart` (unchecked int subtraction, since the server clock wraps).

**Files.** `persistentDataPath/Telemetry/<yyyy-MM-dd_HHmm>_<mId8>/<actor>_<nick>.jsonl`, one JSON object per line.
The Editor and builds on one PC share `persistentDataPath`. For a multi-PC playtest, copy every PC's match folder into
one. Buffered writes, flushed every flush interval and on leaving the room or quitting.

**Line 1, `session`:** schema version, match id, actor, nickname, team, is-master, build commit, Unity version,
platform, and the tuning snapshot:
- `TerritoryConfig`: tiers, incomes, capture times, bounty, starting gold, players per team.
- `GameplayConfig`: free loadout, refund rate, shop out-of-combat time.
- `ArmorConfig`.
- For every `WeaponDefinition` and `AbilityDefinition`: id, name, cost and serialized numbers.
- The OverPower config.

In the Editor the commit is read from `.git`. Builds get it written into a Resources text asset by a pre-build step.

**Events** (short keys in the file; the full list is the plan's schema table):

| Event | Logged by, when | Fields |
|---|---|---|
| `sample` | everyone, every sample interval | balance, x, z, alive, zone id standing in, team, weapon id, equipment/mobility/ultimate ids, armor levels, health, armor, ultimate charge, overheat, ping |
| `goldEarned` | owner, every sample interval (summed) | per source: `zone:<id>` (income attributed to each owned zone; see below), `bounty:<zone>`, `refund:<category>`, `debug` (F1 cheat, so test data is flagged) |
| `purchase` / `refund` | owner, on success | category (weapon/armor/equipment/mobility/ultimate), item id, price or refund, balance after, zone id, free-loadout flag |
| `shopBlocked` | owner, on a refused click | item id, price, reason (territory / combat / gold), shortfall |
| `shots` | shooter, every sample interval (summed per weapon) | weapon id, trigger pulls, projectiles spawned (pellets and burst rounds counted singly) |
| `cast` | caster, on a successful cast | slot, ability id, x, z |
| `dot` | victim, **every sample interval, summed** (added in T3 review: burn ticks every frame, one `hit` line each would be ~120 lines/s per burning victim) | per (attacker actor/team, weapon, ability, source): tick count, raw/armor/health sums, first/last t. Continuous source: `Burn` (status burn, fire field). A lethal burn tick flushes its bucket and is also logged as a normal `hit` |
| `hit` | victim, per applied hit (friendly-fire blocks excluded; **continuous `Burn` damage goes to `dot` instead**) | attacker actor and team, weapon id, **ability id**, damage source, raw amount, armor absorbed, health lost, lethal, distance to the attacker's replicated position, vulnerability active, invulnerable, OverPower active on victim |
| `status` | victim, when applied | effect (stun/slow/vulnerability/knockback/burn), source actor, ability id, duration or magnitude |
| `death` | victim | killer, assists (from the existing `DamageCreditLedger`, same assist window), killing weapon/ability, x, z, time alive, unspent gold, full loadout |
| `respawn` | owner | x, z, time dead |
| `heal` | owner, every sample interval (summed) | health regenerated per zone tier |
| `overheat` | shooter | weapon id, on silenced and on recovered |
| `ultimateReady` / `ultimateUsed` | owner | ability id, seconds since last use |
| `ownership` | master, on applied change | zone, tier, old owner, new owner, held-since stamp (the merge key) |
| `capture` | master | zone, team, started / interrupted / completed / decayed, progress, players counted |
| `bounty` | master when it becomes claimable; owner when paid | zone, amount, hold seconds |
| `overpower` | owner | triggered / expired / broken by distance, zone, health at trigger |
| `phase` / `elimination` | master | phase number, team eliminated, capital adopted |
| `join` / `leave` / `masterChanged` | everyone | actor, team |
| `marker` | owner, via the F1 **Drop marker** button | note (optional text) |

**Income attribution.** The wallet stays exactly as it is. A pure `IncomeAttribution` rule (edit-mode tested) splits
each sample interval's territory income across the zones the owner's team held, as each zone's rate ÷ players per
team. It asserts the parts sum to the wallet's own accrued territory income, within 1 gold per interval.

**Gameplay code changes, kept to event raises:**
- `DamageInfo` gains `AbilityId` (−1 when not from an ability), set at its 9 construction sites.
- New events:
  - `WeaponFiring.Fired(weaponId, projectileCount)`
  - `AbilityRunner.Cast(slot, abilityId)`
  - `GoldWallet.Credited(amount, source)` and `Spent(amount, reason)`
  - `LoadoutScreen`/`ShopPricing` `Purchased`, `Refunded`, `PurchaseRefused`
  - `PlayerOverheat` silenced/recovered
  - `UltimateCharge` ready
  - health regen amount
  - `PlayerStatusEffects` applied
  - capture state change on the master
  - OverPower and phase events, if 2.6 and 2.7 don't already raise them
- Existing events are reused: `PlayerHealth.Damaged`/`Died`, `PlayerLifecycle.AliveChanged`,
  `BuildingManager.OwnershipChanged`, `GoldWallet.BountyReceived`, `AbilityRunner.SlotChanged`.

**Recorder components:**
- `MatchTelemetry` (scene, one per client) owns the writer and the match id.
- `PlayerTelemetry` on the player prefab listens to the owner's events. It is not an `IPunObservable`, and
  `NetworkPrefabObservablesTests` must still pass.
- The master-only listeners live on `MatchTelemetry`.

**F1 debug panel:** "Open telemetry folder", "Drop marker", and a line showing the current file path and event count.

**Volume check.** Shots are summed per interval, not per shot. A 9-player, 25-minute match is estimated at under 100k
lines and under 15 MB. The plan measures a real minute and extrapolates.

## Part 2 — Report builder (Editor)

**Menu `OverPower › Telemetry › Build Report…`:** pick a match folder (defaults to the newest) → merge every `.jsonl` →
write `report.html` and `csv/` into that folder → open the HTML in the browser. Also **Open telemetry folder**.

**`TelemetryAggregator` (pure C#, edit-mode tested against fixture logs with hand-computed totals)** turns merged
events into tables. `CsvWriter` and `HtmlReportWriter` only format those tables, so the two outputs can't disagree.

**CSV tables** (`csv/`, invariant culture, one header row):

| File | One row per |
|---|---|
| `gold_timeline.csv` | sample × player: time, actor, name, team, balance, earned so far, spent so far |
| `economy_by_minute.csv` | minute × team: income per tier, bounty, refunds, spent, zones held per tier, gold gap to the richest team |
| `zone_income.csv` | zone × team: tier, seconds held, gold generated |
| `ownership.csv` | ownership stint: zone, tier, team, from, to, duration, how it ended |
| `captures.csv` | capture attempt: zone, team, start, end, outcome, duration, players |
| `purchases.csv` | purchase or refund: time, player, team, category, item, price, balance after, zone |
| `shop_blocked.csv` | refused click: time, player, item, reason, shortfall |
| `hits.csv` | hit, raw |
| `weapons.csv` | weapon state (all 13): time equipped, trigger pulls, projectiles, hits, accuracy, damage, damage per equipped minute, armor vs health split, kills, mean and median hit distance |
| `abilities.csv` | ability: casts, damage, kills, status applied (count and seconds) |
| `players.csv` | player: kills, deaths, assists, damage dealt/taken, gold earned by source, gold spent, time alive, time in own/enemy/neutral zones, healing |
| `deaths.csv` | death: time, victim, killer, assists, cause, x, z, unspent gold, loadout |

**HTML report** (one file; data embedded as JSON; charts via Chart.js from jsDelivr, so viewing needs internet, and the
tables work without it):
1. **Header:** match length, players per team, commit, markers, tuning snapshot (collapsible), and a warning when free
   loadout or the F1 gold cheat was used.
2. **Economy:**
   - gold per player over time, with team averages;
   - team income per second (stacked by tier), with `BalanceTargets` scenario lines;
   - gold generated per zone by team;
   - gold gap between the richest and poorest team over time;
   - purchase timeline per player against target purchase times;
   - unspent gold at death and at the end;
   - blocked purchases by reason.
3. **Territory:** ownership timeline per zone (Gantt), capture durations per tier, time each team held N zones,
   bounties.
4. **Combat:**
   - weapon table and bars (damage per equipped minute, accuracy, kills);
   - ability table;
   - team-vs-team damage matrix (third-party pressure; this is what OverPower targets);
   - time-to-kill distribution;
   - death and position heatmaps drawn over a top-down arena render.
5. **Players:** the `players.csv` table.
6. **Markers:** each marker with the 30 s of events around it.

**Arena render for heatmaps:** the builder renders the scene top-down into the report (the same orthographic
preview-scene capture used for the arena design, which never dirties the scene) and records the world-to-pixel mapping.

**`BalanceTargets` asset** (`Assets/Gameplay/Config/BalanceTargets.asset`, every value with a tooltip saying where the
default came from):
- gold-flow scenario incomes per team: Losing 5, Struggling 15, Average 23, Dominant 33 gold/s [G p.37];
- target purchase times: primary upgrade 1 at 6.5 min, armor 1 at 10, ultimate at 13.5, primary 2 at 16.5,
  armor 2 at 20 [G p.37];
- target match length 1350 s [G p.36].

They're reference lines only, and nothing in the game reads them.

## Part 3: 3-team vs 2-team phase split (added 2026-09-16, Tudor)

Tudor wants the data for the 3-team phase kept separate from the data after the first team is out.

- **The phase changes ONLY on an elimination** (Tudor, corrected the same day). Data is **Phase 1: 3 teams** from the
  match start, until a team is actually eliminated. It is **Phase 2: 2 teams** from that moment. Player counts never
  change the phase.
- **Runtime:** the master logs an `elimination` event (team, time, teams remaining) and a `phase` event (number,
  teams remaining) when a team is eliminated. Task 2.7's `MatchDirector` raises them. Until 2.7 exists there are no
  eliminations, so every log is Phase 1. A 2-player test stays Phase 1 until one of its two teams is eliminated, which
  ends the match.
- **Aggregator:**
  - Builds a phase timeline from `phase` events: Phase 1 from t=0 until the first `phase` event.
  - Every time-based row gets a `phase` column, and ownership stints are split at the boundary.
  - `weapons`, `abilities`, `players` and `zone_income` get one row per phase plus a whole-match row.
- **Clearly separated output:**
  - **HTML:** three tabs, **Phase 1: 3 teams**, **Phase 2: 2 teams** and **Whole match**, each with its own charts,
    tables and target lines. A tab with no data says so. The whole-match time charts draw a line at the transition.
  - **CSV:** one folder per scope: `csv/phase1_3teams/`, `csv/phase2_2teams/`, `csv/whole_match/`.
- **`BalanceTargets`:**
  - Phase 1 scenario incomes, per team: Losing 5 / Struggling 15 / Average 23 / Dominant 33 gold/s (GDD p.37).
  - Phase 2 scenario incomes, per team: Losing 5 / Even 15 / Winning 25 gold/s (p.38).
  - Phase durations: 900 s / 450 s (p.36).
  - Each tab is drawn against its own phase's lines.
- **Which logs are included:** the report header lists every player seen in the match (join events, sessions) and
  whether their log file is present. It flags anyone missing, e.g. "No log from <name> (actor 3): their damage taken,
  gold and purchases aren't counted." One PC shares one folder automatically. For several PCs, copy every PC's match
  folder into one before building.

## Error handling

- Disk or IO failure: one error log, telemetry disabled for the session, gameplay unaffected.
- `mStart` missing (a room created before this feature): the first master to see it absent writes it, and events
  before that get `t = −1`.
- Partial logs (a late joiner, a crash): the report uses what exists and lists each player's covered time range.
- Unknown event types or a newer schema: skipped with a count in the report header, never a failure.

## Testing and verification

- **Edit-mode:**
  - event serialization round trip;
  - `IncomeAttribution` (sums match, each zone's share correct);
  - aggregator totals on a fixture match (two clients' files, a known number of hits, purchases and captures,
    including a duplicated territory event across a master change);
  - CSV escaping and invariant culture;
  - the report builder writes all 12 CSVs and a non-empty HTML for the fixture.
- **One-client play mode:**
  - with Free Loadout off: capture a zone, earn gold, buy and refund, shoot a test dummy with two weapons, drop a
    marker, die once;
  - build the report and check every number against the Editor's own state at the time (gold property, weapon damage
    dealt from `CombatEvents`);
  - the controller looks at the HTML.
- **Two clients (in 2.8):** both logs merge; each hit appears once; the hit total matches damage taken measured on the
  victim; both players' gold curves match their Player Properties.
- `NetworkPrefabObservablesTests` passes. RpcList unchanged (no RPCs added).

## Out of scope

Cloud upload, the live in-game panel, replays, anti-cheat, automatic balance suggestions, and comparing two matches in
one report (the CSVs cover that in Excel).
