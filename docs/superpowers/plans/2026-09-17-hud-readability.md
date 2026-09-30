# HUD Readability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal (Tudor, 2026-09-17):** the HUD gets out of the way and reads at a glance.

- The whole HUD is 20% smaller, and every label inside an ability square is centred.
- The charge pips are bigger and readable.
- No dark panel behind health, shield, overcharge or the abilities; they hold their own on bright sand instead.
- The corner minimap is 20% more see-through; M puts it at full opacity, and moving with the big map open drops it
  30% again, without flicker.
- Gold and its income move next to the Loadout/shop button.
- F1 stays one key: the test range keeps the top-left, the debug log moves to the right, below the minimap.

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, TextMeshPro (via `com.unity.ugui`), NUnit edit-mode
tests, `unity` CLI.

**Naming rule:** "T1–T4" means zone tiers only. This plan's tasks are "Task 1..7", called **HUD step N** in reports,
commits and `progress.md`.

**Read before starting:** `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\HANDOFF.md` §5 (traps), and
`docs/superpowers/plans/2026-09-17-capture-ring-minimap.md` for the house plan format.

---


## Controller answers to the plan's open questions (2026-09-17)

- **Sequencing:** HUD step 5's scratch script uses `PlayerDisplacement.TeleportTo`, and the movement work touching
  that file must land first. The queue in HANDOFF.md §3 already puts this pass after movement integrity and the
  ability visuals, so no re-check is needed; if it somehow starts earlier, re-read the signature before using it.
- **The toast keeps its full size.** It is a short-lived message the player must read at a glance, so it doesn't
  shrink with the rest of the HUD. Confirmed.
- **The loadout screen's own header keeps a plain gold total, with no income.** The income per second appears once,
  next to the shop button on the HUD. Confirmed.

## Tudor's request, verbatim

> "if you could also add ui improvement to your list it would help. I want the ability bar to be smaller and
> everything centerd properly(the text should be in the middle of the abilty square for example). make the charge
> indicators a bit bigger and use a different font if you have to to make the ui more visible. The shield, health and
> overcharge are fine but i would like if there wasnt that dark opaque background behind them and the abilites since
> it takes away from the visibility. i would also reduce the overall size by 20% I want the minimap to be 20% more
> see through but if the player presses m it should have the opacity to max (optional: unless they are moving with
> the map maximized which would decrease the opacity by 30%) I would also display the gold generation next to the
> shop since these systems are tied together) you can keep the dummy and console log opening next to each other in
> the right side or the easy fix is to just bind them to different things"

---

## Decisions [C]

Every number below is a `UiTheme` field with a plain `[Tooltip]`, so Tudor can retune it without touching code.

### D1. One 20% reduction, as a scale, not as 40 retyped numbers

`hudScale = 0.8`, applied ONCE as `localScale` on three roots:

| Root | Where | Pivot |
|---|---|---|
| `Hud Panel` | `PlayerHud.BuildUi` | (0.5, 0) bottom-centre |
| `Gold Corner` (new) | `PlayerHud.BuildUi` | (1, 0) bottom-right |
| `Shop Corner` (new) | `LoadoutScreen.Builder.BuildToggleButtonCanvas` | (1, 0) bottom-right |

- Why a scale: a designer keeps tuning `barWidth`, `slotWidth` and the text sizes in their own units, and one number
  makes the whole group smaller. Re-typing every size at 80% would destroy that and could never be undone cleanly.
- Why those pivots: the HUD's bottom edge stays exactly `hudBottomOffset` above the screen edge, and the
  gold/shop block stays exactly `loadoutToggleButtonMargin` from the bottom-right corner, at any scale.
- **The ability bar is NOT shrunk a second time.** This single scale is "the ability bar should be smaller".
- **Not scaled:** the toast (a rare full-width alert that must stay loud), the minimap (Tudor asked for opacity, not
  size), MatchUI's win/lose/respawn panels, the chat, and the F1 test range panel (a designer tool).

### D2. `minimapLargeBottomClearance` 380 → 300

The strip the big map leaves clear for the HUD shrinks with the HUD. The panel's own content is roughly
16 + 16 padding + 162 slots + 32 + 22 + 28 bars + 4 × 6 spacing ≈ 300 canvas units once the gold row leaves it
(Task 4), plus `hudBottomOffset` 28; at 0.8 that is ≈ 268. 300 leaves headroom. **Task 5 measures the panel's real
rect height in Play Mode and reports it** — HANDOFF trap 6, measure rather than calculate — and raises this number if
the measurement plus `hudBottomOffset` exceeds 300.

### D3. What replaces the dark panels

| Today | After | Why |
|---|---|---|
| `Hud Panel` `Image` at `panelColor` (0.06, 0.06, 0.08, **0.85**) | **Image deleted**, and `panelColor` deleted from `UiTheme` | Nothing else reads it (`LoadoutScreen.Builder.cs:372` only names it in a comment). A near-opaque slab is exactly what Tudor asked to lose. |
| Slot root `Image` = the whole dark square (`slotReadyColor` 0,0,0,**0.6**) | Slot root `Image` becomes a **thin border** carrying the ready/blocked/active tint, plus a child `Slot Fill` inset by `slotBorderWidth` at `slotFillColor` (0,0,0,**0.22**) | A border reads the state just as clearly, a low-alpha wash keeps the icon/name readable, and the arena shows through. |
| `slotReadyColor` (0, 0, 0, 0.6) | (0.05, 0.05, 0.07, **0.9**) — a dark, near-opaque **border** | A thin dark line is the most legible border on bright sand. |
| `slotBlockedColor` (0.30, 0.30, 0.30, 0.85) | (0.45, 0.45, 0.45, **0.95**) | As a 3-unit border it must be brighter than it was as a 140-unit fill to read at all. |
| `slotActiveGlowColor` (1, 0.85, 0.25, 1) | unchanged | Already a full-alpha amber; as a border it reads as a lit frame. |
| Bar tracks (`barTrackColor`, alpha 1) | unchanged | Tudor: "The shield, health and overcharge are fine". The track is part of the bar, not a panel behind it, and it is what the fill is read against. |
| `slotBorderWidth` | **new**, 3 | |
| `slotFillColor` | **new**, (0, 0, 0, 0.22) | |

### D4. Readability without the panels: the existing font, made heavier

**No new font asset, and no different font.** `UiTheme.font` is `{fileID: 0}` (`UiTheme.asset:17`), so every HUD label
already uses TextMeshPro's default LiberationSans SDF. Weight, size and an outline are enough:

- `textOutlineWidth` **0.2 → 0.26** — a slightly heavier dark rim, to pay for the 20% shrink.
- `hudTextFaceDilate` **new, 0.08** → TMP `_FaceDilate`. This is the weight lever on the font the project already
  ships: it thickens the glyph face itself, which is what a "bolder font" would have bought, with no second asset and
  no licence.
- A soft dark drop shadow, via TMP's underlay pass:
  - `hudTextShadowColor` **new**, (0, 0, 0, 0.75)
  - `hudTextShadowOffset` **new**, (0.5, −0.5)
  - `hudTextShadowSoftness` **new**, 0.25
  - `hudTextShadowDilate` **new**, 0.1

These go on the ONE shared material each screen already builds, through a new `UiTheme.ApplyHudTextStyle(Material)` —
one home for all seven numbers, called by `PlayerHud.ApplyOutline` (`PlayerHud.cs:1232`),
`LoadoutScreen.Builder.ApplyOutline` (`LoadoutScreen.Builder.cs:612`) and `MinimapView.AddLabel`
(`MinimapView.cs:740-745`), which today each set the same two properties by hand.

**TMP trap:** setting `_UnderlayColor`/`_UnderlayOffsetX`… alone does nothing. The underlay pass is off until
`material.EnableKeyword(ShaderUtilities.Keyword_Underlay)` (`"UNDERLAY_ON"`,
`Library/PackageCache/com.unity.ugui@fed111d25ba7/Runtime/TMP/TMP_ShaderUtilities.cs:121`). All six property IDs and
the keyword exist in this package version (lines 15, 48-52, 121 of that file) — verified, not assumed.

### D5. Centring inside an ability square

The key label is the only thing genuinely off-centre today (`PlayerHud.cs:1185-1192`: anchored top-**left**,
`TextAlignmentOptions.TopLeft`, a 90×32 rect at (2, −2) inside a 140-wide slot). The name is centred, but in the icon
box — the top 104 of a 162-tall slot — so it reads as sitting above the middle.

- **Key label:** a full-width strip across the top of the icon box, `TextAlignmentOptions.Center`, height
  `slotKeyRowHeight` **new, 30**. Built LAST inside the icon box so the recharge sweep never draws over it.
- **New `Content Box`** inside the icon box, inset from the top by `slotKeyRowHeight`. The icon, the fallback name
  and the ultimate's READY label centre in THAT — the square a player actually sees — not in a box a fifth of which
  is the key label.
- **Cooldown sweep:** still stretches the whole icon box (it is a cover, and a sweep that stopped under the key would
  look broken). Drawn after `Content Box` and before the key strip.
- **Ultimate charge fill:** still stretches the whole icon box; it is a meter, and filling the full square reads
  better. **READY** gets the `Content Box` inset so it lands in the middle of the visible square.
- **Pip row** (`MiddleCenter`) and **block-reason text** (`Center`) are already centred; their heights move onto the
  theme (`pipRowHeight`, `slotReasonTextHeight`) so nothing is a hidden `const` any more.
- **Silenced banner label** (`PlayerHud.cs:1025-1028`): `MidlineLeft` in a 260-wide `LayoutElement` inside a
  `MiddleCenter` group → the text reads left inside a centred box. Becomes `Center` with `preferredWidth` 0 (the
  layout group sizes it), so the icon + text pair is genuinely centred over the row.
- **Slot heights are unchanged.** Below the icon box the content is 2 + `pipRowHeight` 20 + 2 + `slotReasonTextHeight`
  26 = 50, still inside `slotCooldownAreaHeight` 58. Slot = 104 + 58 = 162, row width = 4 × 140 + 3 × 10 = 590 =
  `barWidth`. The `barWidth` invariant in its own tooltip still holds.

### D6. Charge pips

HANDOFF §4 already records "Charge pip size is not on `UiTheme`" — it is a hardcoded `8f` (`PlayerHud.cs:636-637`)
with a hardcoded `2f` spacing (`PlayerHud.cs:1127`) inside a `const float PipRowHeight = 14f` (`PlayerHud.cs:1113`).

- `pipSize` **new, 14** (from 8) — 5.7% → 10% of a 140-wide square, so bigger *relative to the square* as asked.
- `pipSpacing` **new, 4** (from 2).
- `pipRowHeight` **new, 20** (from the 14 const). A pip with its outline is 14 + 2 × 2 = 18, so 20 fits it.
- `pipOutlineWidth` **new, 2**, `pipOutlineColor` **new**, (0, 0, 0, 0.85) — the same dark-outline trick
  `MinimapView.BuildMarker` (`MinimapView.cs:662-669`) already uses. Without the HUD panel, a white pip on sand has
  nothing to read against.
- `slotReasonTextHeight` **new, 26** (from the `const float ReasonTextHeight = 26f`, `PlayerHud.cs:1114`).
- **Pips stay square, not discs.** `GeneratedSprites.Disc` exists and would work, but the HUD is a blocky, squared-off
  design and a square pip with a dark rim reads as "a charge" perfectly well at 14 units. One fewer sprite
  dependency, and nothing else changes.
- Three charges + outlines = 3 × 18 + 2 × 4 = 62 units wide, well inside a 140 slot.

### D7. Gold moves next to the shop

- `PlayerHud` **keeps owning** the readout — it already holds `goldWallet`, `UpdateGold` and the
  `lastGoldBalance`/`lastGoldIncome` change caches (`PlayerHud.cs:328-341`) — but builds it in a new bottom-right
  `Gold Corner` on its own canvas instead of as the first row of `Hud Panel` (`PlayerHud.cs:730-735`).
- It sits **directly above** the "Loadout (P)" button, right-aligned to the same screen edge, `goldShopGap`
  **new, 8** canvas units above it. Both are inside a (1, 0)-pivoted root at `hudScale`, so they stay aligned at any
  scale.
- Two lines in one label, so it stays a narrow column and never allocates a second `TextMeshProUGUI`:
  ```
  Gold 1234
  +7.7/s
  ```
  The second line at `goldIncomeSizePercent` **new, 75** of `bodyTextSize`, via a `<size=75%>` tag — the same
  pattern `loadoutPriceLineSizePercent` already uses (`UiTheme.cs:166-167`).
- Built by a new pure `ShopPricing.GoldHudLabel(balance, income, sizePercent)`, next to the existing
  `ShopPricing.GoldLabel` (`ShopPricing.cs:121`), so the `CultureInfo.InvariantCulture` rule that
  `PlayerHud.UpdateGold`'s own comment explains is pinned by a test instead of by a comment.
- **No duplicate.** The gold row leaves `Hud Panel` entirely. The loadout screen's own in-modal header gold
  (`LoadoutScreen.cs:553`) is NOT a duplicate: its dim covers the whole screen at `sortingOrder −5` and the HUD canvas
  is at −10, so the two are never both visible. It keeps `ShopPricing.GoldLabel` — inside the shop you are reading a
  balance you are about to spend, not watching an income tick.

### D8. Minimap opacity

New fields, all on `UiTheme` under Minimap:

| Field | Value | Meaning |
|---|---|---|
| `minimapCornerOpacity` | **0.8** | The corner map, 20% more see-through than today's effective 1.0. |
| `minimapLargeOpacity` | **1.0** | M puts it at full opacity. |
| `minimapLargeMovingOpacityDrop` | **0.3** | Moving with the big map open → 1.0 − 0.3 = **0.7**. |
| `minimapOpacityFadeSeconds` | **0.25** | A full 0→1 fade takes this long; no jump on start/stop. |
| `minimapMovingEnterSpeed` | **1.0** m/s | Moving STARTS above this. |
| `minimapMovingExitSpeed` | **0.35** m/s | Moving STOPS below this. The gap is the deadzone. |
| `minimapSpeedSmoothingSeconds` | **0.15** | Exponential smoothing of the measured speed before the test. |

- `GameplayConfig.asset:15` `baseMoveSpeed: 5`, so 1.0 m/s is 20% of walking pace and 0.35 is 7%. A player nudging a
  key crosses neither.
- **Applied with a `CanvasGroup`** on the minimap `root` (`MinimapView.cs:345`), which multiplies every graphic's
  alpha under it, including those under the nested markers `Canvas` (`MinimapView.cs:384`). `interactable = false`
  and `blocksRaycasts = false`, so the class comment's "NEVER BLOCKS A SHOT" guarantee is untouched.
- **Speed is measured, not read from config.** `PlayerMotor.CurrentSpeed` (`PlayerMotor.cs:100-108`) is the
  *configured* speed — it reads 5 m/s while a stunned player stands still. `MinimapView` samples its own
  `transform.position` delta on the XZ plane instead, which is true for dashes, knockback and stuns alike.
- The rule is pure (`MinimapOpacity`) and edit-mode tested: corner / large / large-while-moving, the hysteresis, the
  fade step and the smoothing.

### D9. F1 keeps one key; the debug log moves right

- **One key.** `TestRangePanel` (`Keyboard.current.f1Key`, `TestRangePanel.cs:136-137`) and `DebugOverlay`
  (`Input.GetKeyDown(KeyCode.F1)`, `DebugOverlay.cs:106`) both stay on F1.
  `ProjectSettings/ProjectSettings.asset:940` is `activeInputHandler: 2` (Both), so the legacy poll keeps working.
- The test range panel keeps the top-left (`TestRangePanel.cs:209-212`, width 420, `sortingOrder` 500).
- `DebugOverlay`'s box AND its "F1: debug log" hint move to the **right edge, below the corner minimap's reserved
  band**, clamped to the screen. Today both draw at `Rect(8, 8, …)` (`DebugOverlay.cs:121, 128`), straight under the
  test range panel.
- **Debug-log numbers are in SCREEN PIXELS**, not canvas units: IMGUI has no `CanvasScaler`. Their tooltips say so.
  - `debugLogWidthPixels` **new, 420**
  - `debugLogMaxHeightFraction` **new, 0.45**
  - `debugLogScreenMarginPixels` **new, 8**
  - `debugLogGapBelowMinimapPixels` **new, 6**
- The band is computed from the theme's **corner** numbers times the CanvasScaler scale factor, NOT from the map's
  live `MapRoot` rectangle — the big map moves and scales that rectangle (`MinimapView.SetLarge`,
  `MinimapView.cs:469-494`), and a log that jumped every time the player pressed M would be worse than one sitting a
  few pixels lower than it strictly has to.
- `DebugOverlay` is self-installing (`[RuntimeInitializeOnLoadMethod]`, `DebugOverlay.cs:37-46`), so it has no
  serialized reference to anything. It reaches the theme through a new one-line
  `MinimapView.Theme` property on the already-static `MinimapView.Local` — the minimap is exactly the thing being
  cleared, so coupling to it is honest, and no new static is introduced. With no minimap yet (the menu, before a
  player spawns) it falls back to its own consts.
- The maths is pure (`HudScreenLayout`) and edit-mode tested.

### D10. Ordering and scope

- Task 1 takes the **before** captures, at `BASE`, before touching anything; Task 7 compares against them.
- `Assets/Gameplay/Config/GameplayConfig.asset` and
  `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` must stay **unchanged** in every task.
  No RPC is added, renamed or removed.

---

## What exists (read before building; every number checked in the file)

| Thing | Where | Today |
|---|---|---|
| HUD canvas | `PlayerHud.cs:656-679` | `ScreenSpaceOverlay`, `overrideSorting`, `sortingOrder −10`, scaler from theme, **no** `GraphicRaycaster` |
| `Hud Panel` | `PlayerHud.cs:681-723` | anchor/pivot bottom-centre, `anchoredPosition (0, hudBottomOffset=28)`, `Image` at `panelColor` (0.06, 0.06, 0.08, **0.85**), `VerticalLayoutGroup` spacing 6, padding `hudPanelPadding` 16, `ContentSizeFitter` preferred both |
| Row order (top→bottom) | `PlayerHud.cs:730-802` | gold, OverPower, slots row, overheat, armor, health |
| Gold label | `PlayerHud.cs:730-735`, updated `:328-341` | `"Gold 1234  +7.7/s"`, `bodyTextSize` 24 bold, `goldTextColor` (1, 0.82, 0.2, 1), width `barWidth` 590, centred, **first row of the panel** |
| Slots row | `PlayerHud.cs:754-790` | width 4 × `slotWidth` 140 + 3 × `hudSlotSpacing` 10 = **590**, height `slotIconBoxHeight` 104 + `slotCooldownAreaHeight` 58 = **162**, `childAlignment UpperCenter` |
| Slot box | `PlayerHud.cs:1039-1194` | root `Image` = `slotReadyColor` (0,0,0,**0.6**), `raycastTarget` **left true**; icon box = top 104; name centred in the icon box |
| Key label | `PlayerHud.cs:1185-1192` | **top-LEFT**, `TopLeft` alignment, `sizeDelta (90, 32)`, at (2, −2), `bodyTextSize` 24 bold |
| Charge pips | `PlayerHud.cs:620-648`, row `:1113-1136` | **hardcoded 8×8**, spacing **2**, `const PipRowHeight = 14`, `const ReasonTextHeight = 26`, `pipAvailableColor` white, `pipSpentColor` (1,1,1,0.15), **no outline** |
| Cooldown cover | `PlayerHud.cs:1093-1108` | `Filled`/`Vertical`/`Bottom`, `cooldownCoverColor` (0,0,0,0.65), stretches the icon box |
| Ultimate meter | `PlayerHud.cs:1153-1180` | fill stretches the icon box; `READY` at `smallTextSize` 20 stretched over the icon box |
| Silenced banner | `PlayerHud.cs:964-1031` | wash at `silencedWashAlpha` 0.35; label `MidlineLeft` in a 260-wide `LayoutElement` |
| Shared text material | `PlayerHud.cs:1232-1241` | one `Material` clone, `ID_OutlineWidth` = `textOutlineWidth` **0.2**, `ID_OutlineColor` = (0,0,0,0.9). No dilate, no underlay |
| Font | `UiTheme.asset:17` | `font: {fileID: 0}` → **TMP default**, LiberationSans SDF |
| Loadout toggle button | `LoadoutScreen.Builder.cs:501-535` | own canvas `sortingOrder −10` **with** a `GraphicRaycaster`; anchor/pivot (1, 0), `sizeDelta (190, 48)`, at `(−24, 24)`, `Image` = `barTrackColor` |
| Loadout header gold | `LoadoutScreen.cs:553` | `ShopPricing.GoldLabel(gold)` → `"Gold 1234"` |
| Gold source | `GoldWallet.cs:69-75`, `:183-222` | `Balance` (int), `IncomePerSecond` (double), recomputed every owner `Update` from `GoldMath.PlayerIncomePerSecond` |
| Minimap canvas | `MinimapView.cs:329-345` | `sortingOrder −9`, **no** `GraphicRaycaster`, every `Image` `raycastTarget = false` (`:720`) |
| Minimap root | `MinimapView.cs:345-346`, `:469-494` | corner: anchor (1,1), inset `minimapCornerMargin` 24 + `minimapFrameWidth` 5, size `minimapCornerSize` 340, scale 1. Large: anchor (0.5,0.5), `y = minimapLargeBottomClearance/2`, scale `largeDiameter / 340`. **No `CanvasGroup`** |
| Minimap alphas | `UiTheme.asset` | `minimapBackgroundTint` a = 0.9, `minimapFrameColor` a = 0.9, `minimapLargeBottomClearance` **380** |
| Minimap harness API | `MinimapView.cs:72-77` | `IsBuilt`, `IsLargeOpen`, `MapRoot`, `ZoneBubbleCount`, `LinkCount`. `theme` is **private** |
| Debug overlay | `DebugOverlay.cs:104-139` | F1 toggles; hint `Rect(8, 8, 400, 20)`; box `Rect(8, 8, min(760, w−16), min(340, h/2))`; "copied" label `Rect(16, h−4, 400, 24)` |
| Test range panel | `TestRangePanel.cs:196-212` | canvas `sortingOrder 500`, panel anchor/pivot (0,1) at (16, −16), width **420**, `ContentSizeFitter` vertical |
| Base move speed | `GameplayConfig.asset:15` | `baseMoveSpeed: 5` |
| `panelColor` readers | `PlayerHud.cs:696` only | `LoadoutScreen.Builder.cs:372` merely names it in a comment |

---

## Rules for every task (each has cost hours on this project)

1. Branch `limit-testing` only; push after each task's commit. `unity command editor_status` must answer before
   editing. **If it does not answer, stop and report — do not edit blind.**
2. **Tudor uses this computer while you work.** Never rely on the real mouse, the real keyboard or window focus.
   **Never call `editor_focus`.** If a capture shows another application's window over the Game view, retake it.
3. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `unity command editor_stop`, poll
   `unity command editor_status` until `playMode: "stopped"` and `compiling: false`.
4. `unity command recompile`, then poll `unity command recompile_status` until completed with `errors: []` (the only
   compile truth). **Tests async only:** `unity command run_tests -- --mode editor --async_tests true`, then poll
   `unity command test_status`.
5. **Dirty scene → modal dialog → silent Editor hang.** Before tests, recompile, build or a scene open, run
   `unity command eval -- --code "return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;"`, READ
   the answer, and continue only if it is `False`. **Do not chain it with `&&`** (trap 8b). To discard:
   `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity", UnityEditor.SceneManagement.OpenSceneMode.Single)`.
   Never click a dialog.
6. **CLI:** form `unity command <name> -- --flag value`; files
   `unity command eval_file -- --file "<path>" --timeout 20000`; long scripts
   `unity command --timeout 240 run_script -- --file "<path>" --entry Type.Method --timeout_ms 200000`. Eval code
   writes `UnityEngine.Object`, never bare `Object`.
7. **Before ANY Play Mode measurement**, list the room's actors and abort on an unexpected player:
   ```
   unity command eval -- --code "var r = Photon.Pun.PhotonNetwork.CurrentRoom; if (r == null) return \"no room\"; var s = \"\"; foreach (var p in r.Players) s += p.Value.ActorNumber + \":\" + p.Value.NickName + (p.Value.IsLocal ? \"(me)\" : \"\") + \" \"; return r.PlayerCount + \" | \" + s;"
   ```
   Expected: `1 | <n>:EditorHost(me)`. Anything else — a second client, a leftover Player build — **stop and report**;
   another agent may be mid-task on the one Editor.
8. **SCRATCH** = `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\ff89ef31-6781-4dad-a39f-d996ebdaf3fa\scratchpad`
   (amended 2026-09-18: the session that wrote this plan, `8bc97fd1-...`, is gone).
   Scratch scripts in `SCRATCH\hud-readability\`, captures in `SCRATCH\hud-readability\captures\`. Never under
   `Assets/`.
9. **Captures:**
   - Size: **whatever the real Game view is at Task 1 Step 0 — record it, and every capture in this plan uses that
     same size.** *(Amended 2026-09-18: this plan was written when the Game view was 616×576; on the night of
     2026-09-18 it reports **1920×1080** (`UnityEditor.Handles.GetMainGameViewSize()`), and Tudor may set it himself.
     Never change the Game view's resolution setting. If it changes between the BEFORE captures and a later one,
     say so and compare proportions, not pixels.)*
   - Screen Space Overlay UI needs `--source screen` (Play Mode only); the default `--source camera` misses the whole
     HUD.
   - Always saved to an explicit path: `--save_path "Temp/hud/<name>.png"` goes under the project `Temp/`; copy the
     file to `SCRATCH\hud-readability\captures\`. If a path outside `Assets/` is refused, use `Assets/Temp/hud/`,
     then delete that folder and its `.meta`.
   - **Read every PNG yourself and describe what you actually see, honestly**, before claiming anything about it.
     Agents on this project have claimed "no overlap", "clearly distinct" and "fill works" and all three were wrong
     until someone looked (trap 5).
10. **`UiTheme.asset` is edited BY HAND** (trap 8c). *(Amended 2026-09-18: trap 8c's reason — the asset being stale
    against the class — no longer holds. At `cd7b8c4` every field in `UiTheme.cs` has its key in the asset, in
    declaration order, with no orphans. The hand-edit procedure stays anyway: it is cheap, and it keeps each diff
    to exactly the intended lines.)* Per task, in this order:
    1. Add / change / remove the C# fields in `UiTheme.cs`.
    2. `unity command recompile`, poll `recompile_status` to `errors: []`.
    3. Hand-edit `Assets/Gameplay/Config/UiTheme.asset` YAML, inserting each new key **in field-declaration order**
       (right after the key named in the task), deleting removed keys, and changing changed values.
    4. `unity command eval -- --code "UnityEditor.AssetDatabase.ImportAsset(\"Assets/Gameplay/Config/UiTheme.asset\", UnityEditor.ImportAssetOptions.ForceUpdate); return \"imported\";"`
    5. Read the values back by eval and print them.
    6. `git diff -- Assets/Gameplay/Config/UiTheme.asset` — **only the intended lines**. If Unity backfilled anything
       else, report it and do not commit the extras.
    - **Never `SetDirty` + `SaveAssets` on the theme.**
11. `AssetDatabase.SaveAssets()` flushes every dirty asset. Use `AssetDatabase.SaveAssetIfDirty(asset)`, then
    `git status`. **`Assets/Gameplay/Config/GameplayConfig.asset` and
    `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` must stay unchanged. Add no RPC,
    rename none, remove none.**
12. Never move a player with `transform.position` (physics restores it). Use `PlayerDisplacement.TeleportTo` to a
    point outside colliders, and wait at least a frame before relying on the new position.
13. Measure with game-time stamps, not CLI wall time. Anything timed runs in an in-process coroutine that writes its
    result to a file.
14. Every designer-facing value lives on `UiTheme` with a plain `[Tooltip]`, one home each. Comments explain *why*,
    for a designer reader.
15. Commit messages end with your own session's `Co-Authored-By:` line. No unmeasured number in a commit message.
    Stage **only** the task's listed files; run `git status` first. Other agents may have uncommitted edits in the
    tree — never stage those.
16. Append judgement calls to
    `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under
    `## HUD readability (2026-09-17)` as short `[C]` lines (outside the repo; do not commit it). Check the file's
    headings afterwards — agents have deleted one before.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/UI/UiTheme.cs` (modify) | + Hud Scale; + text weight/shadow; − `panelColor`; + slot border/fill/key row; + pip fields; + gold gap/size; + minimap opacity; + debug log; `ApplyHudTextStyle` | 1, 2, 3, 4, 5, 6 |
| `Assets/Gameplay/Config/UiTheme.asset` (modify, by hand) | The same, in declaration order | 1, 2, 3, 4, 5, 6 |
| `Assets/scripts/UI/PlayerHud.cs` (modify) | Scale, centring, slot border/fill, text style, pips, gold corner | 1, 2, 3, 4 |
| `Assets/scripts/UI/LoadoutScreen.Builder.cs` (modify) | `Shop Corner` wrapper + scale; shared text style | 1, 2 |
| `Assets/scripts/UI/ShopPricing.cs` (modify) | `GoldHudLabel` | 4 |
| `Assets/Tests/GoldHudLabelTests.cs` (create) | Format + InvariantCulture | 4 |
| `Assets/scripts/UI/MinimapOpacity.cs` (create) | Pure opacity/hysteresis/fade rule | 5 |
| `Assets/Tests/MinimapOpacityTests.cs` (create) | Its tests | 5 |
| `Assets/scripts/UI/MinimapView.cs` (modify) | `CanvasGroup`, speed sampling, `Theme`, harness readouts; shared text style | 2, 5, 6 |
| `Assets/scripts/UI/HudScreenLayout.cs` (create) | Pure canvas-scale + debug-log rect | 6 |
| `Assets/Tests/HudScreenLayoutTests.cs` (create) | Its tests | 6 |
| `Assets/scripts/Debug/DebugOverlay.cs` (modify) | Draw right, below the minimap | 6 |

---

# Task 1 (HUD step 1): before captures, theme fields, the 20% scale, and centring

**Files**
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/UI/PlayerHud.cs`, `Assets/scripts/UI/LoadoutScreen.Builder.cs`
- Scratch (not committed): `SCRATCH\hud-readability\HudReport.cs`

---

- [ ] **Step 0: Record `BASE` and take the BEFORE captures.**

  1. `git rev-parse HEAD` → put the hash in your report as `BASE`. Task 7 diffs assets against it.
  2. `git status` — **none of this plan's files** (the file map above) may have uncommitted changes. *(Amended
     2026-09-18: the list of another agent's files that stood here is stale — the movement work it named was
     committed long ago.)* Read `git status` fresh: anything it shows that is not yours is **never staged** (rule
     15), and say in your report what was there. `PlayerDisplacement.TeleportTo(Vector3)` is stable at the
     amendment's HEAD; still re-check it (rule 12) rather than assuming. If any of THIS plan's files is dirty, stop
     and report.
  3. Dirty-scene check (rule 5). Read the answer. Continue only on `False`.
  4. `unity command editor_play`, poll `editor_status` until playing.
  5. Join the room:
     ```
     unity command eval -- --code "Photon.Pun.PhotonNetwork.NickName = \"EditorHost\"; Photon.Pun.PhotonNetwork.JoinLobby(); return \"joining\";"
     ```
     Poll until in room with a local player:
     ```
     unity command eval -- --code "return Photon.Pun.PhotonNetwork.InRoom + \" \" + (PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber) != null);"
     ```
     → `True True`.
  6. **Run the actor check (rule 7).** Abort on anything but one local player.
  7. Take these four, each `--source screen --save_path "Temp/hud/<name>.png"`, then copy to
     `SCRATCH\hud-readability\captures\`:
     - `before-hud.png` — at rest.
     - `before-map-corner.png` — same frame is fine; take it separately so the pair is unambiguous.
     - `before-map-large.png` — after
       `unity command eval -- --code "Overpower.UI.MinimapView.Local.ToggleLarge(); return Overpower.UI.MinimapView.Local.IsLargeOpen;"` → `True`. Close it again afterwards.
     - `before-f1-hint.png` — **F1 cannot be pressed** (rule 2, no real keyboard). Take a plain screen capture and
       read the top-left corner: `DebugOverlay`'s "F1: debug log" hint draws at `Rect(8, 8, 400, 20)`
       (`DebugOverlay.cs:121`), which is exactly where the test range panel opens (`TestRangePanel.cs:210-212`).
       Say in your report exactly what you see in the top-left 200×60 pixels.
  8. **Read all four PNGs yourself** and write two honest sentences each: the panel's darkness, where the key labels
     sit, how big the pips look, where the gold row is, how opaque the map is.
  9. `unity command editor_stop`; poll until stopped. Dirty check → `False`.

- [ ] **Step 1: Add the Task 1 theme fields.** In `Assets/scripts/UI/UiTheme.cs`.

  After `matchWidthOrHeight` (line 21), in the **Canvas** header:

```csharp
        [Tooltip("How big the whole HUD is drawn, as a fraction of the sizes below: 1 is full size, 0.8 is 20% " +
                 "smaller (Tudor, 2026-09-17). Applied ONCE, as a scale on the HUD panel and on the gold/shop " +
                 "block in the bottom-right corner - every other size on this asset stays in its own units, so a " +
                 "designer tunes Bar Width or Slot Width normally and this one number makes the whole group " +
                 "bigger or smaller. It does NOT scale the minimap, the toast, the match panels, the chat or the " +
                 "F1 test range panel, which each sit on their own.")]
        [Range(0.5f, 1.5f)] public float hudScale = 0.8f;
```

  After `slotCooldownAreaHeight` (line 96), in the **HUD slots** header:

```csharp
        [Tooltip("Height of the key strip (LMB / RMB / SPACE / SHIFT) across the top of a slot, in canvas units " +
                 "(Tudor, 2026-09-17: the key used to sit in the top-left corner). The icon and the ability name " +
                 "centre themselves in whatever is left of the icon box below it, so both read as centred in the " +
                 "square at once - which one shared rect could never do.")]
        public float slotKeyRowHeight = 30f;
        [Tooltip("Height of the block-reason line (\"recharging\", \"stunned\") under a slot's charge pips, in " +
                 "canvas units. Together with the pip row it has to fit inside Slot Cooldown Area Height above.")]
        public float slotReasonTextHeight = 26f;
```

  Then the **centring-only** part of `PlayerHud` (Step 2) uses `slotKeyRowHeight`/`slotReasonTextHeight`. The
  border/fill fields arrive in Task 2 — do **not** add them here.

- [ ] **Step 2: Centre everything inside a slot.** In `Assets/scripts/UI/PlayerHud.cs`, `BuildSlot`.

  **2a.** Right after the icon box's rect is set up (`PlayerHud.cs:1059-1066`), insert `Content Box` and re-parent
  the icon and name to it:

```csharp
            // Everything the eye reads as "the ability" lives here, under the key strip: the icon, the fallback
            // name, and (for the ultimate) its READY label. Its own rect - rather than the whole icon box - is
            // what makes them centred in the SQUARE a player sees, instead of centred in a box whose top strip
            // is the key label (Tudor, 2026-09-17: "the text should be in the middle of the ability square").
            GameObject contentBox = new GameObject("Content Box", typeof(RectTransform));
            contentBox.transform.SetParent(iconBox.transform, false);
            RectTransform contentRt = contentBox.GetComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = new Vector2(0f, -theme.slotKeyRowHeight);
```

  Change `iconGo.transform.SetParent(iconBox.transform, false)` → `contentBox.transform`, and
  `AddLabel(iconBox.transform, "", theme.bodyTextSize, …)` for `ui.fallbackNameText` → `AddLabel(contentBox.transform, …)`.

  **2b.** Leave `Cooldown Cover` a child of `iconBox` and leave it stretched over the whole icon box — it is a
  cover, and a sweep that stopped under the key would look broken. Add this to its comment:

```csharp
                // Still the WHOLE icon box, not Content Box: a recharge sweep that stopped short of the key strip
                // would read as a drawing bug, not as a cooldown. It is built after Content Box (so it covers the
                // icon and name) and before the key strip below (so the key stays readable while recharging).
```

  **2c.** `Ultimate Charge Fill` stays stretched over the whole icon box (it is a meter, and a full-square fill
  reads better). `READY` gets the Content Box inset — replace its offsets (`PlayerHud.cs:1174-1177`) with:

```csharp
                    readyRt.offsetMin = Vector2.zero;
                    // The same inset Content Box uses, so READY lands in the middle of the square a player sees
                    // rather than in the middle of a box whose top strip is the key label (HUD step 1).
                    readyRt.offsetMax = new Vector2(0f, -theme.slotKeyRowHeight);
```

  **2d.** Replace the key label block (`PlayerHud.cs:1183-1192`) entirely with:

```csharp
            // Tudor, 2026-09-17: centred, not tucked in a corner. A full-width strip across the TOP of the icon
            // box, so the icon/name below it stay centred in the rest of the square (Content Box above). Built
            // last inside the icon box, so it draws over the recharge sweep and the ultimate meter and the key
            // stays readable in every state. Body Text Size, as before: the longest label (SPACE) must fit.
            TextMeshProUGUI keyText = AddLabel(iconBox.transform, keyLabel, theme.bodyTextSize, FontStyles.Bold);
            RectTransform keyRt = keyText.rectTransform;
            keyRt.anchorMin = new Vector2(0f, 1f);
            keyRt.anchorMax = new Vector2(1f, 1f);
            keyRt.pivot = new Vector2(0.5f, 1f);
            keyRt.anchoredPosition = Vector2.zero;
            keyRt.sizeDelta = new Vector2(0f, theme.slotKeyRowHeight);
            keyText.alignment = TextAlignmentOptions.Center;
            keyText.enableWordWrapping = false;
```

  Note the parent changed from `go.transform` to `iconBox.transform`, and the whole label is now built AFTER the
  `if (withCooldown)` block, exactly where it already sits.

  **2e.** Replace the two `const float` lines (`PlayerHud.cs:1113-1114`) and the two positions below them with the
  theme values:

```csharp
                // Pip row and block-reason text sit BELOW the icon box, in the Slot Cooldown Area Height band
                // reserved for them - positions derive from Slot Icon Box Height so they never drift out of sync
                // with it. Both heights moved onto UiTheme in HUD step 1/3: they were the last two sizes in this
                // file a designer could not reach.
                float pipRowY = -(theme.slotIconBoxHeight + 2f);
                float reasonY = pipRowY - theme.pipRowHeight - 2f;
```

  …but `theme.pipRowHeight` does not exist until Task 3. **In this task use a local**
  `float pipRowHeight = 14f;` with a `// HUD step 3 moves this onto UiTheme.` comment, and replace it in Task 3.
  `ReasonTextHeight` becomes `theme.slotReasonTextHeight` now (the field is added in Step 1 above).

  **2f.** Silenced banner label (`PlayerHud.cs:1023-1028`): centre it.

```csharp
            TextMeshProUGUI text = AddLabel(content.transform, "WEAPON SILENCED", theme.bodyTextSize, FontStyles.Bold);
            LayoutElement textLe = text.gameObject.AddComponent<LayoutElement>();
            textLe.preferredHeight = 30f;
            text.color = theme.overheatSilencedColor;
            // Centred, and no pinned preferred WIDTH (Tudor, 2026-09-17): a left-aligned label inside a fixed
            // 260-unit box let the layout group centre the box while the words sat against its left edge, so the
            // banner read as off-centre over the slot row. The group now sizes the label to the words themselves.
            text.alignment = TextAlignmentOptions.Center;
```

- [ ] **Step 3: Apply the one 20% scale.**

  **3a.** In `PlayerHud.BuildUi`, right after `panelRt.anchoredPosition = …` (`PlayerHud.cs:690`):

```csharp
            // Tudor, 2026-09-17: one 20% reduction of the whole HUD, applied ONCE here as a scale rather than by
            // re-typing every size on UiTheme at 80% - a designer still tunes Bar Width, Slot Width and the text
            // sizes in their own units, and Hud Scale is the single number that makes the group smaller or
            // bigger. The pivot above is bottom-centre, so shrinking keeps the HUD's bottom edge exactly Hud
            // Bottom Offset above the screen edge instead of floating up off it.
            panel.transform.localScale = Vector3.one * theme.hudScale;
```

  **3b.** In `LoadoutScreen.Builder.BuildToggleButtonCanvas` (`LoadoutScreen.Builder.cs:515-521`), wrap the button
  in a scaled corner root so it shrinks with the HUD and stays aligned with the gold block Task 4 puts above it:

```csharp
            // The bottom-right corner group, scaled by the same Hud Scale as the HUD panel (Tudor, 2026-09-17).
            // A wrapper rather than a scale on the button itself, because HUD step 4 hangs the gold readout above
            // this button from PlayerHud's own canvas with an identical wrapper: two roots with the same anchor,
            // the same pivot and the same scale stay aligned at any screen size, where two independently scaled
            // children would drift apart the moment either size changed.
            GameObject corner = new GameObject("Shop Corner", typeof(RectTransform));
            corner.transform.SetParent(canvasGo.transform, false);
            RectTransform cornerRt = corner.GetComponent<RectTransform>();
            cornerRt.anchorMin = cornerRt.anchorMax = cornerRt.pivot = new Vector2(1f, 0f);
            cornerRt.anchoredPosition = Vector2.zero;
            cornerRt.sizeDelta = Vector2.zero;
            corner.transform.localScale = Vector3.one * theme.hudScale;

            GameObject buttonGo = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            buttonGo.name = "Loadout Toggle Button";
            buttonGo.transform.SetParent(corner.transform, false);
```

  (the rest of the method is unchanged — the button keeps anchor/pivot (1, 0) and
  `anchoredPosition (−loadoutToggleButtonMargin, loadoutToggleButtonMargin)`, now measured inside the scaled
  wrapper.)

- [ ] **Step 4: Hand-edit `UiTheme.asset`** per rule 10.

  - `hudScale: 0.8` immediately after `matchWidthOrHeight: 0.5`.
  - `slotKeyRowHeight: 30` and `slotReasonTextHeight: 26` immediately after `slotCooldownAreaHeight: 58`.
  - Nothing else changes in this task.

  Read back:
  ```
  unity command eval -- --code "var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Overpower.UI.UiTheme>(\"Assets/Gameplay/Config/UiTheme.asset\"); return t.hudScale + \" \" + t.slotKeyRowHeight + \" \" + t.slotReasonTextHeight;"
  ```
  Expected `0.8 30 26`. Then `git diff -- Assets/Gameplay/Config/UiTheme.asset` → exactly three added lines.

- [ ] **Step 5: Recompile and run the tests.** Rules 4 and 5. Expected: `errors: []`, and the same pass total as
  `BASE` (no test touches these files yet). **Report the total.**

- [ ] **Step 6: Measure and capture.**

  1. Dirty check → `False`. `unity command editor_play`, poll, join, **actor check (rule 7)**.
  2. Write `SCRATCH\hud-readability\HudReport.cs`:

```csharp
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// Read-only HUD geometry report (HUD steps 1-7). Not committed.
public static class HudReport
{
    public static string Run()
    {
        var hud = UnityEngine.Object.FindObjectsByType<Overpower.UI.PlayerHud>(FindObjectsSortMode.None)
                                    .FirstOrDefault(h => h.enabled);
        if (hud == null) return "ABORT: no enabled PlayerHud (is a local player spawned?)";

        var panel = hud.transform.Find("Player Hud Canvas/Hud Panel") as RectTransform;
        if (panel == null) return "ABORT: Hud Panel not found";
        Canvas.ForceUpdateCanvases();

        string s = $"panel rect={panel.rect.width:0.#}x{panel.rect.height:0.#} scale={panel.localScale.x:0.###} " +
                   $"bg={(panel.GetComponent<Image>() == null ? "none" : panel.GetComponent<Image>().color.ToString())}\n";

        foreach (Transform slot in panel.Find("Slots Row"))
        {
            if (!slot.name.StartsWith("Slot ")) continue;
            var rt = (RectTransform)slot;
            var border = slot.GetComponent<Image>();
            var fill = slot.Find("Slot Fill");
            var key = slot.Find("Icon Box")?.GetComponentsInChildren<TextMeshProUGUI>(true)
                          .FirstOrDefault(t => t.text.Length > 0 && t.text == slot.name.Substring(5));
            var pips = slot.Find("Pips");
            s += $"{slot.name}: rect={rt.rect.width:0.#}x{rt.rect.height:0.#} border={(border == null ? "none" : border.color.ToString())} " +
                 $"fill={(fill == null ? "none" : fill.GetComponent<Image>().color.ToString())} " +
                 $"key={(key == null ? "?" : key.alignment + " rect=" + key.rectTransform.rect.width.ToString("0.#") + "x" + key.rectTransform.rect.height.ToString("0.#"))} " +
                 $"pips={(pips == null ? 0 : pips.childCount)}";
            if (pips != null && pips.childCount > 0)
            {
                var le = pips.GetChild(0).GetComponent<LayoutElement>();
                s += $" pipSize={le.preferredWidth:0.#}";
            }
            s += "\n";
        }

        int raycastTargets = hud.GetComponentsInChildren<Graphic>(true).Count(g => g.raycastTarget);
        s += $"hud raycast targets = {raycastTargets}\n";

        var gold = hud.transform.Find("Player Hud Canvas/Gold Corner");
        string goldText = "none";
        if (gold != null)
        {
            var label = gold.GetComponentInChildren<TextMeshProUGUI>(true);
            goldText = label == null ? "no label" : label.text.Replace("\n", " | ");
        }
        s += "gold corner = " + goldText + "\n";
        return s;
    }
}
```

  Run `unity command eval_file -- --file "SCRATCH\hud-readability\HudReport.cs" --timeout 20000` — if `eval_file`
  will not take a static class, use
  `unity command --timeout 240 run_script -- --file "SCRATCH\hud-readability\HudReport.cs" --entry HudReport.Run --timeout_ms 200000`.

  **Report every line of the output.** Expected after this task: `scale=0.8`, four slots at `140x162` /
  `140x104`, `key=Center rect=140x30`, `gold corner = none` (it moves in Task 4), `hud raycast targets = 4`
  (the four slot backgrounds; Task 2 takes them to 0).

  3. Capture `--source screen --save_path "Temp/hud/s1-hud.png"`, copy to SCRATCH, **read it**, and say honestly:
     is the HUD visibly smaller, is every key label centred at the top of its square, is the ability name centred
     in the rest of the square, and does anything overlap?
  4. `unity command editor_stop`; poll until stopped. Dirty check → `False`.

- [ ] **Step 7: Commit + push** (only these files):

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/PlayerHud.cs Assets/scripts/UI/LoadoutScreen.Builder.cs
git commit -m "feat(ui): the HUD is 20% smaller and every label inside an ability square is centred (HUD step 1)" -m "Co-Authored-By: <your session's line>"
git push
```

Add to the assumptions file: `[C] one Hud Scale of 0.8 instead of retyping every size at 80%`.

---

# Task 2 (HUD step 2): the dark panels go, and the HUD holds its own without them

**Files**
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/UI/PlayerHud.cs`, `Assets/scripts/UI/LoadoutScreen.Builder.cs`,
  `Assets/scripts/UI/MinimapView.cs`

---

- [ ] **Step 1: Theme changes.** In `Assets/scripts/UI/UiTheme.cs`.

  **1a. Delete `panelColor`** (lines 34-37, the field and its `[Tooltip]`; keep the `[Header("Panels")]`, which
  `highlightColor` below still needs). Nothing else reads it — confirm with
  `grep -rn "panelColor" Assets/scripts/` before deleting, and expect only `PlayerHud.cs:696` (removed in Step 2)
  and the comment at `LoadoutScreen.Builder.cs:372`. Reword that comment:

```csharp
            // Its OWN colour - see Loadout Panel Colour's tooltip. The HUD has no panel behind it at all any more
            // (HUD step 2); this modal still does, because it deliberately hides the world behind it.
```

  **1b. Add the text-weight and shadow fields**, after `textOutlineWidth` (line 32), in the **Text** header:

```csharp
        [Tooltip("How much thicker every glyph is drawn, 0 to 1 (TextMeshPro's Face Dilate). This is the weight " +
                 "control for the font the project already uses - there is no second, bolder font asset - and it " +
                 "was raised in HUD step 2 because the dark panel that used to sit behind the HUD is gone. Around " +
                 "0.08 reads as a firmer version of the same letters; past ~0.2 they start to close up.")]
        [Range(0f, 0.5f)] public float hudTextFaceDilate = 0.08f;
        [Tooltip("Colour of the soft shadow dropped under every UI text, so a word keeps its shape over the " +
                 "arena's bright sand without a panel behind it. Alpha 0 turns the shadow off.")]
        public Color hudTextShadowColor = new Color(0f, 0f, 0f, 0.75f);
        [Tooltip("How far the shadow is offset from the text, in font units (x right, y up - so a negative y " +
                 "drops it below the letters, which is what reads as a shadow rather than a halo).")]
        public Vector2 hudTextShadowOffset = new Vector2(0.5f, -0.5f);
        [Tooltip("How blurred the shadow's edge is, 0 to 1. Soft enough not to read as a second, offset copy of " +
                 "the text; hard enough to still darken the ground under it.")]
        [Range(0f, 1f)] public float hudTextShadowSoftness = 0.25f;
        [Tooltip("How far the shadow spreads outward from the glyph before it fades, 0 to 1. Together with " +
                 "Softness this is what makes the shadow a pool under the word rather than an outline of it.")]
        [Range(0f, 1f)] public float hudTextShadowDilate = 0.1f;
```

  **1c. Add the slot border/fill fields**, after `slotReasonTextHeight` (added in Task 1), in **HUD slots**:

```csharp
        [Tooltip("Thickness of the border drawn around a weapon/ability slot, in canvas units (HUD step 2). The " +
                 "border is what carries the ready / blocked / active colour now: Tudor asked for the dark box " +
                 "behind the abilities to go, so the slot is a thin frame over a faint wash instead of a filled " +
                 "square. Raise it if the state colour is hard to see at a glance.")]
        public float slotBorderWidth = 3f;
        [Tooltip("The faint wash inside a slot's border (HUD step 2) - just enough to keep an icon or an ability " +
                 "name readable over the arena's bright sand, low enough to see the ground through. Raise the " +
                 "alpha if names are hard to read; drop it to 0 for a frame with nothing inside it at all.")]
        public Color slotFillColor = new Color(0f, 0f, 0f, 0.22f);
```

  **1d. Retune three existing colours' tooltips and defaults** (the values change in the asset in Step 5; change
  the C# defaults too so a freshly created theme matches):

```csharp
        [Tooltip("Slot BORDER colour when the slot can be used right now (HUD step 2 - it used to be the whole " +
                 "square's fill). Dark and near-opaque: a thin dark line is the most legible frame on the arena's " +
                 "bright sand.")]
        public Color slotReadyColor = new Color(0.05f, 0.05f, 0.07f, 0.9f);
        [Tooltip("Slot BORDER colour when the slot is blocked - dead, stunned, silenced, recharging, or an " +
                 "empty/not-ready slot. Brighter than it was as a full-square fill (HUD step 2): three canvas " +
                 "units of mid-grey has to work harder than a hundred and forty did.")]
        public Color slotBlockedColor = new Color(0.45f, 0.45f, 0.45f, 0.95f);
        [Tooltip("Slot BORDER colour while the ability is active - a channel, a dash mid-flight, sprint held. " +
                 "Unchanged by HUD step 2: at full alpha it already reads as a lit frame.")]
        public Color slotActiveGlowColor = new Color(1f, 0.85f, 0.25f, 1f);
```

  **1e. Add the one home for the text style**, at the end of the class (after `coneLineMaterial`):

```csharp
        /// <summary>Writes this theme's outline, weight and drop-shadow onto one shared TextMeshPro material -
        /// the one home for those seven numbers, called by PlayerHud, the loadout screen and the minimap, which
        /// each build exactly one material for every label they own (see PlayerHud.ApplyOutline's comment for why
        /// one shared material beats letting TMP clone one per label).
        ///
        /// The keyword is the part that is easy to get wrong: setting _UnderlayColor and friends does nothing at
        /// all until UNDERLAY_ON is enabled on the material, so the shadow silently never appears.</summary>
        public void ApplyHudTextStyle(Material material)
        {
            if (material == null)
                return;

            material.SetFloat(ShaderUtilities.ID_OutlineWidth, textOutlineWidth);
            material.SetColor(ShaderUtilities.ID_OutlineColor, textOutlineColor);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, hudTextFaceDilate);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, hudTextShadowColor);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, hudTextShadowOffset.x);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, hudTextShadowOffset.y);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, hudTextShadowSoftness);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, hudTextShadowDilate);
            if (hudTextShadowColor.a > 0f)
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            else
                material.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        }
```

  `UiTheme.cs` already has `using TMPro;` (line 2) and `using UnityEngine;` (line 3), which is all
  `ShaderUtilities` and `Material` need.

- [ ] **Step 2: Remove the HUD's panel background.** In `PlayerHud.BuildUi`, delete lines 693-697 (the comment and
  the three `panelBackground` lines) and put this in their place:

```csharp
            // NO background image (Tudor, 2026-09-17): the near-opaque slab that used to sit behind the bars and
            // slots was the single biggest thing between a player and the arena. What replaces it is the text
            // treatment itself - a heavier face, a dark outline and a soft drop shadow, all from UiTheme's Text
            // section - plus each bar's own track and each slot's own border. One fewer Graphic here also means
            // one fewer thing in the HUD's draw batch.
```

- [ ] **Step 3: Slot border + fill.** In `PlayerHud.BuildSlot`, replace lines 1054-1055 with:

```csharp
            // The slot's own Image is now the BORDER (HUD step 2): it still carries the ready / blocked / active
            // tint that UpdateAbilitySlots writes, but it is only visible as a Slot Border Width frame, because
            // the Slot Fill child below covers everything inside it. raycastTarget off like every other HUD
            // Graphic - this canvas has no GraphicRaycaster, but a stray raycast target here is exactly the kind
            // of thing that later blocks a shot when someone adds one.
            ui.background = go.AddComponent<Image>();
            ui.background.color = theme.slotReadyColor;
            ui.background.raycastTarget = false;

            // The only fill a slot has left: a faint wash inset by the border width, so an icon or an ability
            // name still has something to sit on without hiding the arena behind it.
            GameObject slotFillGo = new GameObject("Slot Fill", typeof(RectTransform));
            slotFillGo.transform.SetParent(go.transform, false);
            RectTransform slotFillRt = slotFillGo.GetComponent<RectTransform>();
            slotFillRt.anchorMin = Vector2.zero;
            slotFillRt.anchorMax = Vector2.one;
            slotFillRt.offsetMin = new Vector2(theme.slotBorderWidth, theme.slotBorderWidth);
            slotFillRt.offsetMax = new Vector2(-theme.slotBorderWidth, -theme.slotBorderWidth);
            Image slotFill = slotFillGo.AddComponent<Image>();
            slotFill.color = theme.slotFillColor;
            slotFill.raycastTarget = false;
```

  `Slot Fill` is the slot's FIRST child, so everything built after it (icon box, pips, reason) draws on top.
  Nothing in `UpdateWeaponSlot`/`UpdateAbilitySlots` changes: they still write `ui.background.color`, which is now
  the border.

- [ ] **Step 4: Route all three text materials through the theme.**

  - `PlayerHud.ApplyOutline` (`PlayerHud.cs:1232-1241`): replace the two `Set*` lines inside the `if` with
    `theme.ApplyHudTextStyle(hudTextMaterial);` and extend the doc comment with one sentence saying the seven
    numbers now live on `UiTheme.ApplyHudTextStyle`.
  - `LoadoutScreen.Builder.ApplyOutline` (`LoadoutScreen.Builder.cs:612-621`): same, on `loadoutTextMaterial`.
  - `MinimapView.AddLabel` (`MinimapView.cs:740-745`): same, on `textMaterial`.

- [ ] **Step 5: Hand-edit `UiTheme.asset`** per rule 10.

  - **Delete** the line `panelColor: {r: 0.06, g: 0.06, b: 0.08, a: 0.85}`.
  - **Change** `textOutlineWidth: 0.2` → `textOutlineWidth: 0.26`.
  - **Insert after** `textOutlineWidth: 0.26`, in this order:
    ```yaml
      hudTextFaceDilate: 0.08
      hudTextShadowColor: {r: 0, g: 0, b: 0, a: 0.75}
      hudTextShadowOffset: {x: 0.5, y: -0.5}
      hudTextShadowSoftness: 0.25
      hudTextShadowDilate: 0.1
    ```
  - **Insert after** `slotReasonTextHeight: 26`:
    ```yaml
      slotBorderWidth: 3
      slotFillColor: {r: 0, g: 0, b: 0, a: 0.22}
    ```
  - **Change** `slotReadyColor: {r: 0, g: 0, b: 0, a: 0.6}` → `{r: 0.05, g: 0.05, b: 0.07, a: 0.9}`.
  - **Change** `slotBlockedColor: {r: 0.3, g: 0.3, b: 0.3, a: 0.85}` → `{r: 0.45, g: 0.45, b: 0.45, a: 0.95}`.

  Read back and print `textOutlineWidth`, `hudTextFaceDilate`, `hudTextShadowColor`, `slotBorderWidth`,
  `slotFillColor`, `slotReadyColor`, `slotBlockedColor`. `git diff` the asset: 1 deletion, 7 insertions,
  3 changes, nothing else.

- [ ] **Step 6: Recompile and run the tests.** Expected: `errors: []`, same total as Task 1.

- [ ] **Step 7: Measure and capture.** Play, join, **actor check (rule 7)**, then:

  1. `HudReport.Run` — expected `bg=none`, `border=RGBA(0.050, 0.050, 0.070, 0.900)`,
     `fill=RGBA(0.000, 0.000, 0.000, 0.220)`, and **`hud raycast targets = 0`**.
  2. Confirm the shadow is actually on:
     ```
     unity command eval -- --code "var t = UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(UnityEngine.FindObjectsSortMode.None); foreach (var x in t) { if (x.GetComponentInParent<Overpower.UI.PlayerHud>() != null) return x.fontSharedMaterial.IsKeywordEnabled(\"UNDERLAY_ON\") + \" dilate=\" + x.fontSharedMaterial.GetFloat(\"_FaceDilate\") + \" outline=\" + x.fontSharedMaterial.GetFloat(\"_OutlineWidth\"); } return \"no hud text found\";"
     ```
     Expected `True dilate=0.08 outline=0.26`. **If it reads `False`, the keyword line is the bug** — fix it before
     capturing.
  3. Capture `s2-hud.png` (`--source screen`). **Read it.** Say honestly: is the dark slab gone, can you read the
     health/shield/overcharge numbers and the ability names over the sand, and does each slot read as a frame with
     a clear ready/blocked state?
  4. Capture `s2-hud-blocked.png` with a slot blocked, so the blocked border colour is on screen:
     ```
     unity command eval -- --code "var v = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber); var s = v.GetComponent<PlayerStatusEffects>(); s.ApplyStun(3f); return \"stunned\";"
     ```
     (if `ApplyStun`'s real name differs, find it with `grep -n "public void.*Stun" Assets/scripts/Player/PlayerStatusEffects.cs` and use that; if there is no such entry point, block the slots by firing until silenced instead, and say which route you used). **Read the PNG** and say whether blocked reads clearly different from ready.
  5. Stop; dirty check → `False`.

- [ ] **Step 8: Commit + push:**

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/PlayerHud.cs Assets/scripts/UI/LoadoutScreen.Builder.cs Assets/scripts/UI/MinimapView.cs
git commit -m "feat(ui): the HUD's dark panel is gone; text carries its own weight, outline and shadow (HUD step 2)" -m "Co-Authored-By: <your session's line>"
git push
```

Assumptions file: `[C] no panel; slot = thin border + 0.22 wash; text keeps the existing font, with Face Dilate
0.08 and a TMP underlay shadow instead of a new font asset`.

---

# Task 3 (HUD step 3): bigger, readable charge pips

**Files**
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/UI/PlayerHud.cs`

---

- [ ] **Step 1: Theme fields.** After `pipSpentColor` (`UiTheme.cs:106-107`), in **HUD slots**:

```csharp
        [Tooltip("Width and height of one charge pip, in canvas units (HUD step 3 - this used to be a number " +
                 "typed into PlayerHud where no designer could reach it). Tudor asked for bigger indicators: at " +
                 "14 a pip is a tenth of a 140-unit slot, where the old 8 was a seventeenth. The pips sit in a " +
                 "row Pip Row Height tall, which has to be at least this plus twice Pip Outline Width.")]
        public float pipSize = 14f;
        [Tooltip("Gap between two charge pips, in canvas units. Wide enough to count them at a glance without " +
                 "the row running past the slot's edge.")]
        public float pipSpacing = 4f;
        [Tooltip("Height of the charge pip row under a slot's icon, in canvas units. Must be at least Pip Size " +
                 "plus twice Pip Outline Width, or the pips are squashed. Together with Slot Reason Text Height " +
                 "it has to fit inside Slot Cooldown Area Height.")]
        public float pipRowHeight = 20f;
        [Tooltip("Thickness of the dark rim around each charge pip, in canvas units - the same trick the " +
                 "minimap's markers use. Without the HUD's old dark panel behind it, a plain white pip on the " +
                 "arena's bright sand has nothing to read against.")]
        public float pipOutlineWidth = 2f;
        [Tooltip("Colour of that rim. Dark and near-opaque, so it works under both the available and the spent " +
                 "pip colour above.")]
        public Color pipOutlineColor = new Color(0f, 0f, 0f, 0.85f);
```

- [ ] **Step 2: Track the pip roots.** In `PlayerHud`'s `SlotUi` class (`PlayerHud.cs:108`), replace the single
  `pips` line with:

```csharp
            // The coloured FACE of each pip - what SetPips tints. Each face is a child of its own pip root
            // below, because a pip is two Images now (a dark rim and a face on top of it, HUD step 3).
            public readonly List<Image> pips = new List<Image>();
            // The pip roots, in the same order - what SetPips destroys when the charge count changes. Kept
            // separately rather than walking up from a face's parent: one list that owns the lifetime is
            // harder to get wrong than a transform.parent hop that silently orphans the rim.
            public readonly List<GameObject> pipRoots = new List<GameObject>();
```

- [ ] **Step 3: Rebuild `SetPips`** (`PlayerHud.cs:620-648`). Replace the body's rebuild block with:

```csharp
            if (ui.pips.Count != maxCharges)
            {
                foreach (GameObject old in ui.pipRoots)
                    Destroy(old);
                ui.pipRoots.Clear();
                ui.pips.Clear();

                for (int i = 0; i < maxCharges; i++)
                {
                    // The pip root IS the dark rim: one Image sized Pip Size + twice the rim, with the coloured
                    // face inset inside it. Two Images per pip instead of one, and no extra layout columns - the
                    // rim is the thing the layout group measures, and the face is its child.
                    GameObject pip = new GameObject("Pip", typeof(RectTransform));
                    pip.transform.SetParent(ui.pipRow, false);
                    LayoutElement le = pip.AddComponent<LayoutElement>();
                    float outer = theme.pipSize + 2f * theme.pipOutlineWidth;
                    le.preferredWidth = outer;
                    le.preferredHeight = outer;
                    // See panelLayout's comment in BuildUi: pipRow's child control is ON, so this
                    // LayoutElement alone becomes the pip's actual rendered size.
                    Image rim = pip.AddComponent<Image>();
                    rim.color = theme.pipOutlineColor;
                    rim.raycastTarget = false;

                    GameObject faceGo = new GameObject("Face", typeof(RectTransform));
                    faceGo.transform.SetParent(pip.transform, false);
                    RectTransform faceRt = faceGo.GetComponent<RectTransform>();
                    faceRt.anchorMin = Vector2.zero;
                    faceRt.anchorMax = Vector2.one;
                    faceRt.offsetMin = new Vector2(theme.pipOutlineWidth, theme.pipOutlineWidth);
                    faceRt.offsetMax = new Vector2(-theme.pipOutlineWidth, -theme.pipOutlineWidth);
                    Image face = faceGo.AddComponent<Image>();
                    face.raycastTarget = false;

                    ui.pipRoots.Add(pip);
                    ui.pips.Add(face);
                }
            }
```

  The tint loop below it (`for (int i = 0; i < ui.pips.Count; i++) ui.pips[i].color = …`) is unchanged.

- [ ] **Step 4: Use the theme's row numbers.** In `BuildSlot`:
  - delete the Task 1 local `float pipRowHeight = 14f;` and its `// HUD step 3 moves this onto UiTheme.` note;
  - `float reasonY = pipRowY - theme.pipRowHeight - 2f;`
  - `pipRt.sizeDelta = new Vector2(0f, theme.pipRowHeight);`
  - `pipLayout.spacing = theme.pipSpacing;` (replacing the literal `2f` at `PlayerHud.cs:1127`).

  Check the arithmetic in a comment right above the pip row:

```csharp
                // 2 + Pip Row Height + 2 + Slot Reason Text Height has to stay inside Slot Cooldown Area Height
                // (58 today: 2 + 20 + 2 + 26 = 50). If a designer raises the pips past that, the reason line
                // starts overhanging the bottom of the slot - which is why all four numbers are on UiTheme, and
                // why each of their tooltips names this sum.
```

- [ ] **Step 5: Hand-edit `UiTheme.asset`** per rule 10. Insert after `pipSpentColor: {r: 1, g: 1, b: 1, a: 0.15}`:

```yaml
  pipSize: 14
  pipSpacing: 4
  pipRowHeight: 20
  pipOutlineWidth: 2
  pipOutlineColor: {r: 0, g: 0, b: 0, a: 0.85}
```

  Read back, `git diff` → five added lines only.

- [ ] **Step 6: Recompile and run the tests.** Expected `errors: []`, same total.

- [ ] **Step 7: Measure and capture.** Play, join, **actor check**, then:

  1. Give yourself an ability with more than one charge and spend one, so both pip colours are on screen:
     ```
     unity command eval -- --code "var v = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber); var r = v.GetComponent<AbilityRunner>(); var s = r.StatusFor(Overpower.Abilities.AbilitySlot.Mobility); return s == null ? \"no mobility ability\" : s.ChargesAvailable + \"/\" + s.MaxCharges;"
     ```
     If `MaxCharges` is 1, pick whichever of the three slots reports the most charges and say which. Cast it once
     through `AbilityRunner`'s own cast entry point (not by pressing a key — rule 2), and re-read the counts.
  2. `HudReport.Run` — expected `pips=<n> pipSize=18` on that slot (14 + 2 × 2).
  3. Capture `s3-pips.png` (`--source screen`). **Read it.** Say honestly: can you count the pips at a glance, is
     a spent pip clearly different from an available one, and does the dark rim separate them from the sand?
  4. Stop; dirty check → `False`.

- [ ] **Step 8: Commit + push:**

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/PlayerHud.cs
git commit -m "feat(ui): charge pips are bigger, rimmed and tunable on UiTheme (HUD step 3)" -m "Co-Authored-By: <your session's line>"
git push
```

Assumptions file: `[C] pips stay square with a dark rim (no disc sprite); 8 -> 14 units, 5.7% -> 10% of a slot`.

---

# Task 4 (HUD step 4): gold and its income move next to the shop button

**Files**
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/UI/PlayerHud.cs`, `Assets/scripts/UI/ShopPricing.cs`
- Create: `Assets/Tests/GoldHudLabelTests.cs`

---

- [ ] **Step 1: Theme fields.** After `goldTextColor` (`UiTheme.cs:321`), in **Gold**:

```csharp
        [Tooltip("Gap between the top of the 'Loadout (P)' button and the gold readout sitting above it, in " +
                 "canvas units. Tudor, 2026-09-17: gold and the shop are the same system, so the readout moved " +
                 "out of the ability bar and up against the button that spends it.")]
        public float goldShopGap = 8f;
        [Tooltip("Font size of the income line (\"+7.7/s\") as a percentage of the balance line above it. The " +
                 "balance is the number you act on; the income is context, so it is deliberately smaller - the " +
                 "same relationship Loadout Price Line Size Percent gives a shop item's price.")]
        [Range(30f, 100f)] public float goldIncomeSizePercent = 75f;
```

- [ ] **Step 2: The pure formatter.** In `Assets/scripts/UI/ShopPricing.cs`, next to `GoldLabel` (line 121):

```csharp
        /// <summary>The HUD's own two-line gold readout, next to the shop button (HUD step 4): the balance on
        /// top, this second's income under it at sizePercent of the balance's size. InvariantCulture on BOTH
        /// numbers, and pinned by a test rather than by a comment: on a machine whose culture uses a comma as the
        /// decimal separator, "+7,7/s" reads as a thousands separator, i.e. as an income seventy times too big.
        ///
        /// The shop screen's own header keeps GoldLabel above - inside the shop you are reading a balance you are
        /// about to spend, not watching it tick up, and the two are never on screen at the same time (the shop's
        /// dim covers the HUD's canvas).</summary>
        public static string GoldHudLabel(int balance, double incomePerSecond, float sizePercent)
        {
            int percent = Mathf.Clamp(Mathf.RoundToInt(sizePercent), 1, 100);
            string income = incomePerSecond.ToString("0.0", CultureInfo.InvariantCulture);
            return $"{GoldLabel(balance)}\n<size={percent.ToString(CultureInfo.InvariantCulture)}%>+{income}/s</size>";
        }
```

- [ ] **Step 3: The test.** Create `Assets/Tests/GoldHudLabelTests.cs`:

```csharp
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The HUD's gold readout is the one string a player reads every few seconds, and the one place a
    /// machine's own culture could quietly turn "+7.7/s" into "+7,7/s" - which reads as 77, not 7.7. These run the
    /// formatter under a comma-decimal culture on purpose.</summary>
    public class GoldHudLabelTests
    {
        [Test]
        public void TheBalanceIsOnTheFirstLineAndTheIncomeOnTheSecond()
        {
            string s = ShopPricing.GoldHudLabel(1234, 7.7, 75f);
            string[] lines = s.Split('\n');
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual("Gold 1234", lines[0]);
            Assert.AreEqual("<size=75%>+7.7/s</size>", lines[1]);
        }

        [Test]
        public void TheIncomeKeepsADecimalPointUnderACommaDecimalCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                StringAssert.Contains("+7.7/s", ShopPricing.GoldHudLabel(1234, 7.7, 75f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ZeroIncomeStillShowsOneDecimal()
        {
            StringAssert.Contains("+0.0/s", ShopPricing.GoldHudLabel(0, 0.0, 75f));
        }

        [Test]
        public void TheSizePercentIsClampedToSomethingTmpWillAccept()
        {
            StringAssert.Contains("<size=1%>", ShopPricing.GoldHudLabel(0, 0.0, 0f));
            StringAssert.Contains("<size=100%>", ShopPricing.GoldHudLabel(0, 0.0, 400f));
        }
    }
}
```

- [ ] **Step 4: Move the readout in `PlayerHud`.**

  **4a.** Delete the gold row from `BuildUi` (`PlayerHud.cs:725-735`: the whole comment block, the `goldText`
  `AddLabel`, `goldLe` and the two lines after it). Leave the OverPower label where it is, and reword its own
  comment's "directly under the gold row" to "at the top of the HUD panel".

  **4b.** Add a `BuildGoldCorner(canvasGo.transform)` call in `BuildUi`, right before `BuildToast(...)`, and the
  method itself next to `BuildToast`:

```csharp
        /// <summary>The gold readout, bottom-right, directly above the "Loadout (P)" button (Tudor, 2026-09-17:
        /// "display the gold generation next to the shop since these systems are tied together"). It used to be
        /// the first row of Hud Panel, where it pushed the bars and slots down and had nothing to do with either.
        ///
        /// It lives on THIS canvas, not on the loadout screen's own toggle canvas, because PlayerHud is what
        /// already holds the GoldWallet and the change caches that keep an unmoving number from re-allocating a
        /// string sixty times a second - see UpdateGold. The two canvases line up because both put their content
        /// inside an identical (1, 0)-anchored, (1, 0)-pivoted root scaled by Hud Scale, so the gap between the
        /// readout and the button is Gold Shop Gap at every screen size and every scale (see
        /// LoadoutScreen.BuildToggleButtonCanvas's Shop Corner).</summary>
        private void BuildGoldCorner(Transform canvasParent)
        {
            GameObject corner = new GameObject("Gold Corner", typeof(RectTransform));
            corner.transform.SetParent(canvasParent, false);
            RectTransform cornerRt = corner.GetComponent<RectTransform>();
            cornerRt.anchorMin = cornerRt.anchorMax = cornerRt.pivot = new Vector2(1f, 0f);
            cornerRt.anchoredPosition = Vector2.zero;
            cornerRt.sizeDelta = Vector2.zero;
            corner.transform.localScale = Vector3.one * theme.hudScale;

            goldText = AddLabel(corner.transform, "", theme.bodyTextSize, FontStyles.Bold);
            RectTransform goldRt = goldText.rectTransform;
            goldRt.anchorMin = goldRt.anchorMax = goldRt.pivot = new Vector2(1f, 0f);
            // Sits on top of the button: the button's own margin, plus the button, plus the gap.
            goldRt.anchoredPosition = new Vector2(
                -theme.loadoutToggleButtonMargin,
                theme.loadoutToggleButtonMargin + theme.loadoutToggleButtonHeight + theme.goldShopGap);
            // Two lines of Body Text Size, the second one smaller - the label writes its own <size> tag, so one
            // TextMeshProUGUI serves both instead of a second one to keep in step.
            goldRt.sizeDelta = new Vector2(theme.loadoutToggleButtonWidth, 2f * theme.bodyTextSize + 10f);
            goldText.color = theme.goldTextColor;
            goldText.alignment = TextAlignmentOptions.Right;
            goldText.enableWordWrapping = false;
        }
```

  **4c.** `UpdateGold` (`PlayerHud.cs:328-341`): replace the interpolated string and its doc comment:

```csharp
        /// <summary>The bottom-right readout next to the shop button - "Gold 1234" over "+7.7/s". The formatting
        /// (and the InvariantCulture rule behind it) lives in ShopPricing.GoldHudLabel, where a test pins it.
        /// Still gated on the balance AND the income both being unchanged, so an idle wallet never re-allocates
        /// a string or re-lays-out a text sixty times a second.</summary>
        private void UpdateGold()
        {
            if (goldWallet == null)
                return;

            int balance = goldWallet.Balance;
            double income = goldWallet.IncomePerSecond;
            if (balance == lastGoldBalance && income == lastGoldIncome)
                return;

            goldText.text = ShopPricing.GoldHudLabel(balance, income, theme.goldIncomeSizePercent);
            lastGoldBalance = balance;
            lastGoldIncome = income;
        }
```

  **4d.** `goldText`'s field comment (`PlayerHud.cs:75`) becomes:
  `private TextMeshProUGUI goldText; // "Gold 1234" over "+7.7/s", bottom-right - see BuildGoldCorner.`

- [ ] **Step 5: Hand-edit `UiTheme.asset`.** Insert after `goldTextColor: {r: 1, g: 0.82, b: 0.2, a: 1}`:

```yaml
  goldShopGap: 8
  goldIncomeSizePercent: 75
```

  Read back; `git diff` → two added lines only.

- [ ] **Step 6: Recompile and run the tests.** Expected `errors: []`, and the total is **Task 3's total + 4**
  (the four `GoldHudLabelTests`). Report both totals.

- [ ] **Step 7: Measure and capture.** Play, join, **actor check**, then:

  1. `HudReport.Run` — expected `gold corner = Gold <n> | <size=75%>+<x>/s</size>` and NO gold row inside the panel
     (the panel's height should have dropped by roughly `bodyTextSize + 8 + 6` = 38 canvas units versus Task 3's
     report; state both measured heights).
  2. Confirm there is exactly one HUD gold label:
     ```
     unity command eval -- --code "var v = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber); int n = 0; foreach (var t in v.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.text.StartsWith(\"Gold \") && t.GetComponentInParent<Overpower.UI.PlayerHud>() != null) n++; return n;"
     ```
     Expected `1`.
  3. Capture `s4-gold.png` (`--source screen`). **Read it.** Say honestly: are the balance and the income both
     legible in the bottom-right, do they sit directly above the "Loadout (P)" button and share its right edge, is
     there any gap or overlap, and is the ability bar clear of them?
  4. Stop; dirty check → `False`.

- [ ] **Step 8: Commit + push:**

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/PlayerHud.cs Assets/scripts/UI/ShopPricing.cs Assets/Tests/GoldHudLabelTests.cs Assets/Tests/GoldHudLabelTests.cs.meta
git commit -m "feat(ui): gold and its income move next to the shop button, out of the ability bar (HUD step 4)" -m "Co-Authored-By: <your session's line>"
git push
```

Assumptions file: `[C] gold is a two-line readout above the Loadout (P) button; the shop screen's own header keeps
the plain balance, and the two are never both on screen`.

---

# Task 5 (HUD step 5): minimap opacity states

**Files**
- Create: `Assets/scripts/UI/MinimapOpacity.cs`, `Assets/Tests/MinimapOpacityTests.cs`
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/UI/MinimapView.cs`
- Scratch (not committed): `SCRATCH\hud-readability\MapOpacityRun.cs`

---

- [ ] **Step 1: The pure rule.** Create `Assets/scripts/UI/MinimapOpacity.cs`:

```csharp
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// How see-through the minimap is right now (Tudor, 2026-09-17): the corner map is a little transparent so it
    /// takes less of the arena away, pressing M puts the big map at full opacity, and moving with the big map open
    /// drops it again so a player can still see where they are running.
    ///
    /// Pure, and therefore tested in edit mode rather than eyeballed in Play Mode, because the interesting part is
    /// not the three numbers - it is that the map must not STROBE. Two separate speeds (not one threshold) mean a
    /// player drifting around walking pace cannot cross the line several times a second, and the alpha then fades
    /// between states instead of snapping, so even a real start/stop reads as a change of state rather than a
    /// flicker.
    /// </summary>
    public static class MinimapOpacity
    {
        /// <summary>Whether the player counts as moving, given what they counted as last frame. Between the two
        /// speeds the answer is simply "whatever it already was" - that gap IS the deadzone. Passing the two
        /// speeds the wrong way round still works: the higher of the two always starts movement.</summary>
        public static bool IsMoving(bool wasMoving, float speed, float enterSpeed, float exitSpeed)
        {
            float low = Mathf.Min(enterSpeed, exitSpeed);
            float high = Mathf.Max(enterSpeed, exitSpeed);
            if (speed >= high)
                return true;
            if (speed <= low)
                return false;
            return wasMoving;
        }

        /// <summary>The opacity this state should settle at. The moving drop comes off the LARGE opacity only
        /// (Tudor: "unless they are moving with the map maximized which would decrease the opacity by 30%") -
        /// the corner map never dims further for movement, since it is already the quiet one.</summary>
        public static float TargetAlpha(bool largeOpen, bool moving, float cornerAlpha, float largeAlpha,
                                        float largeMovingDrop)
        {
            if (!largeOpen)
                return Mathf.Clamp01(cornerAlpha);

            return Mathf.Clamp01(moving ? largeAlpha - largeMovingDrop : largeAlpha);
        }

        /// <summary>One frame of a fade that crosses the whole 0-1 range in fadeSeconds. A fadeSeconds of 0 (or
        /// less) snaps straight to the target, so a designer can turn the fade off entirely.</summary>
        public static float Step(float current, float target, float deltaTime, float fadeSeconds)
        {
            if (fadeSeconds <= 0f || deltaTime <= 0f)
                return target;

            return Mathf.MoveTowards(current, target, deltaTime / fadeSeconds);
        }

        /// <summary>Exponential smoothing of a measured speed, frame-rate independent: after smoothingSeconds a
        /// step change is about 63% of the way there. Raw per-frame position deltas are noisy enough on their own
        /// to tip a threshold back and forth, which is the other half of why the map used to be able to flicker.
        /// A smoothingSeconds of 0 (or less) returns the raw sample.</summary>
        public static float SmoothSpeed(float current, float sample, float deltaTime, float smoothingSeconds)
        {
            if (smoothingSeconds <= 0f || deltaTime <= 0f)
                return sample;

            return Mathf.Lerp(current, sample, 1f - Mathf.Exp(-deltaTime / smoothingSeconds));
        }
    }
}
```

- [ ] **Step 2: The tests.** Create `Assets/Tests/MinimapOpacityTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapOpacityTests
    {
        private const float Corner = 0.8f;
        private const float Large = 1f;
        private const float Drop = 0.3f;

        [Test]
        public void TheCornerMapSitsAtTheCornerOpacity()
        {
            Assert.AreEqual(0.8f, MinimapOpacity.TargetAlpha(false, false, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void MovingDoesNotDimTheCornerMap()
        {
            Assert.AreEqual(0.8f, MinimapOpacity.TargetAlpha(false, true, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void TheLargeMapIsFullyOpaqueWhenStandingStill()
        {
            Assert.AreEqual(1f, MinimapOpacity.TargetAlpha(true, false, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void MovingWithTheLargeMapOpenDropsItByTheDrop()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.TargetAlpha(true, true, Corner, Large, Drop), 1e-5f);
        }

        [Test]
        public void ADropBiggerThanTheOpacityClampsAtInvisibleRatherThanGoingNegative()
        {
            Assert.AreEqual(0f, MinimapOpacity.TargetAlpha(true, true, Corner, 0.2f, 0.9f), 1e-5f);
        }

        [Test]
        public void MovingStartsOnlyAboveTheEnterSpeed()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(false, 1.2f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(false, 0.9f, 1f, 0.35f));
        }

        [Test]
        public void MovingStopsOnlyBelowTheExitSpeed()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(true, 0.5f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(true, 0.3f, 1f, 0.35f));
        }

        [Test]
        public void InsideTheDeadzoneTheAnswerNeverChanges()
        {
            // The whole point: a player hovering at 0.6 m/s gets the state they already had, both ways round,
            // so the map cannot strobe between them.
            Assert.IsTrue(MinimapOpacity.IsMoving(true, 0.6f, 1f, 0.35f));
            Assert.IsFalse(MinimapOpacity.IsMoving(false, 0.6f, 1f, 0.35f));
        }

        [Test]
        public void TheSpeedsSwappedRoundStillBehaveTheSameWay()
        {
            Assert.IsTrue(MinimapOpacity.IsMoving(false, 1.2f, 0.35f, 1f));
            Assert.IsFalse(MinimapOpacity.IsMoving(true, 0.3f, 0.35f, 1f));
        }

        [Test]
        public void AFullFadeTakesExactlyFadeSeconds()
        {
            float a = 0f;
            for (int i = 0; i < 25; i++) // 25 x 0.01s = 0.25s
                a = MinimapOpacity.Step(a, 1f, 0.01f, 0.25f);
            Assert.AreEqual(1f, a, 1e-4f);
        }

        [Test]
        public void TheFadeNeverOvershootsItsTarget()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0.69f, 0.7f, 1f, 0.25f), 1e-5f);
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0.71f, 0.7f, 1f, 0.25f), 1e-5f);
        }

        [Test]
        public void AZeroFadeSnaps()
        {
            Assert.AreEqual(0.7f, MinimapOpacity.Step(0f, 0.7f, 0.016f, 0f), 1e-5f);
        }

        [Test]
        public void SmoothingConvergesOnTheSampleAndZeroSmoothingIsTheRawSample()
        {
            Assert.AreEqual(4f, MinimapOpacity.SmoothSpeed(0f, 4f, 0.016f, 0f), 1e-5f);

            float s = 0f;
            for (int i = 0; i < 60; i++)
                s = MinimapOpacity.SmoothSpeed(s, 4f, 0.016f, 0.15f);
            Assert.AreEqual(4f, s, 0.01f);
        }

        [Test]
        public void SmoothingIsFrameRateIndependent()
        {
            float fast = 0f;
            for (int i = 0; i < 40; i++)
                fast = MinimapOpacity.SmoothSpeed(fast, 5f, 0.005f, 0.15f);

            float slow = 0f;
            for (int i = 0; i < 10; i++)
                slow = MinimapOpacity.SmoothSpeed(slow, 5f, 0.02f, 0.15f);

            Assert.AreEqual(fast, slow, 0.05f, "0.2s of smoothing must land in the same place at either step size");
        }
    }
}
```

- [ ] **Step 3: Theme fields.** After `minimapTeammateDotColor` (`UiTheme.cs:446`), in **Minimap**, BEFORE the
  `MinimapBubbleDiameter` method:

```csharp
        [Tooltip("How solid the small corner map is, 0 is invisible and 1 is fully opaque (Tudor, 2026-09-17: " +
                 "the corner map should be 20% more see-through, so 0.8). It multiplies everything on the map at " +
                 "once - the picture, the bubbles, the links and the markers.")]
        [Range(0f, 1f)] public float minimapCornerOpacity = 0.8f;
        [Tooltip("How solid the large map is while M is held open, 0 to 1. Tudor asked for full opacity here: " +
                 "you opened it on purpose, so nothing is hiding behind it that you would rather be looking at.")]
        [Range(0f, 1f)] public float minimapLargeOpacity = 1f;
        [Tooltip("How much opacity the large map gives up while you are moving, 0 to 1 (Tudor, 2026-09-17: " +
                 "\"decrease the opacity by 30%\"), so you can still see where you are running. Subtracted from " +
                 "Large Opacity above; the corner map never dims for movement.")]
        [Range(0f, 1f)] public float minimapLargeMovingOpacityDrop = 0.3f;
        [Tooltip("Seconds a full fade from invisible to solid takes. The map fades between its opacity states " +
                 "rather than snapping, so starting and stopping reads as a change of state, not a flicker. 0 " +
                 "turns the fade off.")]
        public float minimapOpacityFadeSeconds = 0.25f;
        [Tooltip("How fast you have to be going, in metres per second, before the large map counts you as " +
                 "MOVING. The base move speed is 5, so 1 is a fifth of walking pace. It is deliberately higher " +
                 "than Moving Exit Speed below - see that field.")]
        public float minimapMovingEnterSpeed = 1f;
        [Tooltip("How slow you have to be going, in metres per second, before the large map counts you as " +
                 "STOPPED. Deliberately lower than Moving Enter Speed: between the two the map keeps whatever " +
                 "state it already had, so a player drifting around one single threshold cannot make it strobe.")]
        public float minimapMovingExitSpeed = 0.35f;
        [Tooltip("Seconds of smoothing on the measured speed before it is compared with the two speeds above. " +
                 "Frame-to-frame position deltas are noisy enough on their own to tip a threshold back and " +
                 "forth. 0 uses the raw per-frame speed.")]
        public float minimapSpeedSmoothingSeconds = 0.15f;
```

  Also change `minimapLargeBottomClearance`'s default to `300f` and add to its tooltip:
  `" HUD step 5 lowered it from 380 to 300 because the HUD itself is 20% smaller (Hud Scale) and the gold row left it."`

- [ ] **Step 4: Wire it into `MinimapView`.**

  **4a.** New fields, next to `largeOpen` (`MinimapView.cs:129`):

```csharp
        private CanvasGroup fade;
        private float shownAlpha = -1f;   // < 0 = "not set yet", so the first frame snaps instead of fading in.
        private float smoothedSpeed;
        private bool moving;
        private Vector3 lastSamplePosition;
        private bool hasSamplePosition;
```

  **4b.** Harness readouts, next to `IsLargeOpen` (`MinimapView.cs:73`):

```csharp
        /// <summary>The opacity the map is actually drawn at this frame (HUD step 5) - for harness checks, so a
        /// capture is not the only way to tell the three states apart.</summary>
        public float CurrentOpacity => fade != null ? fade.alpha : 1f;
        /// <summary>The smoothed planar speed the moving/stopped test is made against, in metres per second.</summary>
        public float MeasuredSpeed => smoothedSpeed;
        /// <summary>Whether the map currently counts the player as moving.</summary>
        public bool IsMoving => moving;
        /// <summary>The theme this map is laid out from. Read-only, and for one caller: the F1 debug log overlay
        /// (HUD step 6) has to clear the corner map's reserved band, and this is the only live handle on the
        /// numbers that band is computed from - it installs itself at runtime and has nothing serialized.</summary>
        public UiTheme Theme => theme;
```

  **4c.** In `BuildFrame`, right after `root.sizeDelta = Vector2.one * theme.minimapCornerSize;`
  (`MinimapView.cs:346`):

```csharp
            // One CanvasGroup over the whole map is what makes Tudor's three opacity states a single number
            // (HUD step 5): it multiplies every Graphic underneath, including the ones on the markers' own nested
            // Canvas, so the picture, the bubbles, the links and the dots all fade together and nothing has to
            // remember its own colour's alpha. Interactable and blocksRaycasts are both off, so the class
            // comment's "NEVER BLOCKS A SHOT" guarantee holds through this component too.
            fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.interactable = false;
            fade.blocksRaycasts = false;
```

  **4d.** In `LateUpdate`, after `UpdatePlayers();` (`MinimapView.cs:227`), add `UpdateOpacity();`, and the method:

```csharp
        /// <summary>Tudor, 2026-09-17: the corner map is a little see-through, M makes it solid, and moving with
        /// M open dims it again. The rule (including the deadzone that stops it strobing) is MinimapOpacity, so
        /// it is tested in edit mode; this method only measures the speed and hands the answer to a CanvasGroup.
        ///
        /// Speed is MEASURED from this player's own position, not read from PlayerMotor.CurrentSpeed: that
        /// property is the CONFIGURED speed, which still reads 5 m/s while a stunned player stands perfectly
        /// still. A position delta is true for a dash, a knockback and a stun alike.</summary>
        private void UpdateOpacity()
        {
            if (fade == null)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            Vector3 position = transform.position;
            if (hasSamplePosition && deltaTime > 0f)
            {
                Vector3 step = position - lastSamplePosition;
                step.y = 0f; // Planar: falling off the arena is not "moving with the map open".
                smoothedSpeed = MinimapOpacity.SmoothSpeed(smoothedSpeed, step.magnitude / deltaTime,
                                                           deltaTime, theme.minimapSpeedSmoothingSeconds);
            }
            lastSamplePosition = position;
            hasSamplePosition = true;

            moving = MinimapOpacity.IsMoving(moving, smoothedSpeed,
                                             theme.minimapMovingEnterSpeed, theme.minimapMovingExitSpeed);
            float target = MinimapOpacity.TargetAlpha(largeOpen, moving, theme.minimapCornerOpacity,
                                                      theme.minimapLargeOpacity,
                                                      theme.minimapLargeMovingOpacityDrop);
            // The very first frame snaps: a map that faded up from nothing every time a player spawned would
            // read as a bug, not as a nicety.
            float next = shownAlpha < 0f
                ? target
                : MinimapOpacity.Step(shownAlpha, target, deltaTime, theme.minimapOpacityFadeSeconds);
            if (!Mathf.Approximately(next, shownAlpha))
            {
                fade.alpha = next;
                shownAlpha = next;
            }
        }
```

  **4e.** Add one line to the class comment's M and P paragraph (`MinimapView.cs:51-53`):

```
    /// OPACITY: the corner map is drawn a little see-through, M makes it solid, and moving with M open dims it
    /// again (UiTheme > Minimap, and the MinimapOpacity rule). One CanvasGroup on the map's root carries all of it.
```

- [ ] **Step 5: Hand-edit `UiTheme.asset`.**
  - **Change** `minimapLargeBottomClearance: 380` → `minimapLargeBottomClearance: 300`.
  - **Insert after** `minimapTeammateDotColor: {r: 0.45, g: 1, b: 0.45, a: 1}`:
    ```yaml
      minimapCornerOpacity: 0.8
      minimapLargeOpacity: 1
      minimapLargeMovingOpacityDrop: 0.3
      minimapOpacityFadeSeconds: 0.25
      minimapMovingEnterSpeed: 1
      minimapMovingExitSpeed: 0.35
      minimapSpeedSmoothingSeconds: 0.15
    ```
  Read back and print all seven plus the clearance. `git diff` → seven added lines, one changed.

- [ ] **Step 6: Recompile and run the tests.** Expected `errors: []`, and the total is **Task 4's total + 14**
  (the fourteen `MinimapOpacityTests`). Report both.

- [ ] **Step 7: Measure the three states live.** Play, join, **actor check**, then:

  1. **Measure the HUD panel height** (decision D2): `HudReport.Run` again and read `panel rect=`. If
     `panel.rect.height * hudScale + hudBottomOffset` exceeds 300, raise `minimapLargeBottomClearance` in the asset
     to the next multiple of 20 above it and say so in your report. Print both numbers either way.
  2. Write `SCRATCH\hud-readability\MapOpacityRun.cs`. It runs entirely in-process (rule 13) - CLI round trips are
     seconds long and the fade is a quarter of a second:

```csharp
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// HUD step 5: walks the minimap through corner -> large -> large+moving -> large -> corner and records the
/// CanvasGroup alpha every frame, so the three states and the fade are MEASURED, not eyeballed. Not committed.
public static class MapOpacityRun
{
    private const string OutPath = "Temp/hud/map-opacity.txt";

    public static string Run()
    {
        var map = Overpower.UI.MinimapView.Local;
        if (map == null || !map.IsBuilt) return "ABORT: no built local MinimapView";
        var view = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
        if (view == null) return "ABORT: no local player";
        Directory.CreateDirectory("Temp/hud");
        map.StartCoroutine(Drive(map, view.transform));
        return "started - read " + OutPath + " in about 6 seconds";
    }

    private static IEnumerator Drive(Overpower.UI.MinimapView map, Transform player)
    {
        var log = new StringBuilder();
        var display = player.GetComponent<PlayerDisplacement>();

        yield return Sample(map, log, "corner", 0.6f);

        if (map.IsLargeOpen) map.ToggleLarge();
        map.ToggleLarge();
        yield return Sample(map, log, "large-still", 0.8f);

        // Moving: many small teleports, ~0.08 m each frame, which at 60fps is roughly 4.8 m/s - well over
        // Moving Enter Speed. transform.position is never touched (physics would put it straight back).
        float movedFor = 0f;
        while (movedFor < 1.2f)
        {
            Vector3 next = player.position + player.forward * 0.08f;
            display.TeleportTo(next);
            movedFor += Time.unscaledDeltaTime;
            log.AppendLine($"moving\t{map.CurrentOpacity.ToString("0.000", CultureInfo.InvariantCulture)}\t" +
                           $"{map.MeasuredSpeed.ToString("0.00", CultureInfo.InvariantCulture)}\t{map.IsMoving}");
            yield return null;
        }

        yield return Sample(map, log, "large-stopped", 1.2f);
        map.ToggleLarge();
        yield return Sample(map, log, "corner-again", 0.8f);

        File.WriteAllText(OutPath, log.ToString());
    }

    private static IEnumerator Sample(Overpower.UI.MinimapView map, StringBuilder log, string label, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            log.AppendLine($"{label}\t{map.CurrentOpacity.ToString("0.000", CultureInfo.InvariantCulture)}\t" +
                           $"{map.MeasuredSpeed.ToString("0.00", CultureInfo.InvariantCulture)}\t{map.IsMoving}");
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
```

  If `PlayerDisplacement.TeleportTo`'s real signature differs, check it with
  `grep -n "public .*TeleportTo" Assets/scripts/Player/PlayerDisplacement.cs` and match it. Teleport along open
  ground — check the destination is clear first, and if the player hits a wall, pick another facing and say so.

  3. Run it, wait, then read `Temp/hud/map-opacity.txt` and report:
     - the settled `corner` alpha → **0.800**;
     - the settled `large-still` alpha → **1.000**;
     - the settled `moving` alpha → **0.700**, with `IsMoving True` and a measured speed above 1;
     - the settled `large-stopped` alpha back at **1.000**, with `IsMoving False`;
     - the settled `corner-again` alpha → **0.800**;
     - **and, for the two transitions, that the alpha column is monotone** (never up-down-up) and that the change
       takes roughly 0.25 s × the size of the step. Quote the actual rows. If it oscillates, the deadzone or the
       fade is wrong — report it before going further.
  4. Captures (`--source screen`), each read and described honestly:
     - `s5-map-corner.png` — is the corner map visibly more see-through than `before-map-corner.png`, and is it
       still readable?
     - `s5-map-large.png` — is the big map solid?
     - `s5-map-large-moving.png` — *(amended 2026-09-18: capture method pinned.)* Use the **same**
       `capture_game_view --source screen` at **616×576** as every other capture in this plan. **Never** fall back
       to an in-process `ScreenCapture.CaptureScreenshot`: on this machine it comes out **1920×1080**, which cannot
       be compared with the 616×576 `before-map-*.png`. To give the CLI round trip time to land, keep the player
       walking with the big map open for **8 s** in a separate run of the moving coroutine (after the alpha
       recording, not during it), take the CLI capture about 2 s in, and confirm from the coroutine's own log that
       `IsMoving` was `True` at the capture's timestamp. If it still cannot be caught, say so plainly and skip this
       one capture rather than change capture path.
     - `s5-map-large-hud.png` — does the big map still clear the (now smaller) HUD, with no overlap?
  5. Stop; dirty check → `False`.

- [ ] **Step 8: Commit + push:**

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/MinimapOpacity.cs Assets/scripts/UI/MinimapOpacity.cs.meta Assets/Tests/MinimapOpacityTests.cs Assets/Tests/MinimapOpacityTests.cs.meta Assets/scripts/UI/MinimapView.cs
git commit -m "feat(ui): the corner minimap is more see-through, M makes it solid, and moving with M open dims it (HUD step 5)" -m "Co-Authored-By: <your session's line>"
git push
```

Assumptions file: `[C] corner 0.8 / large 1.0 / large+moving 0.7, 0.25s fade, moving 1.0 m/s in and 0.35 m/s out;
speed measured from position, not PlayerMotor.CurrentSpeed`.

---

# Task 6 (HUD step 6): F1 keeps one key; the debug log moves right, below the minimap

**Files**
- Create: `Assets/scripts/UI/HudScreenLayout.cs`, `Assets/Tests/HudScreenLayoutTests.cs`
- Modify: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`,
  `Assets/scripts/Debug/DebugOverlay.cs`

---

- [ ] **Step 1: The pure rule.** Create `Assets/scripts/UI/HudScreenLayout.cs`:

```csharp
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where the F1 debug log may draw, in real screen pixels. Its own file, and pure, because the debug log is
    /// the one thing on screen that is NOT a Canvas: it is IMGUI, so it has no CanvasScaler and no reference
    /// resolution. Everything it has to stay clear of - the corner minimap above all - is laid out in UiTheme's
    /// canvas units, and the only honest way to compare the two is to convert with the same formula UGUI uses.
    ///
    /// Tudor, 2026-09-17: F1 stays one key. The test range panel keeps the top-left corner it has always had, and
    /// the log moves to the right-hand side, under the map, where the two can no longer sit on top of each other.
    ///
    /// The band is worked out from the theme's CORNER minimap numbers, never from the map's live rectangle: M
    /// moves and scales that rectangle, and a log that jumped down the screen every time someone opened the map
    /// would be worse than one sitting a few pixels lower than it strictly has to.
    /// </summary>
    public static class HudScreenLayout
    {
        /// <summary>How many screen pixels one canvas unit is worth, for a CanvasScaler in Scale With Screen Size
        /// mode - Unity's own documented formula, a blend of the width and height ratios in log space, which is
        /// why it is a Pow/Lerp/Log rather than a plain Lerp of the two ratios.</summary>
        public static float CanvasScaleFactor(Vector2 referenceResolution, float match,
                                              float screenWidth, float screenHeight)
        {
            float referenceWidth = Mathf.Max(1f, referenceResolution.x);
            float referenceHeight = Mathf.Max(1f, referenceResolution.y);
            float logWidth = Mathf.Log(Mathf.Max(1f, screenWidth) / referenceWidth, 2f);
            float logHeight = Mathf.Log(Mathf.Max(1f, screenHeight) / referenceHeight, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, Mathf.Clamp01(match)));
        }

        /// <summary>How far down from the top of the screen, in pixels, the corner minimap's reserved band ends:
        /// its margin from the screen edge plus its own bounding size, both in canvas units, scaled.</summary>
        public static float MinimapBandBottomPixels(float cornerMargin, float cornerSize, float scaleFactor) =>
            Mathf.Max(0f, (cornerMargin + cornerSize) * Mathf.Max(0f, scaleFactor));

        /// <summary>The debug log's rectangle in GUI space (y grows DOWNWARD, which is what GUI.Box wants): along
        /// the right edge inside margin, starting gap pixels below the minimap band, never taller than
        /// maxHeightFraction of the screen and never running off the bottom of it.</summary>
        public static Rect DebugLogRect(float screenWidth, float screenHeight, float minimapBandBottom,
                                        float width, float maxHeightFraction, float margin, float gap)
        {
            float w = Mathf.Min(Mathf.Max(1f, width), Mathf.Max(1f, screenWidth - 2f * margin));
            float top = Mathf.Max(margin, minimapBandBottom + gap);
            float available = Mathf.Max(0f, screenHeight - margin - top);
            float h = Mathf.Min(Mathf.Max(0f, screenHeight) * Mathf.Clamp01(maxHeightFraction), available);
            float x = Mathf.Max(margin, screenWidth - margin - w);
            return new Rect(x, top, w, h);
        }
    }
}
```

- [ ] **Step 2: The tests.** Create `Assets/Tests/HudScreenLayoutTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class HudScreenLayoutTests
    {
        private static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        [Test]
        public void AtTheReferenceResolutionOneCanvasUnitIsOnePixel()
        {
            Assert.AreEqual(1f, HudScreenLayout.CanvasScaleFactor(Reference, 0.5f, 1920f, 1080f), 1e-4f);
        }

        [Test]
        public void MatchZeroFollowsTheWidthAndMatchOneFollowsTheHeight()
        {
            Assert.AreEqual(0.5f, HudScreenLayout.CanvasScaleFactor(Reference, 0f, 960f, 1080f), 1e-4f);
            Assert.AreEqual(0.5f, HudScreenLayout.CanvasScaleFactor(Reference, 1f, 1920f, 540f), 1e-4f);
        }

        [Test]
        public void AtTheProjectsCaptureSizeTheFactorIsTheGeometricMeanOfBothRatios()
        {
            // 616x576 is the real Game view this project captures at. sqrt(616/1920 * 576/1080) = 0.4136...
            float expected = Mathf.Sqrt((616f / 1920f) * (576f / 1080f));
            Assert.AreEqual(expected, HudScreenLayout.CanvasScaleFactor(Reference, 0.5f, 616f, 576f), 1e-4f);
        }

        [Test]
        public void TheMinimapBandIsItsMarginPlusItsSizeInPixels()
        {
            Assert.AreEqual(182f, HudScreenLayout.MinimapBandBottomPixels(24f, 340f, 0.5f), 1e-3f);
        }

        [Test]
        public void TheLogSitsAgainstTheRightEdgeBelowTheBand()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 576f, 150f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(616f - 8f - 420f, r.x, 1e-3f);
            Assert.AreEqual(156f, r.y, 1e-3f);
            Assert.AreEqual(420f, r.width, 1e-3f);
            Assert.AreEqual(576f * 0.45f, r.height, 1e-3f);
        }

        [Test]
        public void ANarrowScreenShrinksTheLogInsteadOfPushingItOffTheLeftEdge()
        {
            Rect r = HudScreenLayout.DebugLogRect(300f, 576f, 0f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(8f, r.x, 1e-3f);
            Assert.AreEqual(284f, r.width, 1e-3f);
        }

        [Test]
        public void ATallMinimapBandShortensTheLogRatherThanRunningOffTheBottom()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 400f, 360f, 420f, 0.45f, 8f, 6f);
            Assert.AreEqual(366f, r.y, 1e-3f);
            Assert.AreEqual(400f - 8f - 366f, r.height, 1e-3f);
            Assert.LessOrEqual(r.yMax, 400f - 8f + 1e-3f);
        }

        [Test]
        public void TheLogNeverGetsANegativeHeight()
        {
            Rect r = HudScreenLayout.DebugLogRect(616f, 200f, 400f, 420f, 0.45f, 8f, 6f);
            Assert.GreaterOrEqual(r.height, 0f);
        }

        [Test]
        public void WithNoMinimapBandTheLogStillClearsTheScreenMargin()
        {
            Rect r = HudScreenLayout.DebugLogRect(1920f, 1080f, 0f, 420f, 0.45f, 8f, 0f);
            Assert.AreEqual(8f, r.y, 1e-3f);
        }
    }
}
```

- [ ] **Step 3: Theme fields.** A new header at the very END of `UiTheme.cs` (after `coneLineMaterial`, and after
  `ApplyHudTextStyle` — put the fields above the method or move the method below them, but keep the fields in one
  contiguous declaration block so the YAML order is unambiguous):

```csharp
        [Header("Debug log (F1)")]
        // These four are in SCREEN PIXELS, not canvas units, and deliberately so: the debug log is IMGUI, which
        // has no CanvasScaler to scale anything for it. Everything else on this asset is in canvas units.
        [Tooltip("Width of the F1 debug log panel, in SCREEN PIXELS (the log is IMGUI - it has no canvas, so it " +
                 "does not scale with the rest of the UI). It shrinks on a screen too narrow to hold it.")]
        public float debugLogWidthPixels = 420f;
        [Tooltip("The tallest the F1 debug log may get, as a fraction of the screen height. It is shortened " +
                 "further if there is not that much room left under the minimap.")]
        [Range(0.1f, 1f)] public float debugLogMaxHeightFraction = 0.45f;
        [Tooltip("Gap between the F1 debug log and the edges of the screen, in SCREEN PIXELS.")]
        public float debugLogScreenMarginPixels = 8f;
        [Tooltip("Gap between the bottom of the corner minimap and the top of the F1 debug log, in SCREEN " +
                 "PIXELS (Tudor, 2026-09-17: the log used to open in the top-left corner, on top of the F1 test " +
                 "range panel). The gap is measured against the CORNER map's reserved space even while the large " +
                 "map is open, so the log does not jump every time someone presses M.")]
        public float debugLogGapBelowMinimapPixels = 6f;
```

- [ ] **Step 4: Move the overlay.** In `Assets/scripts/Debug/DebugOverlay.cs`:

  **4a.** Add `using Overpower.UI;` at the top, and these consts next to `MaxLines` (line 26):

```csharp
    // Used before any minimap exists to read the theme from - the menu, or the seconds before a player spawns.
    // They are what this overlay drew at before UiTheme had any say, so nothing gets worse in that case.
    const float FallbackWidthPixels = 420f;
    const float FallbackMaxHeightFraction = 0.45f;
    const float FallbackMarginPixels = 8f;
    const float HintHeightPixels = 20f;
```

  **4b.** Add the rect method above `OnGUI`:

```csharp
    /// <summary>Where the log (and its hint) draw: the right-hand side, below the corner minimap, clamped to the
    /// screen (Tudor, 2026-09-17 - it used to open in the top-left corner, straight on top of the F1 test range
    /// panel, which is why he offered to rebind one of them; moving it is the better half of that offer, since
    /// one key for "show me the tools" is fewer things for a playtester to remember).
    ///
    /// The numbers come from UiTheme, reached through the local player's minimap - which is also the thing being
    /// cleared. This component installs itself at runtime (see Install) and has nothing serialized, so there is
    /// no Inspector slot to put a theme in; borrowing the minimap's is honest rather than inventing a static
    /// somewhere for one caller.</summary>
    Rect LogRect()
    {
        MinimapView minimap = MinimapView.Local;
        UiTheme theme = minimap != null ? minimap.Theme : null;
        if (theme == null)
            return HudScreenLayout.DebugLogRect(Screen.width, Screen.height, 0f, FallbackWidthPixels,
                                                FallbackMaxHeightFraction, FallbackMarginPixels, 0f);

        float scale = HudScreenLayout.CanvasScaleFactor(theme.referenceResolution, theme.matchWidthOrHeight,
                                                        Screen.width, Screen.height);
        float band = HudScreenLayout.MinimapBandBottomPixels(theme.minimapCornerMargin, theme.minimapCornerSize,
                                                             scale);
        return HudScreenLayout.DebugLogRect(Screen.width, Screen.height, band, theme.debugLogWidthPixels,
                                            theme.debugLogMaxHeightFraction, theme.debugLogScreenMarginPixels,
                                            theme.debugLogGapBelowMinimapPixels);
    }
```

  **4c.** Replace `OnGUI` (lines 116-139) with:

```csharp
    void OnGUI()
    {
        Rect area = LogRect();

        if (!visible)
        {
            // Always show the hint, so a tester who has never been told still finds it - in the log's own
            // column, so it can no longer land on the F1 test range panel in the top-left corner.
            GUI.Label(new Rect(area.x, area.y, area.width, HintHeightPixels), $"{ToggleKey}: debug log");
            return;
        }

        GUI.Box(area, $"Debug log — {CopyKey} copies to clipboard");

        GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 22f, area.width - 16f, area.height - 32f));
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (string line in lines)
            GUILayout.Label(line);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (Time.realtimeSinceStartup - copiedAt < 2f)
        {
            float y = Mathf.Min(area.yMax + 2f, Screen.height - HintHeightPixels);
            GUI.Label(new Rect(area.x, y, area.width, HintHeightPixels), $"copied {lines.Count} lines");
        }
    }
```

  **4d.** Add one line to the class comment: `/// The log draws on the RIGHT, under the corner minimap, so it and the F1 test range panel (top-left) never overlap.`

- [ ] **Step 5: Hand-edit `UiTheme.asset`.** Append at the very end of the file, AFTER
  `coneLineMaterial: {fileID: 2100000, guid: ad2e00cea264ef94fa08363867d004e5, type: 2}`:

```yaml
  debugLogWidthPixels: 420
  debugLogMaxHeightFraction: 0.45
  debugLogScreenMarginPixels: 8
  debugLogGapBelowMinimapPixels: 6
```

  Read back; `git diff` → four added lines only. (If Step 3 put the fields anywhere other than last in the class,
  put them in the matching place in the YAML instead — declaration order is what matters, not "the end".)

- [ ] **Step 6: Recompile and run the tests.** Expected `errors: []`, and the total is **Task 5's total + 9**.
  Report both.

- [ ] **Step 7: Verify live.** Play, join, **actor check**, then:

  1. Read the computed rect without pressing anything:
     ```
     unity command eval -- --code "var t = Overpower.UI.MinimapView.Local.Theme; float s = Overpower.UI.HudScreenLayout.CanvasScaleFactor(t.referenceResolution, t.matchWidthOrHeight, UnityEngine.Screen.width, UnityEngine.Screen.height); float b = Overpower.UI.HudScreenLayout.MinimapBandBottomPixels(t.minimapCornerMargin, t.minimapCornerSize, s); var r = Overpower.UI.HudScreenLayout.DebugLogRect(UnityEngine.Screen.width, UnityEngine.Screen.height, b, t.debugLogWidthPixels, t.debugLogMaxHeightFraction, t.debugLogScreenMarginPixels, t.debugLogGapBelowMinimapPixels); return UnityEngine.Screen.width + \"x\" + UnityEngine.Screen.height + \" scale=\" + s + \" band=\" + b + \" rect=\" + r;"
     ```
     Report the whole line. At 616×576 expect roughly `scale=0.414 band=150.6 rect=(x:188.0, y:156.6, width:420.0, height:259.2)`.
  2. Show the log without a key press (rule 2), by flipping the private field:
     ```
     unity command eval -- --code "var o = UnityEngine.Object.FindObjectsByType<DebugOverlay>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None); if (o.Length != 1) return \"expected 1 DebugOverlay, found \" + o.Length; var f = typeof(DebugOverlay).GetField(\"visible\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); f.SetValue(o[0], true); return \"shown\";"
     ```
     Capture `s6-debuglog.png` (`--source screen`). **Read it.** Say honestly: is the log on the right, does it
     start below the minimap without touching it, and is the top-left corner (where the test range panel opens)
     completely clear of it?
  3. Open the test range panel the same way and capture `s6-f1-both.png` with BOTH showing:
     ```
     unity command eval -- --code "var p = UnityEngine.Object.FindObjectsByType<Overpower.TestRange.TestRangePanel>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None); if (p.Length != 1) return \"expected 1 TestRangePanel, found \" + p.Length; var m = typeof(Overpower.TestRange.TestRangePanel).GetMethod(\"SetVisible\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); m.Invoke(p[0], new object[] { true }); return \"shown\";"
     ```
     **Read it.** Say honestly whether the two panels overlap anywhere at all. Set both back to hidden afterwards.
  4. Stop; dirty check → `False`.

- [ ] **Step 8: Commit + push:**

```
git status
git add Assets/scripts/UI/UiTheme.cs Assets/Gameplay/Config/UiTheme.asset Assets/scripts/UI/HudScreenLayout.cs Assets/scripts/UI/HudScreenLayout.cs.meta Assets/Tests/HudScreenLayoutTests.cs Assets/Tests/HudScreenLayoutTests.cs.meta Assets/scripts/Debug/DebugOverlay.cs
git commit -m "fix(ui): the F1 debug log moves under the minimap on the right, clear of the test range panel (HUD step 6)" -m "Co-Authored-By: <your session's line>"
git push
```

Assumptions file: `[C] F1 stays one key; the log moved instead of being rebound; its sizes are in screen pixels
because IMGUI has no CanvasScaler`.

---

# Task 7 (HUD step 7): the capture pass, and nothing broken

Nothing in this task edits gameplay. It measures, it captures, and it says honestly what it found.

**Files**
- Modify: nothing in `Assets/`, unless a check fails and the controller asks for a fix.
- Scratch (not committed): `SCRATCH\hud-readability\RaycastAudit.cs`

---

- [ ] **Step 1: Static diffs against `BASE`** (Task 1's hash):

```
git diff --stat BASE..HEAD -- Assets/Gameplay/Config/UiTheme.asset
git diff BASE..HEAD -- Assets/Gameplay/Config/UiTheme.asset
git diff --stat BASE..HEAD -- Assets/Gameplay/Config/GameplayConfig.asset
git diff --stat BASE..HEAD -- Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset
git diff --stat BASE..HEAD
```

  Expected:
  - `UiTheme.asset`: **1 deletion** (`panelColor`), **28 insertions** (3 + 7 + 5 + 2 + 7 + 4 across Tasks 1-6),
    **4 changed values** (`textOutlineWidth` 0.2 → 0.26, `slotReadyColor`, `slotBlockedColor`,
    `minimapLargeBottomClearance` 380 → 300, plus anything the Task 5 measurement raised). Nothing else.
    **List every changed line in your report.**
  - `GameplayConfig.asset`: **no output**.
  - `PhotonServerSettings.asset`: **no output**.
  - The whole diff touches only the files this plan's file map lists.

- [ ] **Step 2: Full test run.** Dirty check → `False`, recompile → `errors: []`,
  `run_tests --mode editor --async_tests true`, poll. Expected: **`BASE`'s total + 27** (4 gold, 14 opacity,
  9 layout), **all passing**. Report the total and any failure in full.

- [ ] **Step 3: Enter Play Mode once for everything below.** Dirty check → `False`, `editor_play`, poll, join,
  **actor check (rule 7)**. Everything from here on runs in this one session.

- [ ] **Step 4: R1 — no new raycast targets.** Write `SCRATCH\hud-readability\RaycastAudit.cs`:

```csharp
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// HUD step 7 R1: every Graphic on the local player's own canvases that could swallow a shot. Not committed.
public static class RaycastAudit
{
    public static string Run()
    {
        var view = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber);
        if (view == null) return "ABORT: no local player";

        string s = "";
        foreach (var canvas in view.GetComponentsInChildren<Canvas>(true))
        {
            var targets = canvas.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget).ToArray();
            s += $"{canvas.name}: order={canvas.sortingOrder} raycaster={(canvas.GetComponent<GraphicRaycaster>() != null)} " +
                 $"targets={targets.Length}";
            foreach (var t in targets)
                s += $" [{t.transform.parent?.name}/{t.name}]";
            s += "\n";
        }
        return s;
    }
}
```

  Run it. Expected:
  - `Player Hud Canvas`: `raycaster=False targets=0` (Task 2 turned the four slot backgrounds off — they were the
    only ones, and they were never reachable anyway without a raycaster);
  - `Minimap Canvas`: `raycaster=False targets=0`;
  - `Loadout Toggle Canvas`: `raycaster=True targets=2` (the button's own `Image` and its label background — read
    the names and say exactly which two);
  - `Loadout Screen Canvas`: unchanged from before this plan.

  **Report every line.** Then fire a shot with the cursor over the new gold readout's corner and over the corner
  minimap, and confirm a bullet actually leaves:
  ```
  unity command eval -- --code "var v = PlayerLookup.GetPhotonViewFor(Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber); var w = v.GetComponent<WeaponFiring>(); int before = UnityEngine.Object.FindObjectsByType<BulletController>(UnityEngine.FindObjectsSortMode.None).Length; w.TryFire(); return before + \" -> \" + UnityEngine.Object.FindObjectsByType<BulletController>(UnityEngine.FindObjectsSortMode.None).Length;"
  ```
  If `TryFire` is not public or takes arguments, find the real entry point with
  `grep -n "public .*TryFire" Assets/scripts/Weapons/WeaponFiring.cs` and use it; say which you used. A hitscan
  weapon spawns no `BulletController` — switch to a projectile weapon through the test range panel's own
  `OnWeaponSelected` first, or report that you could not and why. **Aim follows Tudor's real mouse while Unity is
  unfocused (trap 8d)**, so do not judge WHERE the shot went — only that one was fired.

- [ ] **Step 5: R2 — P and M still behave.**

  1. M opens the big map:
     `Overpower.UI.MinimapView.Local.ToggleLarge(); return Overpower.UI.MinimapView.Local.IsLargeOpen;` → `True`.
  2. P closes M: open the loadout screen
     (`PlayerLookup.GetPhotonViewFor(...).GetComponent<Overpower.UI.LoadoutScreen>().Open()`), then read
     `Overpower.UI.MinimapView.Local.IsLargeOpen + " " + Overpower.UI.LoadoutScreen.IsOpen` → `False True`.
     Capture `s7-p-open.png`. **Read it.** Say whether the loadout screen looks unchanged apart from its text now
     carrying the shadow, and whether its own header gold still reads "Gold N".
  3. M closes P: `ToggleLarge()` again → `True False`. Capture `s7-m-after-p.png`. **Read it.** Say whether the big
     map clears the HUD entirely (no overlap at all) and whether the gold/shop corner is still visible under it.
  4. Close the big map.

- [ ] **Step 6: The after captures.** Each `--source screen`, copied to SCRATCH, **read and described honestly**,
  each one named against its before:

  | After | Compare with | Say |
  |---|---|---|
  | `after-hud.png` | `before-hud.png` | Is the HUD visibly ~20% smaller? Is the dark slab gone? Are health, shield and overcharge still readable on sand? |
  | `after-slot-detail.png` (one slot recharging, one blocked, pips part-spent) | `before-hud.png` | Is every key label centred at the top of its square? Is the name centred below it? Can you count the pips? |
  | `after-map-corner.png` | `before-map-corner.png` | Is the corner map more see-through, and still readable? |
  | `after-map-large.png` | `before-map-large.png` | Is the big map solid, and does it clear the HUD? |
  | `after-gold.png` | `before-hud.png` | Are the balance and the income above the Loadout (P) button, right-aligned, with no overlap, and is there no gold anywhere in the ability bar? |
  | `after-f1-both.png` | `before-f1-hint.png` | Test range top-left, debug log right and below the map, no overlap anywhere. |

- [ ] **Step 7: Final numbers.** Run `HudReport.Run` one last time and report every line, plus:
  - the HUD panel's rect height at `BASE` (Task 1 step 6) versus now;
  - `hud raycast targets` at `BASE` (4) versus now (0);
  - the three measured minimap opacities from Task 5.

- [ ] **Step 8: Stop and tidy.** `editor_stop`, poll until stopped, dirty check → `False`, `git status` clean.
  Delete `Assets/Temp/hud` and its `.meta` if you had to use that path. Confirm no Player build is running.

- [ ] **Step 9: Report.** One message with:
  - the three static diffs (Step 1);
  - the test total, before and after;
  - the raycast audit;
  - every capture's honest description;
  - the measured numbers;
  - **anything that did not match**, stated plainly. A capture you could not take, a check you could not run, or a
    number that came out wrong is a finding, not a failure to hide.

  Nothing is committed in this task unless the controller asks for a fix. If it does, the fix goes back to the task
  that owns the file, and this task re-runs from Step 1.

---

## Self-review: Tudor's asks → tasks

| Tudor's ask (his words) | Where it lands | Task |
|---|---|---|
| "I want the ability bar to be smaller" | `hudScale` 0.8 on `Hud Panel` — the ONE reduction, not a second one | 1 |
| "everything centerd properly" | Key strip centred at the top; `Content Box` centres icon/name/READY; silenced banner label centred; pips and reason already centred, heights now on the theme | 1 |
| "the text should be in the middle of the abilty square for example" | `Content Box` — the name centres in the square a player sees, not in a box a fifth of which was the key label | 1 |
| "make the charge indicators a bit bigger" | `pipSize` 8 → 14 (5.7% → 10% of a slot), `pipSpacing` 2 → 4, `pipRowHeight` 14 → 20 | 3 |
| "use a different font if you have to to make the ui more visible" | **Not needed, and not done.** The existing TMP default font plus `hudTextFaceDilate` 0.08, `textOutlineWidth` 0.2 → 0.26, and a TMP underlay shadow. No new asset, no licence | 2 |
| "The shield, health and overcharge are fine" | Bar heights, colours and tracks untouched | — |
| "i would like if there wasnt that dark opaque background behind them and the abilites" | `Hud Panel`'s `Image` deleted and `panelColor` removed from the theme; each slot becomes a 3-unit border over a 0.22-alpha wash | 2 |
| "since it takes away from the visibility" | Readability replaced by the text's own means: heavier face, stronger outline, soft shadow, pip rims, slot borders | 2, 3 |
| "i would also reduce the overall size by 20%" | The same single `hudScale` 0.8, on the HUD panel and the bottom-right gold/shop block | 1 |
| "I want the minimap to be 20% more see through" | `minimapCornerOpacity` 0.8 via a `CanvasGroup` | 5 |
| "if the player presses m it should have the opacity to max" | `minimapLargeOpacity` 1.0 | 5 |
| "(optional: unless they are moving with the map maximized which would decrease the opacity by 30%)" | `minimapLargeMovingOpacityDrop` 0.3 → 0.7, measured from real position deltas | 5 |
| …without flicker (controller reading 5) | `minimapOpacityFadeSeconds` 0.25 + a 0.35–1.0 m/s deadzone + 0.15 s speed smoothing, all pinned by 14 edit-mode tests | 5 |
| "display the gold generation next to the shop since these systems are tied together" | Balance and income, two lines, right-aligned directly above the Loadout (P) button; the row leaves `Hud Panel`; exactly one HUD label, checked live | 4 |
| "you can keep the dummy and console log opening next to each other in the right side or … bind them to different things" | One key kept (F1); the debug log and its hint move right, below the corner minimap, clamped; the test range keeps the top-left | 6 |
| Every value a designer value (controller reading 8) | 28 new `UiTheme` fields, each with a plain `[Tooltip]`; the last two hidden `const`s in `PlayerHud` (`PipRowHeight`, `ReasonTextHeight`) and the hardcoded `8f` pip size move onto the theme too | 1–6 |
| `UiTheme.asset` hand-edited, never `SetDirty`+`SaveAssets` (trap 8c) | Rule 10, restated per task with the exact YAML neighbour | 1–6 |
| `GameplayConfig.asset` / `PhotonServerSettings.asset` unchanged | Rule 11; proved by diff | 7 |
| Before/after captures at 616×576, read honestly | Before in Task 1 step 0; after in Task 7 step 6 | 1, 7 |
| Nothing blocks shooting; P and M still behave | R1 (raycast audit + a real shot), R2 (P/M both ways) | 7 |
