# Arena Rebuild, Amendment 1: Jersey Barriers and Closed Wall Corners

> Read `docs/superpowers/plans/2026-09-18-arena-rebuild.md` (the **base plan**) first; this amends it. Same rules, same
> naming ("arena step N"), same tech. One new step (**4a**), and steps 4, 5, 6, 7, 8 amended. Steps 0-2 are untouched
> (their brief already records the barrier site), and nothing here contradicts Tudor's column answer.
>
> **Provenance:** a read-only opus planner, 2026-09-19. Every `file:line` and every measured number was read at HEAD
> **`795a701`** (scene YAML parsed by script; nothing run in the Editor). `57eb849` landed while this was written; it
> touches none of the cited lines (`DummyTarget.cs:204,297` re-checked). **Re-read before editing** (line drift: the
> base plan's `sourceOutline` at scene `:26198-26206` is now `:26163-26171`).

## Tudor's answers to Q9-Q13 (2026-09-19, ~03:10). THESE OVERRIDE the Decisions and step text below

All five are the defaults, so no step changes; the implementer still says in its report how each was applied.

| # | Question | Answer [T] |
|---|---|---|
| 9 | Barrier height | "waist high" (the default) |
| 10 | Can a Sonic Pulse shove someone over it? | "no" (the default: a shoved player stops against it like a wall) |
| 11 | A dash that runs out on top of it | "default is good" (land on the nearer side) |
| 12 | The two narrow ways past the planks | "leave them open" (the default) |
| 13 | Does sprint hop it? | "sprinting does not hop the barrier" (the default) |

Also [T], earlier the same night: the barrier's placement drawing ("yes this is perfect"), and the base plan's eight
answers (the capital gets 1 big column, tiers II-IV 2/3/4 smaller ones; round towers of the same footprint; a neutral
floor picked for visibility; the old art switched off until he has played the new arena).

## Tudor's words (2026-09-19)
> "i would also implement the jersey barrier in this remade arena. i would cover the entrance to the tier 3 zone from
> the pocket" (his drawing: "yes this is perfect", `Resources/loops/Limit Test/captures/arena-jersey-barrier-placement.png`)
>
> "if you could also make sure that in this arena the walls dont have an empty space at the corners"

**GDD p.29** (`gdd-match-loop-facts.md:14`): "tall walls block movement + projectiles (unless a purchased upgrade),
crossable by leap/blink but not dash; jersey barriers block movement only, projectiles pass, crossable by any movement
option". There is no leap in the code; the movement options are Dash 14, Blink 15, Sprint 16, Teleport (portals) 17 and
Zip Gun 18.

**A name clash to avoid:** Tudor's "pocket" is the **Tier III recess**, whose scene walls are named `Flank Side A/B` and
`Flank Back ±7m/0m`. The scene walls named `Pocket R/L/Back` are the **capital enclosure**. Reports say "recess".

---

## What exists (measured at `795a701`)

### A. Today's boundary leaves 12 notches, at inward-pointing corners only
The Source outline (scene `:26163-26171`, ArenaSymmetry's `sourceOutline`): P0 (96.184, 60.034), P1 (98.782, 61.534),
P2 (89.012, 78.456), P3 (86.414, 76.956), P4 (75.451, 95.944), P5 (75.451, 121.401), P6 (54.649, 121.401),
P7 (54.649, 95.944). `ArenaBounds.FromSourceOutline` (`ArenaBounds.cs:28-46`) appends the 120° and 240° turns. So
Source edge 7 runs P7 → the 120° third's P0, and **each third's P0 is a seam** (the 240° third's last edge arrives there).

- The walls in `Source/Boundry` are 22 plain objects. Each wall's inner face lies on the outline within 1 mm. They are
  0.724 m thick and span y **0.06-6.04 m**, so they float 6 cm above a y 0 floor.
- Lengths: `Flank Side` 3.0, `Flank Back` and `Wall` 7.0, `Pocket R/L` 6.498, `Pocket Back` 7.421. So "7 × 5.98 × 0.72" was
  only approximate.
- Outward corners (90°: P1, P2, P5, P6) are closed today: both pieces run 0.73 m past the corner.
- The inward corners are not:

| Corner | Where | Interior angle | Pieces meeting there | Today |
|---|---|---|---|---|
| **P0** | Tier III recess mouth, **seam** | 270° | 240° third's copy of `Wall L 14m` + Source `Flank Side A` | each stops **0.73 m** short |
| **P3** | Tier III recess mouth | 270° | `Flank Side B` + `Wall R 14m` | each stops **0.73 m** short |
| P4 | capital enclosure mouth | 210° | `Wall R 28m` + `Pocket R 0` | each stops 0.196 m short |
| P7 | capital enclosure mouth | 210° | `Pocket L 0` + `Wall L 28m` | each stops 0.196 m short |

What the notches are:
- **Tudor's hole:** a 0.73 × 0.73 m square notch, open to the sky, at each recess mouth corner. Three of the six are seams.
- Through each notch runs a diagonal slit about 6 mm wide (0.730 m of shortfall against 0.724 m of thickness).
- The base plan's Decision 3 captures walls box for box, so it would carry all 12 notches into the new arena. On a flat
  floor it would also add a 6 cm crack under every wall.

### B. The barrier site (the Source recess, behind Tier III zone 4 at (82.75, 63.56))
- **The mouth line** runs P0 → P3 (19.54 m). It continues the outer wall's inner face. The recess behind it is 3 m deep.
- **The "planks"** are the `House_06L` houses, each box 2.61 × 5.73 × 7.18 m:
  - `Source/Buildings/House_06L (4)`: collider centre (85.38, 72.77), yaw 58.4°;
  - the **240° third's copy** of Source `House_06L (6)` (Source (44.17, 71.98), yaw −61.0°): collider centre ≈ (91.54, 62.05).
  - So each recess's two planks come from **two different thirds**.
- **Along the mouth line (metres from P0):**
  - the plank copy covers 2.70-5.43 and `House_06L (4)` covers 15.03-17.84;
  - so **the gap between the planks is ≈ 9.6 m**, and **the side lanes are ≈ 2.7 m (P0 side) and ≈ 1.7 m (P3 side)**;
  - a player is 1.4 m wide (capsule radius 0.7, `Multiplayer Player.prefab:3039+`), so both lanes stay walkable. The drawing shows them too (Open #12).
- **The planks run** from 6.6 m inside the line to **0.60-0.64 m past it**, into the recess.
- **Zone 4's tower** is 9.87 m from the mouth line, almost centred on the gap. Its ring (`captureRadius` 10; the trigger is
  `captureRadius − bodyRadius`, `Building capture.cs:155`) runs along the barrier's middle.
- **Expected barrier row:**
  - centre ≈ (91.3, 69.0), Unity yaw −120° (local +Z faces the arena), length ≈ 9.8 m;
  - its copies: ≈ (65.5, 22.7) at yaw 0° (the bottom recess, zone 3) and the 120° turn (zone 5).
  - Step 0's `# jersey barrier site` measures the real numbers. **Measure, don't trust these.**

### C. Every movement option, and what a new "Barrier" layer does to it with no code change
The player: dynamic Rigidbody, interpolated, ContinuousDynamic, `excludeLayers` 0 (`Multiplayer Player.prefab:2989-3014`), on Default. Fixed step 0.02 s.

| Option | How it moves the body | Unchanged code + a Barrier layer | Needs |
|---|---|---|---|
| Walking (and Sprint, a speed multiplier) | `rb.MovePosition`, unswept, 0.1 m/step (`PlayerMotor.cs:298-308`) | Stopped by physics contact | nothing |
| Dash (3 m at 18 m/s, `Dash.prefab:50-51`), Zip pull (25 m/s, `ZipGunAbility.cs:65,250`) | `DisplaceVoluntary` → capsule sweep on `blockMask` Default+Building (`PlayerDisplacement.cs:77,130,311,315`), then `MovePosition` | The sweep doesn't see it, but the body's contact solver fights it: it stalls or pops out on a random side | **exclude the layer during the move; end clear** |
| Knockback (Sonic Pulse, `SonicPulseAbility.cs:169`) | `Displace` (Forced), same sweep | Same fight | **add the layer to the Forced sweep** (stops like a wall, Open #10) |
| Blink (range 9) | `TeleportTo` after `GroundProbe` (Default+Building) and `IsCapsuleBlocked` (Default+Building) (`BlinkAbility.cs:79,121-130`); walks back toward the caster on a refusal (`BlinkDestinationSearch.cs:61-92`) | **Can land inside a barrier** (the ground ray finds the floor under it) | the fit check sees barriers |
| Portals | Placement: `OverlapBox` on Default+Building (`TeleportAbility.cs:466-481`); path: `PathCrossesBoundary`, Boundry only (`:202`); exit: `IsCapsuleBlocked`, Building only (`:434-442`) | **Can be placed on a barrier and arrive inside it**; crossing is already allowed | placement and exit checks see barriers |
| Remote copies | Lerp by `rb.MovePosition` on a dynamic body (`PlayerMotor.cs:197-199`); snap only above 3 m per update (`:38`) | **Stall at the barrier** while their owner dashes over it: a desync on screen | **remote copies always ignore the layer** |
| Respawn, safety-net return | `TeleportTo` / `rb.position` (`PlayerLifecycle.cs:716-725`); the safe spot is remembered when grounded (`PlayerMotor.cs:233`) | A spot mid-crossing could be remembered, then returned to | **never remember a spot on a barrier** |
| Corpse | Collider off, kinematic (`PlayerLifecycle.cs:786-800`) | No contact | nothing |
| Test-range dummy knockback | `CapsuleCastAll` on Default+Building (`DummyTarget.cs:204,297`) | Pushed through | add the layer |

### D. Shots, beams, splash, aim and placement against a Barrier layer
- **Already ignore it (no change):**
  - every shot prefab with `hitMask` 9: the Baseline, Burst ×2, SMG, Rocket ×3 and Zip projectiles (`ProjectileMotor.cs:179-180`; projectiles have no collider or Rigidbody, `:8-9`) and both Laser hitscans (`Hitscan.cs:100-102`);
  - X-Ray: 9 minus Building (`IgnoreWalls.cs:32-36`);
  - the laser warning line, through the same mask (`Hitscan.cs:237,242-248`);
  - rocket splash: `splashMask` 1 on all three rocket prefabs, occlusion Building-only (`ExplodeOnImpact.cs:111-112`);
  - `SafeMuzzlePosition` (`WeaponFiring.cs:188-189,206`) and the aim cone (`AimConeView.cs:78,301`);
  - the flamethrower and sonic-pulse occlusion, Building-only (`FlamethrowerAbility.cs:282`, `SonicPulseAbility.cs:184`);
  - mine, AoE and fence detection: `~0` overlaps that keep only `IDamageable`s (`Mine.cs:281-289`, `AoeZone.cs:159`, `ElectricFence.cs:163`);
  - the fire field (`burnMask` 1).
- **Would stop on it (must change):**
  - **Stun Gun Bullet** (`hitMask` 4294967295, `Stun Gun Bullet.prefab`);
  - **Raybeam** (`hitMask` ~0, `Raybeam.prefab`, `RaybeamAbility.cs:234-235,267`).
  - Both pass through `HitMasks.StripNonNegotiableLayers` (`HitMasks.cs:17-20`), so one line there covers every shot now and later.
- **Placement:**
  - deployable cover `CheckBox` on Default+Building (`DeployableCoverAbility.cs:55,117`) would overlap a barrier;
  - a mine's landing (`MineAbility.cs:166-171`) could sit inside one, hidden in the concrete;
  - both need the layer. `PlayerSpaceProbe.IsPathClear` (Building-only, `:68-79`) rightly lets a mine be thrown over.

### E. Layers and the matrix
- **`TagManager.asset`:** user layers 8-31 are free. **Layer 8 is `ProjectSettings/TagManager.asset:17`** (`  - `).
- **`DynamicsManager.asset:20`**, `m_LayerCollisionMatrix` decoded: only Building×DeadPlayer and Bullet×Bullet are off.
  - **Row 8 is already all-on**, so Default (players) × Barrier collides with **no matrix edit**.
  - DeadPlayer and Bullet never touch it (corpses have no collider; bullets have neither collider nor body).

### F. The symmetry tool, the minimap, networking
- **Rebuild thirds** copies Source's groups and turns them (`ArenaSymmetryBuilder.cs:55-86`).
  - A `Barriers` group is copied like any other.
  - **Keep barriers out of `Boundry`:**
    - `PathCrossesBoundary` counts Boundry hits only (`ArenaSymmetry.cs:109-146`), and it casts on Building only (`:120`);
    - `Validate` checks every Boundry box's +Z face midpoint against the outline, within 0.15 m (`ArenaSymmetryBuilder.cs:160-191`, face `:184`).
  - A networked barrier is refused outright (`:215-241`).
- **The minimap bake** renders every layer (`TopDownRender.cs:24-36`, default culling mask), so barriers show up.
  - At 1024 px over 198.6 m (`MinimapConfig.asset:17-18`), a 0.6 m barrier is about 3 px wide.
  - Framing reads MeshRenderer corners (`MinimapBaker.cs:163-176`); low barriers never set it.
- **Networking: nothing new is needed.**
  - Barriers are static scene geometry, identical on every client, with no PhotonView.
  - Movement is owner-simulated; `PlayerDisplacement` is owner-only (`:28-31`).
  - Hits are victim-side, and the masks are code constants, the same on every client.
  - **The one desync** is C's remote-copy stall, fixed locally.
  - Settle nudges are ≤ ~1 m, below the 3 m snap, so other screens see a glide.
  - No RPC, no property, `PhotonServerSettings` unchanged.

---

## Decisions (amending the base plan) [C]

- **D3 amended: boundary walls come from the outline, not the capture.** Blocks (houses, crates, rocks, plants) stay box for box.
  - `ArenaWallPlan` (pure) makes **one straight box per outline edge**, 24 in all (8 per third).
  - Each box's inner face runs exactly corner to corner, and the box stands outside the outline.
  - **At an outward corner** the arriving wall runs on by `thickness × tan(turn/2)` (= 0.724 m at these 90° corners), which fills the notch behind the corner.
  - **At an inward corner** the two walls already overlap behind the corner, so there is no extension (an extension there would poke into the play space).
  - A straight joint gets 1 cm of overlap.
  - Seams are ordinary edges: the Source's last edge ends on the 120° third's P0, so all three thirds close on each other.
  - Every wall is a box from **y −1 to y 6.036** (today's top, buried bottom).
  - **Why this and not captured walls plus corner posts:**
    - The outline is already the one home of the wall line: Validate pins walls to it, and today's walls sit on it within 1 mm.
    - Captured pieces would need per-corner special cases.
    - A post under `Boundry` must also pass Validate's inner-face rule.
    - One box per run also removes the 14 in-line seams per third, whose vertical edges can tilt a Bounce shot sideways.
  - **Same map:** the lines are identical; only the corners close and the walls reach the floor.
- **D11 amended:** every solid piece is on Building **except the barriers, which sit on the new `Barrier` layer (8)**.
  - Only a living player's body collides with that layer: it is in no shot, splash, aim, occlusion or ground mask.
- **D18 amended:** a sixth material, `Arena Barrier` (URP Lit, base 0.86/0.64/0.26 amber [C], tuned in step 7).
  - It must read against the floor, walls, blocks, all three team colours and the warning red (1, 0.2, 0.15, `UiTheme.asset:140`).
- **D21: one home for layer facts, `ArenaLayers`** (`Overpower.Arena`, static, lazily cached like `GroundSnap.cs:25-31`).
  - It holds `BarrierLayerName`, `Barrier`, `WallsAndBarriers` (Building|Barrier) and `BodiesWallsAndBarriers` (Default|Building|Barrier).
  - It logs once and returns 0 if the layer is missing.
- **D22: shots pass by invariant.** `HitMasks.StripNonNegotiableLayers` also strips Barrier: a barrier can never be ticked into a shot.
- **D23: movement.**
  - **Stopped:** walking (and Sprint, Open #13) by physics contact; knockback by adding Barrier to the Forced sweep.
  - **Crossing (dash and zip pull):**
    - they sweep on Default+Building as today;
    - while one runs, the body **excludes the Barrier layer** (`rb.excludeLayers`);
    - when it ends overlapping a barrier, the body is moved out through the barrier's long face on the side its centre is already on (`BarrierCrossingRule`); exactly on the middle line, the side it was heading. If that side doesn't fit, the other side; then the move's start point;
    - the body is released (exclusion off, walking back) only once it is clear.
  - **Blink and portals** cross by construction. Their fit checks now see barriers, so they never land inside one; a blink aimed into one lands on the near side (walk-back).
  - **Remote copies** always exclude the layer.
  - **The safe spot** is never remembered on a barrier.
- **D24: the look and the blocker are separate.**
  - The look is one cube, **1.0 m tall** (waist, Open #9) and **0.6 m thick** (never under 0.4 m: walking is unswept).
  - Its BoxCollider spans **y −1 to 3 m**, taller than it looks. Only bodies use the layer, so the extra height costs nothing, and at any drawn height nobody can be lifted onto it.
  - Both bands live in `ArenaLayout`.
- **D25: the site** is one `Barrier` row in `ArenaLayout.asset`, for the Source recess, and Rebuild makes the other two.
  - The barrier's arena-side face lies on the mouth line ("level with the outer wall line").
  - Each end runs 0.10 m into a plank.
  - If a plank does not reach 0.6 m past the line, the barrier shifts arena-ward until both ends overlap; the shift is reported.

---

## Step 4a: the Barrier layer and the crossing rules (new; code only, scene unchanged)

Runs after step 2 and **before step 4** (independent of step 3). Start from a clean tree, the lock taken, and the dirty
check False. **Do the layer first, before any Play Mode.**

**Files:**
- **New:** `Assets/scripts/Arena/ArenaLayers.cs`, `Assets/scripts/Arena/BoxFootprint.cs`, `Assets/scripts/Combat/BarrierCrossingRule.cs`, `Assets/Tests/HitMasksTests.cs`, `Assets/Tests/BarrierCrossingRuleTests.cs`.
- **Modified:** `ProjectSettings/TagManager.asset`, `HitMasks.cs`, `PlayerDisplacement.cs`, `PlayerSpaceProbe.cs`, `PlayerMotor.cs`, `BlinkAbility.cs`, `TeleportAbility.cs`, `DeployableCoverAbility.cs`, `MineAbility.cs`, `DummyTarget.cs`.

- [ ] **1. Red** (+8). They must fail: the layer is missing, and compilation fails with `CS0246 … 'BarrierCrossingRule'`. Report both verbatim.
  - **`HitMasksTests`:**
    1. `TheBarrierLayerExistsPlayersCollideWithItAndArenaLayersNameIt`: `NameToLayer("Barrier") >= 0`; `!Physics.GetIgnoreLayerCollision(Default, Barrier)`; `ArenaLayers.Barrier == 1 << layer`; the two combined masks are exact.
    2. `NoShotMaskEverKeepsBarrierBulletOrDeadPlayer`: `Strip(~0)` has none of the three bits; `Strip(9) == 9`.
  - **`BarrierCrossingRuleTests`** (a footprint 10 × 0.6 m, radius 0.7, skin 0.05):
    3. `PastTheMiddleTheCapsuleLeavesByTheFarFace`
    4. `ShortOfTheMiddleItGoesBackOutTheWayItCame`
    5. `ExactlyOnTheMiddleTheWayTheMoveWasHeadingWins`
    6. `TheExitClearsByRadiusPlusSkinAndKeepsTheAlongPosition` (1.05 m from the middle line)
    7. `AShallowMoveStillLeavesStraightOutNotAlongTheBarrier` (a 10° move exits perpendicular)
    8. `OverlapIsTheCapsuleCircleAgainstTheFootprint` (a corner within the radius: true; 1 mm clear: false)
- [ ] **2. The layer.** One eval:
  - load `AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]`;
  - through a `SerializedObject`, set `layers[8]` = `Barrier` and call `ApplyModifiedPropertiesWithoutUndo`;
  - then `AssetDatabase.SaveAssetIfDirty(tm)`. Only if the file didn't change, and `git status --short` is otherwise clean, fall back to `AssetDatabase.SaveAssets()`.

  **Gate** (read each value, then decide):
  - `git diff --numstat ProjectSettings` prints exactly `1 1 ProjectSettings/TagManager.asset`, and the line is `  - Barrier` at `:17`;
  - `DynamicsManager.asset` is byte-unchanged;
  - eval: `NameToLayer("Barrier") == 8`, and `Physics.GetIgnoreLayerCollision(0, 8)` is False.
- [ ] **3. `ArenaLayers`, `BoxFootprint`, `BarrierCrossingRule`** (pure except `ArenaLayers`).
  - `BoxFootprint` (`Overpower.Arena`): an upright box seen from above.
    - Fields: `Centre`, `Along` (unit), `HalfLength`, `HalfWidth`, `Across`.
    - Methods: `Contains(p)`, `OverlapsCircle(p, r)`, and `FromBox(Vector3 worldCentre, Quaternion rotation, Vector3 worldSize)`.
  - `BarrierCrossingRule.ExitPoints(Vector2 centre, Vector2 moveDir, float radius, in BoxFootprint barrier, out Vector2 first, out Vector2 other)`:
    - `s` = the signed offset across the barrier;
    - `side` = sign(s), or sign(moveDir·Across) when |s| < 1 mm;
    - `first = centre + Across·(side·(HalfWidth + radius + DisplacementSweepRule.SkinMetres) − s)`; `other` is the mirror.
  - Class comment: the GDD rule, and why "the side you're already on" (the smallest correction; a nudge, never a surprise).
- [ ] **4. Shots:** `HitMasks.StripNonNegotiableLayers` also removes `ArenaLayers.BarrierLayerName`'s bit. Its comment says why: "GDD p.29: projectiles pass jersey barriers; `Stun Gun Bullet` and `Raybeam` tick every layer".
- [ ] **5. `PlayerDisplacement`** (it stays the only writer of `ExternalMotionControl`, and becomes the only writer of `rb.excludeLayers`):
  - `blockMask` becomes two fields:
    - `voluntaryBlockMask` (Default+Building, today's value; a barrier never stops a dash or zip pull);
    - `forcedBlockMask` (`ArenaLayers.BodiesWallsAndBarriers`; a shove stops at it like at a wall).
    - `AllowedTravel` takes the mask by kind. `CanStartVoluntary` uses the voluntary one, so standing flush against a barrier never refuses a dash over it.
  - **New `Start`:** `if (!photonView.IsMine) rb.excludeLayers |= ArenaLayers.Barrier;`. The comment: a copy only follows its owner, whose own body already obeys barriers, and a copy that collided would stall on screen while its owner hopped over.
  - **`StartMove(Voluntary)`:**
    - turns the exclusion on (`ignoringBarriers = true`);
    - remembers `moveStart = rb.position`;
    - clears `settlePending` (the new move owns the crossing).
  - **`Finish`:**
    - if `ignoringBarriers` and the capsule at `rb.position` overlaps a Barrier collider (`PlayerSpaceProbe.IsInsideBarrier`), keep `ExternalMotionControl` and the exclusion on, and set `settlePending`;
    - otherwise release both. A move started from the callback takes over.
  - **`FixedUpdate`:** first, `if (settlePending && Settle()) return;`. `Settle`:
    - finds the overlapped barrier's BoxCollider → `BoxFootprint.FromBox`;
    - calls `ExitPoints(rb.position.xz, direction, capsule.radius, …)`;
    - takes the first candidate where `!IsCapsuleBlocked(capsule, root, ArenaLayers.WallsAndBarriers, transform)`, else `moveStart`;
    - calls `rb.MovePosition` and returns true;
    - on a later step with no overlap: release (exclusion off; `ExternalMotionControl` false only if no move is active) and return false;
    - after 5 settle steps: `TeleportTo(moveStart)` plus a `Debug.LogError` (it should never fire).
  - **Death** (`HandleAliveChanged(false)`) and **`OnDisable`** release at once: a corpse has no collider, and respawn teleports.
  - **Update the class comment** (the Barrier paragraph) and the `blockMask` comment at `:40-50`.
- [ ] **6. The rest:**
  - `PlayerSpaceProbe.IsInsideBarrier(capsule, root, self)` = `IsCapsuleBlocked(…, ArenaLayers.Barrier, self)`.
  - `PlayerMotor.CheckArenaBounds`: `grounded = signed >= radius && IsStandingOnFloor(p) && !PlayerSpaceProbe.IsInsideBarrier(capsule, p, transform)` (`:233`).
  - `BlinkAbility`:
    - `IsCapsuleBlocked` uses `ArenaLayers.BodiesWallsAndBarriers` (`:128`);
    - the ground probe keeps `blockMask`, so a barrier top is never floor.
  - `TeleportAbility`: `IsBlocked` uses `BodiesWallsAndBarriers`, and `IsExitClear` uses `WallsAndBarriers`.
  - `DeployableCoverAbility.IsBlocked` uses `BodiesWallsAndBarriers`.
  - `MineAbility`: a candidate is also rejected when `Physics.CheckSphere(feet + up·0.3, 0.3, ArenaLayers.Barrier)`. The walk-back then lands it in front; a mine thrown over still lands beyond.
  - `DummyTarget.displaceBlockMask` uses `BodiesWallsAndBarriers`.
  - Every mask comment says why, for a designer reader.
- [ ] **7. Green:** +8. Every existing test passes, including `DisplacementSweepRuleTests`, `BlinkDestinationSearchTests` and `OutOfArenaRuleTests`.
- [ ] **8. Single-client Play Mode check.**
  - **Setup:**
    - the actor check first;
    - build a **runtime-only test barrier** 8 m from S1 across open floor: a `CreateGameObjectWithHideFlags` cube on layer 8, look 10 × 1 × 0.6, collider y −1..3, never saved;
    - drive with the movement-integrity harness (`2026-09-17-movement-integrity.md:505-560`): `Place` via `TeleportTo`, `WalkInto`, `Cast` via `AbilityRunner.TryCast` by reflection, `loadout.SetAbility`;
    - aim with `SetAimOverride`; no real input, no `editor_focus`.
  - **The recorder:** every physics step it logs `rb.position`, `IsInsideBarrier`, `rb.excludeLayers` and `ExternalMotionControl` to a file.

  | # | Action | Expected |
  |---|---|---|
  | P1 | `WalkInto` the barrier 1.5 s; again after a Sprint (16) cast | Stops: centre ≥ face + 0.65 m, never beyond |
  | P2 | Dash (14) perpendicular, from centre-to-near-face 1.0, 1.7, 2.5, 3.2, 3.5, 4.0 m; and from 2.0 m at 30° | Crosses from ≤ 1.7 m. Ends overlapping are nudged to the side the centre is on. **No step after release overlaps.** `excludeLayers` returns to 0 and `ExternalMotionControl` to false within 3 steps of the end |
  | P3 | Zip Gun (18) at a crate beyond it | Carried over, ends clear |
  | P4 | Blink (15) to 3 m beyond; to the barrier's centre; to 0.3 m past its far face | Beyond; near side; clear (the fit check) |
  | P5 | Portal (17): one each side; one centred on the barrier; travel | Allowed; refused; arrival clear |
  | P6 | `Displace(toward, 3, 10, cb)` on self; Sonic Pulse (23) on a dummy 1 m in front, toward it | `Blocked`, the blocker is the test barrier; the dummy stops at the face |
  | P7 | A dummy 1.5 m behind, shooter 4 m in front: weapons 1, 2 (direct and floor splash 1 m from the dummy), 5, 7 (at 45° to the barrier), 8, 11, 13; abilities 21, 22, 23, 24 | `DummyTarget.AnyDamaged` records every hit (22: stun applied). Weapon 7 shows **no redirect** at the barrier. Weapon 11's warning line reaches past it. The aim cone's line lengths are > the barrier distance |
  | P8 | Mine (19) at the footprint and beyond; Cover (20) overlapping | In front / beyond, never inside; refused |

  - **Finish:** stop, then the dirty check reads False, and the scene gate prints **UNCHANGED**.
  - Remote-copy gliding needs two clients: it goes to the next two-client session (see Risks).
- [ ] **9. Commit** the 15 files plus the new `.meta`s: `feat(arena): arena step 4a - the Barrier layer: walking and shoves stop, every shot passes, dash, zip, blink and portals cross and never end inside`.

## Step 4 (amended): walls from the outline, barrier rows, the builder

**Added files:**
- `Assets/scripts/Arena/ArenaWallPlan.cs`, `Assets/scripts/Arena/ArenaWallCoverage.cs` (pure);
- `Assets/Tests/ArenaWallPlanTests.cs`, `Assets/Tests/ArenaWallCoverageTests.cs`;
- `Assets/Gameplay/Arena/Materials/Arena Barrier.mat`.

**Modified:** `Assets/scripts/Arena/ArenaBounds.cs`, which gains `public static ArenaBounds FromPolygon(IReadOnlyList<Vector2>)` so tests can use any outline.

- [ ] **1. Red** (the base plan's 5, adjusted, plus 7 new = **+12**):
  - **Base test 3** becomes `BuildMakesWallsFromTheOutlineBlocksOnBuildingAndBarriersOnTheirOwnLayer`:
    - walls under `Boundry`, blocks under `Blocks`, barriers under `Barriers`, all three groups marked;
    - a barrier is layer 8, its look spans y 0 to its row height, and its collider spans y −1..3.
  - **Base test 5 (`BuiltWallsSitOnTheOutline`):** its tiny arena now has an outline and no captured walls.
  - **New:**
    6. `CaptureLeavesWallsToTheOutlineAndReadsBarriersBack`: Boundry units are skipped and counted; a `Barriers` unit becomes a Barrier row.
    7. `EveryEdgeGetsOneWallWhoseInnerFaceRunsCornerToCorner`
    8. `OnlyOutwardCornersRunOnAndByJustEnoughToFillTheNotch`: 90° → t; 30° → t·tan 15°; inward → 0; straight → 0.01.
    9. `TheRealOutlineIsClosedAtEveryCornerAndSeam`: the 8 scene points and centre as constants, all 24 walls, `FindHoles` is empty.
    10. `NoWallReachesIntoTheArena`: `FindIntrusions` is empty.
    11. `AnOutlineListedTheOtherWayRoundGivesTheSameWalls`
    12. `TheHoleFinderFindsTodaysKindOfCornerNotch`: two walls each 0.73 m short of a 270° corner → holes there; the same walls meeting → none.
- [ ] **2. `ArenaWallPlan.ForSource(IReadOnlyList<Vector2> wholeOutline, int sourceEdges, float thickness)`** → `List<Run>`.
  - Each `Run` holds `InnerStart`, `InnerEnd` (extension included), `Inward`, `Length`, `UnityYawDegrees` (local +Z faces in, local +X along the edge) and `Footprint(thickness)`.
  - The winding comes from the signed area, so the outline may be listed either way round, like `ArenaBounds`.
  - The class comment quotes Tudor and explains outward/inward corners and seams in two sentences.
- [ ] **3. `ArenaWallCoverage`:**
  - `FindHoles(ArenaBounds, IReadOnlyList<BoxFootprint>, float band)`:
    - samples every 0.05 m along each edge at outward offsets 0.02, band/2 and band − 0.02;
    - plus a fan around every corner (radii 0.03-band, every 3°);
    - reports each exterior point closer than `band` that no footprint covers. Covering the whole band means nothing (a ray, a view or a body) can pass.
  - `FindIntrusions` samples inward offsets 0.02, 0.1 and 0.3.
- [ ] **4. `ArenaLayout` changes:**
  - `PieceKind { Block, Barrier }`.
  - New fields, each with a plain `[Tooltip]`:
    - `wallMaterial`, `wallThickness 0.724`, `wallBottomY −1`, `wallTopY 6.036`;
    - `barrierMaterial`, `barrierBlockingBottomY −1`, `barrierBlockingTopY 3`.
  - `Piece.size`'s tooltip for a barrier: "x = length; y = how tall it looks (1.0 m, waist height: you see and shoot over it); z = thickness (0.6 m, never under 0.4 m)".
  - `ArenaLayoutCapture`: under `Boundry` → skip and report ("walls come from Source Outline"); under `Barriers` → Barrier; otherwise Block, as planned.
- [ ] **5. `BuildSource`:**
  - `Boundry` gets one Cube per `Run`: centre (x, (bottom+top)/2, z), yaw, scale (length, top − bottom, thickness), `Arena Wall`, Building, `BatchingStatic`. Name it `Wall <i>`.
  - `Blocks` stay as planned.
  - `Barriers`: one Cube per row, sitting on y 0, layer 8, `Arena Barrier`, `BatchingStatic`. Its BoxCollider's local `center.y`/`size.y` are set from the blocking band.
- [ ] **6. Material:** `Arena Barrier` (URP Lit, D18).
- [ ] **7. Capture, then the barrier row** (`SCRATCH\CaptureLayout.cs`, then `SCRATCH\AddBarrierRow.cs`, edit mode):
  - compute the row from `facts-before.txt`'s `# jersey barrier site` and D25, printing the arithmetic;
  - expect ≈ B's numbers, or explain the difference;
  - the capture report's walls now say "22 skipped (from the outline)".
- [ ] **8. The BEFORE coverage** (read-only): run `FindHoles` on today's Boundry BoxColliders in all three thirds into `coverage-before.txt`. Expect clusters at the 12 corners in A. It is the red evidence for step 5.
- [ ] **9. Green:** +12. Dirty check False; the scene gate prints UNCHANGED.
- [ ] **10. Commit**, message amended: `feat(arena): arena step 4 - the layout asset, walls from the outline (corners and seams closed), barrier rows and the primitive builder (not applied yet)`.

## Step 5 (amended): the arena in the scene, with closed corners and three barriers

- [ ] **1. Red** (base +3, plus **3 new = +6**). Run them on today's scene first; they fail as expected.
  - **Base test 1:** every collider is a Building BoxCollider, **except under `Barriers`, which is layer 8**.
  - **New:**
    4. `TheBoundaryIsClosedAtEveryCornerAndSeam`: `FindHoles` over the BoxColliders of all three `Boundry` groups (`BoxFootprint.FromBox` of the world centre, rotation and size) is empty, and so is `FindIntrusions`.
    5. `EveryBoundaryWallStandsFromBelowTheFloorToFullHeight`: bottom ≤ 0, top ≥ 5.9 (today 0.06 fails).
    6. `EachTierIIIRecessHasOneBarrierOnItsOwnLayerMeetingCoverAtBothEnds`:
       - exactly 3 barriers, one per third, each within 1 m of a recess mouth line;
       - collider band ≤ −0.5 / ≥ 2.7;
       - each end's footprint overlaps a Building BoxCollider (one of them in the neighbouring third);
       - nothing else in the scene on layer 8.
- [ ] **4. Report** (amended): Source `Boundry` **8**, `Blocks` 27, `Barriers` **1**. The facts gain:
  - every new wall's inner face covers every old wall's inner face (under `Old Arena (off)`), which proves "same lines";
  - each barrier's distance to every spawn (all > 5 m).
- [ ] **9. Play Mode** (amended): add the corners (V17) to the spawn check. Top-down captures `topdown-after.png` plus a crop of each recess. **The controller looks:** no notch, an amber line across each recess.
- [ ] **10. Commit:** `feat(arena): arena step 5 - the arena rebuilt from primitives: corners and seams closed, a jersey barrier across each Tier III recess (old art switched off, not deleted)`.

## Step 6 (amended): new rows

| # | Check | Expected |
|---|---|---|
| V14 | At all three barriers: `WalkInto` from the zone side and from the recess side; dash across both ways | Stops; crosses and ends clear |
| V15 | A dummy in the zone-4 recess, shooter on the zone side: weapons 1, 2 (splash), 7, 11 (line), 13, and 22 | Every hit lands; weapon 7 does not bounce at the barrier |
| V16 | Walk through both side lanes of each recess | Passable (widths as step 0) |
| V17 | Rays at 1.98 m and 0.5 m, from 0.5 m inside each of the 12 former notches, fanned across the outside in 1° steps; `WalkInto` each recess-mouth corner diagonally for 1 s | Every first hit is a `Boundry` collider within 0.05 m of the outline; `LeftArena` never fires |
| V18 | Minimap: the pixel at each barrier centre in `ArenaMinimap.png` against a floor pixel 3 m away | Summed \|ΔRGB\| ≥ 60; **the controller looks** at the big map |
| V19 | Capture S4: 3 m in front of the zone-4 barrier, facing it | **The controller looks:** it reads as low and unlike a wall. The ring edge it stands on (about 3 m hidden) is noted |

## Step 7 (amended)
- Sample the barrier against the floor, wall, block, the three team colours and the warning red. Every pair must reach ≥ 60.
- Tune `Arena Barrier` with the others.
- Commit: `tune(arena): arena step 7 - floor, wall, block, barrier and tower colours read against all three teams`.

## Step 8 (amended)
- `[C]` lines for D3 (amended), D11, D18 and D21-D25, in play terms.
- Opens 9-13 as QUESTIONs with their defaults.
- Replace the base plan's "22 walls" in any note with "8 per third, from the outline".

---

## Rules and gates that change
1. **ProjectSettings:**
   - byte-unchanged in every step **except 4a**, which changes exactly `TagManager.asset:17` (`  - Barrier`);
   - `DynamicsManager.asset` is byte-unchanged always. Its row 8 is already all-on; if a later step finds it isn't, stop and report.
   - Add both to rule 14's list.
2. **Scene gate:**
   - **4a:** UNCHANGED against the previous step's commit.
   - **4:** UNCHANGED.
   - **5:** changes, under the SCENE_SAVED rule.
   - **6:** unchanged unless there is a fix. **7:** unchanged.
3. **Byte-unchanged as before:** `Multiplayer Player.prefab` (the exclusion is runtime only), `GameplayConfig.asset`, `TerritoryConfig.asset`, `PhotonServerSettings.asset` (RpcList equals `rpclist-base.txt`), `Assets/Sources/`.
   - Still: 21 PhotonViews, no RPC added, renamed or removed, no room or player property, `PlayerNetSync` the only `IPunObservable`.
   - No PhotonView or `MonoBehaviourPun` under `Barriers` (Rebuild refuses it anyway).
4. **No new serialized field on an existing prefab or scene component.**
   - The masks are code constants on purpose (`PlayerDisplacement.cs:40-45`: "a dash that could be tuned to pass through walls would break the arena").
   - Designer numbers live in `ArenaLayout.asset`, with tooltips.
5. **Layer lookups** happen in `Awake`, `Start` or a lazy static, never in a MonoBehaviour static initialiser (`PlayerDisplacement.cs:46-49`).
6. **Pure tests use literal constants** (the 8 outline points, the centre), like `RadialSymmetryTests`: a scene edit can't silently re-baseline them.

## Open for Tudor (each has a default; nothing is blocked)
9. **How tall the barrier looks.**
   - Every shot flies over it at any height, and the camera always shows a player behind it (waist height hides only their feet).
   - Options:
     - knee-high: easy to miss from above until you bump it;
     - **waist-high (default):** clearly a low wall you shoot over;
     - chest-high: reads like cover, so players may expect it to stop bullets and feel cheated.
   - Cost: one number.
10. **Can a Sonic Pulse shove someone over a barrier?**
    - **Default: no.** A shoved player stops against it, as against a wall; being shoved isn't their own move.
    - Alternative: the shove carries them over, so a pulse can throw an enemy into or out of the recess.
    - Cost: one line and one test.
11. **A dash that runs out on top of the barrier.**
    - **Default:** you land on whichever side you're nearer, so past the middle you finish the hop and short of it you drop back.
    - Alternatives:
      - always finish the hop: a dash started up to about 3.7 m away gets you over;
      - always drop back: you must start within about 1.7 m to clear it.
    - Cost: one line.
12. **The two narrow ways past the planks.**
    - There's a gap of about 2.7 m and one of about 1.7 m between the planks and the recess walls; a player is 1.4 m wide.
    - So the recess can still be walked into, just not straight from the tower.
    - **Default: leave them open, as drawn.**
    - Alternative: barrier them too, so only a dash, zip gun, blink or portal gets you in.
    - Cost: two more barrier rows per third, no code.
13. **Sprint.**
    - **Default:** sprinting is walking faster, so a sprinter stops at a barrier like anyone walking.
    - Alternative: sprint hops barriers too, which reads "any movement option" literally. A sprinter could then flow through every barrier without spending a dash.
    - Cost: a little code.

**Not questions:**
- The X-Ray laser and the Scope change nothing at a barrier: every shot already passes it.
- Walls become one continuous piece per straight run. That also closes two small (20 cm) notches at each capital's entrance.
- The barrier stands right on Tier III's ring: pressed against its zone side you count as in the zone; behind it you don't.

## Risks (new)
- **R15: the exclusion is left on,** so the player walks through barriers.
  - Released only when the capsule is clear; released at once on death and in `OnDisable`; a 5-step cap.
  - P2 records `excludeLayers` returning to 0 after every move.
- **R16: `ExternalMotionControl` stuck on during a settle.** It is the same flag and the same cap. P2 records it going false within 3 steps.
- **R17: remote copies.** `excludeLayers` on copies is verified single-client only by reading it.
  - **Next two-client session:** the host dashes over a barrier; Client2's recorder of the host's copy shows no stall and an unchanged `RemoteSnapCount`.
- **R18: a thin barrier and unswept walking.** A 0.1 m step against 0.6 m, and the tooltip forbids going under 0.4 m.
- **R19: the barrier hides about 3 m of zone III's ring edge.** V19 looks at it; step 7 may darken the barrier's top if it hurts.
- **R20: the planks come from two thirds.** Moving either plank can open a gap at a barrier's end; step 5 test 6 catches it.
- **R21: `SaveAssetIfDirty` on a ProjectSettings object** may not write it. The fallback `SaveAssets` is allowed only on an otherwise clean `git status`, before any Play Mode (trap 20).

## Test-count delta and commits

| Step | Base plan | Amended | Delta |
|---|---|---|---|
| 4a (new) | — | +8 (`HitMasksTests` 2, `BarrierCrossingRuleTests` 6) | +8 |
| 4 | +5 | +12 (base 5 adjusted, plus 1 capture, 5 wall plan, 1 coverage) | +7 |
| 5 | +3 | +6 | +3 |
| **Total** | **BASE + 28** | **BASE + 46** (plus steps 1-2's real column-size delta) | **+18** |

**Order:** 0 → 1 → 2 → 3 → **4a** → 4 → 5 → 6 → 7 → 8 → (9). Step 4a needs only step 2 done and may run before 3.

**Commits:**
- **4a:** `feat(arena): arena step 4a - the Barrier layer: walking and shoves stop, every shot passes, dash, zip, blink and portals cross and never end inside`
- **4:** `feat(arena): arena step 4 - the layout asset, walls from the outline (corners and seams closed), barrier rows and the primitive builder (not applied yet)`
- **5:** `feat(arena): arena step 5 - the arena rebuilt from primitives: corners and seams closed, a jersey barrier across each Tier III recess (old art switched off, not deleted)`
- **6:** only on a fix.
- **7:** `tune(arena): arena step 7 - floor, wall, block, barrier and tower colours read against all three teams`
