# Plan: playtest follow-ups that change mechanics (2026-09-29)

Follows workflow v1.1 (`C:\UniStuff\Y3\MinorSkilled\Resources\core\`). Tudor did the value changes himself
(`6d8a0a4`, `0de8fb7`, `4297e13`, `7c6183c`); this plan covers what needs new behaviour. Source: the feedback file
`C:\UniStuff\Y3\MinorSkilled\Resources\playtests\2026-09-26-playtest-feedback.md` (rows T8-T18).

**Goal (play terms):** the recesses become places worth fighting over; the shop and upgrades are understandable and
usable while dead; Dash, Sonic Pulse, Mines, the teleport portals and the AoE ultimate play as Tudor redesigned them; new players get the
basics on the name screen; everyone can see the score with Tab; the ultimate meter complaint is explained or fixed.

## Designer's answers and defaults (Ask step)
**D** = Tudor decided, **A** = default until Tudor says otherwise. The session confirms the A rows with Tudor at its
start (play terms, one question each, default stated), then proceeds with whatever he answers.

**2026-09-29 session start: Tudor confirmed every A row as stated** (the look/wording specifics as offered: the shield
limit "Upgrade 1 of 2" / "2 of 2 (max)"; tips = keys, overheat + Vent, charges, the mode; a capture counts for everyone
standing in the circle when it flips; swap button "Swap (sell back +N)" + "Sold X: +N gold"; the shield ring for its 2 s
+ a "Wasted" pop). Changes: D2 = the middle of each recess; D18 adds a SLOWED label (STUNNED takes priority).

| # | Decision | Who |
|---|---|---|
| D1 | Health pack: +50 health, reappears 30 s after it's taken. **Look (09-29): glows green when ready, with a floating cross (the classic health-pack plus) above it; while taken, the same cross stays, greyed out (replaces D3's "faint marker"). The cross stands upright (a plus sign in a vertical plane), bobs, and slowly spins around the vertical axis. On the minimap: a small cross per pack, green when ready, grey when taken (Task 4b).** | D |
| D2 | Health packs sit in the three Tier III recesses (one each), **in the middle of the recess zone**. Not in the capital pockets; not in the recess that appears when the map shrinks. | D (09-29) |
| D3 | A pack can only be taken by a living player who isn't at full health; it heals up to max health, never above. While it's gone, a faint marker shows where it will return. | A |
| D4 | The shop opens while dead; what you buy is yours when you respawn (same prices, same rules). | A |
| D5 | Ability and item descriptions show as a tooltip after hovering 1 s; the shield (armour) shows "upgrade 1 of 2" style limits. | D (the limit wording: A) |
| D6 | Weapon upgrade tree: arrows from each weapon to its upgrades, forming branches. 2-3 mockups first; Tudor picks one. **09-29: Layout A (top-down tree: Baseline on top, the four families below, two upgrades under each) - https://claude.ai/artifact/QPVHbRi8Gojf8CaZ7hAvEA.** Prices stay at the playtest's -20%. | D |
| D7 | Mines arm 1.5 s after being placed (done by Tudor, `84391e5`) and blink while arming. | D (the blink: A) |
| D8 | Dash: 3 charges. When all 3 are used, the Dash locks until 2 have refilled; the charge marks turn red while locked. Using it with charges left works as today. | D |
| D9 | Sonic Pulse also pushes the user 4 m backwards (away from where they aim), at the same speed as the enemy push; the user is never stunned by hitting a wall. | D |
| D10 | Name screen: a short panel of tips: the keys, overheat and the Vent, how ability charges work, the game mode in two lines. Texts in `UiTheme`. | D (content: A) |
| D11 | AoE Zone ultimate: while it is up, pressing the ultimate key once more throws it to the cursor, at most 5 m from the player; from then on it stays there (stops following the player) for its remaining time. | D (the "stays there" and "once": A) |
| D12 | Hold Tab: a scoreboard grouped by team, one row per player: name, kills, deaths, assists, damage dealt, zones captured (captures the player took part in). Release to close. | D (hold vs toggle, what counts as a capture: A) |
| D13 | The ultimate meter complaint is investigated before anything is changed (see Task 9). | A |
| D15 | Teleport portals: teammates can use them too, not only the player who placed them; standing anywhere on the portal counts (not just its middle); the portal is 20% smaller (2.5 m → 2.0 m across). | D |
| D16 | A teammate's trip works like the owner's (the same 3 s channel, interrupted the same way) and uses the same portal charge and cooldown as the owner's own trips; enemies still can't use them. | A |
| D17 | **Last stand, changed:** a base that is neutral or held by an enemy is lost only when every member of its team is dead. While any member is alive, the team is in its last stand: nobody on it can respawn - neither players who were already dead when the base fell nor players who die after. The two-team "out at once if the other team holds a base" rule goes. Holding another team's base still counts as having a base. | D |
| D18 | A **"STUNNED"** label with a short countdown bar over a stunned player (everyone sees it) and on your own HUD. Yellow text, a thin bar shrinking over the stun's time. **The same for slowed ("SLOWED")**; when both apply, STUNNED always shows (the stronger effect). | D (09-29: the look confirmed, SLOWED added by Tudor) |
| D19 | The shop shows what a swap costs: selling back and the refund amount on the button, and a short message after a sale. | D (the wording: A) |
| D20 | The shield ultimate shows its armed window: a visible ring/glow for its 2 s, and a "wasted" cue if nothing hit you in time. | D (the look: A) |
| D21 | **Connection lost:** a "Connection lost - Rejoin" panel. Rejoining puts you back in the same match as the same player (same team, gold, loadout) - also after a crash and restart, within 2 minutes. Identity: a random id saved on the PC the first time the game runs (not the name, not the IP: names repeat and IPs change). Two players with the same name are fine; they're told apart by the id. | D (the id, the 2 minutes: A) |
| D22 | When a match ends, the result screen's button takes you back to the name screen (a fresh start) instead of quitting; Escape still offers "Close the game?". | D |
| D23 | Teleport portals show their cooldown: after any trip (owner or teammate) the pair can't be used for 10 s (the owner's 1 charge / 10 s, already the rule); while it can't, its glow turns grey for everyone, back to its colour when usable. | D (09-29, the look: grey) |
| D24 | Health pack position: in the Tier III recess behind the capture zone, between the arena wall and the short yellow wall (drives players into the recess). A full-health player can't take it; 99/100 can (and uses it up). | D (09-29) |
| D25 | The ultimate: an empty ultimate slot shows "No ultimate"; the go-live wipe stays; shield hits give no charge; burning counts like any other damage (Tudor, later on 09-29: no extra). | D (09-29) |
| D26 | Retaking a base during a last stand respawns the waiting teammates (as today). | D (09-29) |
| D27 | You're out of combat when you die and when you respawn (shop, armour recharge, regen). | D (09-29) |
| D14 | Every new number lives in an editable asset with a plain tooltip (the QA rule: "everything in ScriptableObjects"). | D |

## Rules that bind every task (from the handover, `Resources\loops\Limit Test\HANDOFF.md`)
- **No RPC added, renamed or removed** (the list is index-based; the count stays 31). New shared state goes in Room or
  Player Properties (master-only writer, check-and-set; act on the echo, never on the send). Appending a parameter to an
  existing RPC is allowed only if every build then matches.
- The Editor lock; one Editor-driving agent at a time; reviewers read-only; tests guard rules, never Tudor's numbers;
  Tudor's uncommitted asset changes are his; never the real mouse/keyboard/focus; list the room's actors before
  measuring; never stop a process by name; never print the Photon App IDs; the runtime server on only for a development
  Client2 build.

## Tasks (in order; 30-90 min each)

### Task 1: Mines show they're arming (D7)
- The 1.5 s delay is done (`84391e5`). **Files:** the mine's view script: a blink while arming, visible to everyone who
  can see the mine.
- **Test first:** the arming rule (placed at t=0: no trigger at t=1.4, triggers at t=1.6 with an enemy on it), as a pure
  rule if the logic isn't one already - it guards the delay whatever the number is.
- **Check:** Play Mode: place a mine on a test dummy; it explodes only after 1.5 s; capture the blink.
- **Review:** standard.

### Task 2: Dash charge lock-out (D8)
- **Files:** the charge pool rule (find `ChargePool`/`ChargeCountRule` under `Assets/scripts/Abilities` or `Combat`),
  the Dash module (a new field "charges needed after running dry", 2, with a tooltip, living with Dash's other numbers),
  the ability HUD (charge marks red while locked; colour in `UiTheme`).
- **Test first:** pure rule: 3 charges → use 3 → locked; 1 refills → still locked; 2 refilled → usable; using 2 then
  waiting behaves as today.
- **Check:** Play Mode: capture the red marks while locked.
- **Review:** standard.

### Task 3: Sonic Pulse pushes the user back (D9)
- **Files:** `SonicPulseAbility` (a new field "self push distance", 4 m, tooltip), using the existing forced-move system
  (`PlayerDisplacement`), never `transform.position`; the self push never applies the wall-hit stun.
- **Test first:** the direction rule (the user moves opposite to the aim direction) and "no stun for the user" as pure
  rules.
- **Check:** Play Mode: cast against a wall behind you and in the open; the user ends 4 m back, not stunned. Two clients:
  the other client sees the user move (it's the owner's own movement, replicated as usual).
- **Review:** standard.

### Task 3b: Portals for teammates, easier to enter, smaller (D15, D16) - networking
- **Today:** a portal is personal (`Assets/scripts/Abilities/Mobility/Portal.cs`, `TeleportAbility.cs`); the channel
  (`PortalChannelState`) runs only in the placing player's own ability, on their own client. Every client already
  knows every portal (`Portal`'s per-owner registry), so a teammate's client can find an ally's portal pair.
- **Change:** a teammate standing on an ally's portal channels for themselves on their own client (their own player
  moves, through `PlayerDisplacement`), with the same rules as the owner (3 s, cancelled by moving out, stun, silence).
  "Standing on it" = the player's body overlaps the circle (portal radius + the player's radius), for owner and allies
  alike. The trip spends the portal's charge/cooldown the way D16 says; find how the owner's charge is spent and share
  it without a new RPC (a Player or Room Property written by the portal's owner, or an appended parameter on an existing
  message - say which in the report). Diameter 2.5 → 2.0 on `Assets/Resources/Portal.prefab` (grep the saved value).
- **Test first:** pure rules: who may use a portal (owner or teammate, alive, not an enemy); "on the portal" with the
  body radius at the edge; the shared charge.
- **Check:** two clients on the same team: B uses A's portal pair; the charge is spent on both screens; an enemy (a
  third client, or a forced team) can't; entering at the rim works. Captures.
- **Review:** the strongest model (networking).

### Task 3c: Portals greyed while cooling down (D23)
- **Files:** the portal's view (`Portal.cs` / its prefab visual), `UiTheme` or the portal prefab for the grey. Driven by the owner's `tpRdy` Player Property (Task 3b), so every client agrees; no RPC.
- **Test first:** pure rule: shown usable ↔ owner has a charge.
- **Check:** two clients: after a trip both see the pair grey for ~10 s, then coloured. Captures.
- **Review:** standard (reads an existing property; no new shared state).

### Task 4: Health packs in the Tier III recesses (D1-D3) - networking
- **Design:** positions come from the arena layout (the Tier III recess piece, rotated into the three thirds by the
  arena builder - read `ArenaLayout.asset` and `ArenaSymmetry`/the builder). State per pack in a Room Property written
  only by the master: "taken until <server time>". A living, not-full player overlapping a pack asks for it by... use
  the same pattern the project already uses for master-arbitrated requests (read how zone capture requests reach the
  master; if none fits without a new RPC, append a parameter to an existing request RPC and say so in the report).
  The master checks the rules (alive, not full, pack available), writes "taken until", and the taker's own client applies
  the heal (health is owned by the victim's client, see `PlayerHealth`). Everyone shows/hides the pack from the property.
- **Asset:** a `HealthPackConfig` (or fields in `TerritoryConfig`/`GameplayConfig`, whichever fits the project's
  pattern): heal 50, respawn 30 s, pickup radius, with tooltips.
- **Test first:** pure rules: may take (alive, below max, available); heal amount clamps to max; available again after
  30 s of server time (wrap-safe, like the other timers).
- **Check:** two clients: both see the pack; B takes it (health goes up by 50, capped); A sees it vanish and return 30 s
  later; two players touching it in the same instant → only one heals. Capture.
- **Review:** the strongest model (networking).

### Task 4b: Health packs on the minimap (D1 look)
- **Files:** the minimap (find its zone-bubble code), reading the pack state Task 4 exposes. A small cross icon per pack: green when ready, grey when taken; colours/size in `UiTheme`. No network change.
- **Check:** Play Mode: a ready pack and a taken pack on the minimap (capture).
- **Review:** standard.

### Task 4c: Health packs into the recess (D24)
- Move each pack into its Tier III recess, between the arena wall and the short yellow wall; minimum missing health 0.
- **Check:** capture per recess; a two-client re-take isn't needed (position only). **Review:** standard.

### Task 5a: Upgrade-tree mockups (D6) - Tudor chooses
- Make 2-3 mockups of the weapon upgrade tree with arrows (e.g. top-down tree, left-to-right branches, radial) as one
  page (an Artifact) using the real weapon names and prices from the weapon assets. Stop and let Tudor pick.

### Task 5b: Shop clarity and shop while dead (D4, D5, D6, D19)
- **Files:** `PlayerInputRouter` (`ShopSuppressed` currently blocks the dead), `LoadoutScreen` (open while dead; the
  purchase path must work while dead and apply at respawn - check the out-of-combat gate and any "alive" assumption),
  the tooltip (1 s hover; the existing description text), the armour upgrade limit shown, the chosen upgrade-tree layout, the swap/sell-back
  price on the button (D19). Texts in `UiTheme`.
- **Test first:** the pure rules that change (may open the shop while dead; tooltip delay rule if extracted).
- **Check:** Play Mode: die, open the shop, buy, respawn with it; hover 1 s → tooltip; captures of each.
- **Review:** standard.

### Task 6: AoE Zone recast to the cursor (D11) - networking
- **Files:** `AoeZoneAbility`, `AoeZone` (a networked deployable that follows its caster), the ultimate input path.
  A recast while the zone is up moves it to the cursor point clamped to 5 m from the caster (field "recast range", 5,
  tooltip), once; it then stops following. The move must reach every client (the zone's owner moves it; check how its
  position replicates) and late joiners.
- **Test first:** pure rules: clamp to 5 m; only one recast; no recast after it ended.
- **Check:** two clients + a late joiner: the zone jumps to the cursor on both, stays there, keeps ticking damage there.
- **Review:** the strongest model (networking).

### Task 7: Tab scoreboard (D12) - networking
- **Files:** a `ScoreboardPanel` (hold the existing but unused Scoreboard action in `PlayerInputRouter`), stats per
  player in Player Properties (kills, deaths, assists, damage dealt, zones captured), each written only by its owner's
  client or the master, whichever already owns that fact (read `PlayerCombatCredit`, `PlayerLifecycle`, the capture
  code). Throttle writes (damage adds up; publish at most a few times a second).
- **Test first:** pure rules for counting (an assist, a capture credit, damage summing) and the table's ordering.
- **Check:** two clients: kill, assist, capture → both scoreboards agree; capture of the panel.
- **Review:** the strongest model (networking).

### Task 8: Name-screen tips (D10)
- **Files:** the name-entry UI, `UiTheme` texts. Check it's readable at 1280×720 and 1920×1080 (captures).
- **Review:** standard.

### Task 9: The ultimate meter complaint (D13) - investigate first
- Use superpowers:systematic-debugging. Evidence so far (the controller, 2026-09-29): in the merged playtest logs
  (`Resources\Telemetry\merged-2026-09-26_9a017d96\`), after live every player's meter refilled after each use, in 10 s
  to about 4 minutes; nothing shows a meter stuck for good. Suspects: damage sources that don't raise the event
  `UltimateCharge` listens to (`CombatEvents.LocalDamageDealt`): burns, the flamethrower, the AoE Zone, mines, the fire
  field, reflected/indirect damage; charge lost at death or at respawn; the meter's display vs its value.
- Report the cause with evidence before changing anything; a fix gets a test that fails without it.
- **Review:** standard (the strongest model if the cause is networking).

### Task 9a: "No ultimate" on the empty slot (D25)
- The Ultimate slot shows "No ultimate" (text in `UiTheme`) instead of the meter's READY while nothing is equipped.
- **Check:** Play Mode capture after go-live with no ultimate. **Review:** standard.

### Task 9a2: ~~Burning enemies charges the ultimate~~ - dropped (Tudor: burning counts like any other damage, which it already does)

### Task 9b: Last stand (D17) - rules, networking
- **Files:** `Assets/scripts/Match/Rules/MatchPhaseRules.cs` (`IsOutNow`, `SpawnCapitalFor`, `IsLastStandDeath`),
  `MatchDirector` (the team statuses), `PlayerLifecycle` (a respawn countdown running when the base falls must end in the
  last stand, not at home). Update the tests that pin the old two-team "instant" rule and the "countdown that began before
  the fall ends at home" rule - they describe rules Tudor has now changed; say which tests changed and why.
- **Test first:** pure rules: base neutral/enemy-held + a member alive → not out, nobody respawns (dead before or after);
  every member dead → out; holding another team's base → respawns there; two-team phase no longer instant.
- **Check:** two or three clients: drain a team's base, kill its members one by one; nobody respawns; the last death knocks
  the team out; the match ends correctly. Captures + recorder lines.
- **Review:** the strongest model (rules every client must agree on).

### Task 9c: The STUNNED and SLOWED labels (D18)
- **Files:** the status-effect view (find where stun and slow are applied and shown), `UiTheme` (text, colours, sizes).
- **Test first:** pure rule: which label shows (stun beats slow; none when neither).
- **Check:** Play Mode: stun a test dummy and yourself; capture both. Two clients: the other player sees it.

### Task 9d: The shield's window (D20)
- **Files:** `InvulnerabilityAbility` / its view; `UiTheme`. The cue shows for the armed window and a "wasted" cue when it
  ends untriggered; everyone sees the ring.
- **Check:** Play Mode captures of both outcomes (hit in time / not).

### Task 9e: Connection lost, rejoin, and coming back after a crash (D21) - networking
- **Today:** rooms have no player time-to-live, so a dropped player is gone at once; nothing identifies a returning player.
- **Change:** a random id saved on the PC on first run, used as the Photon user id (`AuthValues`); rooms keep a dropped
  player for 120 s (`PlayerTtl`, an editable value); on a disconnect, a "Connection lost - Rejoin" panel
  (`ReconnectAndRejoin`); on start-up, if this PC's id was in a match that's still running, offer "Rejoin your match"
  (`RejoinRoom`). A rejoin keeps the same actor, so team, gold and loadout (Player Properties) come back; the player
  respawns as after a death. Texts in `UiTheme`.
- **Test first:** pure rules around it (when to offer a rejoin; the id is created once and reused).
- **Check:** two clients: cut B's connection (disconnect by script) → panel → Rejoin → same team, gold, loadout; stop B's
  process (by its PID) and restart within 120 s → "Rejoin your match" works; after 120 s it doesn't. Captures.
- **Review:** the strongest model (networking).

### Task 9f: Back to the name screen after a match (D22)
- **Files:** the result panel (`MatchUI`), the room flow (leave the room, return to the name screen with a clean state:
  nothing left over from the last match). Escape → "Close the game?" stays.
- **Check:** end a match (or force the result panel) → the name screen → join a new match normally. Capture.

### Task 10: One multi-client check of everything
- A fresh development Client2; two clients (three if possible). Everything above, plus the checks still owed from before
  the playtest (friction F6 in `Resources\workflow-log.md`): play over UDP including a mid-match join, the host starting
  with two and three teams, typing in chat in a build, the log folder and zip next to the game's .exe, a neutral base =
  no respawn, capture sounds off, shots at half volume. Captures of each; every result in the progress log.

### Task 11: Merge into main, then branch for vision
- Only after Task 10 passes AND Tudor says "merge" in that session: merge `limit-testing` into `main` (no force push),
  push, then create a new branch (e.g. `vision`) from main.
- Then start the next feature at the **Ask** step: fog of war + a vision cone + the centre (Tier IV) revealing enemy
  positions to the team that owns it every 30 s. Brainstorm with Tudor first (superpowers:brainstorming); no code before
  he approves a design.

## Risks and out of scope
- Tasks 3b, 4, 6, 7, 9b and 9e add or change shared state: every build in a room must match; test a late joiner each time.
- Out of scope: the ScriptableObject top ten (`so-top-ten-plan-2026-09-26.md`), compacting code comments, the vision
  feature itself (Task 11 only starts its design).

## Done when
- Every task reviewed (approve), checked in Play Mode or on two clients with captures, logged (progress, workflow log,
  feedback file rows T8-T18 updated), and the Task 10 check passes; then Tudor decides the merge.
