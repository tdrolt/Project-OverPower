# Mark Laser, Damage Numbers and the Yellow Shield Bar: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to carry out this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal (Tudor, 2026-09-18):**
- **The Mark laser replaces the charge leaf.** A hit marks the enemy; your next hit on them within 2 s deals +50% and uses the mark; the hit after that marks again. Charging leaves the laser tree. The wind-up stays.
- **Damage numbers** pop, animated, next to an enemy whenever you damage them.
- **The health bar turns yellow** while the Invulnerability shield's immunity runs.

**Architecture:**
- **Pure rules, tested in edit mode:**
  - `MarkLedger` handles one target's marks, keyed by attacker: apply, cash in, expire, clear.
  - `ImmuneLookClock` decides how long the yellow bar lasts.
  - `DamageNumberMotion` handles a number's pop, rise and fade, rounding, merging and side offset.
  - `MarkIndicatorRule` sets the mark diamond's fade and pulse.
  - `DamageCreditLedger` gains `HasPending` and a per-attacker cashed-mark flag.
  - `DamageInfo` and `DamageResult` gain the mark fields.
- **Adapters:**
  - `PlayerHealth.ApplyDamage` and `DummyTarget.ApplyDamage` decide the mark after `HitVerdict.Lands`.
  - `Hitscan.Fire` passes the weapon's mark numbers into `DamageInfo`.
  - `PlayerCombatCredit` sends the victim's truth: amount, cashed flag and mark seconds left.
  - `InvulnerabilityAbility` drives the yellow bar through `PlayerHealth`.
  - `PlayerHud` builds a hit-feedback overlay canvas that hosts `DamageNumberView` and `MarkIndicatorView`.
- **Networking:**
  - **No RPC added, renamed or removed.** `RPC_DamageCredit` gets two appended parameters, once, in step 4.
  - No new property. `PlayerNetSync` stays the only `IPunObservable`.

**Tech stack:** Unity 6000.0.70f1 (URP, Mono), Photon PUN 2, NUnit edit-mode tests, the `unity` CLI and the two-client harness.

**Naming rule:** these tasks are numbered 0 to 8. Reports, commits and `progress.md` call them **"mark step N"**.

**Provenance:**
- Written by a read-only opus planner. Every `file:line` below was read at HEAD **`9266fdc`**.
- 2.7b step 9 (telemetry) may land first and touches the same report files as mark step 6.
- **Re-read before editing.** Line numbers drift.

---

## Tudor's answers to "Open for Tudor" (2026-09-18 evening). THESE OVERRIDE the Decisions and step text below

Tudor answered all seven questions. **Where a Decision or a step below says otherwise, this section wins.** The
implementer of each step reads this first and says in its report how each override was applied.

His words, verbatim: "1. only the player that applied it and the player that it has been applied to should see the
mark 2. the number should appear next to the impact with the damage it dealt each tick 3. everything that deals damage
4. one for each enemy that you are hitting 5. default is good 6. make ti so there s an overlay so it doesnt mess with
the shield 7. you can have the blocked if its not too hard to implement. if it requires too much additional
implementation just leave it"

| # | Answer [T] | What changes |
|---|---|---|
| 1 | The mark is seen by **the player who applied it and the player it is on**, nobody else | **Step 5:** the diamond also shows on the **victim's own screen** over their own head while any attacker's mark on them is live. The victim's client already holds its `MarkLedger`, so no network is needed: a `PlayerHealth.AnyMarkLive` (or `LongestMarkSecondsLeft`) read by a small owner-only view. The shooter's diamond is unchanged. The victim sees one diamond whoever marked them (no stacking, so at most one per attacker; showing one is enough). |
| 2 | "the number should appear **next to the impact** with the damage it dealt each tick" | **Step 2:** a number is anchored **at the impact point**, not pinned above the head. **[C] How, with no network change:** the shooter's own client simulates its own shots, so when `PlayerHealth.ApplyDamage` (or `DummyTarget.ApplyDamage`) runs on a copy the shooter does **not** own, with `info.SourceActorNumber == local actor`, it raises a local `CombatEvents.LocalImpactSeen(victim, info.HitPoint)` **before** the `IsMine` return. The view keeps the latest impact per victim as an offset from the victim's transform (so it follows them), and anchors that victim's number there when the victim's credit report arrives. With no recent local impact (a burn tick, a fire field, a mine, anything not simulated on the shooter's side, or older than ~1 s), it falls back to the victim's centre at `damageNumberAnchorHeight`. `damageNumberScreenOffset` stays as a small nudge so the number sits beside the impact, not on it. |
| 3 | Everything that deals damage | Unchanged (the plan's default). |
| 4 | "**one for each enemy** that you are hitting" | **Step 2:** **one live number per enemy.** While you keep damaging an enemy, each new report **adds** its damage to that enemy's number, re-pops it and restarts its life, and the number moves to the latest impact. When the reports stop, it rises and fades as planned. This replaces the merge window (`damageNumberMergeSeconds`) and the left/right alternation (`damageNumberSpread`): drop both fields and `ShouldMerge`/`Side` from `DamageNumberMotion` and its tests (adjust the test count and say so). Add one field, **`damageNumberHoldSeconds`** (default 0.6): how long an enemy's number stays solid after the last hit before it starts to rise and fade. So step 2 adds **13** theme lines, not 14. A marked (+50%) hit still makes the number bigger and orange for the rest of its life. |
| 5 | The killing blow shows what landed | Unchanged. |
| 6 | "make it so there's an **overlay** so it doesn't mess with the shield" | **Step 1:** the health and armour fills **keep their own colours**. During the immunity a **yellow overlay** covers the bar instead: on your own HUD bar and on the bar over your head (every screen). **[C]** One translucent Image per bar, the size of the whole bar, drawn over both fills, **created in code** (the HUD is built in code; for the overhead bar `PlayerHealth` creates it at runtime as the last child of the bar's own parent, copying the health fill's rect), so **no prefab changes**. `immuneBarColor` becomes the overlay's colour, with default alpha **0.45** (`(1, 0.86, 0.1, 0.45)`) so the fills show through. The tests assert the overlay is active with that colour and **the fills are untouched**. |
| 7 | "Blocked" if not too hard, otherwise leave it | **Step 4, the implementer's call:** a hit the shield blocks shows a small grey "Blocked" at the impact on the shooter's screen, **only if** it fits in step 4's single `RPC_DamageCredit` signature change (e.g. a third appended value, or `amount 0` with a blocked flag) and stays small (about 30 lines, no new RPC, no change to the rate cap). Otherwise leave it out and say why. Add the theme text/colour fields only if it's built. |

**Still defaults (not asked again):** no numbers over your own head when you're hit (Open #2's question as written);
the yellow shows only while the 4 s immunity runs, not while the trap is merely armed.

---

## Tudor's words [T] (verbatim)

**1. The Mark laser:**
> "after you are done with the current task i also had an idea for the charging laser upgrade. instead of having 2 charge paths i thought a more original idea might be to make it so whenever you hit an enemy you mark them and then whenever you hit them again withing 2 seconds you deal 50% more damage. this implements a new mechanic but its more original than the previous in my opinion."

> "yes but the charge is no longer a mechanic for the laser weapon tree, now you have the mark upgrade and the xray one"

> "the mark gets used up, but it can be reapplied endlessly by hitting an extra time after consuming it / no stacking markers for now at least / the mark should be applied instantly but it can only be used by the player who applied it"

> "keep the wind-up, that's what i meant."

**2. Damage numbers:**
> "i think a good polish that could be relatively cheap is to have damage numbers pop up whenever you shoot an enemy. it could make the prototype look nicer. the number would appear next to the enemy i think and if you can make it more animated it would be even cooler."

**3. The yellow bar:**
> "while the shield is active could you also make the healthbar yellow to indicate that?"

**Already recorded with Tudor [T]:**
- **"No stacking" means at most one mark per attacker on a target.** Several attackers can each hold their own mark on the same enemy, and each can cash in only their own.
- **The burst charge (weapon 6) keeps its charge.** Only the laser tree loses charging. The shared charge code stays.
- **Both bars turn yellow only while the 4 s immunity runs,** not while the shield is merely armed. The colour is a `UiTheme` field. Tudor can still ask for "armed" too.

---

## What exists (verified at `9266fdc`)

### A. The laser tree and the firing path
- **Weapon assets** (`Assets/Gameplay/Weapons/`):
  - `11 Laser.asset`: `canCharge 0`, damage 21, fire interval 0.7, `windupSeconds 0.6`, range 26. Projectile prefab `42f3e68e…` (fileID `6611911768405770581`).
  - `12 Laser - Charge.asset`: display name "Charge", `canCharge 1`, `maxChargeSeconds 0.9`, `chargeDamageMultiplier 2.2`, `chargeRangeMultiplier 1.6`. Every other value matches 11, **including the same projectile prefab as 11.**
  - `13 Laser - Through Walls.asset`: "X-Ray", `canCharge 0`, its own through-walls prefab `9a13196b…`.
  - All three descriptions end "…after a short warning".
- **`WeaponDefinition.cs`:**
  - The Charge header runs `:186-221`; Feedback starts at `:223`; `OnValidate` is at `:248-289`.
  - The class comment says the stat block is the one home: "no per-weapon code anywhere".
- **`WeaponFiring.cs`:**
  - The fire RPC is `AllViaServer` (`:455-457`).
  - `RPC_FireWeapon` (`:559-599`) takes the wind-up branch at `:590-593`.
  - `FireAfterWindup` is at `:643-657`. Each client counts the wind-up from its own receipt (`:625-630`).
  - Charge: `ChargeFraction` `:523-530`, `ChargeHeld` `:544`, press/release gating `:287-317`, `ChargedProjectileCount`/`ChargedDamage` `:777-795`.
  - **Stale after this plan:** the comment at `:306`, "weapons 6 and 12 - the only two that CanCharge".
- **`Hitscan.cs`:**
  - `Fire` is at `:179-199` and builds the `DamageInfo` at `:188-190`.
  - `ChargedRange` is at `:215-222`; its comment `:201-214` talks about "a charging laser".
- **Other readers:**
  - `LoadoutScreen.WeaponNumbersText` (`:1053-1071`) adds "hold to charge" (`:1063-1064`) and the wind-up (`:1067-1068`).
  - Stale comments: `TestRangePanel.cs:330-332`, `ChargeCountRule.cs:17-19`, `ChargeCountRuleTests.cs:72`, and the `UiTheme` charge-ring tick tooltip `:617-619` ("the laser's, which ramps…").
  - `AimConeView:202` and `ChargeRingView:151,179` go through `CanCharge`, so they need no change.
- **`TuningSnapshot.cs:64-70`** runs `JsonUtility.ToJson(weapon)`, so new `WeaponDefinition` fields reach the telemetry tuning header for free.
- **`WeaponConfigRuleTests.cs`** loads the real catalogue (`:19-25`) and "deliberately pins no tuning number" (`:8-16`).

### B. The damage funnel
- **`DamageInfo.cs`:**
  - `DamageInfo` is at `:12-50`. `AbilityId` was appended last with a default (`:29-48`); that is the precedent for appending fields.
  - `DamageResult` is at `:52-68`.
- **`DamageResolver.cs:32`** clamps overkill. A killing blow reports only the health that was left.
- **`PlayerHealth.ApplyDamage` (`:281-347`):**
  1. `IsMine`/`isDead` check (`:285`).
  2. `HitVerdictRule.Classify` (`:291-297`).
  3. The combat clock (`:302-303`).
  4. Early returns for self, teammate and shielded hits (`:305-326`).
  5. `DamageResolver` (`:328-330`).
  6. `Damaged` fires (`:336`).
  7. The lethal block (`:338-344`).
- **`ResetForRespawn` (`:402-420`)** is also run by the match-start fresh start (`PlayerLifecycle.cs:435`).
- **`DummyTarget` has its own funnel:** `ApplyDamage` `:435-465`, with no verdict. `NotifyLocalCombatCredit` (`:476-490`) raises `CombatEvents` directly. `ResetToFull` is at `:395-417`.

### C. The victim-to-attacker channel
- **`PlayerCombatCredit`:**
  - Only the victim's own client subscribes (`:73-87`).
  - `HandleDamaged` records `result.Total` (`:113-121`).
  - **`Update` flushes on a fixed 0.25 s cadence (`:98-108`, `creditFlushSeconds` `:31-34`).** `nextFlushTime` advances even with nothing to send, so an isolated hit waits 0 to 0.25 s.
  - `HandleDied` flushes at once (`:131-165`).
  - `SendCredit` targets one attacker (`:181-188`).
  - **`RPC_DamageCredit` (`:202-221`) runs on the victim's object, on the attacker's machine.** Its `transform` is therefore the victim as the attacker sees it.
- **`DamageCreditLedger.Drain` (`:52-70`)** allocates two lists per call.
- **`CombatEvents` (`:22-36`):** `LocalDamageDealt` and `LocalTakedown`, raised only on the attacker's machine.
- **`PhotonServerSettings.asset`:** the RpcList is at `:36-81`; `RPC_DamageCredit` is `:80`. The two retired no-ops in `PlayerLifecycle` (`:866-880`) are not needed.

### D. The shield: who knows "immune right now"
- **`PlayerStatusEffects.IsInvulnerable` (`:83`) is owner-only:**
  - `Apply` is `IsMine`-guarded (`:139-142`).
  - `Update` ticks only on the owner (`:107-110`).
  - The status starts at the hit (`:197-213`).
  - **A remote copy's `IsInvulnerable` is always false.**
- **The only replicated signal is the Invulnerability ability's phase 1:**
  - The owner's `OwnerTick` sends it one frame after the hit (`InvulnerabilityAbility.cs:122-126`), over `RPC_CastAbility` with `RpcTarget.All` (`AbilityRunner.cs:276`).
  - Every client runs `ShowShield` (`:148-149`, `:209-228`), which draws the sphere for `invincibleSeconds`.
  - `ClearShield` is at `:230-235`. `Interrupt` clears it on Died/Unequipped, never on Stunned/Silenced (`:186-200`). `OnDestroy` is at `:240`.
- **Other hooks:** `AbilityOwner.Health` (`AbilityOwner.cs:23`). `AbilityRunner.HandleAliveChanged` runs on every client (`:488-494`).

### E. The bars
- **The overhead bar:**
  - `PlayerHealth` fields are at `:31-45`. **The shield fill is drawn over the health fill, each against its own maximum (`:37-41`),** so full armour hides the health fill entirely.
  - `ApplyTheme` sets the colours once (`:168-191`). `UpdateOverheadBar` is at `:200-221`.
  - **`Update` returns for non-owners at `:230-231`,** so anything a remote copy must tick has to run before that line.
  - `SetOverheadBarVisible` is at `:149-153`.
  - The bar sits about 3 m up (the `HealthBarCanvas` anchored y is 3, `Multiplayer Player.prefab:3783`).
- **`PlayerHud` (owner only):**
  - `healthFill`/`armorFill` `:69-72`; `UpdateHealthAndArmor` `:397-430`; the health `BuildBar` `:873`; the armour fill colour `:1081`.
  - The canvas is an overlay at sorting order -10 with no raycaster (`:730-755`).
  - `AddLabel` and the shared text material are at `:1425-1465`. `MatchStartPanel.Create` (`:881-882`) is the precedent for a sub-view.
- **`UiTheme`:** `healthColor`/`shieldColor` are at `:77-78` (asset `:37-38`). The last field is `matchLiveTwoTeamsToastText` (`:679`, asset `:211`).
- **Test rig:** `PlayerHealthOverheadBarTests` (`:24-77`) builds a `PlayerHealth` with three Images and calls `Awake` by reflection.

### F. Telemetry
- **`PlayerTelemetry`:** `HandleDamaged` (the victim) is at `:816-829`, `HandleDummyDamaged` at `:836-849`. `WriteHitLine` (`:884-910`) writes `raw = info.Amount` (`:898`).
- **`TelemetryAggregator`:** `BuildHits` `:789-824`; per-weapon hit counts `:1274-1282`; the `WeaponRow` build `:1338-1360`.
- **`ReportTables`:** `HitRow` `:221-240`, `WeaponRow` `:242-265`.
- **`CsvReportWriter`:** hits `:147-156`, weapons `:158-170`. `CsvReportWriterTests` (`:73-95`) finds columns by header name, **so columns appended at the end are safe.**

---

## Decisions [C]

1. **Where the mark lives: on the victim's own client, inside `PlayerHealth`'s funnel.**
   - A pure `MarkLedger` holds attacker actor number → expiry time.
   - The victim is the one client that applies the damage, so it is the one client that can decide +50% with no message.
   - Keyed per attacker, as already recorded with Tudor: each attacker has at most one mark on a target and can cash in only their own. A teammate's Mark laser keeps its own mark.
2. **Only a hit that lands touches marks (`HitVerdict.Lands`).**
   - These neither mark nor cash in:
     - self and teammate hits;
     - a hit that springs the armed trap;
     - hits during the immunity;
     - hits on a dead player (the `isDead` return at `:285`).
   - A live mark survives a blocked hit and expires on its own. The 4 s immunity outlasts the 2 s window, so in practice the shield wipes the mark.
3. **The 2 s window runs on the victim's clock, hit landing to hit landing, and includes the edge.**
   - Each client starts the 0.6 s wind-up when it receives the shot, so the wind-up cancels out. Only network jitter between the two shots differs.
   - "Applied instantly" means the mark is on in the very frame the marking hit lands. There is no arming delay.
4. **Mark and cash-in alternate for ever.**
   - Mark, then cash in, then mark, and so on. The hit that cashes in never re-marks.
   - The +50% never grows.
   - A hit after the window is simply a fresh mark.
5. **What the bonus multiplies:**
   - It multiplies the hit's raw damage, before vulnerability, reduction and armour, in `DamageResolver`'s order.
   - It stacks multiplicatively with OverPower's +10%.
   - A cashed hit's `DamageInfo` carries the scaled amount onward, to `Damaged`, the credit, the telemetry `raw` and the kill.
6. **Only a marking weapon touches marks.** Its mark window is above 0. Any other weapon, ability, splash or burn neither marks nor cashes in, and it leaves an existing mark alone.
7. **The Mark numbers live on `WeaponDefinition`,** under a new Mark header: `markWindowSeconds` (default 0 = no mark) and `markedDamageMultiplier` (default 1). Weapon 12 sets 2 and 1.5. Why not an effect component on a prefab:
   - 11 and 12 share one beam prefab.
   - The stat block is the house's one home for per-weapon numbers.
   - The shop hover and the telemetry tuning header read the stat block for free.
   - Only `Hitscan` reads the fields, stated in the tooltip exactly like `chargeRangeMultiplier`; a guard test keeps them off projectile weapons.
8. **Death and respawn clear the victim's marks:** on the lethal hit, and in `ResetForRespawn` (which also covers the match-start fresh start). An attacker who dies needs nothing: the mark expires in 2 s, before any respawn.
9. **The laser tree:**
   - Weapon 12 keeps id 12 (ids cross the wire) and becomes "Mark".
   - Its asset is renamed through the Editor to `12 Laser - Mark.asset`.
   - Charging goes off with every charge number neutral (`canCharge 0`, `maxChargeSeconds 0`, `chargeDamageMultiplier 1`, `chargeRangeMultiplier 1`).
   - `windupSeconds 0.6` stays on 11, 12 and 13.
   - The shared charge code stays (weapon 6 uses it). Only comments that call weapon 12 a charge weapon change.
10. **Damage numbers show on the shooter's screen only, and show the damage that landed.**
    - That is armour absorbed plus health lost, as the victim's own client computed it.
    - It is delivered by the existing credit message, whose `transform` gives the position.
    - The victim's own screen shows nothing new (Open #2).
11. **The credit flush becomes "leading edge", and moves to `LateUpdate`.**
    - An isolated hit's credit leaves at the end of the frame it landed.
    - Hits that follow within `creditFlushSeconds` (0.25) wait and merge.
    - The rate cap (one message per attacker per victim per 0.25 s) and the totals are unchanged. Ultimate charge and armour recharge just hear sooner.
    - `LateUpdate` puts all of one frame's shotgun pellets into one message.
12. **Rapid hits merge rather than stack:**
    - The victim batches per 0.25 s.
    - The view merges a report for the same target that arrives within `damageNumberMergeSeconds` (0.12) of the newest label: it adds the amount and re-pops the label.
    - Otherwise a new label appears. Consecutive labels alternate left and right by `damageNumberSpread`.
    - Test-range dummies report every hit (they have no 0.25 s batching), so numbers on dummies pop more often. That is accepted.
13. **The numbers overlay:**
    - Its own overlay canvas ("Hit Feedback Canvas", sorting order -20, below the HUD, no `GraphicRaycaster`).
    - 24 pooled TMP labels built with `PlayerHud.AddLabel`, sharing the HUD text material.
    - **No allocation per hit:** text goes through TMP `SetText("{0:0}", value)`, never string interpolation.
    - Each number is pinned to a world point and projected every frame, so its size doesn't change with Scope zoom.
14. **Marked hits look different:** bigger (`damageNumberMarkedScale`) and in `markColor`. That colour is also the mark diamond's (step 5), so the two teach each other.
15. **The yellow bar follows the shield bubble.**
    - `InvulnerabilityAbility.ShowShield` calls `PlayerHealth.ShowImmuneLook(invincibleSeconds)`, and `ClearShield` calls `ClearImmuneLook()`. This runs on every client, the owner's included (`RpcTarget.All`).
    - The overhead bar (every screen) and the owner's HUD both read `PlayerHealth`'s one flag.
    - **One field, `UiTheme.immuneBarColor`, and both fills (health and shield) turn yellow.** The shield fill covers the health fill on the overhead bar, so tinting health alone would be invisible at full armour.
    - Nothing shows while the shield is merely armed.
    - `PlayerHealth.Update` expires the look before its owner-only return.
16. **The mark diamond (step 5):**
    - It shows on the shooter's screen only, from the victim's truth: every credit message carries `markSecondsLeft` for that attacker.
    - It hides on a cash-in, on expiry, or on the death report (marks are cleared before the death flush, which carries 0).
17. **No new RPC.**
    - `RPC_DamageCredit(float amount, byte takedown, bool cashedMark, float markSecondsLeft, PhotonMessageInfo info)` gets two parameters appended in **step 4 only**.
    - The RpcList indexes names, so it doesn't change. Every client build must match from step 4 on.
18. **Telemetry: yes, a `mark` key on `hit` lines.**
    - 1 means the hit placed a mark; 2 means it cashed one in (its `raw` already includes the +50%).
    - The key is written only when non-zero, so every other hit line stays byte-identical.
    - The weapons table gains `marksPlaced` and `marksCashed`, which answers "how often do players cash the mark?".
19. **Files that change and files that don't:**
    - No prefab or scene changes.
    - `UiTheme.asset` gets 1 + 14 + 4 new lines.
    - Each of the 13 weapon assets gets 2 new lines.
    - Weapon 12 also gets its own value edits and the rename.
    - No component on a prefab gains a serialized field, so the prefab re-save trap (CODING-STANDARDS §6) does not apply.

---

## Where the code or the brief differ

1. **"Immune right now" on a remote copy is not `IsInvulnerable`.** That property is owner-only (What exists D). The bubble's phase message is the only replicated signal, so the bar follows the bubble.
2. **The brief says "the health bar" in one colour field.** On the overhead bar, armour is drawn over health, so both fills tint with the same one field (Open #6).
3. **The brief suggests an effect component.** 11 and 12 share one beam prefab, so the Mark numbers go on `WeaponDefinition` instead (Decision 7).
4. **Removing charge also removes reach.** Weapon 12 went up to 41.6 m at full charge; now it reaches 26 m like 11 and 13. The Scope plan's reach table (`2026-09-18-scope-ability.md`) is now stale on that row. The quote in `ScopeAbility.cs` is Tudor's words and is left as is.
5. **Damage numbers from the victim's truth would lag 0 to 0.25 s on the existing fixed-cadence flush.** Decision 11 removes that for isolated hits.

---

## Open for Tudor (ANSWERED 2026-09-18 evening, see the table at the top)

Each had a default [C]; Tudor's answers at the top of this plan now override them.

1. **Who sees the mark?**
   - **Default:** only you, the player who placed it. A small orange diamond over the enemy fades out over the 2 s.
   - (b) The marked player also sees a diamond over their own head. Cheap, and it helps them dodge your next shot.
   - (c) Your teammates see your marks too. Needs an extra network message; more work.
2. **Numbers over your own head when you are hit?**
   - **Default:** no. Your health bar already shows it.
   - Option: small red numbers over yourself. Cheap.
3. **What gets a number?**
   - **Default:** everything you deal that lands: gun hits, rocket splash, burns, fire zones, mines, the Raybeam.
   - Option: only gun hits. Cheap.
4. **Fast weapons (SMG, flamethrower):**
   - **Default:** a fresh number about four times a second per enemy, each showing what landed since the last.
   - Option: one number per enemy that keeps counting up while you keep hitting, then floats away. Medium cost.
5. **The killing blow shows what the enemy had left.** A 21-damage laser that finishes someone on 6 health shows "6".
   - **Default:** show what actually landed.
   - Option: show the shot's full damage on a kill. Cheap.
6. **Yellow bar: the armour part goes yellow too.**
   - **Default:** yes. On the bar over a head, armour sits on top of health, so a player with full armour would otherwise show no yellow at all.
   - Option: health only. Then full-armour players look unchanged over their heads during the shield.
7. **A hit the shield blocks:**
   - **Default:** no number. The yellow bar already tells you.
   - Option: a small grey "Blocked". Cheap.

---

## Rules for every task

The same as `2026-09-18-match-start-and-final-two.md` "Rules for every task", with these changes.

1. **Before starting:** read `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\HANDOFF.md` §5.
2. **Branch:** `limit-testing` only.
3. **Editor:**
   - `unity command editor_status` must answer first.
   - There is one Editor. If another agent is driving it, stop and ask the controller.
   - Never `editor_focus`.
4. **Recompiling and tests:**
   - Never edit `.cs` files, recompile, run tests or build in Play Mode.
   - After `recompile`, poll `recompile_status`; it is the only compile truth.
   - Run tests async only (`run_tests -- --mode editor --async_tests true`, then `test_status`).
5. **The dirty-scene check:** read `SceneManager.GetActiveScene().isDirty` on its own, never chained with `&&` (trap 8b). Continue only on `False`.
6. **After Play Mode:** `git diff --stat -- "Assets/Scenes/Game Scene.unity"` must be empty.
7. **Moving and aiming:** move players only with `PlayerDisplacement.TeleportTo`, aim only with `PlayerAim.SetAimOverride` (trap 8d), and fire only with `WeaponFiring.TryFire()` from an in-process coroutine.
8. **Before any measurement,** list the room's actors.
9. **SCRATCH** means the session scratchpad, subfolder `mark-laser`. All scripts, recordings and captures go there, never under `Assets/`. Every capture gets an explicit save path (trap 8a). The controller looks at every capture (trap 5).
10. **Designer values:**
    - Every designer-facing value lives on an asset with a plain `[Tooltip]`, in one home.
    - Pool sizes are code constants: they are not look values.
    - Comments say *why*, for a designer reader.
11. **Commits:**
    - Stage only the files the step lists, and check `git status --short` first. Other agents commit on this branch.
    - Use the session attribution line.
    - Put no unmeasured number in a message.
12. **Assumptions:**
    - Append `[C]` lines under a new `## Mark laser, damage numbers, yellow bar (2026-09-18)` heading at the end of `assumptions-for-tudor.md`. The file is outside the repo: never commit it.
    - Then list its `## ` headings to confirm none was lost.
13. **`UiTheme.asset` and the weapon assets:**
    - Add only the new fields, one line each, by a targeted YAML edit at the field's declaration position. Never `SetDirty`+save (trap 8c).
    - `git diff` on each asset must show exactly the added or changed lines.
    - After editing, run `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` by eval. Then read the values back from the loaded object by reflection, and grep the YAML on disk (CODING-STANDARDS §6).
    - **Weapon 12's rename goes through the Editor (`AssetDatabase.RenameAsset`), never `git mv`,** and happens before `WeaponDefinition.cs` gains its new fields (step 4.1).
14. **Networking:**
    - `git diff BASE -- Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset` must show **no RpcList change** in any step.
    - `PlayerNetSync` stays the only `IPunObservable`.
    - `RPC_DamageCredit`'s signature changes in step 4 and never again in this plan.
    - **From step 4 on, every client build must match.** Any cross-machine test needs a fresh Client2 build.
15. **Every step is playtest-ready on its own** (unlike 2.7b). The two-client rows are gathered in step 8 so one Client2 build covers them all; each step names its rows.
16. **No component on a prefab gains a serialized field in this plan.** If an implementer finds they need one, stop and ask: the prefab re-save trap applies.

---

## File map

| File | Responsibility | Step |
|---|---|---|
| `Assets/scripts/UI/ImmuneLookClock.cs` (create) | How long the yellow look lasts | 1 |
| `Assets/Tests/ImmuneLookClockTests.cs` (create) | 4 tests | 1 |
| `Assets/Tests/PlayerHealthImmuneTintTests.cs` (create) | 2 rig tests | 1 |
| `Assets/scripts/Player/PlayerHealth.cs` | Step 1: `ShowImmuneLook`, `ClearImmuneLook`, `ShowsImmuneLook`, the tick before the owner-only return | 1 |
| same | Step 4: `MarkLedger` in the funnel, clears, `MarkSecondsLeftFor` | 4 |
| `Assets/scripts/Abilities/Ultimate/InvulnerabilityAbility.cs` | `ShowShield`/`ClearShield` drive the look | 1 |
| `Assets/scripts/UI/PlayerHud.cs` | Step 1: bar tint | 1 |
| same | Step 2: hit-feedback canvas, 24 labels, `DamageNumberView.Create` | 2 |
| same | Step 5: 8 diamonds, `MarkIndicatorView.Create` | 5 |
| `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset` | 1 field (step 1), 14 fields (step 2), 4 fields (step 5); the step 4 tooltip fix | 1, 2, 4, 5 |
| `Assets/scripts/Combat/DamageCreditLedger.cs` | Step 2: `HasPending` | 2 |
| same | Step 4: per-attacker cashed flag, 3-tuple `Drain` | 4 |
| `Assets/Tests/DamageCreditLedgerTests.cs` | +1 (step 2), +1 (step 4) | 2, 4 |
| `Assets/scripts/Combat/CombatEvents.cs` | `LocalHitReported` (step 2), `LocalMarkReported` (step 4) | 2, 4 |
| `Assets/scripts/Player/PlayerCombatCredit.cs` | Step 2: leading-edge `LateUpdate` flush, raise `LocalHitReported` | 2 |
| same | Step 4: two appended RPC parameters, cashed flag, mark seconds left, raise `LocalMarkReported` | 4 |
| `Assets/scripts/TestRange/DummyTarget.cs` | Step 2: raise `LocalHitReported` | 2 |
| same | Step 4: `MarkLedger`, both events | 4 |
| `Assets/scripts/UI/DamageNumberMotion.cs` (create) | Pose, rounding, merge, side | 2 |
| `Assets/Tests/DamageNumberMotionTests.cs` (create) | 8 tests | 2 |
| `Assets/scripts/UI/DamageNumberView.cs` (create) | Pool, subscribe, animate | 2 |
| `Assets/scripts/Combat/MarkLedger.cs` (create) | `MarkOutcome`, `MarkLedger` | 3 |
| `Assets/Tests/MarkLedgerTests.cs` (create) | 12 tests | 3 |
| `Assets/scripts/Combat/DamageInfo.cs` | `MarkWindowSeconds`, `MarkedDamageMultiplier`, `WithAmount`; `DamageResult.Mark`, `WithMark` | 3 |
| `Assets/Tests/DamageInfoTests.cs` | +3 | 3 |
| `Assets/scripts/Data/WeaponDefinition.cs` | Mark header, 2 fields, `OnValidate` warnings | 4 |
| `Assets/Gameplay/Weapons/*.asset` (13) | +2 YAML lines each | 4 |
| `12 Laser - Charge.asset` → `12 Laser - Mark.asset` | Rename; name, description, charge off, mark values | 4 |
| `Assets/scripts/Weapons/Effects/Hitscan.cs` | Mark fields into `DamageInfo`; comment | 4 |
| `Assets/scripts/UI/LoadoutScreen.cs` | The Mark numbers in the hover | 4 |
| `WeaponFiring.cs`, `ChargeCountRule.cs`, `ChargeCountRuleTests.cs`, `TestRangePanel.cs` | Comment-only fixes | 4 |
| `Assets/Tests/WeaponConfigRuleTests.cs` | +5 | 4 |
| `Assets/scripts/UI/MarkIndicatorRule.cs` (create) | Diamond fade and pulse | 5 |
| `Assets/Tests/MarkIndicatorRuleTests.cs` (create) | 3 tests | 5 |
| `Assets/scripts/UI/MarkIndicatorView.cs` (create) | Pool, follow, fade | 5 |
| `TelemetryKeys.cs`, `PlayerTelemetry.cs`, `Editor/Telemetry/ReportTables.cs`, `TelemetryAggregator.cs`, `CsvReportWriter.cs`, `HtmlReportWriter.cs` | The `mark` key; weapon-table counts | 6 |
| `Assets/Tests/TelemetryMarkTests.cs` (create), `CsvReportWriterTests.cs` | +2, +1 | 6 |
| `docs/superpowers/specs/2026-09-16-telemetry-design.md` | One line on `mark` | 6 |
| `assumptions-for-tudor.md`, `progress.md` (outside the repo, not committed) | `[C]` lines, progress row | 7 |

---

### Step 0: start state

- [ ] **1. Check the Editor and the tree.**
  - `editor_status` answers with `playMode: "stopped"` and `compiling: false`. If another agent is driving the Editor, **stop and ask**.
  - The dirty-scene check reads `False`.
  - `git status --short` must be clean. If it isn't, stop and report every file.
- [ ] **2. Record the baselines.**
  - `git rev-parse HEAD` is **BASE**.
  - Save `git show BASE:Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset | grep -A60 RpcList` to `SCRATCH/rpclist-base.txt`.
  - Run the tests and record the passing count as **BASE TESTS**: 1030 before 2.7b step 9, about 1037 after it. If anything fails, stop.
  - Note whether 2.7b step 9 has landed. Mark step 6 must start after it.

---

### Step 1: the yellow bar during the immunity

**Files:** `ImmuneLookClock.cs` (create), `ImmuneLookClockTests.cs` (create), `PlayerHealthImmuneTintTests.cs` (create), `PlayerHealth.cs`, `PlayerHud.cs`, `InvulnerabilityAbility.cs`, `UiTheme.cs`, `UiTheme.asset`.

- [ ] **1. Red.** Write both test files. They must fail to compile with `CS0246 ... 'ImmuneLookClock'`. Report that exact message.
  - **`ImmuneLookClockTests` (namespace `Overpower.Tests`, uses `Overpower.UI`):**
    - `TheLookStaysOnForExactlyItsSeconds`: `Show(10, 4)`, then `IsOn(10)`, `IsOn(13.99f)`, `!IsOn(14)`.
    - `ClearEndsItAtOnce`: `Show(10, 4)`, `Clear()`, `!IsOn(10.1f)`.
    - `ANewShowRestartsFromTheLatestTrigger`: `Show(10, 4)`, `Show(12, 4)`, then `IsOn(15.9f)`, `!IsOn(16)`.
    - `ZeroOrNegativeSecondsShowsNothing`: `Show(10, 0)` gives `!IsOn(10)`; `Show(10, -1)` gives `!IsOn(10)`.
  - **`PlayerHealthImmuneTintTests`:** the same rig as `PlayerHealthOverheadBarTests`, plus a theme.
    - The theme is made with `ScriptableObject.CreateInstance<UiTheme>()`, with `barSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,4,4), Vector2.one * 0.5f)` and distinct `healthColor`, `shieldColor` and `immuneBarColor`. Assign it through the private `theme` field before `Awake`.
    - `LogAssert.Expect` the GameplayConfig and ArmorConfig errors only.
    - `ShowImmuneLookTurnsBothOverheadFillsTheImmuneColour`: `ShowImmuneLook(4f)`, then both fill Images' `color == immuneBarColor` and `ShowsImmuneLook` is true.
    - `ClearImmuneLookPutsHealthAndShieldColoursBack`: Show then `ClearImmuneLook()`, then health is `healthColor` and shield is `shieldColor`.
    - Tear down the theme, the sprite and the GameObject.
- [ ] **2. `ImmuneLookClock`** (namespace `Overpower.UI`). A sealed class with one field, `until = float.NegativeInfinity`:
  - `Show(float now, float seconds)`: `until = seconds > 0f ? now + seconds : float.NegativeInfinity`.
  - `Clear()`.
  - `IsOn(float now) => now < until`.
  - Class comment: the yellow bar lasts as long as the bubble; the clock is pure so the timing is tested.
- [ ] **3. `UiTheme`,** in "Bars" right after `shieldColor` (`:78`):
  - `[Tooltip("Colour of the health and shield fills - on your own HUD and on the bar over your head, on every screen - while the Invulnerability shield's immunity is running (the seconds after it triggers, not while it is only armed). Both fills take it because on the bar over a head the shield is drawn over health. Keep it clearly different from Health and Shield.")] public Color immuneBarColor = new Color(1f, 0.86f, 0.1f, 1f);`
  - Asset: after line 38 insert `  immuneBarColor: {r: 1, g: 0.86, b: 0.1, a: 1}`.
- [ ] **4. `PlayerHealth`:**
  - Fields: `private readonly ImmuneLookClock immuneLook = new ImmuneLookClock(); private bool immuneLookApplied;` and `public bool ShowsImmuneLook => immuneLookApplied;`.
  - `public void ShowImmuneLook(float seconds) { immuneLook.Show(Time.time, seconds); ApplyImmuneLook(immuneLook.IsOn(Time.time)); }`
  - `public void ClearImmuneLook() { immuneLook.Clear(); ApplyImmuneLook(false); }`
  - `private void ApplyImmuneLook(bool on)`: return if unchanged; latch the value; if `theme` is null, return; otherwise set `healthFillImage.color = on ? theme.immuneBarColor : theme.healthColor` and `shieldFillImage.color = on ? theme.immuneBarColor : theme.shieldColor` (null-checked).
  - **`Update`, before the `!IsMine || isDead` return at `:230`:** `if (immuneLookApplied && !immuneLook.IsOn(Time.time)) ApplyImmuneLook(false);`. Comment: a remote copy's look must expire too, which is why this sits above the owner-only return.
  - `ResetForRespawn`: `ClearImmuneLook();`.
  - Doc comments name the one source: `InvulnerabilityAbility`'s bubble, on every client.
- [ ] **5. `InvulnerabilityAbility`:**
  - At the end of `ShowShield`: `if (Owner != null && Owner.Health != null) Owner.Health.ShowImmuneLook(invincibleSeconds);`.
  - In `ClearShield`, the same guard, then `ClearImmuneLook()`.
  - **Use Unity's `!=`, never `?.`,** on `Owner.Health`: `OnDestroy` runs while a leaving player is being destroyed.
  - Comment: the bar follows the bubble, so the armed trap stays invisible (unchanged design).
- [ ] **6. `PlayerHud.UpdateHealthAndArmor`:**
  - `bool immune = playerHealth.ShowsImmuneLook; if (immune != lastImmuneLook) { healthFill.color = immune ? theme.immuneBarColor : theme.healthColor; armorFill.color = immune ? theme.immuneBarColor : theme.shieldColor; lastImmuneLook = immune; }`
  - Add the `lastImmuneLook` cache field.
- [ ] **7. Green.** Recompile, then run the tests. **Expect BASE TESTS + 6.**
- [ ] **8. Single-client Play Mode check.**
  - Alone, list the actors.
  - Equip id 25 in the Ultimate slot, call `UltimateCharge.Fill()`, and cast through `AbilityRunner.TryCast` by reflection (the 2.7b step 1 recipe).
  - An in-process recorder logs, every frame: `IsReactiveInvulnerabilityArmed`, `IsInvulnerable`, `ShowsImmuneLook`, the HUD `healthFill`/`armorFill` colours and the overhead fill colours.
  - 1 s after arming, `ApplyDamage` with an enemy `DamageInfo`: 10 damage, from an actor number not in the room.
  - **Expected:**
    - Normal colours while only armed.
    - `immuneBarColor` on all four fills from the trigger frame +1 frame, for `invincibleSeconds` ±1 frame.
    - Normal colours afterwards.
  - Capture the Game view (616×576) mid-immunity to `SCRATCH/step1-yellow.png`; the controller looks at it.
  - Then arm again and die by a lethal self-crafted hit from another actor during the immunity. After respawn, the colours are normal.
  - **Two-client:** row A1 in step 8.
- [ ] **9. Commit** the eight files.

---

### Step 2: damage numbers

**Files:** `DamageCreditLedger.cs`, `DamageCreditLedgerTests.cs`, `CombatEvents.cs`, `PlayerCombatCredit.cs`, `DummyTarget.cs`, `DamageNumberMotion.cs` (create), `DamageNumberMotionTests.cs` (create), `DamageNumberView.cs` (create), `PlayerHud.cs`, `UiTheme.cs`, `UiTheme.asset`.

**Why before the Mark:** the Mark's +50% becomes visible in single-client (a bigger orange number) the moment step 4 lands. The credit path is also touched here without changing the RPC signature, which changes once, in step 4.

- [ ] **1. Red.**
  - **`DamageCreditLedgerTests` +1, `HasPendingOnlyWhileUnsentDamageWaits`:**
    - A new ledger is false.
    - `Record(2, 10, 0)` makes it true.
    - `Drain()` makes it false.
    - `Record(2, 0, 0)` or `Record(0, 5, 0)` leaves it false.
    - `Clear()` makes it false.
  - **`DamageNumberMotionTests` (8), all against `DamageNumberMotion` (namespace `Overpower.UI`):**
    - `ThePopStartsBigAndSettlesToNormalSizeByPopSeconds`: `Evaluate(lifeAge 0, popAge 0, …, popScale 1.5, marked false).Scale == 1.5`; at `popAge == popSeconds` it is `1`; after that it stays `1`.
    - `ItRisesWithoutEverGoingBackDownAndReachesTheFullRiseAtTheEnd`: sample 20 ages; `Rise` never decreases; `Rise(lifetime) == rise`; `Rise(0) == 0`.
    - `ItStaysOpaqueUntilTheFadeStartsAndIsGoneAtTheEnd`: `Alpha == 1` at `fadeStart * lifetime`; strictly between 0 and 1 halfway through the fade; `0` at the lifetime.
    - `AMarkedNumberIsDrawnBiggerThroughoutItsLife`: at 5 ages, the marked scale equals the unmarked scale × `markedScale`.
    - `TheNumberShownIsRoundedButALandedHitNeverShowsZero`: `Shown(0.3f) == 1`, `Shown(21.4f) == 21`, `Shown(31.5f) == 32`, `Shown(32.5f) == 33` (half up, not banker's), `Shown(0f) == 0`.
    - `AReportForTheSameTargetInsideTheMergeWindowMerges`: `ShouldMerge(true, 0.05f, 0.12f)` is true.
    - `ADifferentTargetOrAnOlderNumberStartsANewOne`: `ShouldMerge(false, 0.05f, 0.12f)` and `ShouldMerge(true, 0.2f, 0.12f)` are false.
    - `ConsecutiveNumbersAlternateSides`: `Side(0) == 1`, `Side(1) == -1`, `Side(2) == 1`.
  - The build fails on the missing members. Report the message.
- [ ] **2. `DamageCreditLedger.HasPending`:** set true by a `Record` that records something; cleared by `Drain` and `Clear`. Add a doc comment.
- [ ] **3. `DamageNumberMotion`:** a static class plus a `readonly struct DamageNumberPose { Scale; Rise; Alpha }` in the same file.
  - `Evaluate(float lifeAge, float popAge, float lifetime, float popSeconds, float popScale, float rise, float fadeStart01, float markedScale, bool marked)`:
    - `Scale = (marked ? markedScale : 1) * Lerp(popScale, 1, popAge / popSeconds)`, clamped.
    - `Rise = rise * (1 - (1 - t)^2)`, with `t = lifeAge / lifetime`.
    - `Alpha` is 1 until `fadeStart01`, then linear to 0.
    - Guard zero or NaN durations.
  - `Shown(float amount) => amount > 0 ? Max(1, (int)Math.Floor(amount + 0.5)) : 0`.
  - `ShouldMerge(bool sameTarget, float newestAge, float mergeSeconds) => sameTarget && newestAge <= mergeSeconds`.
  - `Side(int sequence) => (sequence & 1) == 0 ? 1f : -1f`.
- [ ] **4. `CombatEvents`** (add `using UnityEngine;`, and extend the class comment: Transform is a Unity type, still no Photon or MonoBehaviour):
  - `public static event Action<Transform, float, bool> LocalHitReported;`
  - `public static void RaiseHitReported(Transform victim, float amount, bool cashedMark) => LocalHitReported?.Invoke(victim, amount, cashedMark);`
  - Doc: raised on the attacker's machine with the damage that actually landed on `victim`; `cashedMark` is false until mark step 4.
- [ ] **5. `PlayerCombatCredit`:**
  - Rename `Update` to **`LateUpdate`**, with the leading-edge guard: `if (!photonView.IsMine || !ledger.HasPending || Time.time < nextFlushTime) return; nextFlushTime = Time.time + creditFlushSeconds;`, then drain and send as before.
  - Comment:
    - An isolated hit's credit now leaves at the end of the frame it landed. Before, a fixed 0.25 s tick added up to 0.25 s.
    - Later hits inside the window still merge. Same rate cap, same totals.
    - `LateUpdate` puts all of one frame's pellets into one message.
  - Rewrite `creditFlushSeconds`'s tooltip: the shortest gap between two reports to the same attacker.
  - In `RPC_DamageCredit`'s `amount > 0f` branch, after the existing two calls: `CombatEvents.RaiseHitReported(transform, amount, false);`. Comment: `transform` is the victim as this attacker sees it, because the RPC runs on the victim's object.
- [ ] **6. `DummyTarget.NotifyLocalCombatCredit`,** after `RaiseDamageDealt`: `CombatEvents.RaiseHitReported(transform, result.Total, false);`.
- [ ] **7. `UiTheme`,** a new header `[Header("Damage numbers (2026-09-18)")]` after `matchLiveTwoTeamsToastText` (`:679`). Each field gets a plain tooltip, and each is appended to the asset after line 211 as one YAML line in this order (14 lines):

  | Field | Default | Tooltip gist |
  |---|---|---|
  | `showDamageNumbers` | `true` | Pop a number beside an enemy each time your damage lands on them. Off hides them; nothing else changes. |
  | `damageNumberTextSize` | `30` | Canvas units, at the number's settled size |
  | `damageNumberColor` | `(1, 1, 1, 1)` | An ordinary hit's number |
  | `markColor` | `(1, 0.45, 0.1, 1)` | A hit that used up your mark (+50%), and the diamond over an enemy you have marked. Keep it apart from the team colours and Immune Bar Colour |
  | `damageNumberMarkedScale` | `1.4` | A marked hit's number is this much bigger for its whole life |
  | `damageNumberLifetimeSeconds` | `0.8` | From the pop to gone |
  | `damageNumberPopSeconds` | `0.12` | How long the pop takes to settle |
  | `damageNumberPopScale` | `1.5` | How big a number starts, as a multiple of its settled size |
  | `damageNumberRise` | `60` | Canvas units it floats up over its life |
  | `damageNumberFadeStart` | `0.55` | `[Range(0,1)]` Fraction of its life before it starts to fade |
  | `damageNumberAnchorHeight` | `2` | Metres above the target where the number is pinned (the bar over a head sits at about 3) |
  | `damageNumberScreenOffset` | `(50, 0)` | Canvas units from that point; positive x puts it to the right, "next to the enemy" |
  | `damageNumberSpread` | `14` | Canvas units that consecutive numbers alternate left and right of the offset, so rapid hits don't sit on each other |
  | `damageNumberMergeSeconds` | `0.12` | A report for the same enemy arriving within this long of its newest number adds into it and re-pops it (shotgun pellets a frame apart) |

- [ ] **8. `DamageNumberView`** (namespace `Overpower.UI`; `MonoBehaviour`; `public static DamageNumberView Create(Transform owner, UiTheme theme, RectTransform canvasRect, TextMeshProUGUI[] labels)`):
  - One slot per label, and a struct array. Each slot holds: label, rect, target, world anchor, spawn time, pop time, amount, marked, side, active.
  - `const int PoolSize = 24`, commented "not a tuning value".
  - Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
  - **The handler:**
    - Ignore when `!theme.showDamageNumbers`, the victim is null, or `amount <= 0`.
    - If the newest slot `ShouldMerge`: add the amount, OR in `marked`, set the pop time to now, and `SetText` again.
    - Otherwise take a free slot or the oldest, anchor it at `victim.position + Vector3.up * theme.damageNumberAnchorHeight`, and take `Side(sequence++)`.
    - Text: `label.SetText("{0:0}", DamageNumberMotion.Shown(amount))`. **Never interpolation or ToString.**
    - Colour: `marked ? theme.markColor : theme.damageNumberColor`.
  - **`LateUpdate`, per active slot:**
    - Past its lifetime: hide.
    - Otherwise `Camera.main.WorldToScreenPoint(anchor)`; hide if `z < 0`.
    - `RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out local)`.
    - `anchoredPosition = local + new Vector2(offset.x + side * spread, offset.y + pose.Rise)`; `localScale = pose.Scale`; alpha from `pose.Alpha`.
  - Class comment covers:
    - shooter-only (`CombatEvents`);
    - the victim's truth;
    - the merge and batching rule, and that dummies report every hit;
    - no allocation per hit.
- [ ] **9. `PlayerHud.BuildUi`,** a new `BuildHitFeedbackCanvas()` called after the HUD canvas is built:
  - A "Hit Feedback Canvas": overlay, `overrideSorting` at order -20, a `CanvasScaler` from the theme (copy `:742-752`), **no `GraphicRaycaster`**.
  - Build `DamageNumberView.PoolSize` labels with `AddLabel(canvas, "", theme.damageNumberTextSize, FontStyles.Bold)`, each with word wrap off, `sizeDelta (200, 60)`, centre pivot, and inactive.
  - Then `DamageNumberView.Create(transform, theme, canvasRect, labels)`.
  - Keep the canvas `RectTransform` in a field for step 5.
- [ ] **10. Green.** **Expect BASE TESTS + 6 + 9.**
- [ ] **11. Single-client Play Mode check** (test range).
  - List the actors. F1: spawn the dummies.
  - A recorder subscribes to `CombatEvents.LocalHitReported` and logs time and amount. It also dumps the view's active slots every frame by reflection: text, colour, scale, `anchoredPosition` and alpha.
  - Aim with `SetAimOverride` at one stationary dummy, fire with `TryFire`, and `ResetToFull` the dummy between sequences.
    - **Weapon 1:** 3 shots. **Expected:** 3 labels, each appearing the frame of the hit; the text equals the rounded `result.Total` from `DummyTarget.AnyDamaged`; pop, rise and fade over about 0.8 s; alternating sides.
    - **Weapon 10 (shotgun), point blank, one pull:** **one** label showing the sum of the pellets.
    - **Weapon 9, a 1 s hold:** labels no more often than one per `damageNumberMergeSeconds`, summing to the total dealt.
    - **Weapon 11:** one label per beam, appearing when the beam lands (0.6 s after the press).
  - Confirm the text reads "21", not "21.0".
  - Force one marked label by invoking the handler by reflection with `cashedMark: true`. It must be bigger and `markColor`.
  - Captures mid-pop to `SCRATCH/step2-numbers-*.png` at 616×576. The controller looks at them. **Tune only theme values if they look wrong; say which and why.**
  - **Two-client:** rows A2 to A4 in step 8.
- [ ] **12. Commit** the eleven files.

---

### Step 3: the Mark rule (pure, red first)

**Files:** `MarkLedger.cs` (create), `MarkLedgerTests.cs` (create), `DamageInfo.cs`, `DamageInfoTests.cs`. Nothing calls these yet, so there is no Play Mode check.

- [ ] **1. Red.** It must fail to compile with `CS0246 ... 'MarkLedger'`. Report that exact message.

```csharp
using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Tudor, 2026-09-18: a hit marks the enemy; your next hit on them within 2 s deals +50% and uses the mark up;
    /// the hit after that marks again, endlessly. One mark per attacker per target, cashable only by whoever placed it.
    /// The ledger lives on the TARGET's own client (damage is victim-side), so "now" is always the target's clock.</summary>
    public class MarkLedgerTests
    {
        private const float Window = 2f;
        private const int Me = 2;
        private const int Other = 3;

        [Test]
        public void TheFirstHitMarks()
        {
            Assert.AreEqual(MarkOutcome.Applied, new MarkLedger().OnLandedHit(Me, 10f, Window));
        }

        [Test]
        public void TheNextHitInsideTheWindowCashesTheMark()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window));
        }

        [Test]
        public void AHitExactlyAtTheWindowEdgeStillCashes()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 12f, Window));
        }

        [Test]
        public void AHitAfterTheWindowMarksAfreshInsteadOfCashing()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Me, 12.01f, Window));
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 13.5f, Window), "the fresh mark counts from 12.01");
        }

        [Test]
        public void TheHitAfterACashMarksAgainSoItAlternatesEndlessly()
        {
            var marks = new MarkLedger();
            var seen = new MarkOutcome[6];
            for (int i = 0; i < seen.Length; i++)
                seen[i] = marks.OnLandedHit(Me, 10f + 0.7f * i, Window);
            CollectionAssert.AreEqual(new[] { MarkOutcome.Applied, MarkOutcome.Cashed, MarkOutcome.Applied,
                                              MarkOutcome.Cashed, MarkOutcome.Applied, MarkOutcome.Cashed }, seen);
        }

        [Test]
        public void OnlyTheAttackerWhoPlacedTheMarkCanCashIt()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Other, 10.3f, Window), "someone else's hit places THEIR mark");
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window), "and leaves mine alone");
        }

        [Test]
        public void EachAttackerKeepsTheirOwnMarkOnTheSameTarget()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            marks.OnLandedHit(Other, 10.2f, Window);
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 10.7f, Window));
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Other, 10.9f, Window));
        }

        [Test]
        public void AHitFromAWeaponThatDoesNotMarkNeitherMarksNorCashes()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(Me, 10.5f, 0f), "X-ray, a rocket, a burn tick");
            Assert.AreEqual(MarkOutcome.Cashed, marks.OnLandedHit(Me, 11f, Window), "the mark survived it");
        }

        [Test]
        public void AnUnknownAttackerNeverMarks()
        {
            var marks = new MarkLedger();
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(0, 10f, Window));
            Assert.AreEqual(MarkOutcome.None, marks.OnLandedHit(-1, 10f, Window));
            Assert.AreEqual(0f, marks.SecondsLeft(0, 10f));
        }

        [Test]
        public void ClearForgetsEveryMark()
        {
            // Death and respawn.
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            marks.OnLandedHit(Other, 10f, Window);
            marks.Clear();
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Me, 10.5f, Window));
            Assert.AreEqual(MarkOutcome.Applied, marks.OnLandedHit(Other, 10.5f, Window));
        }

        [Test]
        public void SecondsLeftCountsDownAndIsZeroOnceCashedOrExpired()
        {
            var marks = new MarkLedger();
            marks.OnLandedHit(Me, 10f, Window);
            Assert.AreEqual(2f, marks.SecondsLeft(Me, 10f), 1e-4f);
            Assert.AreEqual(0.5f, marks.SecondsLeft(Me, 11.5f), 1e-4f);
            Assert.AreEqual(0f, marks.SecondsLeft(Me, 12.5f));
            Assert.AreEqual(0f, marks.SecondsLeft(Other, 10f));
            marks.OnLandedHit(Me, 13f, Window);
            marks.OnLandedHit(Me, 13.5f, Window);
            Assert.AreEqual(0f, marks.SecondsLeft(Me, 13.5f), "cashed");
        }

        [Test]
        public void OnlyACashedHitIsScaledAndNeverMoreThanOnce()
        {
            Assert.AreEqual(21f, MarkLedger.ScaledAmount(21f, MarkOutcome.None, 1.5f));
            Assert.AreEqual(21f, MarkLedger.ScaledAmount(21f, MarkOutcome.Applied, 1.5f));
            Assert.AreEqual(31.5f, MarkLedger.ScaledAmount(21f, MarkOutcome.Cashed, 1.5f), 1e-4f);
            Assert.AreEqual(0f, MarkLedger.ScaledAmount(21f, MarkOutcome.Cashed, -2f), "a negative multiplier never heals");
        }
    }
}
```

- [ ] **2. `DamageInfoTests` +3:**
  - `ADamageInfoDoesNotMarkUnlessToldTo`: the old 8-argument constructor gives `MarkWindowSeconds 0` and `MarkedDamageMultiplier 1`.
  - `WithAmountChangesOnlyTheAmount`: every other field is equal.
  - `AResultWithAMarkKeepsEveryDamageFigure`: `new DamageResult(3, 18, false, false).WithMark(MarkOutcome.Cashed)` keeps all four figures and `Total`, and `Mark == Cashed`. The default is `None`.
- [ ] **3. The rule.**

```csharp
using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>What one landed hit did to its attacker's mark on this target.</summary>
    public enum MarkOutcome : byte
    {
        /// <summary>The weapon does not mark, or the attacker is unknown. Marks are untouched.</summary>
        None = 0,
        /// <summary>No live mark from this attacker, so this hit placed one. Normal damage.</summary>
        Applied = 1,
        /// <summary>This attacker's mark was live: this hit uses it up and deals the bonus.</summary>
        Cashed = 2,
    }

    /// <summary>
    /// The Mark laser's rule (Tudor, 2026-09-18). One TARGET's marks, kept on that target's own client, because
    /// damage is victim-side: the client that applies the damage is the one that decides the bonus, with no message.
    /// PlayerHealth.ApplyDamage asks it only for a hit that LANDS (HitVerdict.Lands), so self, teammate, shield-blocked
    /// hits and hits on the dead neither mark nor cash. Keyed per attacker: at most one mark per attacker, cashable only
    /// by whoever placed it; another attacker's hit places their own mark and leaves yours alone. Cashing never re-marks,
    /// so a steady stream alternates mark, cash, mark... The window is inclusive and measured on this client's clock,
    /// hit landing to hit landing.
    /// No allocation per hit once an attacker has an entry (Dictionary reuses freed slots); entries are bounded by the
    /// players in the room.
    /// </summary>
    public sealed class MarkLedger
    {
        private readonly Dictionary<int, float> expiresAtByAttacker = new Dictionary<int, float>();

        public MarkOutcome OnLandedHit(int attackerActor, float now, float windowSeconds)
        {
            if (attackerActor <= 0 || !(windowSeconds > 0f))
                return MarkOutcome.None;

            if (expiresAtByAttacker.TryGetValue(attackerActor, out float expiresAt) && now <= expiresAt)
            {
                expiresAtByAttacker.Remove(attackerActor);
                return MarkOutcome.Cashed;
            }

            expiresAtByAttacker[attackerActor] = now + windowSeconds;
            return MarkOutcome.Applied;
        }

        public float SecondsLeft(int attackerActor, float now) =>
            expiresAtByAttacker.TryGetValue(attackerActor, out float expiresAt) && now <= expiresAt ? expiresAt - now : 0f;

        public void Clear() => expiresAtByAttacker.Clear();

        public static float ScaledAmount(float amount, MarkOutcome outcome, float markedDamageMultiplier) =>
            outcome == MarkOutcome.Cashed ? amount * System.Math.Max(0f, markedDamageMultiplier) : amount;
    }
}
```

- [ ] **4. `DamageInfo.cs`:**
  - **`DamageInfo`:** append `public readonly float MarkWindowSeconds; public readonly float MarkedDamageMultiplier;` and the constructor parameters `float markWindowSeconds = 0f, float markedDamageMultiplier = 1f` after `abilityId`. Every call site keeps compiling, following the `AbilityId` precedent.
  - Add `public DamageInfo WithAmount(float amount)`, which copies every field.
  - **`DamageResult`:** add `public readonly MarkOutcome Mark;`, a constructor parameter `MarkOutcome mark = MarkOutcome.None`, and `public DamageResult WithMark(MarkOutcome mark)`.
  - Doc comments: 0 means "this hit does not mark"; the fields come from the firing weapon's stat block, read on the victim's client.
- [ ] **5. Green.** **Expect BASE TESTS + 30** (+6 + 9 + 15).
- [ ] **6. Commit** the four files.

---

### Step 4: the Mark in play (laser tree, funnel, credit, shop)

**Files:**
- Weapons: `WeaponDefinition.cs`, the 13 weapon assets (12 renamed), `Hitscan.cs`.
- Funnel and credit: `PlayerHealth.cs`, `DummyTarget.cs`, `PlayerCombatCredit.cs`, `DamageCreditLedger.cs`, `DamageCreditLedgerTests.cs`, `CombatEvents.cs`.
- Shop: `LoadoutScreen.cs`.
- Comment-only: `WeaponFiring.cs`, `ChargeCountRule.cs`, `ChargeCountRuleTests.cs`, `TestRangePanel.cs`, `UiTheme.cs` (tooltip).
- Tests: `WeaponConfigRuleTests.cs`.

- [ ] **1. Rename first,** before any C# change.
  - By eval: `AssetDatabase.RenameAsset("Assets/Gameplay/Weapons/12 Laser - Charge.asset", "12 Laser - Mark")`.
  - Then `git status --short`: a rename of the asset and its `.meta` (the GUID is unchanged).
  - **`git diff -M` on the new path shows only `m_Name`.** If Unity re-serialised more, stop and report.
- [ ] **2. Red: `WeaponConfigRuleTests` +5.** Update the class comment: rules and design pins, still no tuning numbers.
  - `NoWeaponInTheLaserTreeCharges`: walk each weapon's `Parent` chain; any weapon that is id 11 or descends from it must have `!CanCharge`. Tudor, 2026-09-18: charging left the laser tree.
  - `EveryWeaponInTheLaserTreeStillWindsUp`: every one of them has `WindupSeconds > 0`. Tudor: "keep the wind-up".
  - `TheLaserTreeHasAMarkLeaf`: some weapon whose parent is 11 has `MarkWindowSeconds > 0`. Tudor: "the mark upgrade and the xray one".
  - `OnlyBeamWeaponsCarryAMark`: `MarkWindowSeconds > 0` implies `ProjectilePrefab.GetComponent<Hitscan>() != null`.
  - `EveryMarkingWeaponPaysMoreOnTheCash`: `MarkWindowSeconds > 0` implies `MarkedDamageMultiplier > 1`.
  - They fail to compile until the properties exist.
- [ ] **3. `DamageCreditLedgerTests` +1, `ACashedMarkRidesOnlyWithItsOwnAttackersNextDrain`:**
  - `Record(2, 21, 0, false)`, `Record(3, 31.5f, 0, true)`, then `Drain`: actor 2 has `cashedMark` false, actor 3 true.
  - Then `Record(3, 21, 1, false)` and `Drain`: actor 3 false.
  - Existing tests keep compiling: `Drain` becomes a named 3-tuple `(int actor, float amount, bool cashedMark)`.
- [ ] **4. `WeaponDefinition`,** a `[Header("Mark")]` after `chargeMaxProjectiles` and before Feedback:
  - `markWindowSeconds = 0f`. Tooltip: "Seconds a mark lasts. Hitting an enemy marks them; your NEXT hit on them within this many seconds deals Marked Damage Multiplier times the damage and uses the mark up, and the hit after that marks them again. Only you can use your own mark, and marks never stack. Measured between the two hits landing, on the target's own game, so the wind-up doesn't eat into it. 0 = this weapon does not mark. Only beam weapons (a Hitscan component on the Projectile Prefab) read this today."
  - `markedDamageMultiplier = 1f`. Tooltip: "Damage of the hit that uses up a mark, as a multiple of Damage: 1.5 is 50% more. Applied before armour, like any hit's damage, and on top of OverPower's bonus. Ignored while Mark Window Seconds is 0."
  - Read-only properties.
  - `OnValidate` warnings, named: window > 0 with multiplier ≤ 1 ("the mark pays nothing"); window > 0 with no Hitscan on the projectile prefab ("only beams read the mark").
- [ ] **5. The assets (YAML, Rule 13).**
  - In **each of the 13** assets, insert after the `  chargeMaxProjectiles: …` line: `  markWindowSeconds: 0` and `  markedDamageMultiplier: 1`.
  - In `12 Laser - Mark.asset`, the mark lines instead read `2` and `1.5`, and change:
    - `displayName: Mark`
    - `description: Hitting an enemy marks them. Your next hit on them soon after deals bonus damage and uses the mark up; the hit after that marks them again. Fires after a short warning.` (no colon, so no quoting)
    - `canCharge: 0`, `maxChargeSeconds: 0`, `chargeDamageMultiplier: 1`, `chargeRangeMultiplier: 1`
  - **Unchanged:** id, parent, gold cost 1600, damage 21, fire interval 0.7, `windupSeconds 0.6`, the shared projectile prefab, range, overheat.
  - Import, then read back through the catalogue: `Resolve(12)` gives `CanCharge false`, `MarkWindowSeconds 2`, `WindupSeconds 0.6`; 11 and 13 give `MarkWindowSeconds 0`.
  - **`git diff` shows exactly 26 added lines plus weapon 12's 6 changed lines.**
- [ ] **6. `Hitscan.Fire`:**
  - The `DamageInfo` at `:188-190` gains `shot.Weapon.MarkWindowSeconds, shot.Weapon.MarkedDamageMultiplier`.
  - Comment: read from the victim's own copy of the weapon asset, the same build on every client.
  - Reword `ChargedRange`'s comment to "a charging beam weapon". No laser charges now; the maths stays for any future beam.
- [ ] **7. `PlayerHealth`:**
  - Add `private readonly MarkLedger marks = new MarkLedger();`.
  - After the `Shielded` return (`:325-326`):
    - `MarkOutcome mark = marks.OnLandedHit(info.SourceActorNumber, Time.time, info.MarkWindowSeconds);`
    - `DamageInfo landed = mark == MarkOutcome.Cashed ? info.WithAmount(MarkLedger.ScaledAmount(info.Amount, mark, info.MarkedDamageMultiplier)) : info;`
  - Resolve with `landed.Amount`; `result = …WithMark(mark)`; `Damaged?.Invoke(result, landed)`.
  - In the lethal block, `marks.Clear()` **before** `Died?.Invoke(landed)`.
  - `ResetForRespawn`: `marks.Clear()`.
  - Add `public float MarkSecondsLeftFor(int attackerActor) => marks.SecondsLeft(attackerActor, Time.time);`.
  - Comment above the mark line: Tudor's rule, why here (after the verdict, on the victim), and the Decision 2 cases.
- [ ] **8. `DummyTarget`:** the same ledger and the same three lines.
  - When the result is lethal, `marks.Clear()` **before** `NotifyLocalCombatCredit`.
  - `ResetToFull` clears the marks.
  - `AnyDamaged(this, result, landed)`.
  - In `NotifyLocalCombatCredit`:
    - `RaiseHitReported(transform, result.Total, result.Mark == MarkOutcome.Cashed)`
    - `CombatEvents.RaiseMarkReported(transform, marks.SecondsLeft(PhotonNetwork.LocalPlayer.ActorNumber, Time.time))`
- [ ] **9. The credit (the one RPC signature change in this plan):**
  - `DamageCreditLedger.Record(int, float, float, bool cashedMark = false)`: `entry.cashedMark |= cashedMark`. `Drain` returns the 3-tuple and resets the flag.
  - `PlayerCombatCredit`:
    - `HandleDamaged` passes `result.Mark == MarkOutcome.Cashed`.
    - `SendCredit(actor, amount, takedown, cashedMark)` sends `photonView.RPC(nameof(RPC_DamageCredit), attacker, amount, takedown, cashedMark, playerHealth != null ? playerHealth.MarkSecondsLeftFor(actor) : 0f)`.
    - `HandleDied` carries each drained entry's flag (`AmountFor` becomes `EntryFor`).
  - `RPC_DamageCredit(float amount, byte takedown, bool cashedMark, float markSecondsLeft, PhotonMessageInfo info)`:
    - `RaiseHitReported(transform, amount, cashedMark)` inside `amount > 0`.
    - `CombatEvents.RaiseMarkReported(transform, markSecondsLeft)` **always**, after the sender check.
    - Comment, in the house wording of `WeaponFiring:450-454`: parameters appended, the RpcList indexes names, every build must match.
  - `CombatEvents`: `public static event Action<Transform, float> LocalMarkReported;` and `RaiseMarkReported`. Doc: seconds your mark on `victim` has left, 0 = none; nothing listens until mark step 5.
- [ ] **10. `LoadoutScreen.WeaponNumbersText`:**
  - After the wind-up: `if (def.MarkWindowSeconds > 0f) sb.Append($" · mark: next hit within {Compact(def.MarkWindowSeconds)}s +{Compact((def.MarkedDamageMultiplier - 1f) * 100f)}%");`.
  - "hold to charge" disappears from 12 by itself.
- [ ] **11. Comment-only fixes:**
  - `WeaponFiring.cs:306`: "weapon 6 - the only one that CanCharge since the laser tree dropped charging, 2026-09-18".
  - `ChargeCountRule.cs:18`: "a weapon that charges something continuous" (no weapon number).
  - `ChargeCountRuleTests.cs:72`: "A weapon that charges damage or range, not a count."
  - `TestRangePanel.cs:330-332`: the id prefix still separates any two weapons that share a display name.
  - `UiTheme` `chargeRingStepTickColor` tooltip: drop "(the laser's, which ramps damage and range smoothly)". This is a tooltip, so there is no YAML change.
- [ ] **12. Green.** **Expect BASE TESTS + 36.** Check that `git diff -- …PhotonServerSettings.asset` is empty.
- [ ] **13. Single-client Play Mode check** (test range). Recorders log `DummyTarget.AnyDamaged` (`info.Amount`, `result.Mark`, `Time.time`), `LocalHitReported` and `LocalMarkReported`.
  - **Weapon 12, stationary dummy, 3 shots 0.8 s apart,** then `ResetToFull`, 3 more:
    - **Expected `raw`:** 21, 31.5, 21, then 21, 31.5, 21.
    - **Expected mark outcome:** Applied, Cashed, Applied, twice over.
    - The numbers read "21", then a bigger orange "32", then "21".
    - `LocalMarkReported` seconds left are about 2, 0, about 2.
  - **Two hits 2.5 s apart:** both Applied, both 21.
  - **A kill sequence:** the marks are cleared; after the reset, the first hit is Applied. Note the killing blow's number (what was left).
  - **Weapons 11 and 13:** every hit is `None`, 21.
  - **Charge gone:** with 12, hold LMB 1.5 s. `ChargeHeld` stays false, no charge ring is drawn, the shot fires 0.6 s after the press, and `CurrentChargeFraction` is 0.
  - **The shop:** P, hover node 12; capture the hover strip to `SCRATCH/step4-hover.png`. If the numbers line overflows the strip, move the mark text to its own line and re-capture.
  - **Two-client:** rows A5 to A10 in step 8.
- [ ] **14. Commit** every listed file. **Stage the rename as a rename:** the old path's deletion plus the new path and its `.meta`.

---

### Step 5: the mark diamond (the shooter's screen)

**Files:** `MarkIndicatorRule.cs` (create), `MarkIndicatorRuleTests.cs` (create), `MarkIndicatorView.cs` (create), `PlayerHud.cs`, `UiTheme.cs`, `UiTheme.asset`.

- [ ] **1. Red: `MarkIndicatorRuleTests` (3).** `MarkIndicatorRule.Alpha(secondsLeft, totalSeconds, minAlpha, pulseSpeed, time)`:
  - `FullAtTheStartFadingTowardTheFloorAsTheMarkRunsOut`: pulse speed 0. Alpha is 1 at `left == total`, `minAlpha` near 0 left, and strictly in between halfway.
  - `HiddenOnceNoTimeIsLeft`: `left <= 0` gives 0.
  - `ThePulseNeverDropsBelowTheFloorOrAboveOne`: sample 50 times.
- [ ] **2. `MarkIndicatorRule`:** `Lerp(minAlpha, 1, left / total)`, multiplied by a pulse between `minAlpha` and 1 from `sin(time * 2π * pulseSpeed)`, then clamped.
- [ ] **3. `UiTheme`,** a new `[Header("Mark (2026-09-18)")]` after the damage-number fields. Appended to the asset as 4 lines. The colour is `markColor` from step 2.

  | Field | Default | Tooltip gist |
  |---|---|---|
  | `markIndicatorSize` | `22` | Canvas units, the diamond's width and height |
  | `markIndicatorAnchorHeight` | `3.6` | Metres above the enemy: just above the bar over its head |
  | `markIndicatorPulseSpeed` | `2.5` | Pulses per second while the mark is live; 0 = steady |
  | `markIndicatorMinAlpha` | `0.35` | How faint it gets as the mark runs out; 1 = no fade |

- [ ] **4. `MarkIndicatorView`:**
  - `const int PoolSize = 8` (one per possible enemy). Each slot is an Image made from `theme.barSprite`, rotated 45°, `markIndicatorSize` square, `markColor`, `raycastTarget false`.
  - `HandleMarkReported(victim, secondsLeft)`:
    - `<= 0`: hide that victim's diamond if it has one.
    - Otherwise find or take a slot: `expiresAt = now + secondsLeft`, `total = secondsLeft`.
  - `LateUpdate`: hide if the target is destroyed or expired. Otherwise project `target.position + up * markIndicatorAnchorHeight` (it follows the enemy) and apply `MarkIndicatorRule.Alpha`.
  - Class comment: only the shooter sees it; it comes from the victim's truth, never a guess from the shooter's own beam; death sends 0.
- [ ] **5. `PlayerHud`:** build 8 diamonds on the hit-feedback canvas, then `MarkIndicatorView.Create(...)`.
- [ ] **6. Green.** **Expect BASE TESTS + 39.**
- [ ] **7. Single-client check** (dummy, weapon 12):
  - The diamond appears the frame the marking hit lands and hides the frame of the cash-in.
  - After a lone mark, it fades and is gone about 2 s later. It follows a moving (strafer) dummy.
  - On the dummy's death it hides.
  - Capture to `SCRATCH/step5-diamond.png`.
  - **Two-client:** rows A5, A6 and A9.
- [ ] **8. Commit** the six files.

---

### Step 6: telemetry

**Starts only after 2.7b step 9 has landed** (the same aggregator and CSV files). Re-read every line reference.

**Files:** `TelemetryKeys.cs`, `PlayerTelemetry.cs`, `ReportTables.cs`, `TelemetryAggregator.cs`, `CsvReportWriter.cs`, `HtmlReportWriter.cs`, `TelemetryMarkTests.cs` (create), `CsvReportWriterTests.cs`, `docs/superpowers/specs/2026-09-16-telemetry-design.md`.

- [ ] **1. Red:**
  - `TelemetryMarkTests.HitLineCarriesTheMarkOnlyWhenThereIsOne`: through `TelemetryLine`, as `TelemetryPhaseKeysTests` does. A `hit` shape with `mark 2` contains `"mark":2`; `TelemetryKeys.Mark == "mark"`, and no other key uses it.
  - `TelemetryMarkTests.TheWeaponTableCountsMarksPlacedAndCashed`: using the aggregator fixtures, 4 weapon-12 hits (mark 1, 2, 1, 2) and 1 weapon-11 hit (no key). Row 12 is 2/2; row 11 is 0/0.
  - `CsvReportWriterTests.WeaponsCsvHasMarkColumnsAtTheEnd`: `marksPlaced` and `marksCashed` exist, found by header name.
- [ ] **2. Runtime:**
  - `TelemetryKeys.Mark = "mark"`, with a comment: 1 placed, 2 cashed; written only for a hit that touched a mark; a cashed hit's `raw` already includes the bonus.
  - `PlayerTelemetry.WriteHitLine`: `if (result.Mark != MarkOutcome.None) line.Int(TelemetryKeys.Mark, (int)result.Mark);`.
  - This is a new key, not a new event, so `KnownEventNames` is unchanged.
- [ ] **3. Editor:**
  - `HitRow.Mark`, read with default 0.
  - `WeaponRow.MarksPlaced` and `MarksCashed`, counted per weapon in the window.
  - `hits.csv` gets `mark` appended; `weapons.csv` gets `marksPlaced` and `marksCashed` appended.
  - The HTML weapon table gets a "Marks cashed" column next to Accuracy (`~:1201`).
  - The spec gets one paragraph.
- [ ] **4. Green.** **Expect BASE TESTS + 42.** **Every existing telemetry test passes unchanged.**
- [ ] **5. Single-client check:**
  - Record a test-range session: weapon 12 at a dummy (4 hits), then weapon 11.
  - `TelemetryMenu.BuildReport(folder, false)`.
  - The hit lines alternate `mark` 1 and 2 for weapon 12 and carry no `mark` for 11.
  - `weapons.csv` row 12 shows 2 and 2.
  - The tuning header carries `markWindowSeconds` and `markedDamageMultiplier` for every weapon.
  - **Two-client:** Part C in step 8.
- [ ] **6. Commit** the nine files.

---

### Step 7: the assumptions file and progress (outside the repo; never committed)

- [ ] **1.** Under `## Mark laser, damage numbers, yellow bar (2026-09-18)`: the three quotes as `[T]`, every Decision as `[C]` in play terms, and Open #1 to #7 with their defaults, each naming the step that built it.
- [ ] **2.** One line: the Scope plan's charge-laser reach (41.6 m) no longer exists; weapon 12 reaches 26 m.
- [ ] **3.** Add a `progress.md` row with the commit hashes. List the `## ` headings to confirm none was lost.

---

### Step 8: two-client verification

**Before building:**
- `git status --short` must be clean.
- Harness §2:
  1. Switch the runtime server on for the build only.
  2. Set `developmentBuild:true`.
  3. Build `Builds/Client2`.
  4. **Switch the server off at once.**
  5. `git status --short` must be clean again.
- **An old Client2 cannot join these checks,** because `RPC_DamageCredit`'s signature changed in step 4.
- If 2.7b step 11 has not run yet, the controller may run both passes on this one build.

**Recording:**
- Install per-frame recorders on **every receiving client before acting** (CODING-STANDARDS §6: `shot_recorder_tpl.cs` and `sync_recorder_tpl.cs`, extended).
  - **Victim:** `PlayerHealth.Damaged` (`info.Amount`, `result.Total`, `result.Mark`), `ShowsImmuneLook`, the HUD and overhead fill colours, and `PhotonNetwork.ServerTimestamp`.
  - **Shooter:** `LocalHitReported`, `LocalMarkReported`, the beam-draw time (`Hitscan.Fire` via `LaserWarningLine` destroy or `LineRenderer` spawn), the view slots, the victim's overhead fill colours, and `ServerTimestamp`.
- List the actors before each row.
- The Editor is actor 1, the shooter, on team 0. Client2 is the victim, on team 1. Standing targets use `TeleportTo`.

| # | Check | Expected |
|---|---|---|
| A1 | Yellow bar (step 1) | Client2 arms Invulnerability (eval) and the Editor fires weapon 1 once at it. Client2: all four fills `immuneBarColor` for 4 s ±1 frame from the trigger. Editor: Client2's overhead fills turn yellow within one round trip of the trigger (ServerTimestamp terms), for 4 s ± RTT; normal while only armed and after. Capture both screens. |
| A2 | Numbers (step 2) | The Editor hits Client2 5× with weapon 1. Every Client2 hit has a matching Editor report, with equal sums. Each report arrives within about 1 RTT + 1 frame of Client2's hit. **Client2 raises no `LocalHitReported`.** Labels are captured. |
| A3 | SMG merging | Weapon 9 for 2 s. Reports are no more often than one per 0.25 s (+ jitter); the sums match Client2's totals. The first report of the burst arrives within about 1 RTT. |
| A4 | Shotgun | Weapon 10, one point-blank pull. Client2 logs the pellets in 1 or 2 frames; the Editor gets one report and one label (the sum). |
| A5 | Mark (steps 4, 5) | Weapon 12, 4 hits 0.8 s apart on a standing Client2. Client2 `raw`: 21, 31.5, 21, 31.5, with mark 1, 2, 1, 2. Editor reports cashed: false, true, false, true. Orange labels on 2 and 4. The diamond shows after 1 and 3 and hides at 2 and 4. |
| A6 | Expiry | 2 hits 2.5 s apart: both Applied, both 21. The Editor's diamond is gone about 2 s after hit 1. |
| A7 | Shield vs mark | The Editor marks Client2; Client2 arms the shield; the Editor's next hit triggers it (no `Damaged`, no report, no label). Hits during the immunity: nothing. The first hit after it: **Applied** (21). |
| A8 | Teammate | Set Client2's `teamID` to 0 by eval. Laser hits: no `Damaged`, no report, no label, no mark. |
| A9 | Death clears | Mark Client2, then kill it. The death report carries `markSecondsLeft 0` and the diamond hides. After respawn, the first hit is Applied. |
| A10 | Dodge in the wind-up | The Editor fires weapon 12. 0.3 s after `RPC_FireWeapon` arrives on Client2, Client2 teleports 3 m sideways. The Editor's beam draws through the old spot on its own screen; Client2 logs no damage; **no report, no label, no mark.** The next hit that lands is Applied. |

**Part B, three clients (optional).** Client3 also uses weapon 12, on team 2.
- Alternate hits on Client2: Editor, Client3, Editor, Client3.
- **Expected** on Client2: Applied(1), Applied(3), Cashed(1), Cashed(3). Each shooter's diamond shows only their own mark.
- **Fallback:** the pure tests `EachAttackerKeepsTheirOwnMarkOnTheSameTarget` and `OnlyTheAttackerWhoPlacedTheMarkCanCashIt`. Say so in the report.

**Part C, the report.** Build the reports for A5's folders. Client2's log has `hit` lines with `mark` 1 and 2 from attacker 1, weapon 12. `weapons.csv` row 12 shows `marksPlaced 2`, `marksCashed 2`.

**Part D, the final report:**
- Each step's test delta from BASE TESTS (**BASE + 42** in total).
- `git diff --stat BASE..HEAD`, explaining any file not in the file map.
- The RpcList matches `SCRATCH/rpclist-base.txt`.
- `Game Scene.unity` and `Multiplayer Player.prefab` are byte-identical to BASE.
- `UiTheme.asset` differs by exactly 19 added lines.
- The weapon assets differ by 26 added lines, plus weapon 12's 6 changed lines and the rename.
- `RuntimePipelineConfig.json` is off.
- Every row, with its recorder evidence and capture paths.

---

## Risks

- **R1: the RPC signature.**
  - From step 4, mismatched builds fail `RPC_DamageCredit`, and the shooter gets no credit (ultimate charge and armour recharge included).
  - Any cross-machine playtest after step 4 needs every build from the same commit (CODING-STANDARDS §4).
- **R2: the credit timing changes** (Decision 11).
  - Totals and the per-attacker message cap are unchanged. Consumers just hear sooner.
  - `Drain` now runs only when something is pending, so an idle victim allocates nothing.
- **R3: what the shooter sees versus what landed.**
  - A near-dodge in the wind-up (A10) shows the beam crossing the target on the shooter's screen, with no number, no mark and no diamond.
  - That is the victim's truth, the same as damage today, and it is intended.
- **R4: overkill.** The killing blow's number is what was left (Open #5).
- **R5: dummies report every hit.** Numbers on dummies pop more often than on players. Tune looks on real players.
- **R6: `UiTheme.asset` is stale (trap 8c).** YAML edits only: exactly 1, 14 and 4 lines.
- **R7: the rename's re-serialise.** Rename before `WeaponDefinition.cs` changes (step 4.1). If the diff shows more than `m_Name`, stop.
- **R8: the yellow bar follows the bubble.**
  - A late joiner mid-immunity sees neither the bubble nor the yellow (the phase message is not buffered; unchanged).
  - Swapping the ultimate away mid-immunity clears both while the owner stays immune (`Disarm` leaves a running immunity alone). This needs the free warm-up shop; it is accepted.
- **R9: TMP formatting.** Verify "21", not "21.0". Never build strings per hit.
- **R10: line drift.** Other agents commit on this branch; 2.7b step 9 touches step 6's files. Re-read every reference.
- **R11: zero-damage marks.** A marking hit reduced to 0 total by a 100% reduction is never credited, so no diamond. That is an edge case.
- **R12: the stale reach number** in the Scope plan (41.6 m). It is noted in the assumptions file; the old plan doc is not edited.

---

## Order and commits

| Step | What | Tests | Commit |
|---|---|---|---|
| 0 | Start state | BASE | none |
| 1 | The yellow bar during the immunity | +6 | yes |
| 2 | Damage numbers (leading-edge credit, view, dummies) | +9 | yes |
| 3 | The Mark rule and the `DamageInfo`/`DamageResult` fields (pure, red first) | +15 | yes |
| 4 | The Mark in play: laser tree, funnel, credit RPC (+2 parameters), shop line | +6 | yes |
| 5 | The mark diamond on the shooter's screen | +3 | yes |
| 6 | Telemetry: `mark` on `hit` lines, weapon-table counts | +3 | yes |
| 7 | Assumptions and progress (outside the repo) | none | no |
| 8 | Two-client verification (three optional) | none | no |

**Total: BASE TESTS + 42.**

**Dependencies:**
- Steps 1, 2 and 3 are independent of each other and may land in any order.
- Step 4 needs 2 and 3. Step 5 needs 4.
- Step 6 needs 4 and 2.7b step 9.
- Step 8 needs everything.

**Why this order:**
- **The yellow bar comes first:** it is the smallest and fully independent.
- **Numbers come before the Mark:** the +50% is visible the moment step 4 lands, and the credit path is prepared without touching the RPC. That keeps the signature change to one step.
- **The diamond is its own step after the Mark:** it is pure presentation on data step 4 already sends.
