# Playtest Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Phase 1 testable by its designer — visible, movement-sensitive weapon inaccuracy; a working, readable HUD; an overhead health+shield bar; and a P loadout screen (the future shop) with a real weapon upgrade tree.

**Architecture:** Pure rules stay in `Assets/scripts/Combat/` with edit-mode tests (`AimConeState` movement terms, new `WeaponUpgradeTree`). Presentation is uGUI built in code, following `Assets/scripts/UI/PlayerHud.cs` and `Assets/scripts/TestRange/TestRangePanel.cs`, with every visual value on one new `UiTheme` ScriptableObject. All player choices go through the existing replicated `PlayerLoadout` — no new RPCs.

**Tech Stack:** Unity 6000.0.70f1, Photon PUN 2, uGUI + TextMeshPro, Unity Input System, NUnit edit-mode tests, `unity` CLI.

**Spec:** `docs/superpowers/specs/2026-09-14-playtest-polish-design.md` (read it first — it records Tudor's decisions and the two root causes).

---

## Rules for every task (from `Resources/loops/Limit Test/HANDOFF.md` §6 — each has cost time)

1. Branch `limit-testing` only; never touch or push `main`. Push after each task's commits (authorised).
2. Before editing: `unity command editor_status` must answer (a hung Editor still shows `ready` in `unity status`).
3. **Never `run_tests` or `recompile` while in Play Mode** — stop play mode and confirm `editor_status` playMode `stopped` first. A timeout → stop and report, don't retry in a loop.
4. `unity command <name> -- --flag value` (note the `--`). C# via `unity command eval_file -- --file <path.cs>`; write `UnityEngine.Object`, not `Object`. Scratch files go in the session scratchpad.
5. `recompile_status` is the only authoritative compile check (poll until `completed`, `failed:false`, empty `errors`).
6. Prefab edits: `PrefabUtility.LoadPrefabContents` → edit → `SaveAsPrefabAsset` → `UnloadPrefabContents`, then read back. `add_component` on a prefab path silently does not persist. Asset edits: `SerializedObject` + `ApplyModifiedPropertiesWithoutUndo` + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets()`; `git diff` the asset.
7. Measure, don't calculate: armor + health (never health alone); time with game timestamps, not by polling once per interval; UI claims need a **rendered** check (screenshot at full size + the component values), because two code reviews wrongly declared the sprite-less bar fill "correct".
8. Every gameplay/visual value: `[SerializeField]` + plain-language `[Tooltip]`, one home. Comments explain *why*. The owner is a designer who reads this code.
9. Don't touch `Building capture.cs`, `BuildingManager.cs`, `RoomManager.cs`, `CameraTracking.cs`, `PlayerTeamAppearance.cs`, `Team Id.cs`, `PlayerLookup.cs`, or chat **scripts** (chat layout values in the scene/prefab are allowed in Task 5).
10. Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. No number in a commit message unless measured.
11. Join the Photon room in play mode before firing: see how previous harnesses did it (`RoomManager.JoinGame()` when connected and not in a room; poll `PhotonNetwork.InRoom`).

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Combat/AimConeState.cs` (modify) | Moving spread + moving bloom maths | 1 |
| `Assets/Tests/AimConeStateTests.cs` (modify) | Tests for the above | 1 |
| `Assets/scripts/Data/WeaponDefinition.cs` (modify) | `movingSpreadDegrees`, `movingBloomPerSecond`, `description`, invariant warning | 2, 4 |
| `Assets/scripts/Player/PlayerAim.cs` (modify) | `ConfigureCone` takes the two new values | 2 |
| `Assets/scripts/Weapons/WeaponFiring.cs` (modify) | Passes them on equip | 2 |
| 13 `Assets/Gameplay/Weapons/*.asset` (modify) | Starting values, short names, descriptions | 2, 4 |
| `Assets/scripts/UI/UiTheme.cs` + `Assets/Gameplay/Config/UiTheme.asset` (create) | Fonts, sizes, colours, outline, panel alpha, bar sprite, cone line style | 3 |
| `Assets/Gameplay/UI/WhitePixel.png` (create) | Sprite for every filled image | 3 |
| `Assets/scripts/UI/PlayerHud.cs` (modify) | Fill fix, order, readability, pulse | 3, 5 |
| `Assets/scripts/Data/AbilityDefinition.cs` (modify) | `description` (if missing) | 4 |
| 14 ability `Assets/Gameplay/Abilities/*.asset` (modify) | Short names, descriptions | 4 |
| `Assets/Scenes/Game Scene.unity` (modify, layout only) | Smaller chat | 5 |
| `Assets/scripts/Player/PlayerHealth.cs` + `Assets/Resources/Multiplayer Player.prefab` (modify) | Overhead bar option C | 6 |
| `Assets/scripts/Player/AimConeView.cs` (create) | Edge-line cone, owner-only | 7 |
| `Assets/scripts/Combat/WeaponUpgradeTree.cs` + `Assets/Tests/WeaponUpgradeTreeTests.cs` (create) | Pure upgrade-tree rules + cycle detection | 8 |
| Input actions asset (modify) + `Assets/scripts/Player/PlayerInputRouter.cs` (modify) | `Loadout` action on P | 9 |
| `Assets/scripts/UI/LoadoutScreen.cs` (create) | The P screen + HUD button | 9 |

---

### Task 1: Movement terms in `AimConeState` (pure, TDD)

**Files:**
- Modify: `Assets/scripts/Combat/AimConeState.cs`
- Test: `Assets/Tests/AimConeStateTests.cs`

The five-argument constructor must keep working unchanged — `WeaponFiring` (~line 424) builds a jitter-only cone with it.

- [ ] **Step 1: Write the failing tests** — append inside the existing `AimConeStateTests` class (keep its namespace and `using` lines; check the file's existing style first):

```csharp
        // Movement terms. Values chosen so every expected number is exact:
        // min 2, max 10, bloom 1, recovery 1, standing-still 1.5, moving spread 4, moving bloom 3.
        private static AimConeState MovingCone() =>
            new AimConeState(2f, 10f, 1f, 1f, 1.5f, 4f, 3f);

        [Test]
        public void MovingAddsTheFlatSpreadTheInstantMovementStarts()
        {
            var cone = MovingCone();
            cone.Tick(0f, true);
            Assert.AreEqual(6f, cone.EffectiveAngle, 1e-4f); // 2 current + 4 moving spread
        }

        [Test]
        public void StoppingRemovesTheFlatSpreadTheInstantMovementEnds()
        {
            var cone = MovingCone();
            cone.Tick(0f, true);
            cone.Tick(0f, false);
            Assert.AreEqual(2f / 1.5f, cone.EffectiveAngle, 1e-4f);
        }

        [Test]
        public void MovingBloomsAtItsRateMinusRecovery()
        {
            var cone = MovingCone();
            cone.Tick(1f, true);
            Assert.AreEqual(4f, cone.CurrentAngle, 1e-4f); // 2 + (3 - 1) * 1
        }

        [Test]
        public void MovingBloomNeverPassesMaxAngle()
        {
            var cone = MovingCone();
            cone.Tick(10f, true);
            Assert.AreEqual(10f, cone.CurrentAngle, 1e-4f);
            Assert.AreEqual(14f, cone.EffectiveAngle, 1e-4f); // max + moving spread
        }

        [Test]
        public void StandingStillRecoversBloomGainedWhileMoving()
        {
            var cone = MovingCone();
            cone.Tick(1f, true);   // 4
            cone.Tick(1f, false);  // 4 - 1 = 3
            Assert.AreEqual(3f, cone.CurrentAngle, 1e-4f);
        }

        [Test]
        public void FiveArgumentConeHasNoMovementPenalty()
        {
            var cone = new AimConeState(2f, 10f, 1f, 1f, 1.5f);
            cone.Tick(1f, true);
            Assert.AreEqual(2f, cone.CurrentAngle, 1e-4f);
            Assert.AreEqual(2f, cone.EffectiveAngle, 1e-4f);
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `unity command recompile` then poll `unity command recompile_status`.
Expected: compile error — no constructor takes 7 arguments.

- [ ] **Step 3: Implement** — in `AimConeState.cs`:

Add fields after `standingStillMultiplier`:

```csharp
        private readonly float movingSpreadDegrees;
        private readonly float movingBloomPerSecond;
```

Replace `EffectiveAngle` (keep its XML comment, add one sentence about the moving spread):

```csharp
        public float EffectiveAngle =>
            isMoving ? CurrentAngle + movingSpreadDegrees : CurrentAngle / standingStillMultiplier;
```

Replace the constructor with a seven-argument one plus a five-argument overload:

```csharp
        public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                            float recoveryPerSecond, float standingStillMultiplier)
            : this(minAngle, maxAngle, bloomPerShot, recoveryPerSecond, standingStillMultiplier, 0f, 0f)
        {
        }

        /// <param name="movingSpreadDegrees">Added to the spread the instant the owner moves, removed the instant they stop.</param>
        /// <param name="movingBloomPerSecond">How fast the cone widens while the owner keeps moving. Recovery still runs,
        /// so it only grows if this is larger than recoveryPerSecond.</param>
        public AimConeState(float minAngle, float maxAngle, float bloomPerShot,
                            float recoveryPerSecond, float standingStillMultiplier,
                            float movingSpreadDegrees, float movingBloomPerSecond)
        {
            this.minAngle = minAngle;
            this.maxAngle = maxAngle;
            this.bloomPerShot = bloomPerShot;
            this.recoveryPerSecond = recoveryPerSecond;
            this.standingStillMultiplier = standingStillMultiplier;
            this.movingSpreadDegrees = movingSpreadDegrees;
            this.movingBloomPerSecond = movingBloomPerSecond;

            CurrentAngle = minAngle;
            isMoving = false;
        }
```

Replace `Tick` (update its comment: recovery still runs while moving; moving bloom adds on top):

```csharp
        public void Tick(float deltaTime, bool isMoving)
        {
            float bloom = isMoving ? movingBloomPerSecond * deltaTime : 0f;
            CurrentAngle = Mathf.Clamp(CurrentAngle + bloom - recoveryPerSecond * deltaTime, minAngle, maxAngle);
            this.isMoving = isMoving;
        }
```

- [ ] **Step 4: Run tests** — stop play mode if running; `unity command run_tests -- --mode EditMode`.
Expected: all pass (346 + 6).

- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Combat/AimConeState.cs Assets/Tests/AimConeStateTests.cs
git commit -m "feat(combat): moving spread and moving bloom in the aim cone

Moving now costs accuracy: a flat spread the instant you move, plus bloom toward max
while you keep moving (Tudor's decision). The 5-argument cone is unchanged.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push origin limit-testing
```

---

### Task 2: Weapon fields, plumbing, starting values, measured

**Files:**
- Modify: `Assets/scripts/Data/WeaponDefinition.cs` (near `standingStillMultiplier` ~line 140; `OnValidate` ~line 215)
- Modify: `Assets/scripts/Player/PlayerAim.cs` (`ConfigureCone` ~line 67, `Awake` ~line 62)
- Modify: `Assets/scripts/Weapons/WeaponFiring.cs` (the `aim.ConfigureCone(...)` call ~line 227)
- Modify: 13 assets in `Assets/Gameplay/Weapons/`

- [ ] **Step 1: Add the fields** to `WeaponDefinition` directly after `standingStillMultiplier`:

```csharp
        [SerializeField, Tooltip("Degrees added to this weapon's spread the moment the player starts moving, " +
                                 "removed the moment they stop. Makes shooting on the move visibly less accurate.")]
        private float movingSpreadDegrees = 4f;
        public float MovingSpreadDegrees => movingSpreadDegrees;

        [SerializeField, Tooltip("While the player keeps moving, the spread widens by this many degrees per second " +
                                 "toward Max Cone Angle. Recovery Per Second still pulls it back, so this only " +
                                 "does anything when it is larger than Recovery Per Second.")]
        private float movingBloomPerSecond = 3f;
        public float MovingBloomPerSecond => movingBloomPerSecond;
```

- [ ] **Step 2: Add the invariant warning** at the end of `OnValidate` (follow the existing warning's wording style and `Debug.LogWarning(..., this)` form):

```csharp
            if (movingBloomPerSecond > 0f && movingBloomPerSecond <= recoveryPerSecond)
            {
                Debug.LogWarning(
                    $"{name}: Moving Bloom Per Second ({movingBloomPerSecond}) is not larger than Recovery Per Second " +
                    $"({recoveryPerSecond}), so moving never widens the cone - the bloom is decorative. Raise Moving " +
                    $"Bloom Per Second above {recoveryPerSecond}, or set it to 0 on purpose.", this);
            }
```

- [ ] **Step 3: Plumb it.** Extend `PlayerAim.ConfigureCone` with two parameters and its own two serialized fallback fields (same tooltips, defaults 0 so the player prefab's standalone cone is unchanged until a weapon configures it), and pass them into the seven-argument `AimConeState` in both `Awake` and `ConfigureCone`:

```csharp
    public void ConfigureCone(float min, float max, float bloom, float recovery, float standingStill,
                              float movingSpread, float movingBloom)
```

Update the single caller in `WeaponFiring`:

```csharp
            aim.ConfigureCone(weapon.MinConeAngle, weapon.MaxConeAngle, weapon.BloomPerShot,
                              weapon.RecoveryPerSecond, weapon.StandingStillMultiplier,
                              weapon.MovingSpreadDegrees, weapon.MovingBloomPerSecond);
```

Grep for any other `ConfigureCone(` caller and update it too.

- [ ] **Step 4: Recompile** and confirm `recompile_status` clean; run edit-mode tests (all pass).

- [ ] **Step 5: Set starting values [C]** on the 13 weapon assets via an `eval_file` using `SerializedObject` (`movingSpreadDegrees`, `movingBloomPerSecond`), then `AssetDatabase.SaveAssets()`:

| Ids | `movingSpreadDegrees` | `movingBloomPerSecond` |
|---|---|---|
| 1 | 4 | 3 |
| 2, 3, 4 | 8 | 5 |
| 5, 6, 7 | 4 | 3 |
| 8 | 3 | 4 |
| 9 | 4 | 8 |
| 10 | 2 | 2 |
| 11, 12, 13 | 2 | 2.5 |

For each asset, if `movingBloomPerSecond <= recoveryPerSecond`, raise it to `recoveryPerSecond + 1` and record the change. Report the final table. `git diff --stat Assets/Gameplay/Weapons` shows 13 assets.

- [ ] **Step 6: Measure** (play mode, joined room, equip weapon 1 via `PlayerLoadout.SetWeapon(1)`):
  - Read `PlayerAim.EffectiveConeAngle` standing still after recovery (expect `1.5 / 1.5 = 1.0`), the frame after movement starts (expect `1.5 + 4 = 5.5`), and after 2s of continuous movement (expect `min(7, 1.5 + (3 − 2) × 2) + 4 = 7.5`). To create movement, drive the motor the way previous harnesses did (scripted `Rigidbody.MovePosition` steps may not register as `IsMoving` — check how `PlayerMotor.IsMoving` is computed and use a path that sets it; say which).
  - Against a stationary full-health dummy 10m away: 20 baseline shots standing still, then 20 while strafing perpendicular — report hits for each (armor + health lost / damage per hit). Expect fewer hits moving.
  - Stop play mode.

- [ ] **Step 7: Commit** (code + assets), message listing the final value table and the measured angles and hit counts. Push.

---

### Task 3: `UiTheme`, a real bar sprite, and the fill bug

**Files:**
- Create: `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`, `Assets/Gameplay/UI/WhitePixel.png`
- Modify: `Assets/scripts/UI/PlayerHud.cs` (bar builders ~lines 599–730, cooldown cover ~872, ultimate fill ~918)
- Modify: `Assets/Resources/Multiplayer Player.prefab` (assign the theme to `PlayerHud`)

**Root cause (from the spec):** every `Image.Type.Filled` in `PlayerHud` has no sprite; Unity then ignores `fillAmount` and draws the full rect.

- [ ] **Step 1: Reproduce before fixing.** Play mode, joined room. Via eval, set local overheat to 40 (`PlayerOverheat` — find its add/set API) and read the overheat fill `Image`: report `sprite == null`, `type`, `fillAmount`, and capture a full-size game-view screenshot showing the bar at full width. Stop play mode.

- [ ] **Step 2: Create the sprite.** Via eval: create a 4×4 all-white `Texture2D`, `File.WriteAllBytes("Assets/Gameplay/UI/WhitePixel.png", tex.EncodeToPNG())`, `AssetDatabase.ImportAsset`, then set its `TextureImporter` to `textureType = Sprite`, `spriteImportMode = Single`, `filterMode = Point`, `SaveAndReimport()`. Read back that `AssetDatabase.LoadAssetAtPath<Sprite>` is non-null.

- [ ] **Step 3: Create `UiTheme`:**

```csharp
using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Every visual value the HUD, the loadout screen, the aim cone and the overhead bars share, in one asset so
    /// readability is tuned in one place. Presentation only - no gameplay number belongs here.
    /// </summary>
    [CreateAssetMenu(menuName = "Overpower/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Canvas")]
        [Tooltip("The screen size the UI is designed at. Text and panels scale from this to the real screen.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Tooltip("0 = scale with screen width, 1 = with height, 0.5 = a mix. 0.5 keeps ultrawide and 16:10 readable.")]
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;

        [Header("Text")]
        [Tooltip("Font for all UI text. Leave empty to use TextMeshPro's default font.")]
        public TMP_FontAsset font;
        [Tooltip("Size of slot key labels and bar labels.")] public float smallTextSize = 20f;
        [Tooltip("Size of ability and weapon names.")] public float bodyTextSize = 24f;
        [Tooltip("Size of screen titles.")] public float titleTextSize = 34f;
        [Tooltip("Main text colour.")] public Color textColor = Color.white;
        [Tooltip("Secondary text colour (descriptions, locked items).")] public Color mutedTextColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        [Tooltip("Dark outline around text so it stays readable over bright ground.")] public Color textOutlineColor = new Color(0f, 0f, 0f, 0.9f);
        [Tooltip("Outline thickness, 0 to 1. Around 0.2 reads well without looking bold.")] [Range(0f, 1f)] public float textOutlineWidth = 0.2f;

        [Header("Panels")]
        [Tooltip("Background behind HUD groups and the loadout screen.")] public Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        [Tooltip("Border / highlight for the equipped or selected item.")] public Color highlightColor = new Color(1f, 0.78f, 0.25f, 1f);
        [Tooltip("Colour of items you cannot pick yet.")] public Color lockedColor = new Color(1f, 1f, 1f, 0.25f);

        [Header("Bars")]
        [Tooltip("Plain white sprite every filled bar uses. Without a sprite Unity ignores the fill amount and draws the bar full.")]
        public Sprite barSprite;
        [Tooltip("Health fill.")] public Color healthColor = new Color(0.39f, 0.8f, 0.25f, 1f);
        [Tooltip("Shield fill.")] public Color shieldColor = new Color(0.25f, 0.6f, 1f, 1f);
        [Tooltip("Overheat fill below the warning threshold.")] public Color overheatColor = new Color(0.95f, 0.62f, 0.15f, 1f);
        [Tooltip("Overheat fill at or above the warning threshold.")] public Color overheatWarningColor = new Color(1f, 0.35f, 0.1f, 1f);
        [Tooltip("Overheat fill while silenced.")] public Color overheatSilencedColor = new Color(0.9f, 0.1f, 0.1f, 1f);
        [Tooltip("Empty part of every bar.")] public Color barTrackColor = new Color(0.18f, 0.18f, 0.2f, 1f);
        [Tooltip("Pulse the overheat bar while it is at the warning level.")] public bool pulseAtWarning = true;
        [Tooltip("Pulses per second when Pulse At Warning is on. Keep it slow - fast reads as flicker.")] public float pulseSpeed = 1.2f;

        [Header("Aim cone")]
        [Tooltip("Colour of the two lines showing where your shots can go.")] public Color coneLineColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("Colour of the shotgun's inner fan lines.")] public Color coneFanLineColor = new Color(1f, 1f, 1f, 0.25f);
        [Tooltip("Width of the cone lines in metres.")] public float coneLineWidth = 0.04f;
    }
}
```

(If `Assets/scripts/UI/` code uses a different namespace or none — match `PlayerHud.cs`.)

- [ ] **Step 4: Create the asset** via eval (`ScriptableObject.CreateInstance<UiTheme>()`, assign `barSprite` from Step 2, `AssetDatabase.CreateAsset(..., "Assets/Gameplay/Config/UiTheme.asset")`, `SaveAssets`).

- [ ] **Step 5: Wire `PlayerHud`.** Add `[SerializeField, Tooltip("Colours, text sizes and the bar sprite for this HUD.")] private UiTheme theme;`. Every `Image` built with `type = Image.Type.Filled` gets `sprite = theme.barSprite` **before** setting `type`. Replace the HUD's own colour/size fields with theme reads (delete those serialized fields from `PlayerHud`; values now have one home). Apply `theme.referenceResolution` / `matchWidthOrHeight` to its `CanvasScaler`. Log an error in `Awake` if `theme` or `theme.barSprite` is null. Assign the asset on `Multiplayer Player.prefab`'s `PlayerHud` (prefab-edit rule) and read back.

- [ ] **Step 6: Verify rendered fills.** Recompile; play mode; joined room. For overheat 0 / 40 / 80 / 100, shield 50% (`ApplyDamage` armor-able, source −1), health 30%: report each fill `Image.sprite != null`, `fillAmount`, and a full-size screenshot where the bar visibly matches. Cooldown cover: equip Dash, press twice, screenshot mid-recharge. Stop play mode; run tests.

- [ ] **Step 7: Commit + push** (theme script, asset, sprite + `.meta`, HUD, prefab). Tell Tudor to reload the player prefab.

---

### Task 4: Short names and one-line descriptions

**Files:**
- Modify: `Assets/scripts/Data/WeaponDefinition.cs`, `Assets/scripts/Data/AbilityDefinition.cs` (add `description` only if absent)
- Modify: 13 weapon assets, 14 ability assets (not 901–903)

- [ ] **Step 1: Add `description`** (if missing) to both definitions:

```csharp
        [SerializeField, TextArea(1, 3), Tooltip("One line shown when a player hovers this in the loadout screen. " +
                                                 "Say what it does, not its numbers - numbers are shown next to it automatically.")]
        private string description = "";
        public string Description => description;
```

- [ ] **Step 2: Set names and descriptions** via one `eval_file` (SerializedObject `displayName`, `description`; `SaveAssets`). Names [T]; descriptions [C]:

| Id | displayName | description |
|---|---|---|
| 1 | Baseline | Reliable single shots. Every path upgrades from here. |
| 2 | Rocket | Slow, heavy rockets that explode on impact. |
| 3 | Scaling | Rockets hit harder the further they travel. |
| 4 | Cursor | Rockets burst at your cursor and leave fire on the ground. |
| 5 | Burst | Three quick shots per pull. |
| 6 | Charge | Hold to charge up to five shots. |
| 7 | Bounce | Shots ricochet off walls and hit harder each bounce. |
| 8 | SMG | Fast, light, less accurate. |
| 9 | Rapid | Double fire rate, wide spread. Close range. |
| 10 | Shotgun | A wide blast of pellets. Deadly up close. |
| 11 | Laser | Instant beam that pierces every enemy in line. |
| 12 | Charge | Hold to charge for more range and damage. |
| 13 | X-Ray | The beam passes through walls. |
| 14 | Dash | Two quick hops toward your cursor. |
| 15 | Blink | Teleport to your cursor. |
| 16 | Sprint | Run faster, but it builds overheat. |
| 17 | Portal | Place two portals; stand in one to travel to the other. |
| 18 | Zip | Fire a hook that pulls you to what it hits. |
| 19 | Mines | Drop mines that damage and slow enemies. |
| 20 | Cover | Place a wall that blocks shots both ways. |
| 21 | Flame | Set enemies in front of you on fire. |
| 22 | Stun | A slow orb that stuns the first enemy hit. |
| 23 | Pulse | Knock enemies back; walls and allies stun them. |
| 24 | Raybeam | Three beams that make enemies take more damage. |
| 25 | Shield | Become invulnerable, but you can't act. |
| 26 | Fence | An electric ring that hurts and slows anyone crossing it. |
| 27 | Zone | A damaging aura that follows you. |

- [ ] **Step 3: Verify:** read back all 27 via eval; F1 panel and HUD show the short names (screenshot). Tests pass.

- [ ] **Step 4: Commit + push.**

---

### Task 5: HUD layout, readability, overheat pulse, smaller chat

**Files:**
- Modify: `Assets/scripts/UI/PlayerHud.cs`
- Modify: `Assets/Scenes/Game Scene.unity` (chat layout values only)

- [ ] **Step 1: Order.** In `BuildUi`, build the slots row first, then overheat, shield (the armor bar), health, top to bottom inside the bottom-centre panel. The silenced indicator stays an `ignoreLayout` overlay on the slots row. Keep the warning tick on the overheat bar.
- [ ] **Step 2: Text.** Every HUD text is `TextMeshProUGUI` using `theme.font` (when set), `theme.smallTextSize` / `bodyTextSize`, `theme.textColor`, and an outline (`fontMaterial` instance with `outlineWidth = theme.textOutlineWidth`, `outlineColor = theme.textOutlineColor`, or `TMP_Text.outlineWidth/outlineColor`). Panel backgrounds use `theme.panelColor`. Scale slot boxes so the key label and the short name both fit at `bodyTextSize`.
- [ ] **Step 3: Pulse.** Replace the warning flicker with `if (theme.pulseAtWarning)` a sine pulse on the fill colour between `overheatColor` and `overheatWarningColor` at `theme.pulseSpeed` Hz; otherwise a steady `overheatWarningColor`. Silenced uses `overheatSilencedColor`.
- [ ] **Step 4: Chat.** Find the chat UI in `Game Scene` (the objects `chatmanager.cs` references — read, don't edit, the script). Reduce its RectTransform width and height to about 60% and its font size to `theme.smallTextSize` equivalent; keep it bottom-left, clear of the HUD panel. Edit through the Editor (open scene, `SerializedObject`, save scene); report before/after sizes. No script edits.
- [ ] **Step 5: Verify.** Play mode at a 1920×1080 Game view: full-size screenshots — idle; heat 85 (pulsing, report colours at two sample times); silenced; chat open with a message typed. Report the HUD panel and chat rects (no overlap). Stop play mode; tests pass.
- [ ] **Step 6: Commit + push.** Tell Tudor to reload the scene.

---

### Task 6: Overhead bar — shield over health (option C)

**Files:**
- Modify: `Assets/scripts/Player/PlayerHealth.cs` (`armorBar` and `UpdateArmorBar`, added in Task 1.12a)
- Modify: `Assets/Resources/Multiplayer Player.prefab` (`HealthBarCanvas`)

- [ ] **Step 1:** In the prefab, make the shield fill a child drawn **after** (on top of) the health fill with the same rect; both `Image`s use `UiTheme.barSprite`, `Filled`, horizontal; health uses `theme.healthColor`, shield `theme.shieldColor`. Remove the separate armor strip added in 1.12a. Replace the `Slider`-based health bar with a filled `Image` if the slider can't draw over-layers cleanly (say which).
- [ ] **Step 2:** `PlayerHealth` updates both fills at every health/armor mutation it already calls `UpdateArmorBar` from, including `SetHealthFromNetwork` and `SetArmorLevels`: `health / maxHealth` and `armor / ArmorCapacity`. Add `[SerializeField, Tooltip("Shared colours and bar sprite.")] private UiTheme theme;` (assign on the prefab).
- [ ] **Step 3: Verify** (armor + health): a dummy isn't a player — use the local player's own overhead bar. Values: health 100 / shield 25 of 25 → shield fill 1.0 over health 1.0; `ApplyDamage` 40 armor-able → shield 0, health 85; screenshot each at full size and report both `fillAmount`s. Stop play mode; tests pass.
- [ ] **Step 4: Commit + push.** Tell Tudor to reload the player prefab.

---

### Task 7: `AimConeView` — the edge lines

**Files:**
- Create: `Assets/scripts/Player/AimConeView.cs`
- Modify: `Assets/Resources/Multiplayer Player.prefab` (add the component + two child `LineRenderer` objects, or build them in `Awake`)

- [ ] **Step 1: Implement.** Owner-only (`photonView.IsMine`; disable itself otherwise). Each `LateUpdate`:
  - hide if dead (`PlayerLifecycle`), if the loadout screen is open (Task 9 exposes a static `LoadoutScreen.IsOpen`; until then read `PlayerInputRouter`'s tool focus), or if no weapon is equipped;
  - `origin = weaponFiring.SafeMuzzlePosition`; `forward = aim.AimDirection` flattened;
  - `half = aim.EffectiveConeAngle / 2` (the same value the shot sampler reads — do not recompute it);
  - `range = weapon.MaxRange` (× `ChargeRangeMultiplier` × current charge fraction for charge weapons, if `WeaponFiring` exposes the fraction; otherwise uncharged range, and say so);
  - for each edge `dir = Quaternion.AngleAxis(±half, Vector3.up) * forward`; end = `Physics.Raycast(origin, dir, out hit, range, buildingMask, QueryTriggerInteraction.Ignore) ? hit.point : origin + dir * range`;
  - shotgun (`weapon.Simultaneous` and `weapon.SpreadDegrees > 0` — check the property names): two fainter inner lines at ±`SpreadDegrees / 2` using `theme.coneFanLineColor`.
  - Lines use `theme.coneLineColor`, `theme.coneLineWidth`, a shared unlit material (`Sprites/Default` shader), `useWorldSpace = true`, no shadows.
- [ ] **Step 2: Verify.** Play mode, weapon 1: read the drawn half-angle from the line endpoints (`Vector3.SignedAngle` of each edge vs forward) and `aim.EffectiveConeAngle / 2` in the same frame — standing, moving start, 2s moving (report both numbers each time, within 0.05°). Standing 2m from a `Wall_01` facing it: line endpoints stop at the wall face. Weapon 10 shows the inner pair. Full-size screenshot of standing vs moving. Stop play mode; tests pass.
- [ ] **Step 3: Commit + push.**

---

### Task 8: `WeaponUpgradeTree` (pure, TDD)

**Files:**
- Create: `Assets/scripts/Combat/WeaponUpgradeTree.cs`
- Test: `Assets/Tests/WeaponUpgradeTreeTests.cs`

Pure C#, no `UnityEngine` — built from `(id, parentId)` pairs so it tests without assets. `parentId` is `-1` for a weapon with no parent.

- [ ] **Step 1: Write the failing tests:**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class WeaponUpgradeTreeTests
    {
        // The real shape: 1 Baseline -> 2 Rocket, 5 Burst, 8 SMG, 11 Laser; each path -> its two leaves.
        private static WeaponUpgradeTree RealShape() => new WeaponUpgradeTree(new List<(int, int)>
        {
            (1, -1),
            (2, 1), (3, 2), (4, 2),
            (5, 1), (6, 5), (7, 5),
            (8, 1), (9, 8), (10, 8),
            (11, 1), (12, 11), (13, 11),
        });

        [Test]
        public void TheRootIsTheWeaponWithNoParent()
        {
            Assert.AreEqual(1, RealShape().RootId);
        }

        [Test]
        public void TheRootsChildrenAreTheFourPathsInIdOrder()
        {
            CollectionAssert.AreEqual(new[] { 2, 5, 8, 11 }, RealShape().ChildrenOf(1));
        }

        [Test]
        public void LeavesHaveNoChildren()
        {
            var tree = RealShape();
            Assert.IsTrue(tree.IsLeaf(3));
            Assert.IsFalse(tree.IsLeaf(2));
            Assert.IsFalse(tree.IsLeaf(1));
        }

        [Test]
        public void FromBaselineOnlyThePathsAreSelectable()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Equipped, tree.StateOf(1, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(2, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(3, equippedId: 1));
        }

        [Test]
        public void OnRocketItsLeavesUnlockAndOtherPathsLock()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(1, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(3, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(4, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(5, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(6, equippedId: 2));
        }

        [Test]
        public void OnALeafNothingIsSelectableAndItsAncestorsAreOwned()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Equipped, tree.StateOf(3, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(2, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(1, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(4, equippedId: 3));
        }

        [Test]
        public void CanUpgradeOnlyToADirectChild()
        {
            var tree = RealShape();
            Assert.IsTrue(tree.CanUpgrade(1, 2));
            Assert.IsTrue(tree.CanUpgrade(2, 3));
            Assert.IsFalse(tree.CanUpgrade(1, 3));
            Assert.IsFalse(tree.CanUpgrade(2, 5));
            Assert.IsFalse(tree.CanUpgrade(3, 2));
        }

        [Test]
        public void AnUnknownIdIsLockedAndCannotBeUpgradedTo()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(99, equippedId: 1));
            Assert.IsFalse(tree.CanUpgrade(1, 99));
        }

        [Test]
        public void AParentCycleIsReportedInsteadOfLoopingForever()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (1, -1), (2, 3), (3, 2) });
            Assert.IsNotEmpty(tree.Problems);
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(2, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(1, equippedId: 2));
        }

        [Test]
        public void TwoRootsAreReportedAndTheLowestIdIsUsed()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (4, -1), (1, -1), (2, 1) });
            Assert.IsNotEmpty(tree.Problems);
            Assert.AreEqual(1, tree.RootId);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — recompile; expected: `WeaponUpgradeTree` / `UpgradeNodeState` not found.

- [ ] **Step 3: Implement:**

```csharp
using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>How one weapon node looks from the player's current weapon.</summary>
    public enum UpgradeNodeState
    {
        /// <summary>The weapon the player is holding.</summary>
        Equipped,
        /// <summary>A direct upgrade from the equipped weapon - clickable now.</summary>
        Selectable,
        /// <summary>A weapon the player already came through on the way to the equipped one.</summary>
        Owned,
        /// <summary>Not reachable from here without resetting.</summary>
        Locked,
    }

    /// <summary>
    /// The rules of the weapon upgrade tree, kept apart from any UI so the loadout screen now and the shop later ask
    /// the same questions. Built from weapon ids and their parent ids (WeaponDefinition.Parent), so the tree grows
    /// automatically when a designer adds a weapon asset with a parent. Pure C# so it is unit tested.
    /// </summary>
    public sealed class WeaponUpgradeTree
    {
        private readonly Dictionary<int, int> parentOf = new Dictionary<int, int>();
        private readonly Dictionary<int, List<int>> childrenOf = new Dictionary<int, List<int>>();
        private readonly HashSet<int> inCycle = new HashSet<int>();
        private readonly List<string> problems = new List<string>();
        private static readonly IReadOnlyList<int> None = new List<int>();

        /// <summary>The starting weapon everyone begins with; -1 if the data has no root.</summary>
        public int RootId { get; } = -1;

        /// <summary>Data mistakes a designer should fix (cycles, several roots, missing parents). Empty when the tree is sound.</summary>
        public IReadOnlyList<string> Problems => problems;

        public WeaponUpgradeTree(IEnumerable<(int id, int parentId)> nodes)
        {
            foreach (var (id, parentId) in nodes)
                parentOf[id] = parentId;

            var roots = new List<int>();
            foreach (var pair in parentOf)
            {
                if (pair.Value < 0) { roots.Add(pair.Key); continue; }
                if (!parentOf.ContainsKey(pair.Value))
                {
                    problems.Add($"Weapon {pair.Key} has parent {pair.Value}, which is not in the catalogue.");
                    continue;
                }
                if (!childrenOf.TryGetValue(pair.Value, out var list))
                    childrenOf[pair.Value] = list = new List<int>();
                list.Add(pair.Key);
            }
            foreach (var list in childrenOf.Values) list.Sort();

            roots.Sort();
            if (roots.Count == 0) problems.Add("No weapon without a parent - there is no starting weapon.");
            if (roots.Count > 1) problems.Add($"{roots.Count} weapons have no parent; using the lowest id ({roots[0]}) as the start.");
            if (roots.Count > 0) RootId = roots[0];

            // A parent chain longer than the number of weapons can only be a loop (A -> B -> A).
            foreach (int id in parentOf.Keys)
            {
                int steps = 0, current = id;
                while (current >= 0 && parentOf.TryGetValue(current, out int parent) && steps <= parentOf.Count)
                {
                    current = parent;
                    steps++;
                }
                if (steps > parentOf.Count)
                {
                    inCycle.Add(id);
                    problems.Add($"Weapon {id} is part of a parent loop - its Parent chain never reaches the starting weapon.");
                }
            }
        }

        public IReadOnlyList<int> ChildrenOf(int id) =>
            childrenOf.TryGetValue(id, out var list) ? list : None;

        public bool IsLeaf(int id) => parentOf.ContainsKey(id) && ChildrenOf(id).Count == 0;

        /// <summary>True only for a direct child of the given weapon - upgrades go one step at a time.</summary>
        public bool CanUpgrade(int fromId, int toId) =>
            !inCycle.Contains(toId) && parentOf.TryGetValue(toId, out int parent) && parent == fromId && parent >= 0;

        public UpgradeNodeState StateOf(int nodeId, int equippedId)
        {
            if (!parentOf.ContainsKey(nodeId) || inCycle.Contains(nodeId) || inCycle.Contains(equippedId))
                return UpgradeNodeState.Locked;
            if (nodeId == equippedId) return UpgradeNodeState.Equipped;
            if (CanUpgrade(equippedId, nodeId)) return UpgradeNodeState.Selectable;
            if (IsAncestorOf(nodeId, equippedId)) return UpgradeNodeState.Owned;
            return UpgradeNodeState.Locked;
        }

        private bool IsAncestorOf(int ancestorId, int id)
        {
            int current = id, steps = 0;
            while (parentOf.TryGetValue(current, out int parent) && parent >= 0 && steps++ <= parentOf.Count)
            {
                if (parent == ancestorId) return true;
                current = parent;
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests** — all pass (existing + 10).
- [ ] **Step 5: Commit + push.** Mention it closes the Task 0.8 open item (parent cycles now reported).

---

### Task 9: Loadout screen on P and the HUD button

**Files:**
- Modify: the project's input actions asset (find it: `grep -rl "Mobility" Assets --include=*.inputactions`; the 1.0b review named `OverpowerControls.inputactions`) and `Assets/scripts/Player/PlayerInputRouter.cs`
- Create: `Assets/scripts/UI/LoadoutScreen.cs`
- Modify: `Assets/Resources/Multiplayer Player.prefab` (add `LoadoutScreen` to the root, assign `UiTheme`, catalogues)
- Modify: `Assets/scripts/Player/AimConeView.cs` (read `LoadoutScreen.IsOpen`)

This task is UI construction in code; it follows the patterns in `PlayerHud.cs` (canvas + `CanvasScaler` from theme, owner-only build in `Awake`) and `TestRangePanel.cs` (buttons, dropdown wiring, `PlayerInputRouter.SetToolFocus` to suppress gameplay input). Read both before writing.

- [ ] **Step 1: Input.** Add a `Loadout` button action bound to `<Keyboard>/p` in the gameplay map. In `PlayerInputRouter` add `public event System.Action LoadoutPressed;` raised on `performed` — **not** gated by `InputSuppressed` (the key must also close the screen) but ignored while typing in chat. Mirror how existing pressed events are wired and unwired.

- [ ] **Step 2: Screen skeleton.** `LoadoutScreen : MonoBehaviourPun`, owner-only:
  - Serialized: `UiTheme theme`, `WeaponCatalogue weapons`, `AbilityCatalogue abilities`, `ArmorConfig armorConfig` (tooltips).
  - `public static bool IsOpen { get; private set; }` (only the local player's screen sets it).
  - `Awake` (if `IsMine`): build a screen-space overlay canvas (sort order above the HUD, below `MatchUI`), `CanvasScaler` from theme, a full-screen dim, the centre panel (theme panel colour), a title "Loadout" and a close X; start hidden. Build a bottom-right HUD button "Loadout (P)" on a separate always-visible canvas.
  - `Open()` / `Close()` / `Toggle()`: show/hide, set `IsOpen`, `inputRouter.SetToolFocus(true/false)` (read its signature), refresh all states. Subscribe `LoadoutPressed → Toggle`; Esc closes (poll `Keyboard.current.escapeKey.wasPressedThisFrame` while open); the X and the HUD button call `Toggle`.
  - Close automatically on death (`PlayerLifecycle.AliveChanged(false)`) and in `OnDisable`, releasing tool focus.

- [ ] **Step 3: Weapon tree (left column).** Build `WeaponUpgradeTree` from `weapons.Weapons` → `(w.Id, w.Parent != null ? w.Parent.Id : -1)`. If `tree.Problems` is non-empty, `Debug.LogError` each once. Layout: root node centred; its children in a row (id order); each child's children stacked beneath it. Each node is a button showing `displayName`. `Refresh()` colours every node by `tree.StateOf(node.Id, loadout's equipped weapon id)` — Equipped: `theme.highlightColor` border; Selectable: normal; Owned: muted fill; Locked: `theme.lockedColor`, not interactable. Click → only if `tree.CanUpgrade(equipped, node)` → `playerLoadout.SetWeapon(node.Id)` → `Refresh()`. A **Reset weapon** button → `playerLoadout.SetWeapon(tree.RootId)`. (Find how to read the equipped weapon id: `WeaponFiring.Weapon.Id` or `AbilityRunner`/`PlayerLoadout` — use whichever `PlayerLoadout` treats as the source.)

- [ ] **Step 4: Armor (under the tree).** "Absorb {a}/{max}" with a + button, "Recharge {r}/{max}" with a + button, "Reset". Reuse the exact rules `TestRangePanel`'s +Absorb/+Recharge/Reset use (`ArmorUpgradePath`, `ArmorConfig.maxArmorUpgrades`, `PlayerLoadout.SetArmorLevels`) — call the same code path rather than re-deriving it; if that logic lives inside `TestRangePanel`, move it to a small shared static helper and have both call it (say so).

- [ ] **Step 5: Abilities (right column).** For each slot in order Mobility (Shift), Equipment (RMB), Ultimate (Space): a heading with the key, then a wrapping row of cards from `abilities.Abilities` where `Slot` matches and `Id < 900` (debug abilities stay in F1). Click → `playerLoadout.SetAbility(slot, id)` → `Refresh()`; the equipped card (`AbilityRunner.EquippedId(slot)`) gets the highlight border. Subscribe `AbilityRunner.SlotChanged → Refresh`.

- [ ] **Step 6: Hover text.** A single description panel at the bottom of the screen. Pointer-enter on a card (an `EventTrigger` or a tiny `IPointerEnterHandler` component) shows `displayName`, `Description`, and numbers read live: weapon — `Damage`, `FireInterval`, `MaxRange`, `OverheatPerShot` (check exact property names); ability — the module prefab's `AbilityModule` cooldown and charges (read via the same accessor the integrity sweep used: `SerializedObject` is editor-only, so use the module's public `IAbilityStatus`-style properties or add read-only `CooldownSeconds`/`Charges` getters to `AbilityModule` if absent). Pointer-exit clears it.

- [ ] **Step 7: Wire the prefab** (prefab-edit rule): add `LoadoutScreen` to `Multiplayer Player.prefab` root; assign theme, both catalogues, armor config; read back. Make `AimConeView` hide while `LoadoutScreen.IsOpen`. Recompile clean; tests pass.

- [ ] **Step 8: Verify** (play mode, joined room, 1920×1080 Game view, full-size screenshots):
  1. `LoadoutPressed` (invoke the router's handler or the action) opens it; again closes it; the HUD button opens it; Esc closes it. While open: `PlayerInputRouter.InputSuppressed == true`, and the aim cone lines are hidden.
  2. From Baseline: Rocket is Selectable, Scaling Locked. Click Rocket → equipped weapon id 2 (`WeaponFiring.Weapon.Id` and the `weaponId` Custom Property after one frame), Scaling/Cursor Selectable, Burst Locked (clicking it changes nothing). Click Scaling → id 3; nothing Selectable. Reset → id 1.
  3. Each ability card in each slot equips (HUD slot shows the short name); the debug abilities are absent.
  4. Armor: +Absorb twice → 2/2 and a third press refused at `maxArmorUpgrades` 2; Reset → 0/0.
  5. Hover a weapon and an ability → description and numbers match the assets.
  6. Death while open → closes, input un-suppressed after respawn.
  Stop play mode; tests pass.

- [ ] **Step 9: Commit in stages** (input; screen + tree; abilities + armor; hover; prefab) and push. Tell Tudor to reload the player prefab.

---

### Task 10: Wrap-up

- [ ] **Step 1:** Controller check: `editor_status`, `recompile_status`, all edit-mode tests, `git status` clean, 0 unpushed.
- [ ] **Step 2:** Re-run the Phase 1 smoke script (all 13 weapons fired, all 14 abilities cast, 0 errors) — see `progress.md` "PHASE 1 COMPLETE" for what it does; it lives in the old session scratchpad, so recreate it if missing.
- [ ] **Step 3:** Append a "Playtest polish" section to `Resources/loops/Limit Test/progress.md` (commits, measured values, the final moving-spread table [C], any deviations) and update `HANDOFF.md` §3/§4: polish done → Tudor tests every weapon and ability via P → then Phase 2.
- [ ] **Step 4:** Push notification to Tudor: polish ready to test.

---

## Self-review against the spec

| Spec requirement | Task |
|---|---|
| Moving spread + moving bloom, invariant warning, starting values | 1, 2 |
| Cone follows the equipped weapon | 2 (confirmed: `WeaponFiring` → `PlayerAim.ConfigureCone`) |
| Edge-line cone, owner-only, wall-clipped, shotgun fan | 7 |
| Fill bug fixed on every filled image, verified rendered | 3 |
| `UiTheme`, readability, 1920×1080 scaler | 3, 5 |
| HUD order slots → overheat → shield → health; pulse toggle | 5 |
| Short names + descriptions, one home | 4 |
| Smaller chat, no chat script edits | 5 |
| Overhead shield over health, remote-correct | 6 |
| P + HUD button, input suppression, Esc/X | 9 |
| Real tree + free reset, locked nodes, cycle detection | 8, 9 |
| Abilities per slot, debug hidden; armor buttons | 9 |
| Hover description + live numbers | 9 |
| No new RPCs; `PlayerLoadout` for every choice | 9 |
| Measured verification + progress/handoff | every task, 10 |
