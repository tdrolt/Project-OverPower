# Playtest polish before Phase 2 — design

**Date:** 2026-09-14 · **Branch:** `limit-testing` · **Status:** approved by Tudor in brainstorming, 2026-09-14

Tudor's first hands-on test of Phase 1 found that he could not select abilities, could not read the UI, could not see
weapon inaccuracy, and that the overheat bar never filled. This spec fixes those before Phase 2 (match loop) starts.
**Phase 2 starts only after Tudor has tested every weapon and ability with these changes.**

Markers: [T] Tudor decided · [G] GDD · [C] Claude's starting value (Inspector-tunable, Tudor has not designed it).

---

## Decisions taken in brainstorming (do not re-litigate)

| Topic | Decision |
|---|---|
| Moving accuracy | **Both** [T]: a flat spread penalty the moment you move, plus bloom toward the max the longer you keep moving |
| Aim cone visual | **Edge lines** [T]: two thin lines from the muzzle to the weapon's range, visible only to the local player |
| Overhead bar | **Shield drawn over health** [T], on the existing world-space bar. Tudor accepts that a full shield hides max HP |
| Weapon upgrades before gold | **Real tree + free reset** [T]: start on Baseline, pick a path to unlock its two leaves, a leaf is final, "Reset weapon" returns to Baseline |
| Screen + HUD layout | As mocked up and approved [T]: tree left, ability picks right, armor under the tree, clickable Loadout button bottom-right; HUD = slots row on top, then overheat, shield, health; smaller chat bottom-left |
| Short names | Approved [T]: Rocket, Burst, SMG, Laser; leaves Scaling, Cursor, Charge, Bounce, Rapid, Shotgun, Charge, X-Ray; abilities Dash, Blink, Sprint, Portal, Zip, Mines, Cover, Flame, Stun, Pulse, Raybeam, Shield, Fence, Zone |
| UI technology | **uGUI built in code** (as `PlayerHud` / `TestRangePanel` already are) with every visual value on one `UiTheme` asset [T approved approach] |

## Root causes found before designing

1. **The overheat bar never fills — a bug.** `PlayerHud` builds its bars as `Image.Type.Filled` with **no sprite**.
   Unity ignores `fillAmount` on a sprite-less image and draws the full quad, so the bar is always full width and only
   its colour changes (the pulse at the warning threshold is the "flicker"). The same applies to the health, armor,
   cooldown-cover and ultimate-meter images; it went unnoticed where the value is usually full. Two code reviews
   read `fillAmount` in code and wrongly concluded the fill worked. **Must be verified by measuring the rendered
   result**, not by reading code.
2. **The baseline "shoots straight while moving" — tuning, not a bug.** `AimConeState.EffectiveAngle` is the current
   angle while moving and `current / standingStillMultiplier` while still. Baseline `minConeAngle` 1.5 → about ±0.13m
   at 10m while moving, invisible in play. Moving never added spread.

---

## Part 1 — Movement spread and the visible aim cone

### Rule
`WeaponDefinition` gains two fields (one home each, `[Tooltip]` in plain language):
- `movingSpreadDegrees` — added to the cone the instant the player is moving; removed the instant they stop.
- `movingBloomPerSecond` — while moving, the cone's current angle grows at this rate toward `maxConeAngle`; recovery
  (`recoveryPerSecond`) keeps running as today, so net growth while moving is `movingBloomPerSecond − recoveryPerSecond`.

Effective angle:
- moving: `min(current + movingSpreadDegrees, maxConeAngle + movingSpreadDegrees)`
- still: `current / standingStillMultiplier` (unchanged)

`AimConeState` owns the maths (pure, edit-mode tested). `WeaponDefinition.OnValidate` warns when
`movingBloomPerSecond ≤ recoveryPerSecond` and it is non-zero (the bloom would never accumulate — the same kind of
"decorative number" invariant as bloom vs recovery). Moving bloom and firing bloom share `current`.

The cone is still configured per weapon on equip; confirm `PlayerAim` reads the **equipped weapon's** cone values (it
constructs `AimConeState` from its own fields — if those are not refreshed on weapon change, fix that; say so).

### Starting values [C] — every one tuned later by Tudor

| Weapons | `movingSpreadDegrees` | `movingBloomPerSecond` |
|---|---|---|
| 1 Baseline | 4 | 3 |
| 2–4 Rocket path | 8 | 5 |
| 5–7 Burst path | 4 | 3 |
| 8 SMG · 9 Rapid | 3 · 4 | 4 · 8 |
| 10 Shotgun | 2 | 2 |
| 11–13 Laser path | 2 | 2.5 |

Check each against the invariant above and adjust up if needed (report the final table).

### The visual — `Assets/scripts/Player/AimConeView.cs`
- Owner-only (`photonView.IsMine`); nothing drawn for remote players; nothing networked.
- Two `LineRenderer` edges from `WeaponFiring.SafeMuzzlePosition` along aim ± half the **same effective angle the shot
  sampler uses this frame**, out to the weapon's range (charged range where charge applies). Each edge is clipped at the
  first `Building` hit so lines never draw through walls.
- Shotgun (`simultaneous` + `spreadDegrees`): an inner, fainter pair of lines at ± `spreadDegrees / 2` shows the fan;
  the outer pair shows the cone jitter around it.
- Hitscan weapons use the same cone.
- Colour, width, opacity, and "hide while dead / while the loadout screen is open" are tunables (on `UiTheme` or the
  component).

## Part 2 — HUD fixes and readability

- **Fill bug:** give every filled image a real sprite (a 4×4 white sprite asset, referenced from `UiTheme`; Unity's
  built-in `UISprite` is acceptable if it resolves in builds), so `fillAmount` works for health, shield, overheat,
  cooldown covers and the ultimate meter.
- **Order (approved):** slots row on top, then overheat, shield, health, anchored bottom-centre.
- **Readability:** TextMeshPro everywhere in HUD and loadout screen; larger sizes; outline on text drawn over the game;
  opaque-enough panel backgrounds; `CanvasScaler` reference 1920×1080, match 0.5. All of it from `UiTheme`.
- **Overheat bar:** fills correctly; keeps the warning tick at `GameplayConfig` threshold; the warning "flicker"
  becomes a slow pulse with a `pulseAtWarning` toggle and `pulseSpeed` on the theme.
- **Short names:** set `displayName` on the 13 `WeaponDefinition` and 14 ability `AbilityDefinition` assets to the
  approved short names; add a one-line `description` field (where missing) used by the loadout screen's hover text.
  The HUD and loadout screen both read `displayName` — one home.
- **Chat smaller:** change only the chat UI's layout/size/font values (RectTransform, font size) in the scene or
  prefab; no edits to chat logic scripts (`chatmanager.cs` stays untouched).

## Part 3 — Overhead health + shield bar (option C)

On the world-space `HealthBarCanvas` of every player: one bar; health fill; shield fill drawn **over** it, both against
their own max (health / max health, shield / shield capacity). Replaces the separate armor strip added in Task 1.12a.
Updated from the values every client already has (health sync + armor sync + replicated armor levels), so remote
players and late joiners read correctly. Same sprite fix as Part 2.

## Part 4 — Loadout screen (the future shop)

### Behaviour
- Opens with **P** (GDD shop key [G]) and with a clickable **Loadout** button bottom-right of the HUD; closes with P,
  Esc, or its X. While open: gameplay input is suppressed the same way chat typing suppresses it; the cursor is free.
- **Weapon tree [T]:** Baseline at the root; the four paths below; each path's two leaves below it. The currently
  equipped node is highlighted. Selectable nodes: the children of the currently equipped node only. Leaves are final.
  Nodes not reachable are shown locked (muted, dashed). **Reset weapon** equips Baseline.
- **Abilities:** one row of cards per slot (Mobility / Shift, Equipment / RMB, Ultimate / Space); clicking equips it;
  the equipped card is highlighted. Debug abilities (901–903) are hidden from this screen (they stay in F1).
- **Armor:** +Absorb (level x/max), +Recharge (level x/max), Reset — through `ArmorUpgradePath`, respecting
  `ArmorConfig.maxArmorUpgrades`.
- **Hover:** a card shows its `description` and its key numbers read live from the assets — weapons: damage, fire
  interval, range, overheat per shot; abilities: cooldown and charges from the module prefab.

### Structure
- `Assets/scripts/Combat/WeaponUpgradeTree.cs` — **pure**, edit-mode tested: built from `WeaponDefinition` id + parent
  links; answers `Root`, `ChildrenOf(id)`, `IsLeaf(id)`, `CanUpgrade(fromId, toId)`, `StateOf(nodeId, equippedId)` →
  Equipped / Selectable / Owned-ancestor / Locked. Detects parent cycles (the open item from Task 0.8) and reports them
  instead of looping. Prices come later as data, not UI changes.
- `Assets/scripts/UI/LoadoutScreen.cs` — builds the screen in code (owner-only), reads catalogues, calls only
  `PlayerLoadout.SetWeapon` / `SetAbility` / `SetArmorLevels` (already replicated through Custom Properties) and the
  tree for rules. No new RPCs.
- `Assets/scripts/UI/UiTheme.cs` + `Assets/Gameplay/Config/UiTheme.asset` — fonts, sizes, colours, outline, panel
  alpha, bar sprite, cone line style. `PlayerHud`, `LoadoutScreen`, `AimConeView` and the overhead bar read it.
- Input: add a `Loadout` action bound to P in the project's input actions asset; `PlayerInputRouter` exposes it
  (check how chat suppression is implemented and reuse it). F1 test panel unchanged.

---

## Verification (measured, single client, Editor)

- Edit-mode tests: `WeaponUpgradeTree` (paths from root, leaves final, locked nodes, reset, cycle detection);
  `AimConeState` moving spread + moving bloom + invariant; existing 346 still pass.
- **Rendered fills:** for each HUD bar, set a known value (e.g. heat 40 / 80 / 100, shield 50%, health 30%) and read
  the rendered result — screenshot plus `Image.fillAmount` **and** confirm a sprite is assigned. Overhead bar the same.
- **Cone:** the half-angle drawn equals the sampler's effective half-angle while still, moving, and after moving for
  2s (report degrees). A baseline player strafing at 10m against a stationary dummy lands fewer hits than standing
  (report hit counts over 20 shots each, armor + health).
- **Loadout:** P and the button open/close; input suppressed while open; Baseline → Rocket → Scaling works; from
  Rocket, Burst is locked; Reset returns to Baseline; each ability card equips its ability (HUD slot updates);
  armor buttons respect the cap; the choice replicates through `PlayerLoadout` (read the Custom Properties).
- **Readability:** screenshots at 1920×1080 of the HUD and the loadout screen, text legible (capture at full size).
- Stop play mode before running tests or recompiling (the rule that once hung the Editor).

## Out of scope

Gold, prices and purchase gates (Phase 2 shop — this screen is its UI); icon artwork (cards are text); two-client
testing (Tudor's playtest); any change to weapon damage or ability numbers other than the two new movement fields.
