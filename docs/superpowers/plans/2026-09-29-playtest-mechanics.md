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

| # | Decision | Who |
|---|---|---|
| D1 | Health pack: +50 health, reappears 30 s after it's taken. | D |
| D2 | Health packs sit in the three Tier III recesses (one each). Not in the capital pockets; not in the recess that appears when the map shrinks. | A |
| D3 | A pack can only be taken by a living player who isn't at full health; it heals up to max health, never above. While it's gone, a faint marker shows where it will return. | A |
| D4 | The shop opens while dead; what you buy is yours when you respawn (same prices, same rules). | A |
| D5 | Ability and item descriptions show as a tooltip after hovering 1 s; the shield (armour) shows "upgrade 1 of 2" style limits. | D (the limit wording: A) |
| D6 | Weapon upgrade tree: arrows from each weapon to its upgrades, forming branches. 2-3 mockups first; Tudor picks one. | D |
| D7 | Mines arm 1.5 s after being placed (a number: `Assets/Resources/Mine.prefab` › Arm Delay Seconds, now 0.5), and blink while arming. | D (the blink: A) |
| D8 | Dash: 3 charges. When all 3 are used, the Dash locks until 2 have refilled; the charge marks turn red while locked. Using it with charges left works as today. | D |
| D9 | Sonic Pulse also pushes the user 4 m backwards (away from where they aim), at the same speed as the enemy push; the user is never stunned by hitting a wall. | D |
| D10 | Name screen: a short panel of tips: the keys, overheat and the Vent, how ability charges work, the game mode in two lines. Texts in `UiTheme`. | D (content: A) |
| D11 | AoE Zone ultimate: while it is up, pressing the ultimate key once more throws it to the cursor, at most 5 m from the player; from then on it stays there (stops following the player) for its remaining time. | D (the "stays there" and "once": A) |
| D12 | Hold Tab: a scoreboard grouped by team, one row per player: name, kills, deaths, assists, damage dealt, zones captured (captures the player took part in). Release to close. | D (hold vs toggle, what counts as a capture: A) |
| D13 | The ultimate meter complaint is investigated before anything is changed (see Task 9). | A |
| D15 | Teleport portals: teammates can use them too, not only the player who placed them; standing anywhere on the portal counts (not just its middle); the portal is 20% smaller (2.5 m → 2.0 m across). | D |
| D16 | A teammate's trip works like the owner's (the same 3 s channel, interrupted the same way) and uses the same portal charge and cooldown as the owner's own trips; enemies still can't use them. | A |
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

### Task 1: Mines arm later, and show it (D7)
- **Files:** `Assets/Resources/Mine.prefab` (arm delay 0.5 → 1.5, a YAML/serialized change - grep it after saving),
  the mine's view script (a blink while arming, visible to everyone who can see the mine; the owner sees it too).
- **Test first:** the arming rule (a mine placed at t=0 does not trigger at t=1.4 and does at t=1.6 with an enemy on it),
  as a pure rule if the logic isn't one already.
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

### Task 5a: Upgrade-tree mockups (D6) - Tudor chooses
- Make 2-3 mockups of the weapon upgrade tree with arrows (e.g. top-down tree, left-to-right branches, radial) as one
  page (an Artifact) using the real weapon names and prices from the weapon assets. Stop and let Tudor pick.

### Task 5b: Shop clarity and shop while dead (D4, D5, D6)
- **Files:** `PlayerInputRouter` (`ShopSuppressed` currently blocks the dead), `LoadoutScreen` (open while dead; the
  purchase path must work while dead and apply at respawn - check the out-of-combat gate and any "alive" assumption),
  the tooltip (1 s hover; the existing description text), the armour upgrade limit shown, the chosen upgrade-tree layout.
  Texts in `UiTheme`.
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
- Tasks 3b, 4, 6 and 7 add shared state: every build in a room must match; test a late joiner each time.
- Out of scope: the ScriptableObject top ten (`so-top-ten-plan-2026-09-26.md`), compacting code comments, the vision
  feature itself (Task 11 only starts its design).

## Done when
- Every task reviewed (approve), checked in Play Mode or on two clients with captures, logged (progress, workflow log,
  feedback file rows T8-T18 updated), and the Task 10 check passes; then Tudor decides the merge.
