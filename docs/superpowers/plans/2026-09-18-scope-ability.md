# Scope Ability — Spec and Plan

**Requested by Tudor, 2026-09-18.** Written in a separate worktree session (the one that landed the camera zoom-limit
fix, `69bae98`) and handed to the Limit Test controller to build on `limit-testing`. Tudor's spec is reproduced
verbatim below; the controller's notes follow it.

Naming rule: this plan's steps are **"scope step N"** in reports, commits and `progress.md`.

---

## Tudor's spec (verbatim)

New feature for limit-testing: a Scope ability (Tudor's request).

**WHAT I WANT**

Hold right click and your camera zooms out 20% further than normal, so you see more of the arena. Let go and it goes
back. It's for sniper-style builds: Laser, Charge Laser, Baseline and Rockets can already hit targets the shooter
can't see on screen. The 20% has to be ONE plain-tooltipped field on the Scope module prefab, so I can retune it in
the Inspector without touching code. 20% is my number [T]; log any other number you pick as [C].

**ALREADY DECIDED (don't ask again)**

- It's an ability in the Equipment slot (right mouse). Carrying it means giving up mines, cover, raybeam and so on,
  and that trade-off is intended.
- Hold to scope, release to stop. No toggle.
- "Zoom out" means pulling the camera back along its current angle (scale the whole offset), not raising only the
  height. Raising only the height tilts the view top-down and shows roughly 5% more ahead instead of 20%.
- "20% more than other players" means at the SAME scroll zoom, including when fully scrolled out. The scroll zoom is
  clamped by CameraTracking's minZoomMultiplier / maxZoomMultiplier (0.5 / 2, fixed in 69bae98). The scope
  multiplier applies AFTER that clamp. If it just bumped currentZoom, it would do nothing for a player already at the
  scroll limit.
- No cooldown and no heat cost (charges 0, like Sprint). No movement slow, and nothing shown to other players. Scroll
  zoom keeps working while scoped.

**HOW**

- Give CameraTracking a way for another component to add an extra zoom-out on top of the clamp and remove it by the
  same key. Use the same pattern as PlayerMotor.AddSpeedMultiplier / RemoveSpeedMultiplier, so a stale multiplier
  can't survive death or unequip. Ease it in and out over a short tooltipped time instead of snapping.
- Add a ScopeAbility module; SprintAbility is the closest precedent. In OwnerTick, add the multiplier while
  held && canAct and remove it otherwise. Interrupt and OnRespawned clear it. The camera exists only on the owner's
  machine, so ExecuteCast needs nothing and there is no RPC.
- Create a new AbilityDefinition asset, a module prefab and an AbilityCatalogue entry. Use the next free unique id
  (check the catalogue). Gold cost and icon are your call; log them.
- Make sure the new fields are actually written into the module prefab's YAML (re-save it), not only present as C#
  defaults. That's the trap found in ability visuals step 7.
- Add edit-mode tests for the stacking maths: the clamp applies first and the scope after, and removing the key
  returns exactly the unscoped offset.

**VERIFY BY MEASURING, NOT CALCULATING**

My rough geometry (60 degree vertical FOV, offset 0/10/-5): the screen shows about 10 m ahead of the player at 1x
zoom, about 20 m at the 2x scroll limit, and about 24 m at 2x scoped. For comparison, the Laser reaches 26 m (41.6 m
fully charged), Baseline 30 m and Rocket 32 m. Measure the real up-screen reach in the Game view, scoped and
unscoped, at 1x and 2x. Put the measured numbers in progress.md, and save captures so I can see the difference
myself.

Side effect to note, not prevent: zooming out moves GroundPointUnderCursor further out, so a scoped player's
cursor-aimed abilities (Blink, Cursor Rocket) also reach further.

---

## Controller notes (2026-09-18)

**Where it sits in the queue.** After the invulnerability + raybeam rework and before the combined two-client pass.
Two reasons: both changes reshape the Equipment/Ultimate roster (the Raybeam leaves Equipment for Ultimate; Scope
joins Equipment, restoring it to six), so the loadout screen is changed and checked once rather than twice; and both
must land before the two-client build so one Client2 build covers everything. Scope itself needs no two-client check
— the camera is owner-only and nothing crosses the wire.

**What the implementer must know that the spec takes for granted:**

1. **The stacking order is the whole feature.** `CameraTracking.cs` today computes
   `currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed, minZoomMultiplier, maxZoomMultiplier)` and then
   `currentOffset = Quaternion.AngleAxis(yaw, Vector3.up) * (baseOffset * currentZoom)` (lines ~142 and ~147 at
   `69bae98` — read the current file, don't trust these line numbers). The scope multiplier must multiply
   `baseOffset * currentZoom` **after** the clamp and **must not be written back into `currentZoom`**, or the next
   scroll tick re-clamps it away and a player at the 2× limit gains nothing. This is exactly what the red test should
   prove.
2. **Keyed add/remove, like `PlayerMotor.AddSpeedMultiplier`.** Read that implementation first and mirror its
   semantics exactly (what a duplicate key does, what removing an absent key does), so the two systems behave the
   same way and a designer who learned one understands the other.
3. **Easing must not allocate or fight the clamp.** Ease the *applied* scope factor toward its target over a
   tooltipped time; the clamp stays on `currentZoom` alone.
4. **Owner-only, no RPC, no `IPunObservable`.** The camera exists only on the owner's machine. Nothing is added to
   the RpcList.
5. **The prefab re-save trap (ability visuals step 7).** Adding a `[SerializeField]` to a component does **not** write
   it into an existing prefab's YAML; the value then silently comes from the C# initializer, a long-running Editor
   shows a stale value, and a guard test "pinning" it actually reads the code default. The new module prefab must be
   saved *after* every field exists, and the step must end by grepping the prefab YAML for every serialized field
   name and confirming each is present.
6. **Ids cross the wire; filenames do not.** Use the next free explicit `id` in the catalogue and never reuse one.
   Check `AbilityCatalogue.Validate()` is empty afterwards.
7. **The loadout screen.** Equipment goes from five (after the Raybeam leaves) back to six. The grid is
   `Constraint.Flexible` and wraps, but check the hover text: `LoadoutScreen.AbilityNumbersText` must print something
   sensible for an ability with `charges 0` and no cooldown — Sprint's precedent says what that looks like today.
8. **"Nothing shown to other players" includes telemetry.** A scoped player is otherwise indistinguishable. If Scope
   is worth analysing after a playtest (how long players spend scoped), that is a `cast`-style telemetry line from
   the owner, not a network message — flag it as a question for Tudor rather than adding it unasked.

**Measurements the report must contain** (all in the real 616×576 Game view, all measured, none calculated):

| Zoom | Unscoped up-screen reach | Scoped up-screen reach | Ratio |
|---|---|---|---|
| 1× | ? | ? | expect ≈1.20 |
| 2× (scroll limit) | ? | ? | expect ≈1.20 — **the case that proves the multiplier is after the clamp** |

Plus four captures (1× unscoped, 1× scoped, 2× unscoped, 2× scoped) saved for Tudor, and a note on how far a scoped
Blink and Cursor Rocket now reach compared with unscoped.

**Rules:** the same as every other task this session — one Editor at a time; never `editor_focus`; aim only via
`PlayerAim.SetAimOverride`; list the room's actors before any measurement; dirty-scene check before tests, recompile,
a prefab script or Play Mode; tests async only; no unmeasured number in a commit message; stage only your own files;
`[C]` lines to `assumptions-for-tudor.md` under a new `## Scope ability (2026-09-18)` heading.
