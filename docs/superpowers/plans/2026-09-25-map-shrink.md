# The Map Shrinks on a Knockout: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the first team is knocked out (three teams become two), that team's corner of the triangular arena
closes behind a full-height wall with a recess, the centre plays as a Tier III, and every client (late joiners too)
sees the same smaller arena; a host-started two-team match plays on that cut map from going live.

**Architecture:** One new Room Property (`mCut`, the team whose corner is closed) written by the master in the same
write as the three-to-two phase change; everything else is derived on every client from it and the scene. Pure,
edit-mode-tested rules (`PhaseTwoCutRules`) decide which corner and which zones; pure geometry
(`PhaseTwoCutGeometry`) builds the wall line, the new outline and the wall boxes from the arena outline the outer walls
already come from. Existing choke points are widened, not duplicated: `MatchDirector.IsOutOfPlay`, `BuildingManager.
TierOf/TierByZone`, `ArenaSymmetry`'s published outline, `NetworkedDeployable`'s clear pass. A runtime component
(`ArenaPhaseTwoCut`, added by `ArenaSymmetry`, no scene object) builds the wall as primitives and hides what's behind it.

**Tech Stack:** Unity 6000.0.70f1, C#, Photon PUN 2 (Room Properties only; no RPC change), NUnit edit-mode tests
through the `unity` CLI (com.unity.pipeline).

**Spec:** `docs/superpowers/specs/2026-09-25-map-shrink-design.md`. **Tudor's answers and every `[C]` decision:**
`C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md`, section "The map shrinks on a
knockout, 2026-09-25".

---

## Rules for every task (read before each one)

- **Editor lock.** Take it before your first `unity` command, release it after your last (Play Mode stopped, scene not
  dirty):
  `powershell -NoProfile -ExecutionPolicy Bypass -File "C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\editor-lock.ps1" take -Owner "<task name>"`
  (then `release -Owner "<task name>"`). Exit code 1 on `take` = stop and report; never drive the Editor anyway.
- **Tudor uses this computer while you work.** Never rely on the real mouse, keyboard or window focus. Never call
  `editor_focus`. Aim with `PlayerAim.SetAimOverride`; move with `PlayerDisplacement.TeleportTo`; fire and cast
  through public methods (`TryFire`, `PressSlot`, `TryBuildCast`), never synthetic input.
- **List the room's actors before any measurement.** Anyone you didn't put there may be Tudor: stop, leave, report.
- **Tests:** `unity command run_tests -- --mode editor --async_tests true`, then poll `unity command test_status`
  in a bounded foreground loop (never end your turn waiting on a background task). Baseline **1313/1313**.
- **Before tests, a recompile, a build, Play Mode or a scene open:** `unity command list_open_scenes`, read it,
  continue only on `isDirty: false` (a dirty scene opens a modal dialog and the Editor hangs silently).
  `recompile_status` is the only compile truth. Never edit `.cs`, run tests, recompile or build in Play Mode.
- **PowerShell 5.1 mangles double quotes in native arguments:** pass C# to the Editor with `eval_file` from a scratch
  `.cs` in the scratchpad; write commit messages to a file and use `git commit -F`. `eval --timeout` is in ms (30000).
- **A new `[SerializeField]` isn't in an existing asset/scene YAML until re-saved:** add each new asset field as one
  hand-edited YAML line, then `git diff` must show exactly those lines.
- **An asset changed by reflection in Play Mode stays changed in memory** (trap 20): set it back and read it back.
- **Never stop a process by name**, never touch a `UnityCrashHandler64`; stop only PIDs you launched.
  **Never print the Photon App IDs.**
- **Never add, rename or remove an RPC** (the RpcList is index-based). This plan adds none.
- **Tests guard rules, never Tudor's tuning numbers.** No new test may assert a tunable asset value (6.3 m, 19.54 m,
  10 s...). Tests may use literal inputs of their own.
- **Tudor's uncommitted asset changes are his work in progress:** never revert, stash or commit them; `git add` by
  path only. If a commit must touch one of his files, stage HEAD + your lines only (`git show HEAD:<path>` → add your
  lines → `git hash-object -w` → `git update-index --cacheinfo`), then check both diffs.
- **Commit messages** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Push `limit-testing` only.
- **Report back:** what you changed (files), the red-then-green evidence (the failing count/message, then the pass
  count), the final test count, `git status`, anything you decided (one line each, in play terms), anything you could
  not do.

## Decisions (from Tudor's answers; the `[C]` ones are logged for his review)

| # | Decision |
|---|---|
| D1 | The cut is ONE Room Property, `mCut` = the team whose corner is closed, written by the master in the same `SetCustomProperties` call as the ThreeTeams→TwoTeams phase change. A host-started two-team match writes nothing: the left-out team (missing from `mTeams`) is the cut, from going live. |
| D2 | The zones a cut removes are found from the links and the towers' own tiers: the cut team's capital, the Tier II next to it, and every Tier III next to that Tier II. The centre (Tier IV) is never cut. For team 2: zones 8, 2, 4, 5. |
| D3 | Tier IV plays as Tier III while any cut is active (capture time, gold, bounty from `TerritoryConfig`'s Tier III row; three columns; "III" on the minimap). It keeps its own capture radius. |
| D4 | At the knockout the master sets neutral, without bounty history, and resets the in-progress capture of: every Tier III, the centre, and every cut zone (`PhaseTwoCutRules.ZonesToNeutralise`). This replaces `NeutraliseTierThreeZones`. |
| D5 | Which corner is cut: the knocked-out team's, unless that would leave a surviving team with no capital (its only capital is that corner's); then the first team (lowest number) whose corner strands nobody AND whose capital is not held by its own team (nobody lives there). Fallback: the knocked-out team's. |
| D6 | The wall: its face `Phase Two Wall Distance` (6.3 m) past the centre toward the cut capital, from outer wall to outer wall; a recess (`Phase Two Recess Width` 19.54 × `Depth` 3.25 m) centred behind the centre tower; a barrier (`Phase Two Recess Barrier Size` 10.18 × 1 × 0.6) across the middle of the recess mouth. Width or depth 0 = a straight wall; barrier length 0 = no barrier. No planks (Tudor's open question). |
| D7 | The wall is built at runtime, on every client, from the same outline, layout values and tower positions: the same material, thickness, height and layer (`Building`) as the outer walls, under `Source/Boundry/Phase Two Cut` so a portal's path check sees it; the barrier on the `Barrier` layer with the same blocking band as the other barriers. |
| D8 | While a cut stands, `ArenaSymmetry` publishes the playable outline (with the recess) instead of the full one, so blink, portal placement and arrival, and the out-of-arena safety net all respect the wall with no change of their own. |
| D9 | Behind the wall everything disappears: cut zones' towers, rings, capture triggers (in `BuildingCapture`, from `IsOutOfPlay`), and every piece in the `Blocks`, `Barriers` and `Scenery` groups whose position is behind the wall (in `ArenaPhaseTwoCut`). The floor and outer walls stay. This also changes the host-start look: its left-out corner now disappears instead of the capital going grey. |
| D10 | Placed objects: each client destroys what IT owns behind the wall (`RequestDestroy`, owner-only, no RPC). A deployable that follows its caster is skipped. If any of this client's portals is behind the wall, all of its portals go. |
| D11 | Minimap: cut zones' bubbles and links hidden; the closed part darkened and the wall line drawn (one overlay texture over the baked picture, same square of the world); the centre's bubble resized and relabelled "III". |
| D12 | New values have one home each: the wall/recess/barrier numbers in `ArenaLayout.asset` (header "Phase two cut"), the minimap colours/width in `UiTheme.asset`. `ArenaSymmetry` gets a reference to the layout (one scene line). |
| D13 | Every client applies the cut by polling `MatchDirector.CutTeam` once a frame (`ArenaPhaseTwoCut.Update`), so join order, master switches and leaving the room (cut back to none: everything restored) need no event wiring. `MatchDirector` also raises `LiveStateChanged` when the cut changes (the minimap listens already). |
| D14 | Every build in a room must match (a new room key and new behaviour). Note it in HANDOFF like `cFade`. |

## File map

| File | Status | Responsibility |
|---|---|---|
| `Assets/scripts/Match/Rules/PhaseTwoCutRules.cs` | create | Pure: cut team from room facts, cut zones, effective tier, zones to neutralise, which corner to cut |
| `Assets/Tests/PhaseTwoCutRulesTests.cs` | create | Its tests |
| `Assets/scripts/Arena/PhaseTwoCutGeometry.cs` | create | Pure: wall line with recess, playable/closed outlines, wall runs, barrier place |
| `Assets/Tests/PhaseTwoCutGeometryTests.cs` | create | Its tests (toy triangle + the real outline) |
| `Assets/scripts/Arena/ArenaPieceShapes.cs` | create | Pure: a barrier's blocking box, shared by the builder and the runtime cut |
| `Assets/Tests/ArenaPieceShapesTests.cs` | create | Its test |
| `Assets/scripts/Match/MatchDirector.cs` | modify | `mCut` key, `CutTeam`, master write + neutralise at 3→2, `LiveStateChanged` on cut change |
| `Assets/scripts/Match/MatchDirector.Live.cs` | modify | `IsOutOfPlay` gains the cut rule |
| `Assets/scripts/BuildingManager.cs` | modify | `BaseTierOf`, effective `TierOf`/`TierByZone`, `TryGetZoneAt` skips cut zones, `ResetCaptureOf` |
| `Assets/scripts/Player/Building capture.cs` | modify | `EffectiveTier` for capture time + bounty; hide when out of play; runtime columns |
| `Assets/scripts/Arena/TowerLook.cs` | modify | `ApplyColumnsAtRuntime` (re-layout + repaint) |
| `Assets/scripts/Data/ArenaLayout.cs` + `Assets/Gameplay/Config/ArenaLayout.asset` | modify | Phase-two values (4 YAML lines) |
| `Assets/scripts/Arena/ArenaSymmetry.cs` | modify | `layout` reference, `FullBounds`, `UsePlayableBounds`, group-name constants, adds `ArenaPhaseTwoCut` at play |
| `Assets/scripts/Arena/ArenaPhaseTwoCut.cs` | create | Runtime: builds/removes the wall + barrier, swaps the outline, hides pieces, clears own placed objects |
| `Assets/scripts/Abilities/Core/NetworkedDeployable.cs` | modify | `DestroyOwnedWhere` + `FollowsCaster` |
| `Assets/scripts/Abilities/Ultimate/AoeZone.cs`, `ElectricFence.cs` | modify | override `FollowsCaster` |
| `Assets/scripts/Editor/Arena/ArenaPrimitiveBuilder.cs` | modify | use `ArenaPieceShapes`; group-name constants alias `ArenaSymmetry`'s; set `arena.layout` |
| `Assets/Scenes/Game Scene.unity` | modify | ONE line: `ArenaSymmetry.layout` |
| `Assets/Tests/PhaseTwoCutSceneTests.cs` | create | The real scene: for each corner, the survivors' zones open, the cut ones closed, the centre's ring reachable, no staying block through the wall |
| `Assets/scripts/UI/MinimapCutMask.cs` | create | Pure: the overlay's pixels |
| `Assets/Tests/MinimapCutMaskTests.cs` | create | Its tests |
| `Assets/scripts/UI/MinimapView.cs` | modify | hide cut bubbles, tier refresh, the overlay |
| `Assets/scripts/UI/UiTheme.cs` + `Assets/Gameplay/Config/UiTheme.asset` | modify | 3 minimap tokens (3 YAML lines) |
| `Assets/Tests/TowerLookPrefabTests.cs` | modify | the runtime-columns repaint test |

---

### Task 1: The pure rules (`PhaseTwoCutRules`)

**Files:**
- Create: `Assets/scripts/Match/Rules/PhaseTwoCutRules.cs`
- Test: `Assets/Tests/PhaseTwoCutRulesTests.cs`

- [ ] **Step 1: Write the failing tests** (`Assets/Tests/PhaseTwoCutRulesTests.cs`)

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>GDD p.20-21 and p.27 (Tudor, 2026-09-25): the knocked-out team's corner closes. Uses the real arena's
    /// links (the same copy as TerritoryMapTests) and a shuffled-id map, so the rule is shown to follow the links and the
    /// towers' tiers, never zone numbers.</summary>
    public class PhaseTwoCutRulesTests
    {
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 0, 1, 2 }),
            },
            new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

        // The scene's own tiers: 0-2 Tier II, 3-5 Tier III, 6-8 capitals, 9 the centre.
        private static int RealTier(int zone) => zone <= 2 ? 2 : zone <= 5 ? 3 : zone <= 8 ? 1 : zone == 9 ? 4 : 0;

        [Test]
        public void NothingIsCutBeforeTheMatchIsLive()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(live: false, new[] { 0, 1 }, storedCutTeam: 2));
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(false, new int[0], PhaseTwoCutRules.NoCut));
        }

        [Test]
        public void AHostStartCutsTheTeamLeftOutOfTheMatch()
        {
            Assert.AreEqual(1, PhaseTwoCutRules.CutTeam(true, new[] { 0, 2 }, PhaseTwoCutRules.NoCut));
            Assert.AreEqual(2, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1 }, PhaseTwoCutRules.NoCut));
        }

        [Test]
        public void ThreeTeamsAreUncutUntilTheMasterStoresAKnockout()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1, 2 }, PhaseTwoCutRules.NoCut));
            Assert.AreEqual(1, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1, 2 }, storedCutTeam: 1));
        }

        [Test]
        public void TheCutTakesTheCapitalItsTierTwoAndTheTierThreesNextToIt()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.AreEqual(new[] { 2, 4, 5, 8 }, PhaseTwoCutRules.CutZones(map, 2, RealTier));
            CollectionAssert.AreEqual(new[] { 0, 3, 5, 6 }, PhaseTwoCutRules.CutZones(map, 0, RealTier));
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 7 }, PhaseTwoCutRules.CutZones(map, 1, RealTier));
        }

        [Test]
        public void IsZoneCutAgreesWithCutZones()
        {
            TerritoryMap map = RealMap();
            for (int team = 0; team < 3; team++)
            {
                List<int> cut = PhaseTwoCutRules.CutZones(map, team, RealTier);
                for (int zone = 0; zone <= 9; zone++)
                    Assert.AreEqual(cut.Contains(zone), PhaseTwoCutRules.IsZoneCut(map, zone, team, RealTier), $"team {team}, zone {zone}");
            }
        }

        [Test]
        public void TheCentreIsNeverCut()
        {
            TerritoryMap map = RealMap();
            for (int team = 0; team < 3; team++)
                Assert.IsFalse(PhaseTwoCutRules.IsZoneCut(map, 9, team, RealTier));
        }

        [Test]
        public void NoCutMeansNoZones()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.IsEmpty(PhaseTwoCutRules.CutZones(map, PhaseTwoCutRules.NoCut, RealTier));
            Assert.IsFalse(PhaseTwoCutRules.IsZoneCut(map, 8, PhaseTwoCutRules.NoCut, RealTier));
        }

        [Test]
        public void TheCutFollowsTheLinksNotTheZoneNumbers()
        {
            // Team 0's capital is zone 0, its Tier II is 5, which links to Tier III 7 and to the centre 3 (Tier IV).
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)> { (0, new[] { 5 }), (5, new[] { 7, 3 }), (7, new int[0]), (3, new int[0]) },
                new List<(int, int)> { (0, 0) });
            int Tier(int zone) => zone == 0 ? 1 : zone == 5 ? 2 : zone == 7 ? 3 : zone == 3 ? 4 : 0;
            CollectionAssert.AreEqual(new[] { 0, 5, 7 }, PhaseTwoCutRules.CutZones(map, 0, Tier));
        }

        [Test]
        public void TierFourPlaysAsTierThreeOnlyWhileACornerIsCut()
        {
            Assert.AreEqual(3, PhaseTwoCutRules.EffectiveTier(4, cutActive: true));
            Assert.AreEqual(4, PhaseTwoCutRules.EffectiveTier(4, cutActive: false));
            for (int tier = 0; tier <= 3; tier++)
                Assert.AreEqual(tier, PhaseTwoCutRules.EffectiveTier(tier, true), $"tier {tier} never changes");
        }

        [Test]
        public void TheKnockoutNeutralisesEveryTierThreeTheCentreAndTheCutCorner()
        {
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 8, 9 },
                PhaseTwoCutRules.ZonesToNeutralise(RealMap(), 2, RealTier, zoneCount: 10));
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 9 },
                PhaseTwoCutRules.ZonesToNeutralise(RealMap(), PhaseTwoCutRules.NoCut, RealTier, 10), "no cut: the old Tier III reset plus the centre");
        }

        // ownerOfCapital[team] = who holds that team's own starting capital (-1 = nobody).
        private static int Cut(int knockedOut, int[] survivors, params int[] ownerOfCapital) =>
            PhaseTwoCutRules.ChooseCutTeam(knockedOut, survivors, team => ownerOfCapital[team]);

        [Test]
        public void TheKnockedOutTeamsCornerClosesWhenThatStrandsNobody()
        {
            Assert.AreEqual(2, Cut(2, new[] { 0, 1 }, 0, 1, -1), "its capital is neutral");
            Assert.AreEqual(2, Cut(2, new[] { 0, 1 }, 0, 1, 0), "team 0 took it but still holds its own");
        }

        [Test]
        public void ASurvivorLivingInTheKnockedOutCornerKeepsIt()
        {
            // Team 0 lost its own capital to team 1 and lives in team 2's. Team 1 holds its own and team 0's.
            // Team 0's old corner is the one nobody lives in.
            Assert.AreEqual(0, Cut(2, new[] { 0, 1 }, 1, 1, 0));
            // Team 0's old capital is neutral: same answer.
            Assert.AreEqual(0, Cut(2, new[] { 0, 1 }, -1, 1, 0));
        }

        [Test]
        public void ACornerAnotherSurvivorLivesInIsNeverTheAlternative()
        {
            // Team 0 lives only in team 2's capital; team 1 lives only in team 0's; team 1's own capital is empty.
            Assert.AreEqual(1, Cut(2, new[] { 0, 1 }, 1, -1, 0));
        }

        [Test]
        public void NoKnockedOutTeamMeansNoCut()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, Cut(PhaseTwoCutRules.NoCut, new[] { 0, 1 }, 0, 1, 2));
        }
    }
}
```

- [ ] **Step 2: Run the tests; expect a compile failure** (`PhaseTwoCutRules` does not exist: CS0103). Record it.

- [ ] **Step 3: Write the implementation** (`Assets/scripts/Match/Rules/PhaseTwoCutRules.cs`)

```csharp
using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// GDD p.20-21 and its p.27 drawing (Tudor, 2026-09-25): when the first team is knocked out, its corner of the
    /// triangle closes - its capital, its Tier II and the two Tier III next to that Tier II leave play - and the centre
    /// plays as a Tier III. A host-started two-team match plays on that cut map from going live (the left-out team's
    /// corner). Pure C#: every client derives the same answer from the room's facts (MatchDirector.CutTeam), and the
    /// zones are found from the territory links and the towers' own tiers, never from zone numbers.
    /// </summary>
    public static class PhaseTwoCutRules
    {
        public const int NoCut = -1;
        private const int TeamCount = 3;

        /// <summary>The team whose corner is closed: none before live; the one the master stored at the knockout once
        /// there is one; otherwise, in a two-team match, the team left out of it.</summary>
        public static int CutTeam(bool live, IReadOnlyList<int> teamsInMatch, int storedCutTeam)
        {
            if (!live)
                return NoCut;
            if (storedCutTeam >= 0)
                return storedCutTeam;
            if (teamsInMatch == null || teamsInMatch.Count != TeamCount - 1)
                return NoCut;
            for (int team = 0; team < TeamCount; team++)
                if (!Contains(teamsInMatch, team))
                    return team;
            return NoCut;
        }

        /// <summary>The zones a cut removes, ascending: the cut team's capital, the Tier II next to it, and every Tier III
        /// next to that Tier II. The centre (Tier IV) is next to every Tier II but is never cut - it becomes a Tier III.
        /// </summary>
        /// <param name="baseTierOf">A zone's own tier as set on its tower (1-4), never the phase-two stand-in.</param>
        public static List<int> CutZones(TerritoryMap map, int cutTeam, Func<int, int> baseTierOf)
        {
            var zones = new List<int>();
            if (map == null || cutTeam < 0 || baseTierOf == null)
                return zones;
            int capital = map.CapitalOf(cutTeam);
            if (capital < 0)
                return zones;

            zones.Add(capital);
            IReadOnlyList<int> nextToCapital = map.AdjacentTo(capital);
            for (int i = 0; i < nextToCapital.Count; i++)
            {
                int tierTwo = nextToCapital[i];
                if (baseTierOf(tierTwo) != 2)
                    continue;
                AddOnce(zones, tierTwo);
                IReadOnlyList<int> nextToTierTwo = map.AdjacentTo(tierTwo);
                for (int j = 0; j < nextToTierTwo.Count; j++)
                    if (baseTierOf(nextToTierTwo[j]) == 3)
                        AddOnce(zones, nextToTierTwo[j]);
            }
            zones.Sort();
            return zones;
        }

        /// <summary>The same question as CutZones for one zone, without allocating: the ring, the tower and the minimap
        /// ask it every frame (through MatchDirector.IsOutOfPlay).</summary>
        public static bool IsZoneCut(TerritoryMap map, int zone, int cutTeam, Func<int, int> baseTierOf)
        {
            if (map == null || cutTeam < 0 || baseTierOf == null)
                return false;
            int capital = map.CapitalOf(cutTeam);
            if (capital < 0)
                return false;
            if (zone == capital)
                return true;

            IReadOnlyList<int> nextToCapital = map.AdjacentTo(capital);
            for (int i = 0; i < nextToCapital.Count; i++)
            {
                int tierTwo = nextToCapital[i];
                if (baseTierOf(tierTwo) != 2)
                    continue;
                if (zone == tierTwo)
                    return true;
                if (baseTierOf(zone) != 3)
                    continue;
                IReadOnlyList<int> nextToTierTwo = map.AdjacentTo(tierTwo);
                for (int j = 0; j < nextToTierTwo.Count; j++)
                    if (nextToTierTwo[j] == zone)
                        return true;
            }
            return false;
        }

        /// <summary>Tier IV (the centre) plays as Tier III while a corner is cut - GDD p.27 draws a III where the IV was;
        /// Tudor: "no tier 4, only 2 tier 3 in the middle". Every other tier is unchanged.</summary>
        public static int EffectiveTier(int baseTier, bool cutActive) => cutActive && baseTier == 4 ? 3 : baseTier;

        /// <summary>What the master sets neutral (without bounty history) at the knockout, ascending: every Tier III and
        /// the centre (GDD p.21, "all tier 3 territories become neutral" - the centre is one now), plus every cut zone, so
        /// nobody keeps an income or a way in from behind the wall.</summary>
        public static List<int> ZonesToNeutralise(TerritoryMap map, int cutTeam, Func<int, int> baseTierOf, int zoneCount)
        {
            List<int> zones = CutZones(map, cutTeam, baseTierOf);
            if (baseTierOf != null)
                for (int zone = 0; zone < zoneCount; zone++)
                {
                    int tier = baseTierOf(zone);
                    if (tier == 3 || tier == 4)
                        AddOnce(zones, zone);
                }
            zones.Sort();
            return zones;
        }

        /// <summary>
        /// Which corner closes at the knockout ([C], 2026-09-25). The knocked-out team's - unless a surviving team holds
        /// that corner's capital and no other (it lost its own and took theirs): closing it would knock that team out
        /// too. Then the first team (lowest number) whose corner strands nobody and whose capital is not held by its own
        /// team (nobody lives there). Every corner stranding someone can't happen with two survivors; the knocked-out
        /// team's corner is the fallback anyway.
        /// </summary>
        /// <param name="ownerOfCapitalOf">For a team, who holds that team's own starting capital now (-1 = nobody).</param>
        public static int ChooseCutTeam(int knockedOut, IReadOnlyList<int> survivors, Func<int, int> ownerOfCapitalOf)
        {
            if (knockedOut < 0 || ownerOfCapitalOf == null)
                return NoCut;
            if (!StrandsASurvivor(knockedOut, survivors, ownerOfCapitalOf))
                return knockedOut;
            for (int team = 0; team < TeamCount; team++)
            {
                if (team == knockedOut || ownerOfCapitalOf(team) == team)
                    continue;
                if (!StrandsASurvivor(team, survivors, ownerOfCapitalOf))
                    return team;
            }
            return knockedOut;
        }

        // Closing this corner strands a survivor who holds its capital and no other.
        private static bool StrandsASurvivor(int corner, IReadOnlyList<int> survivors, Func<int, int> ownerOfCapitalOf)
        {
            int holder = ownerOfCapitalOf(corner);
            if (holder < 0 || !Contains(survivors, holder))
                return false;
            for (int team = 0; team < TeamCount; team++)
                if (team != corner && ownerOfCapitalOf(team) == holder)
                    return false;
            return true;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value)
                    return true;
            return false;
        }

        private static void AddOnce(List<int> list, int value)
        {
            if (!list.Contains(value))
                list.Add(value);
        }
    }
}
```

- [ ] **Step 4: Recompile (`recompile_status` clean), run the tests: 1313 + 14 new = 1327, all pass.**
- [ ] **Step 5: Commit** `git add` the two new files and their `.meta` files by path;
  message `feat(match): pure rules for the phase-two map cut`.

---

### Task 2: The pure geometry (`PhaseTwoCutGeometry`, `ArenaPieceShapes`)

**Files:**
- Create: `Assets/scripts/Arena/PhaseTwoCutGeometry.cs`, `Assets/scripts/Arena/ArenaPieceShapes.cs`
- Test: `Assets/Tests/PhaseTwoCutGeometryTests.cs`, `Assets/Tests/ArenaPieceShapesTests.cs`

- [ ] **Step 1: Write the failing tests** (`Assets/Tests/PhaseTwoCutGeometryTests.cs`)

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The phase-two wall's shape. A toy equilateral triangle (circumradius 10, centred at (10, 20), apex up)
    /// cut 2 m past the centre toward the apex with a 2 x 1 m recess; then the real arena outline, cut toward each of
    /// the three capitals with the values the design started from (inputs of this test, not Tudor's tuning).</summary>
    public class PhaseTwoCutGeometryTests
    {
        private static readonly Vector2 ToyCentre = new Vector2(10f, 20f);
        private static readonly List<Vector2> Toy = new List<Vector2>
        {
            new Vector2(10f, 30f), new Vector2(10f - 8.660254f, 15f), new Vector2(10f + 8.660254f, 15f),
        };

        private static PhaseTwoCutGeometry ToyCut(float distance = 2f, float width = 2f, float depth = 1f) =>
            PhaseTwoCutGeometry.Build(Toy, ToyCentre, Vector2.up, distance, width, depth, wallThickness: 0.2f);

        [Test]
        public void TheCentreStaysOpenAndTheApexCloses()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.NotNull(cut);
            Assert.Greater(cut.Playable.SignedDistance(ToyCentre), 0f);
            Assert.Less(cut.Closed.SignedDistance(ToyCentre), 0f);
            Vector2 nearApex = new Vector2(10f, 26f);
            Assert.Less(cut.Playable.SignedDistance(nearApex), 0f);
            Assert.Greater(cut.Closed.SignedDistance(nearApex), 0f);
            Assert.IsTrue(cut.IsBehindWall(new Vector3(10f, 0.5f, 26f)));
            Assert.IsFalse(cut.IsBehindWall(new Vector3(10f, 0.5f, 20f)));
        }

        [Test]
        public void TheRecessIsOpenAndBesideItIsClosed()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Vector2 inRecess = new Vector2(10f, 22.5f);
            Assert.Greater(cut.Playable.SignedDistance(inRecess), 0f);
            Assert.IsFalse(cut.IsBehindWall(new Vector3(inRecess.x, 0f, inRecess.y)));
            Vector2 besideRecess = new Vector2(7f, 22.5f);
            Assert.Greater(cut.Closed.SignedDistance(besideRecess), 0f);
        }

        [Test]
        public void TheWallLineRunsFromOuterWallToOuterWallThroughTheRecess()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            ArenaBounds full = ArenaBounds.FromPolygon(Toy);
            Assert.AreEqual(6, cut.WallLine.Count);
            Assert.AreEqual(0f, full.SignedDistance(cut.WallLine[0]), 1e-4f, "starts on the outline");
            Assert.AreEqual(0f, full.SignedDistance(cut.WallLine[5]), 1e-4f, "ends on the outline");
            for (int i = 0; i < 6; i++)
            {
                float past = cut.WallLine[i].y - ToyCentre.y;
                Assert.IsTrue(Mathf.Abs(past - 2f) < 1e-4f || Mathf.Abs(past - 3f) < 1e-4f, $"point {i} is on the wall or the recess back");
            }
        }

        [Test]
        public void OneWallBoxPerSegmentFacingTheOpenSide()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.AreEqual(cut.WallLine.Count - 1, cut.WallRuns.Count);
            for (int i = 0; i < cut.WallRuns.Count; i++)
            {
                ArenaWallPlan.Run run = cut.WallRuns[i];
                Vector2 a = cut.WallLine[i], b = cut.WallLine[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x);
                Assert.AreEqual(0f, Vector2.Dot(run.InnerStart - a, normal), 1e-4f, $"run {i} starts on its segment's line");
                Assert.AreEqual(0f, Vector2.Dot(run.InnerEnd - a, normal), 1e-4f, $"run {i} ends on its segment's line");
                Vector2 middle = (a + b) * 0.5f;
                Assert.Greater(cut.Playable.SignedDistance(middle + run.Inward * 0.05f), 0f, $"run {i} faces the open side");
            }
        }

        [Test]
        public void TheBarrierStandsAcrossTheRecessMouth()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.IsTrue(cut.HasRecess);
            Assert.AreEqual(10f, cut.BarrierCentre.x, 1e-4f);
            Assert.AreEqual(22f, cut.BarrierCentre.y, 1e-4f);
            Vector3 length = Quaternion.Euler(0f, cut.BarrierYawDegrees, 0f) * Vector3.right;
            Assert.AreEqual(0f, length.z, 1e-4f, "its length runs along the wall, across the toy's cut direction (+Z)");
        }

        [Test]
        public void NoRecessMeansAStraightWall()
        {
            PhaseTwoCutGeometry cut = ToyCut(width: 0f);
            Assert.NotNull(cut);
            Assert.IsFalse(cut.HasRecess);
            Assert.AreEqual(2, cut.WallLine.Count);
            Assert.AreEqual(1, cut.WallRuns.Count);
            Assert.Greater(cut.Closed.SignedDistance(new Vector2(10f, 22.5f)), 0f);
        }

        [Test]
        public void AWallThatMissesTheArenaOrARecessThatDoesNotFitIsRefused()
        {
            Assert.IsNull(ToyCut(distance: 20f), "past the apex: the line crosses nothing");
            Assert.IsNull(ToyCut(depth: 50f), "the recess would poke out of the arena");
            Assert.IsNull(ToyCut(width: 30f), "the recess is wider than the wall");
        }

        // ---- the real arena (the Source outline and tower positions of Game Scene, arena rebuild plan section B)

        private static readonly Vector3 ArenaCentre = new Vector3(65.05f, 0f, 53.34f);
        private static readonly List<Vector2> SourceOutline = new List<Vector2>
        {
            new Vector2(96.184f, 60.034f), new Vector2(98.998505f, 61.659004f), new Vector2(89.22851f, 78.581f),
            new Vector2(86.414f, 76.956f), new Vector2(75.451f, 95.944f), new Vector2(75.451f, 121.401f),
            new Vector2(54.649f, 121.401f), new Vector2(54.649f, 95.944f),
        };
        private static readonly Vector2[] Zone =
        {
            new Vector2(35.59f, 36.33f), new Vector2(94.51f, 36.33f), new Vector2(65.05f, 87.36f),   // Tier II 0-2
            new Vector2(65.05f, 32.90f), new Vector2(82.75f, 63.56f), new Vector2(47.35f, 63.56f),   // Tier III 3-5
            new Vector2(15.11f, 24.51f), new Vector2(114.99f, 24.51f), new Vector2(65.05f, 111.00f), // capitals 6-8
            new Vector2(65.05f, 53.34f),                                                              // centre 9
        };
        private static readonly int[][] CutZonesOfTeam = { new[] { 0, 3, 5, 6 }, new[] { 1, 3, 4, 7 }, new[] { 2, 4, 5, 8 } };

        private static PhaseTwoCutGeometry RealCut(int team)
        {
            IReadOnlyList<Vector2> full = ArenaBounds.FromSourceOutline(SourceOutline, ArenaCentre).Polygon;
            Vector2 centre = new Vector2(ArenaCentre.x, ArenaCentre.z);
            return PhaseTwoCutGeometry.Build(full, centre, Zone[6 + team] - centre, 6.3f, 19.54f, 3.25f, 0.724f);
        }

        [Test]
        public void EachCornerCutsItsOwnZonesAndKeepsTheRest([Values(0, 1, 2)] int team)
        {
            PhaseTwoCutGeometry cut = RealCut(team);
            Assert.NotNull(cut);
            for (int zone = 0; zone < Zone.Length; zone++)
            {
                bool shouldClose = System.Array.IndexOf(CutZonesOfTeam[team], zone) >= 0;
                if (shouldClose)
                    Assert.Greater(cut.Closed.SignedDistance(Zone[zone]), 0f, $"zone {zone} is behind team {team}'s wall");
                else
                    Assert.Greater(cut.Playable.SignedDistance(Zone[zone]), 0f, $"zone {zone} stays open when team {team} is cut");
            }
        }

        [Test]
        public void TheCentresWholeCaptureAreaStaysReachable([Values(0, 1, 2)] int team)
        {
            PhaseTwoCutGeometry cut = RealCut(team);
            for (int i = 0; i < 72; i++)
            {
                float radians = i * 5f * Mathf.Deg2Rad;
                Vector2 edge = Zone[9] + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 8f;
                Assert.GreaterOrEqual(cut.Playable.SignedDistance(edge), 0f, $"ring point at {i * 5} degrees");
            }
        }
    }
}
```

And `Assets/Tests/ArenaPieceShapesTests.cs`:

```csharp
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    public class ArenaPieceShapesTests
    {
        [Test]
        public void ABarriersBoxBlocksTheWholeWorldBandWhateverItsLook()
        {
            foreach ((float positionY, float lookHeight) in new[] { (0.5f, 1f), (1f, 2f), (0.25f, 0.5f) })
            {
                ArenaPieceShapes.BarrierBlockingBox(-1f, 3f, positionY, lookHeight, out Vector3 centre, out Vector3 size);
                float worldCentre = positionY + centre.y * lookHeight;
                float worldHeight = size.y * lookHeight;
                Assert.AreEqual(1f, worldCentre, 1e-5f, $"look {lookHeight}");
                Assert.AreEqual(4f, worldHeight, 1e-5f, $"look {lookHeight}");
                Assert.AreEqual(1f, size.x);
                Assert.AreEqual(1f, size.z);
            }
        }
    }
}
```

- [ ] **Step 2: Run; expect compile failure** (`PhaseTwoCutGeometry`, `ArenaPieceShapes` missing). Record it.

- [ ] **Step 3: Write `Assets/scripts/Arena/PhaseTwoCutGeometry.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The phase-two wall's shape (GDD p.27; Tudor, 2026-09-25: "the wall is below the tier 3 zones and make a new
    /// recess for the tier 3 zone that replaces the tier 4 zone"). A straight wall across the arena, its face Wall
    /// Distance past the centre toward the cut capital, from outer wall to outer wall, with a recess (Recess Width x
    /// Recess Depth) centred behind the centre tower. From that one line come: the smaller outline blink, portals and the
    /// out-of-arena safety net ask while the cut stands (Playable), the closed part (the minimap's dark area, and "is
    /// this behind the wall"), one wall box per segment (through ArenaWallPlan, so its corners close exactly like the
    /// outer walls'), and where the recess's barrier stands.
    ///
    /// Plain C#, like ArenaBounds and ArenaWallPlan: every client builds the identical wall from the same outline and
    /// numbers, so no position ever travels over the network. Vector2 = (world X, world Z).
    /// </summary>
    public sealed class PhaseTwoCutGeometry
    {
        /// <summary>The wall's face, from one outer wall to the other: [edge, recess mouth, recess back, recess back,
        /// recess mouth, edge], or just [edge, edge] with no recess.</summary>
        public IReadOnlyList<Vector2> WallLine { get; }

        /// <summary>The arena with the corner cut off and the recess added.</summary>
        public ArenaBounds Playable { get; }

        /// <summary>The part the wall closes off (the recess excluded).</summary>
        public ArenaBounds Closed { get; }

        /// <summary>One box per WallLine segment, facing the open side.</summary>
        public IReadOnlyList<ArenaWallPlan.Run> WallRuns { get; }

        public bool HasRecess { get; }

        /// <summary>The middle of the recess mouth, level with the wall: where its barrier stands.</summary>
        public Vector2 BarrierCentre { get; }

        /// <summary>A Unity yaw whose local +X runs along the wall (a barrier's length axis).</summary>
        public float BarrierYawDegrees { get; }

        private PhaseTwoCutGeometry(List<Vector2> wallLine, ArenaBounds playable, ArenaBounds closed,
                                    List<ArenaWallPlan.Run> runs, bool hasRecess, Vector2 barrierCentre, float barrierYaw)
        {
            WallLine = wallLine;
            Playable = playable;
            Closed = closed;
            WallRuns = runs;
            HasRecess = hasRecess;
            BarrierCentre = barrierCentre;
            BarrierYawDegrees = barrierYaw;
        }

        /// <summary>True for a point behind the wall: inside the old arena, outside the new one. The wall's own body counts
        /// as behind it.</summary>
        public bool IsBehindWall(Vector3 world) => Closed.SignedDistance(world) >= 0f;

        /// <summary>Null when it can't be built: the wall line doesn't cross the outline exactly twice, or the recess
        /// doesn't fit between the outer walls or inside the old arena (with room for its own back wall).</summary>
        /// <param name="fullOutline">The whole arena outline (ArenaSymmetry.FullBounds.Polygon), either winding.</param>
        /// <param name="towardCutCapital">Any vector from the centre toward the cut capital.</param>
        public static PhaseTwoCutGeometry Build(IReadOnlyList<Vector2> fullOutline, Vector2 centre, Vector2 towardCutCapital,
                                                float wallDistance, float recessWidth, float recessDepth, float wallThickness)
        {
            if (fullOutline == null || fullOutline.Count < 3 || towardCutCapital.sqrMagnitude < 1e-8f)
                return null;

            Vector2 u = towardCutCapital.normalized;
            int n = fullOutline.Count;

            // The two outline edges the wall line crosses: "exit" leaves the open side, "entry" comes back into it.
            int exitEdge = -1, entryEdge = -1, crossings = 0;
            for (int i = 0; i < n; i++)
            {
                bool aClosed = Past(fullOutline[i], centre, u, wallDistance) >= 0f;
                bool bClosed = Past(fullOutline[(i + 1) % n], centre, u, wallDistance) >= 0f;
                if (aClosed == bClosed)
                    continue;
                crossings++;
                if (bClosed) exitEdge = i;
                else entryEdge = i;
            }
            if (crossings != 2)
                return null;

            Vector2 exitPoint = CrossingOn(fullOutline[exitEdge], fullOutline[(exitEdge + 1) % n], centre, u, wallDistance);
            Vector2 entryPoint = CrossingOn(fullOutline[entryEdge], fullOutline[(entryEdge + 1) % n], centre, u, wallDistance);
            Vector2 along = (entryPoint - exitPoint).normalized;
            Vector2 mouthMiddle = centre + u * wallDistance;

            var wallLine = new List<Vector2> { exitPoint };
            bool hasRecess = recessWidth > 0f && recessDepth > 0f;
            if (hasRecess)
            {
                Vector2 half = along * (recessWidth * 0.5f);
                Vector2 mouthNear = mouthMiddle - half;
                Vector2 mouthFar = mouthMiddle + half;
                if (Vector2.Dot(mouthNear - exitPoint, along) <= 0f || Vector2.Dot(entryPoint - mouthFar, along) <= 0f)
                    return null;
                Vector2 backNear = mouthNear + u * recessDepth;
                Vector2 backFar = mouthFar + u * recessDepth;
                ArenaBounds full = ArenaBounds.FromPolygon(fullOutline);
                if (full.SignedDistance(backNear) < wallThickness || full.SignedDistance(backFar) < wallThickness)
                    return null;
                wallLine.Add(mouthNear);
                wallLine.Add(backNear);
                wallLine.Add(backFar);
                wallLine.Add(mouthFar);
            }
            wallLine.Add(entryPoint);

            // Playable: the wall line first (so ArenaWallPlan.ForSource returns exactly its runs, corners included),
            // then the open side of the outline in its own order, back to where the wall starts.
            var playable = new List<Vector2>(wallLine);
            for (int i = (entryEdge + 1) % n; ; i = (i + 1) % n)
            {
                playable.Add(fullOutline[i]);
                if (i == exitEdge)
                    break;
            }

            // Closed: from the exit point round the closed side of the outline, then back along the wall line.
            var closed = new List<Vector2> { exitPoint };
            for (int i = (exitEdge + 1) % n; ; i = (i + 1) % n)
            {
                closed.Add(fullOutline[i]);
                if (i == entryEdge)
                    break;
            }
            for (int i = wallLine.Count - 1; i >= 1; i--)
                closed.Add(wallLine[i]);

            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(playable, wallLine.Count - 1, wallThickness);
            float barrierYaw = Quaternion.LookRotation(new Vector3(-u.x, 0f, -u.y), Vector3.up).eulerAngles.y;

            return new PhaseTwoCutGeometry(wallLine, ArenaBounds.FromPolygon(playable), ArenaBounds.FromPolygon(closed),
                                           runs, hasRecess, mouthMiddle, barrierYaw);
        }

        // Metres past the wall line (positive = the closed side).
        private static float Past(Vector2 p, Vector2 centre, Vector2 u, float wallDistance) =>
            Vector2.Dot(p - centre, u) - wallDistance;

        private static Vector2 CrossingOn(Vector2 a, Vector2 b, Vector2 centre, Vector2 u, float wallDistance)
        {
            float pa = Past(a, centre, u, wallDistance);
            float pb = Past(b, centre, u, wallDistance);
            return a + (b - a) * (pa / (pa - pb));
        }
    }
}
```

- [ ] **Step 4: Write `Assets/scripts/Arena/ArenaPieceShapes.cs`**

```csharp
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Box maths shared by the edit-time arena builder and the play-time phase-two cut, so a barrier built
    /// either way blocks exactly the same band.</summary>
    public static class ArenaPieceShapes
    {
        /// <summary>A barrier's BoxCollider, in its own (scaled) space, that blocks from world bottomY to world topY
        /// whatever its look: the barrier's origin sits at world positionY and its look is lookHeight tall (its Y
        /// scale). Amendment 1: only a living player's body collides with the Barrier layer, so a tall band costs
        /// nothing and nobody can be knocked up onto the barrier.</summary>
        public static void BarrierBlockingBox(float bottomY, float topY, float positionY, float lookHeight,
                                              out Vector3 centre, out Vector3 size)
        {
            float blockingCentreYWorld = (bottomY + topY) * 0.5f;
            float blockingHeightWorld = topY - bottomY;
            float scaleY = Mathf.Max(0.0001f, lookHeight);
            centre = new Vector3(0f, (blockingCentreYWorld - positionY) / scaleY, 0f);
            size = new Vector3(1f, blockingHeightWorld / scaleY, 1f);
        }
    }
}
```

- [ ] **Step 5: Make `ArenaPrimitiveBuilder` use it** (`Assets/scripts/Editor/Arena/ArenaPrimitiveBuilder.cs:325-332`): replace
  the four lines computing `blockingCentreYWorld`, `blockingHeightWorld`, `scaleY` and the two `box.` assignments with

```csharp
                    BoxCollider box = barrier.GetComponent<BoxCollider>();
                    ArenaPieceShapes.BarrierBlockingBox(layout.BarrierBlockingBottomY, layout.BarrierBlockingTopY,
                        position.y, piece.size.y, out Vector3 boxCentre, out Vector3 boxSize);
                    box.center = boxCentre;
                    box.size = boxSize;
```
  Behaviour unchanged (the existing `ArenaPrimitiveBuilderTests`/`ArenaPrimitiveSceneStep5Tests` stay green).

- [ ] **Step 6: Recompile, run the tests.** Expected: 1327 + 11 geometry (7 toy + 3 + 3 parameterised... count what
  NUnit reports) + 1 = all pass. If `EachCornerCutsItsOwnZonesAndKeepsTheRest` or the ring test fails, **stop and report
  the failing zone and distances** - do not change the design numbers.
- [ ] **Step 7: Commit** the four new files (+ `.meta`) and the builder by path;
  message `feat(arena): pure geometry for the phase-two wall and recess`.

---

### Task 3: The match wiring (the room key, out of play, tiers, the knockout write)

**Networking task: reviewed by opus.** Read `MatchDirector.cs` and `MatchDirector.Live.cs` in full first; the echo
window (`writesAwaitingEcho`/`lastWritten*`) and the "record state before reacting" order in `ReactToRoomState` must be
kept exactly.

**Files:**
- Modify: `Assets/scripts/Match/MatchDirector.cs`, `Assets/scripts/Match/MatchDirector.Live.cs`,
  `Assets/scripts/BuildingManager.cs`, `Assets/scripts/Player/Building capture.cs`

- [ ] **Step 1: `BuildingManager`** (`BuildingManager.cs:168-203`):
  - Add `BaseTierOf(int zone)` = today's `TierOf` body (`capture.tier`, 0 when unregistered), with a comment: "the
    tower's own tier as set in the scene - PhaseTwoCutRules finds the cut from this, never from the phase-two stand-in".
  - `TierOf(zone)` becomes `PhaseTwoCutRules.EffectiveTier(BaseTierOf(zone), IsCutActive)`, where
    `private static bool IsCutActive => MatchDirector.Instance != null && MatchDirector.Instance.CutTeam >= 0;`.
    Update its doc comment (the centre plays as Tier III while a corner is cut).
  - `TierByZone()` keeps ONE array instance but refills it in place when the cut state flips:
```csharp
    private bool tierCacheCutActive;

    public int[] TierByZone()
    {
        if (tierByZoneCache == null || tierCacheCutActive != IsCutActive)
            RebuildTierByZoneCache();
        return tierByZoneCache;
    }

    private void RebuildTierByZoneCache()
    {
        if (tierByZoneCache == null || tierByZoneCache.Length != ZoneCount)
            tierByZoneCache = new int[ZoneCount];
        tierCacheCutActive = IsCutActive;
        for (int zone = 0; zone < ZoneCount; zone++)
            tierByZoneCache[zone] = TierOf(zone);
    }
```
    Extend `tierByZoneCache`'s comment: the second thing that changes a zone's tier is a corner being cut.
  - `TryGetZoneAt` (`:216-234`): skip a zone that is out of play, first line in the loop:
    `if (MatchDirector.Instance != null && MatchDirector.Instance.IsOutOfPlay(pair.Key)) continue;` (a cut Tier III's
    capture area pokes through the new wall; standing near the wall must not count as being in it).
  - Add, near `SetNeutralWithoutBountyHistory`:
```csharp
    /// <summary>Master only: resets one tower's own capture state to owner (-1 = neutral, progress 0) and republishes
    /// it, so a capture in flight can't complete after a reset (the phase-two knockout reuses the going-live reset).</summary>
    public void ResetCaptureOf(int zone, int owner)
    {
        if (captures.TryGetValue(zone, out BuildingCapture capture) && capture != null)
            capture.ResetForMatchStart(owner);
    }
```
- [ ] **Step 2: `BuildingCapture`** (`Building capture.cs`):
  - Add after `tier`'s declaration:
```csharp
    /// <summary>The tier this tower plays as right now (PhaseTwoCutRules.EffectiveTier): its own, except the centre plays
    /// as Tier III while a corner is cut. Capture time and the bounty read this.</summary>
    public int EffectiveTier =>
        PhaseTwoCutRules.EffectiveTier(tier, MatchDirector.Instance != null && MatchDirector.Instance.CutTeam >= 0);
```
  - `CaptureSeconds` (`:43-44`) reads `territoryConfig.ForTier(EffectiveTier)`; the bounty (`:689`) reads
    `territoryConfig.ForTier(EffectiveTier).captureBounty`. `tier`'s tooltip gains: "The centre (4) plays as 3 once a
    corner is cut."
- [ ] **Step 3: `MatchDirector.cs`**:
  - Key and reader, next to `WinnerKey` (`:45`):
```csharp
        /// Room Property key: int, the team whose corner of the arena is closed (PhaseTwoCutRules). Written by the master
        /// in the SAME write as the three-to-two phase change, so no client ever sees two teams without the wall. Absent
        /// before any knockout and in a host-started two-team match (the left-out team is the cut, derived - see CutTeam).
        public const string CutTeamKey = "mCut";
```
    and beside `ReadWinner`:
    `private static int ReadCutTeam(Hashtable props) => props.TryGetValue(CutTeamKey, out object raw) && raw is int t ? t : PhaseTwoCutRules.NoCut;`
  - In `MatchDirector.Live.cs`, next to `TeamsInMatch`:
```csharp
        /// <summary>The team whose corner is closed right now, or PhaseTwoCutRules.NoCut. Reads the room directly (like
        /// IsLive), so it is right for a late joiner on its very first frame and after a master switch.</summary>
        public int CutTeam => PhotonNetwork.InRoom
            ? PhaseTwoCutRules.CutTeam(IsLive, TeamsInMatch, ReadCutTeam(PhotonNetwork.CurrentRoom.CustomProperties))
            : PhaseTwoCutRules.NoCut;
```
  - `IsOutOfPlay` (`MatchDirector.Live.cs:80-87`) keeps the capital rule and adds the cut (update its comment: "a zone
    behind the phase-two wall, or the cut capital of a host start"):
```csharp
        public bool IsOutOfPlay(int zone)
        {
            BuildingManager buildings = BuildingManager.Instance;
            TerritoryMap map = buildings != null ? buildings.Map : null;
            int capitalTeam = map != null ? map.CapitalTeamOf(zone) : TerritoryMap.Neutral;
            if (MatchStartRules.IsCapitalOutOfPlay(IsLive, capitalTeam, TeamsInMatch))
                return true;
            return map != null && PhaseTwoCutRules.IsZoneCut(map, zone, CutTeam, BaseTierOf);
        }

        // Cached once: IsOutOfPlay runs every frame for every tower and minimap bubble, and a fresh method-group delegate
        // per call would allocate each time.
        private System.Func<int, int> baseTierOf;
        private System.Func<int, int> BaseTierOf => baseTierOf ??= zone =>
            BuildingManager.Instance != null ? BuildingManager.Instance.BaseTierOf(zone) : 0;
```
  - `MasterRecompute` (`:392-409`): decide the cut before the write, put it in the same Hashtable, and replace the
    Tier III reset:
```csharp
            bool phaseTwoStarts = previousPhase == MatchPhase.ThreeTeams && result.Phase == MatchPhase.TwoTeams;
            int cutTeam = phaseTwoStarts ? ChooseCutTeam(buildings, newlyEliminated, result, statuses) : PhaseTwoCutRules.NoCut;

            var props = new Hashtable
            {
                { PhaseKey, (int)result.Phase },
                { EliminatedKey, result.Eliminated.ToArray() },
                { WinnerKey, result.Winner },
            };
            if (cutTeam >= 0)
                props[CutTeamKey] = cutTeam;
            ...
            if (phaseTwoStarts)
                NeutraliseForPhaseTwo(buildings, cutTeam);
```
    and replace `NeutraliseTierThreeZones` (`:414-428`) with (keep and extend its comment: why no bounty history; now
    also the centre and the cut corner, and the in-progress capture reset):
```csharp
        private static void NeutraliseForPhaseTwo(BuildingManager buildings, int cutTeam)
        {
            List<int> zones = PhaseTwoCutRules.ZonesToNeutralise(buildings.Map, cutTeam, buildings.BaseTierOf, buildings.ZoneCount);
            foreach (int zone in zones)
            {
                buildings.SetNeutralWithoutBountyHistory(zone);
                buildings.ResetCaptureOf(zone, TerritoryMap.Neutral);
            }
        }

        /// <summary>D5: the knocked-out team's corner, unless that strands a surviving team - see PhaseTwoCutRules.
        /// ChooseCutTeam. Reads the master's own current snapshot, the same basis Recompute just used.</summary>
        private static int ChooseCutTeam(BuildingManager buildings, List<int> newlyEliminated, MatchPhaseResult result,
                                         TeamStatus[] statuses)
        {
            if (newlyEliminated.Count == 0)
                return PhaseTwoCutRules.NoCut;
            var survivors = new List<int>();
            foreach (TeamStatus status in statuses)
                if (status.InMatch && !result.Eliminated.Contains(status.TeamId))
                    survivors.Add(status.TeamId);
            TerritoryMap map = buildings.Map;
            TerritorySnapshot current = buildings.Current;
            return PhaseTwoCutRules.ChooseCutTeam(newlyEliminated[0], survivors, team =>
            {
                int capital = map.CapitalOf(team);
                return capital >= 0 ? current.OwnerOf(capital) : TerritoryMap.Neutral;
            });
        }
```
  - `OnRoomPropertiesUpdate` (`:243-244`): add `|| propertiesThatChanged.ContainsKey(CutTeamKey)` to
    `touchesElimination` (it only ever arrives in the same write as `mPhase`, so this decrements the same one echo).
  - `ReactToRoomState`: read `int cutTeam = CutTeam;`, keep `int prevCutTeam = lastAppliedCutTeam;`, write
    `lastAppliedCutTeam = cutTeam;` together with the other `lastApplied*` fields (BEFORE any reaction - see the
    method's review-fix comment), and add `|| cutTeam != prevCutTeam` to the `LiveStateChanged` condition (`:615`).
    Declare `private int lastAppliedCutTeam = PhaseTwoCutRules.NoCut;` with the other read-side fields and reset it in
    `OnLeftRoom`. Update the method's doc comment (one line: the cut changing also raises LiveStateChanged, which the
    minimap listens to).
- [ ] **Step 4: Recompile; run the tests** (count unchanged from Task 2 + nothing broken; `TerritoryMapTests`,
  `MatchPhaseRulesTests`, `CaptureRingStateTests` green).
- [ ] **Step 5: Grep gates:** `[PunRPC]` count unchanged (31); no `PhotonNetwork.Instantiate`/`RPC(` added.
- [ ] **Step 6: Commit** the four files by path; message `feat(match): the phase-two cut in the room, out of play and tiers`.

---

### Task 4: The arena view (the wall, the outline, what's behind it, placed objects)

**Files:**
- Modify: `Assets/scripts/Data/ArenaLayout.cs`, `Assets/Gameplay/Config/ArenaLayout.asset` (4 lines),
  `Assets/scripts/Arena/ArenaSymmetry.cs`, `Assets/scripts/Editor/Arena/ArenaPrimitiveBuilder.cs`,
  `Assets/scripts/Abilities/Core/NetworkedDeployable.cs`, `Assets/scripts/Abilities/Ultimate/AoeZone.cs`,
  `Assets/scripts/Abilities/Ultimate/ElectricFence.cs`, `Assets/Scenes/Game Scene.unity` (1 line)
- Create: `Assets/scripts/Arena/ArenaPhaseTwoCut.cs`, `Assets/Tests/PhaseTwoCutSceneTests.cs`

- [ ] **Step 1: `ArenaLayout`** - after the Floor header's fields, add (fields `[SerializeField] private` with read-only
  accessors, like the rest of the class):
```csharp
        [Header("Phase two cut (a team knocked out)")]
        [Tooltip("How far past the arena centre, toward the knocked-out team's capital, the phase-two wall's face " +
                 "stands, in metres. Tudor 2026-09-25: below the two side Tier III, so they and their recesses end up " +
                 "behind it (they start about 7.6 m and 6.7 m past the centre). Larger keeps more of the arena open. " +
                 "Seen in Play Mode only: the wall is built when a corner closes.")]
        [SerializeField, Min(0f)] private float phaseTwoWallDistance = 6.3f;

        [Tooltip("The recess in the phase-two wall behind the centre tower (the centre plays as a Tier III once a " +
                 "corner is cut): its width along the wall, in metres - the same as the outer walls' Tier III " +
                 "recesses. 0 = a straight wall, no recess.")]
        [SerializeField, Min(0f)] private float phaseTwoRecessWidth = 19.54f;

        [Tooltip("How deep that recess goes past the wall's face, in metres - like the other Tier III recesses, so a " +
                 "portal fits. 0 = no recess.")]
        [SerializeField, Min(0f)] private float phaseTwoRecessDepth = 3.25f;

        [Tooltip("The jersey barrier across the middle of the recess mouth, level with the wall: x = length, y = how " +
                 "tall it looks, z = thickness - the same meaning as a Barrier row (the Zone 4 recess's barrier is " +
                 "10.18 x 1 x 0.6). Length 0 = no barrier.")]
        [SerializeField] private Vector3 phaseTwoRecessBarrierSize = new Vector3(10.181736f, 1f, 0.6f);

        public float PhaseTwoWallDistance => phaseTwoWallDistance;
        public float PhaseTwoRecessWidth => phaseTwoRecessWidth;
        public float PhaseTwoRecessDepth => phaseTwoRecessDepth;
        public Vector3 PhaseTwoRecessBarrierSize => phaseTwoRecessBarrierSize;
```
  and in `ArenaLayout.asset`, after `floorThickness: 4`, exactly these four lines:
```
  phaseTwoWallDistance: 6.3
  phaseTwoRecessWidth: 19.54
  phaseTwoRecessDepth: 3.25
  phaseTwoRecessBarrierSize: {x: 10.181736, y: 1, z: 0.6}
```
- [ ] **Step 2: `ArenaSymmetry`**:
  - Constants next to `BoundaryGroupName`: `BlocksGroupName = "Blocks"`, `BarriersGroupName = "Barriers"`,
    `SceneryGroupName = "Scenery"`; in `ArenaPrimitiveBuilder` make `BlocksGroupName`, `BarriersGroupName`,
    `SceneryGroupName` alias them (`= ArenaSymmetry.BlocksGroupName`, like `BoundryGroupName` already does).
  - `public ArenaLayout layout;` with tooltip "The arena's build data (Assets/Gameplay/Config/ArenaLayout.asset). In
    play, the phase-two wall is built from its wall, barrier and Phase Two Cut values, so it matches what Build primitive
    arena makes." (`using Overpower.Data;`).
  - `public ArenaBounds FullBounds { get; private set; }` ("the whole arena's outline, whatever is cut");
    `OnEnable` sets `FullBounds` from the outline and `Bounds = FullBounds`, then
    `if (GetComponent<ArenaPhaseTwoCut>() == null) gameObject.AddComponent<ArenaPhaseTwoCut>();`
  - ```csharp
        /// <summary>While a corner is closed (ArenaPhaseTwoCut), the published outline is the smaller one, so blink,
        /// portals and the out-of-arena safety net all see the wall with no change of their own. Null restores the full
        /// outline.</summary>
        public void UsePlayableBounds(ArenaBounds playable) => Bounds = playable ?? FullBounds;
    ```
  - Update the class comment's "During play this component has exactly one job" paragraph: it publishes the outline
    (the smaller one while a corner is closed) and adds `ArenaPhaseTwoCut`.
  - `ArenaPrimitiveBuilder`: where `BuildAll` has both the arena and the loaded layout, add `arena.layout = layout;`
    (before the scene is marked dirty) so a rebuild keeps the reference.
- [ ] **Step 3: The scene line.** Under the lock, with the scene not dirty, append to the `ArenaSymmetry` MonoBehaviour
  block (script guid `14b536354509c3c4c84a58888d256540`, after its `sourceOutline:` list) exactly:
  `  layout: {fileID: 11400000, guid: de6419cc10bef0f4f80cb54cfda7d344, type: 2}`
  (the guid is `ArenaLayout.asset.meta`'s). Then `unity command open_scene` on `Assets/Scenes/Game Scene.unity`
  (reloads from disk) and read `ArenaSymmetry.layout` back by eval: not null, named `ArenaLayout`. `git diff` on the
  scene = exactly that one added line.
- [ ] **Step 4: `NetworkedDeployable`** (`NetworkedDeployable.cs`, after `DestroyAllPlacedByLocalPlayer`):
```csharp
        /// <summary>A deployable that moves with its caster (the AoE zone; a fence set to follow) is never "left
        /// behind" anywhere, so a pass by position skips it.</summary>
        protected virtual bool FollowsCaster => false;

        /// <summary>Tudor, 2026-09-25: when a corner closes, whatever THIS client placed behind the new wall goes, through
        /// the same single-destroyer path as every other end of life (RequestDestroy - owner only, so every client can run
        /// the same pass and only the owner's copy acts; no RPC). If any of this client's portals is behind the wall, all
        /// of them go: a pair with one end in the closed corner would be a way through the wall.</summary>
        public static void DestroyOwnedWhere(System.Func<Vector3, bool> isGone)
        {
            if (isGone == null)
                return;
            bool aPortalWentBehind = false;
            foreach (NetworkedDeployable deployable in FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None))
            {
                if (!deployable.IsOwnerClient || deployable.FollowsCaster || !isGone(deployable.transform.position))
                    continue;
                if (deployable is Portal)
                    aPortalWentBehind = true;
                deployable.RequestDestroy();
            }
            if (!aPortalWentBehind)
                return;
            foreach (NetworkedDeployable deployable in FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None))
                if (deployable.IsOwnerClient && deployable is Portal)
                    deployable.RequestDestroy();
        }
```
  In `AoeZone` and `ElectricFence`: `protected override bool FollowsCaster => followsCaster;` next to their field.
- [ ] **Step 5: Write `Assets/scripts/Arena/ArenaPhaseTwoCut.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Data;
using Overpower.Match;

namespace Overpower.Arena
{
    /// <summary>
    /// Builds and removes the phase-two wall (GDD p.20-21 and p.27; Tudor, 2026-09-25) on every client from one
    /// replicated number, MatchDirector.CutTeam - so a late joiner and a new master get the same wall with no message
    /// of their own. Added at play by ArenaSymmetry.OnEnable: no scene object.
    ///
    /// On a cut: builds the wall boxes and the recess barrier as primitives with the outer walls' own material,
    /// thickness, height and layers (under Source/Boundry, so a portal's path check sees the wall); publishes the smaller
    /// outline (ArenaSymmetry.UsePlayableBounds) for blink, portals and the safety net; hides every block, barrier and
    /// scenery piece whose position is behind the wall; and destroys what this client placed there. Towers behind the
    /// wall hide themselves (BuildingCapture, from MatchDirector.IsOutOfPlay). On "no cut" (a new room), everything is
    /// put back exactly as it was.
    ///
    /// Polls once a frame instead of subscribing: the cut can appear on a join, a knockout or a host start and go away
    /// on leaving the room, and MatchDirector, BuildingManager and the towers start in no fixed order - one integer
    /// compare a frame catches every case.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaPhaseTwoCut : MonoBehaviour
    {
        public const string GroupName = "Phase Two Cut";
        private static readonly string[] HiddenGroups =
            { ArenaSymmetry.BlocksGroupName, ArenaSymmetry.BarriersGroupName, ArenaSymmetry.SceneryGroupName };

        /// <summary>The running arena's cut, or null outside Play Mode. The minimap reads Geometry from it.</summary>
        public static ArenaPhaseTwoCut Active { get; private set; }

        /// <summary>The team whose corner this client has closed, or PhaseTwoCutRules.NoCut.</summary>
        public int AppliedCutTeam { get; private set; } = PhaseTwoCutRules.NoCut;

        /// <summary>The standing wall's shape, or null while nothing is cut.</summary>
        public PhaseTwoCutGeometry Geometry { get; private set; }

        private ArenaSymmetry arena;
        private Transform built;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();
        private int failedCutTeam = PhaseTwoCutRules.NoCut; // logs a refused build once, not every frame

        private void OnEnable()
        {
            arena = GetComponent<ArenaSymmetry>();
            Active = this;
        }

        private void OnDisable()
        {
            Restore();
            AppliedCutTeam = PhaseTwoCutRules.NoCut;
            if (Active == this)
                Active = null;
        }

        private void Update()
        {
            int cut = MatchDirector.Instance != null ? MatchDirector.Instance.CutTeam : PhaseTwoCutRules.NoCut;
            if (cut == AppliedCutTeam)
                return;

            if (cut < 0)
            {
                Restore();
                AppliedCutTeam = PhaseTwoCutRules.NoCut;
                return;
            }

            PhaseTwoCutGeometry geometry = BuildGeometryFor(cut);
            if (geometry == null)
                return; // towers not registered yet, or refused (logged once): try again next frame

            Restore();
            Apply(geometry);
            AppliedCutTeam = cut;
            Debug.Log($"[Arena] phase two: team {cut}'s corner closed ({geometry.WallRuns.Count} wall boxes, " +
                      $"{hiddenRenderers.Count} renderers hidden).");
        }

        private PhaseTwoCutGeometry BuildGeometryFor(int cutTeam)
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (arena == null || arena.FullBounds == null || buildings == null || buildings.Map == null)
                return null;
            int capital = buildings.Map.CapitalOf(cutTeam);
            if (capital < 0 || !buildings.TryGetZoneCentre(capital, out Vector3 capitalPosition))
                return null;

            ArenaLayout layout = arena.layout;
            if (layout == null)
            {
                LogRefusalOnce(cutTeam, "Arena Symmetry has no Layout assigned");
                return null;
            }

            var centre = new Vector2(arena.centre.x, arena.centre.z);
            PhaseTwoCutGeometry geometry = PhaseTwoCutGeometry.Build(arena.FullBounds.Polygon, centre,
                new Vector2(capitalPosition.x, capitalPosition.z) - centre, layout.PhaseTwoWallDistance,
                layout.PhaseTwoRecessWidth, layout.PhaseTwoRecessDepth, layout.WallThickness);
            if (geometry == null)
                LogRefusalOnce(cutTeam, "the Phase Two Cut values don't fit this arena (the wall must cross it once, and " +
                                        "the recess must fit between the outer walls)");
            return geometry;
        }

        private void LogRefusalOnce(int cutTeam, string why)
        {
            if (failedCutTeam == cutTeam)
                return;
            failedCutTeam = cutTeam;
            Debug.LogError($"[Arena] Can't build the phase-two wall for team {cutTeam}: {why}. The arena stays whole.");
        }

        private void Apply(PhaseTwoCutGeometry geometry)
        {
            Geometry = geometry;
            failedCutTeam = PhaseTwoCutRules.NoCut;
            ArenaLayout layout = arena.layout;

            Transform boundry = arena.source != null ? arena.source.Find(ArenaSymmetry.BoundaryGroupName) : null;
            if (boundry == null)
                Debug.LogWarning("[Arena] No Source/Boundry group: the phase-two wall is built, but a portal's path " +
                                 "check won't see it.");
            built = new GameObject(GroupName).transform;
            built.SetParent(boundry != null ? boundry : transform, false);

            float wallCentreY = (layout.WallBottomY + layout.WallTopY) * 0.5f;
            float wallHeight = layout.WallTopY - layout.WallBottomY;
            int buildingLayer = LayerMask.NameToLayer("Building");
            for (int i = 0; i < geometry.WallRuns.Count; i++)
            {
                ArenaWallPlan.Run run = geometry.WallRuns[i];
                Vector2 centreXZ = run.Centre(layout.WallThickness);
                GameObject wall = NewBox($"Phase Two Wall {i}", layout.WallMaterial, buildingLayer);
                wall.transform.SetPositionAndRotation(new Vector3(centreXZ.x, wallCentreY, centreXZ.y),
                    Quaternion.Euler(0f, run.UnityYawDegrees, 0f));
                wall.transform.localScale = new Vector3(run.Length, wallHeight, layout.WallThickness);
            }

            Vector3 barrierSize = layout.PhaseTwoRecessBarrierSize;
            if (geometry.HasRecess && barrierSize.x > 0f && barrierSize.y > 0f && barrierSize.z > 0f)
            {
                // Lifted by half its look, like every other barrier (ArenaPrimitiveBuilder): the look sits on the floor.
                var position = new Vector3(geometry.BarrierCentre.x, barrierSize.y * 0.5f, geometry.BarrierCentre.y);
                GameObject barrier = NewBox("Phase Two Recess Barrier", layout.BarrierMaterial,
                                            LayerMask.NameToLayer(ArenaLayers.BarrierLayerName));
                barrier.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, geometry.BarrierYawDegrees, 0f));
                barrier.transform.localScale = barrierSize;
                ArenaPieceShapes.BarrierBlockingBox(layout.BarrierBlockingBottomY, layout.BarrierBlockingTopY,
                    position.y, barrierSize.y, out Vector3 boxCentre, out Vector3 boxSize);
                BoxCollider box = barrier.GetComponent<BoxCollider>();
                box.center = boxCentre;
                box.size = boxSize;
            }

            arena.UsePlayableBounds(geometry.Playable);
            HidePiecesBehind(geometry);
            NetworkedDeployable.DestroyOwnedWhere(geometry.IsBehindWall);
        }

        private GameObject NewBox(string boxName, Material material, int layer)
        {
            var go = new GameObject(boxName);
            go.transform.SetParent(built, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>();
            return go;
        }

        private void HidePiecesBehind(PhaseTwoCutGeometry geometry)
        {
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null)
                    continue;
                foreach (string groupName in HiddenGroups)
                {
                    Transform group = third.Find(groupName);
                    if (group == null)
                        continue;
                    foreach (Transform piece in group)
                    {
                        if (!geometry.IsBehindWall(piece.position))
                            continue;
                        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
                            if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
                        foreach (Collider c in piece.GetComponentsInChildren<Collider>())
                            if (c.enabled) { c.enabled = false; hiddenColliders.Add(c); }
                    }
                }
            }
        }

        private void Restore()
        {
            if (built != null)
            {
                Destroy(built.gameObject);
                built = null;
            }
            foreach (Renderer r in hiddenRenderers)
                if (r != null) r.enabled = true;
            foreach (Collider c in hiddenColliders)
                if (c != null) c.enabled = true;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
            if (arena != null)
                arena.UsePlayableBounds(null);
            Geometry = null;
        }
    }
}
```
  (Check `ArenaLayers.BarrierLayerName` and `arena.source/generated120/generated240/centre` names against the real
  classes; they are the ones read in this plan's survey.)
- [ ] **Step 6: Write `Assets/Tests/PhaseTwoCutSceneTests.cs`** (scene-reading, read-only, same `WithGameScene` helper
  as `TerritoryAdjacencySceneTests`): for each team 0-2, build the geometry from the scene's own `ArenaSymmetry`
  (`FromSourceOutline(sourceOutline, centre)`), its `layout` values and the cut capital's tower position (the
  `BuildingCapture` whose `buildingID` is that team's capital in `BuildingManager.CathedralBuildingIDs`), then assert:
  1. `ArenaSymmetry.layout` is assigned (`TheArenaKnowsItsLayout`);
  2. the geometry is not null;
  3. every tower (`BuildingCapture`) whose zone `PhaseTwoCutRules.IsZoneCut` says is cut (links from
     `BuildingManager.TowerDictionary`, tiers from each tower's `tier`) is behind the wall; every other tower is
     `Playable.SignedDistance > 0`;
  4. the centre tower's whole capture area (72 points at its own `captureRadius`) is `Playable.SignedDistance >= 0`;
  5. every piece under `Blocks` in the three thirds whose position is NOT behind the wall has all four footprint corners
     (`position ± right × lossyScale.x/2 ± forward × lossyScale.z/2`) with `Closed.SignedDistance < 0` - no piece that
     stays is cut by the new wall.
  These are rules (they fail only if a layout value breaks the map), not pinned numbers. Name the tests for what they
  guard (e.g. `TheSurvivorsZonesStayOpenAndTheCutOnesCloseForEveryCorner`).
- [ ] **Step 7: Recompile, run the tests: all pass.** Scene not dirty after the run.
- [ ] **Step 8: Commit** by path (the 3 new files + `.meta`, the 8 modified files, the scene's one line);
  message `feat(arena): build the phase-two wall at runtime and close the corner`.

---

### Task 5: The towers (hide the cut ones; the centre's columns)

**Files:**
- Modify: `Assets/scripts/Player/Building capture.cs`, `Assets/scripts/Arena/TowerLook.cs`,
  `Assets/Tests/TowerLookPrefabTests.cs`

- [ ] **Step 1: The failing test** in `TowerLookPrefabTests` (follow the file's own prefab-loading helpers): load
  `Tower Look.prefab`, `ApplyColumns(3)`, `Bind(theme)`, `Refresh(someOwnedState, 0f)`; then `ApplyColumnsAtRuntime(4)`
  and `Refresh(sameState, 0f)` again; assert `ShownColumns == 4` and the fourth slot's cap renderer's property-block
  colour equals `ShownColor` (it was hidden when the colour was cached, so without a forced repaint it keeps no colour).
  Name: `ShowingAColumnAtRuntimePaintsIt`. Run: compile failure (no `ApplyColumnsAtRuntime`).
- [ ] **Step 2: `TowerLook`**:
```csharp
        /// <summary>The same as ApplyColumns, at runtime - the centre drops to three columns while it plays as a Tier III
        /// (PhaseTwoCutRules.EffectiveTier) and gets its fourth back in a new room. Forces the next Refresh to repaint,
        /// since a column that was hidden when the colour was cached has none.</summary>
        public void ApplyColumnsAtRuntime(int tier)
        {
            ApplyColumns(tier);
            shownColorSet = false;
        }
```
  and correct `ApplyColumns`' comment ("Edit-time... nothing here runs at runtime" → "also at runtime through
  ApplyColumnsAtRuntime").
- [ ] **Step 3: `BuildingCapture`** - hide when out of play, columns follow the effective tier. Fields:
```csharp
    // Tudor, 2026-09-25: a zone behind the phase-two wall (or the left-out corner of a host start) disappears - its
    // tower, ring and capture area - rather than showing grey. Toggled only when IsOutOfPlay changes; exactly what was
    // hidden is remembered, so showing it again (a new room) puts the tower back as it was.
    private bool hiddenAsCut;
    private readonly List<GameObject> hiddenChildren = new List<GameObject>();
    private readonly List<Renderer> hiddenOwnRenderers = new List<Renderer>();
    private readonly List<Collider> hiddenOwnColliders = new List<Collider>();
    private int shownColumnsTier; // set to tier in Start: the prefab was built with tier's columns
```
  Method:
```csharp
    private void SetHiddenAsCut(bool hide)
    {
        if (hide == hiddenAsCut)
            return;
        hiddenAsCut = hide;
        if (hide)
        {
            foreach (Transform child in transform)
                if (child.gameObject.activeSelf) { child.gameObject.SetActive(false); hiddenChildren.Add(child.gameObject); }
            foreach (Renderer r in GetComponents<Renderer>())
                if (r.enabled) { r.enabled = false; hiddenOwnRenderers.Add(r); }
            if (flagRenderer != null && flagRenderer.enabled) { flagRenderer.enabled = false; hiddenOwnRenderers.Add(flagRenderer); }
            foreach (Collider c in GetComponents<Collider>())
                if (c.enabled) { c.enabled = false; hiddenOwnColliders.Add(c); }
            return;
        }
        foreach (GameObject go in hiddenChildren) if (go != null) go.SetActive(true);
        foreach (Renderer r in hiddenOwnRenderers) if (r != null) r.enabled = true;
        foreach (Collider c in hiddenOwnColliders) if (c != null) c.enabled = true;
        hiddenChildren.Clear();
        hiddenOwnRenderers.Clear();
        hiddenOwnColliders.Clear();
    }
```
  In `RefreshRingView`, right after the `manager == null` check (before the ring/tower early return):
```csharp
        bool outOfPlay = ZoneOutOfPlay(buildingID);
        SetHiddenAsCut(outOfPlay);
        if (outOfPlay)
            return;

        int effectiveTier = EffectiveTier;
        if (towerLook != null && effectiveTier != shownColumnsTier)
        {
            towerLook.ApplyColumnsAtRuntime(effectiveTier);
            shownColumnsTier = effectiveTier;
        }
```
  and pass `outOfPlay: false` to `CaptureRingState.From` below (it's always false past this point). In `Start`, set
  `shownColumnsTier = tier;` after `towerLook` is found. **First, by eval in edit mode, list one tower's hierarchy
  (children, root components) and report it** - if the root has a renderer that must stay (it shouldn't: the round
  tower is the Tower Look child) or the carpet `flagRenderer` lives elsewhere, say so in the report.
- [ ] **Step 4: Recompile, run the tests: all pass** (the new test green; `CaptureRingStateTests`, `OwnerPaintTests`,
  `TowerLookPrefabTests` unchanged otherwise).
- [ ] **Step 5: Commit** the three files by path; message `feat(towers): cut towers disappear; the centre shows three columns as a Tier III`.

---

### Task 6: The minimap (hidden bubbles, the centre's III, the dark corner and the wall)

**Files:**
- Create: `Assets/scripts/UI/MinimapCutMask.cs`, `Assets/Tests/MinimapCutMaskTests.cs`
- Modify: `Assets/scripts/UI/MinimapView.cs`, `Assets/scripts/UI/UiTheme.cs`, `Assets/Gameplay/Config/UiTheme.asset`
  (3 lines)

- [ ] **Step 1: The failing tests** (`Assets/Tests/MinimapCutMaskTests.cs`) - on the same toy triangle as the geometry
  tests (cut 2 m toward the apex, 2 × 1 recess, wall thickness 0.2):
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapCutMaskTests
    {
        private static PhaseTwoCutGeometry ToyCut() => PhaseTwoCutGeometry.Build(
            new List<Vector2> { new Vector2(10f, 30f), new Vector2(10f - 8.660254f, 15f), new Vector2(10f + 8.660254f, 15f) },
            new Vector2(10f, 20f), Vector2.up, 2f, 2f, 1f, 0.2f);

        [Test]
        public void OpenGroundClosedGroundAndTheWallAreToldApart()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 20f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Closed, MinimapCutMask.Classify(new Vector2(10f, 26f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Wall, MinimapCutMask.Classify(new Vector2(7f, 22.1f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 22.5f), cut, 0.3f), "the recess is open");
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 5f), cut, 0.3f), "outside the arena isn't shaded");
        }

        [Test]
        public void ThePictureCoversTheBakedSquareBottomRowFirst()
        {
            var closed = new Color32(1, 2, 3, 200);
            var wall = new Color32(250, 250, 250, 255);
            // 40 x 40 pixels over a 20 m square centred on (10, 22): 0.5 m a pixel, pixel (0,0) at world (0.25, 12.25).
            Color32[] pixels = MinimapCutMask.Paint(40, new Vector2(10f, 22f), 20f, ToyCut(), 0.3f, closed, wall);
            Assert.AreEqual(1600, pixels.Length);
            Assert.AreEqual(0, pixels[15 * 40 + 19].a, "world (9.75, 19.75): open, transparent");
            Assert.AreEqual(closed, pixels[27 * 40 + 19], "world (9.75, 25.75): behind the wall");
            int wallPixels = 0;
            foreach (Color32 p in pixels)
                if (p.Equals(wall)) wallPixels++;
            Assert.Greater(wallPixels, 0);
        }
    }
}
```
  Run: compile failure.
- [ ] **Step 2: `Assets/scripts/UI/MinimapCutMask.cs`**
```csharp
using System.Collections.Generic;
using UnityEngine;
using Overpower.Arena;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap's picture of a closed corner (Tudor, 2026-09-25: the minimap darkens the closed part and draws the
    /// wall). One texture laid over the baked arena picture, covering the same square of the world, so it turns with
    /// it. Pure: tested without a scene.
    /// </summary>
    public static class MinimapCutMask
    {
        public enum Pixel { Open, Closed, Wall }

        public static Pixel Classify(Vector2 worldXZ, PhaseTwoCutGeometry cut, float wallHalfWidthMetres)
        {
            if (DistanceToLine(worldXZ, cut.WallLine) <= wallHalfWidthMetres)
                return Pixel.Wall;
            return cut.Closed.SignedDistance(worldXZ) >= 0f ? Pixel.Closed : Pixel.Open;
        }

        /// <summary>size x size pixels over the worldSizeMetres square centred on worldCentreXZ, bottom row first
        /// (Texture2D.SetPixels32 order; the baked picture's +Z is up). Open ground is transparent.</summary>
        public static Color32[] Paint(int size, Vector2 worldCentreXZ, float worldSizeMetres, PhaseTwoCutGeometry cut,
                                      float wallHalfWidthMetres, Color32 closed, Color32 wall)
        {
            var pixels = new Color32[size * size];
            float metresPerPixel = worldSizeMetres / size;
            Vector2 corner = worldCentreXZ - Vector2.one * (worldSizeMetres * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var world = corner + new Vector2((x + 0.5f) * metresPerPixel, (y + 0.5f) * metresPerPixel);
                    Pixel kind = Classify(world, cut, wallHalfWidthMetres);
                    pixels[y * size + x] = kind == Pixel.Wall ? wall : kind == Pixel.Closed ? closed : default;
                }
            return pixels;
        }

        private static float DistanceToLine(Vector2 p, IReadOnlyList<Vector2> line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
                best = Mathf.Min(best, ArenaBounds.DistanceToSegment(p, line[i], line[i + 1]));
            return best;
        }
    }
}
```
- [ ] **Step 3: `UiTheme`** - after `outOfPlayZoneColor` (`UiTheme.cs:701`):
```csharp
        [Header("Phase two cut (a team knocked out)")]
        [Tooltip("The minimap's shade over the part of the arena a knockout closed (behind the phase-two wall). Dark and " +
                 "mostly opaque, so it reads as gone, not as ground you can take.")]
        public Color minimapCutAreaColor = new Color(0.05f, 0.05f, 0.06f, 0.75f);
        [Tooltip("The phase-two wall's line on the minimap.")]
        public Color minimapCutWallColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [Tooltip("How thick the phase-two wall's line is drawn on the minimap, in the same canvas units as the link " +
                 "widths (the real wall is under a metre - about one unit, too thin to see).")]
        public float minimapCutWallWidth = 3f;
```
  and in `UiTheme.asset`, right after the `outOfPlayZoneColor:` line, exactly:
```
  minimapCutAreaColor: {r: 0.05, g: 0.05, b: 0.06, a: 0.75}
  minimapCutWallColor: {r: 0.85, g: 0.85, b: 0.85, a: 1}
  minimapCutWallWidth: 3
```
- [ ] **Step 4: `MinimapView`**:
  - `ZoneUi` gains `public int Tier;` and `public TextMeshProUGUI Label;`. `AddLabel` returns its `TextMeshProUGUI`.
  - Extract `ApplyTier(ZoneUi ui, int tier)` from `BuildZone`: sets `ui.Tier`, `ui.Diameter =
    theme.MinimapBubbleDiameter(tier)`, the sizeDelta of `Upright` (`Diameter`), `Ring` (`Diameter + 2 × (outline
    width + progress ring width)`), `Outline` (`Diameter + 2 × outline width`), `Fill` (`Diameter`), and
    `Label.text = MinimapLayout.TierLabel(tier)`. `BuildZone` creates the Images and the label, then calls it.
  - `RecolourOwnership`: per zone, after `zone.OutOfPlay` is read:
    `int tier = manager.TierOf(zone.Zone); if (tier > 0 && tier != zone.Tier) ApplyTier(zone, tier);` and
    `zone.Upright.gameObject.SetActive(zone.Shown && !zone.OutOfPlay);` (a cut zone's bubble disappears like its
    tower; links touching it are already hidden by `ApplyLinkStyle`). Update the Decision 8 comments there.
  - The overlay: in `BuildFrame`, right after the "Arena Picture" is stretched and BEFORE `linksLayer` is made (sibling
    order = draw order: under links and bubbles):
```csharp
            var cutGo = new GameObject("Phase Two Cut", typeof(RectTransform));
            cutGo.transform.SetParent(map, false);
            cutOverlay = cutGo.AddComponent<RawImage>();
            cutOverlay.raycastTarget = false;
            cutOverlay.enabled = false;
            Stretch(cutOverlay.rectTransform);
```
    Fields: `private RawImage cutOverlay; private Texture2D cutTexture; private PhaseTwoCutGeometry paintedCut;` and
    `private const int CutOverlayPixels = 256; // the overlay's sharpness, not a gameplay value`.
    In the per-frame update (the `LateUpdate` that already runs `UpdateZones` once `built`), first:
```csharp
            PhaseTwoCutGeometry cut = ArenaPhaseTwoCut.Active != null ? ArenaPhaseTwoCut.Active.Geometry : null;
            if (cut != paintedCut)
                PaintCutOverlay(cut);
```
    and
```csharp
        /// <summary>Tudor, 2026-09-25: the closed part darkened and the wall drawn, painted once per cut (not per frame)
        /// into one texture over the baked picture - the same square of the world, so it turns with the map.</summary>
        private void PaintCutOverlay(PhaseTwoCutGeometry cut)
        {
            paintedCut = cut;
            if (cut == null)
            {
                cutOverlay.enabled = false;
                return;
            }
            if (cutTexture == null)
                cutTexture = new Texture2D(CutOverlayPixels, CutOverlayPixels, TextureFormat.RGBA32, false)
                {
                    name = "Minimap Phase Two Cut", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
            float metresPerCanvasUnit = config.WorldSizeMetres / Mathf.Max(1f, theme.minimapCornerSize);
            cutTexture.SetPixels32(MinimapCutMask.Paint(CutOverlayPixels, config.WorldCentre, config.WorldSizeMetres, cut,
                theme.minimapCutWallWidth * 0.5f * metresPerCanvasUnit, theme.minimapCutAreaColor, theme.minimapCutWallColor));
            cutTexture.Apply();
            cutOverlay.texture = cutTexture;
            cutOverlay.color = Color.white;
            cutOverlay.enabled = true;
        }
```
    Destroy `cutTexture` in `OnDestroy` if the class has one (follow the file's own cleanup pattern).
- [ ] **Step 5: Recompile; run the tests: all pass** (`MinimapLayoutTests` unchanged).
- [ ] **Step 6: Commit** by path (2 new + `.meta`, 3 modified + the theme asset's 3 lines);
  message `feat(minimap): the closed corner, its wall and the centre as III`.

---

### Task 7: Play Mode check, one client (the controller looks at the captures)

No code change expected. If something fails, **report it with evidence; don't fix gameplay code** (the controller
dispatches a fix).

- [ ] **Step 1:** Lock, scene clean, Play. Join a solo room (scripts in `Resources/loops/Limit Test/briefs/join-room-scripts/`).
  List the actors (only you). As master, go live as a host start by reflection:
  `typeof(MatchDirector).GetMethod("GoLive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(MatchDirector.Instance, new object[] { new[] { 0, 1 } })`.
  Wait for the echo (`MatchDirector.Instance.IsLive`).
- [ ] **Step 2: State** (one eval, print all): `CutTeam` = 2; `ArenaPhaseTwoCut.Active.AppliedCutTeam` = 2; the
  `Phase Two Cut` group's children (5 walls + 1 barrier, their positions/scales, layers `Building`/`Barrier`, parent
  path `…/Source/Boundry/Phase Two Cut`); `ArenaSymmetry.ActiveBounds != FullBounds`; `IsOutOfPlay(z)` for z 0-9
  (true for exactly 2, 4, 5, 8); `TierOf(9)` = 3, `TierByZone()[9]` = 3, `BaseTierOf(9)` = 4; tower 9's
  `TowerLook.ShownColumns` = 3; towers 2, 4, 5, 8: every child inactive, trigger collider disabled; zone 9's capture
  seconds by reflection (`CaptureSeconds`) = `TerritoryConfig.ForTier(3).captureSeconds`.
- [ ] **Step 3: The wall holds** (every result measured):
  - `Physics.Raycast` from 3 m in front of the wall (open side), toward the cut capital, mask `Building`: hits a
    `Phase Two Wall` collider.
  - Walking/dash: `TeleportTo` 2.5 m in front of the wall middle (outside the recess), aim at the cut capital
    (`SetAimOverride`), dash (the Dash slot's public cast path); after 1 s the player's position is on the open side
    (`geometry.IsBehindWall` false).
  - Blink: from 2 m in front, `BlinkAbility.TryBuildCast` aimed through the wall at a point 6 m behind it: refused, or
    its destination is on the open side. Say which.
  - Portal: `ArenaSymmetry.IsInsideArena(pointBehindWall, 0.5f)` = false; in the recess (1.5 m past the face, on the
    axis) = true; `PathCrossesBoundary` from in front of the wall to behind it = true.
  - Safety net: `TeleportTo` a point 5 m behind the wall; within 1 s the player is back on the open side (a `LeftArena`
    return). Read `transform.position` only after `Physics.SyncTransforms()` or a physics step (trap 7).
  - One real shot: `SetAimOverride` at the wall from 6 m, `TryFire`; the projectile's last position (a time-bounded
    recorder) is on the open side.
- [ ] **Step 4: Captures** (explicit paths under `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\captures\map-shrink-2026-09-25\`):
  a top-down of the closed corner (the wall, the recess, the barrier; `capture_scene_view` from above or a top-down
  camera eval), the game view standing in front of the recess, the corner minimap and the large map (M). Describe each
  honestly in the report; the controller looks too.
- [ ] **Step 5: Leaving restores it:** `PhotonNetwork.LeaveRoom()`; after the callback: `AppliedCutTeam` = -1, no
  `Phase Two Cut` group, `ActiveBounds == FullBounds`, towers 2/4/5/8 children active again, tower 9 `ShownColumns` 4,
  `TierOf(9)` = 4, hidden blocks re-enabled (count).
- [ ] **Step 6:** Stop Play; `playMode: stopped`; scene `isDirty: false`; `git status` clean; nothing under
  `Assets/Temp/`; no asset changed by reflection (trap 20 read-back of anything you set). Release the lock.

---

### Task 8: Two clients (host start) and three clients (a real knockout)

Read `Resources/loops/Limit Test/two-client-harness.md` and `briefs/two-client-common-rules.md` first; they apply in
full (recorders on the receiving client BEFORE acting, actors listed, PIDs recorded, runtime server only for the
development build, then off).

- [ ] **Step 1: A fresh development `Client2`** from the new HEAD (runtime server ON only for this build, OFF again at
  once, never committed; build target and `developmentBuild` restored). Recreate `Client3` as a folder copy without
  `.unity-pipeline-runtime-port`. Record both PIDs when launched (one at a time).
- [ ] **Step 2: V1 - two clients, host start.** A = Editor (master, team 0), B = Client2 (team 1), one room by name.
  Recorder on B first (CutTeam, AppliedCutTeam, the wall boxes' centres/yaws, `ActiveBounds` point count, `TierOf(9)`,
  tower 8 hidden, zone owners). A presses Start (`HostStartMatch`); after live + the 5 s countdown:
  - A and B: cut 2; the five wall boxes identical to the millimetre (print both); `TierOf(9)` 3 on both.
  - B: a raycast through the wall hits it; B's blink through the wall refused or landing open-side; B's corner minimap
    capture shows the dark corner and the wall.
- [ ] **Step 3: V2 - three clients, a real knockout.** A = Editor (team 0), B = Client2 (team 1), C = Client3 (team 2).
  Three teams → countdown → live (ThreeTeams). Before the knockout, on B: place a Mine (B's equipment slot, or
  `NetworkedDeployable.Spawn("Mine", …)` from B's own client) in team 2's corner behind where the wall will stand (for
  example 4 m from zone 2's tower). Recorder on A and B first. Then C leaves the room (`PhotonNetwork.LeaveRoom()` on C
  by runtime eval; its team is out once its capital falls); A captures zone 8 (TeleportTo inside its ring, 6-8 m off the
  pivot, and hold; list actors first). On the three-to-two edge, on A and B:
  - the room has `mCut` = 2 and `mPhase` = 2 in the same update (recorder);
  - the wall on both, identical; zones 2, 3, 4, 5, 8, 9 neutral (owners from the snapshot) and zone 9's capture state
    reset; towers 2, 4, 5, 8 hidden; B's mine gone on B and on A;
  - the survivors were sent home (existing behaviour) and nobody stands behind the wall.
- [ ] **Step 4:** Shut B and C down by their PIDs. `git status` clean; `Game Scene.unity`, `GameplayConfig.asset`,
  `PhotonServerSettings.asset`, `Multiplayer Player.prefab` byte-unchanged; run the tests once: all pass. Release the lock.
- [ ] **Step 5:** Report: V1/V2 table (PASS / FAIL with the recorder lines / NOT TESTABLE with why), capture paths,
  both clients' wall numbers side by side.

---

### Task 9: Docs and the final review (controller)

- [ ] **Step 1:** opus whole-diff review (from the commit before Task 1 to HEAD): the room key's write/echo, late join,
  master switch mid-transition, the tier cache, the hide/restore symmetry, allocation per frame, the builder's
  unchanged output. Fix what it finds (small fixes by the controller under the lock; larger ones dispatched).
- [ ] **Step 2:** `progress.md` rows (one per task, numbers and commits), `assumptions-for-tudor.md` (the section's
  `[C]` lines finalised with the Inspector names; the planks question still open unless he answered), HANDOFF §2/§3 (the
  new room key `mCut`: every build in a room must match; the cut is built at runtime; the Inspector values), `editor-queue.md`.
- [ ] **Step 3:** Push `limit-testing`.

---

## Self-review (done while writing)

- **Spec coverage:** wall + recess + barrier (T2 geometry, T4 build); smaller outline for blink/portal/safety net (T4 +
  T7 check); everything behind hidden (T4 pieces, T5 towers); centre as Tier III incl. columns and minimap (T3, T5,
  T6); neutralise + capture reset (T3); placed objects (T4, checked in T8 V2); minimap (T6); host start (T1 rule, T7,
  T8 V1); which corner (T1 rule, T3 wiring); late joiners (room key read directly; T4 polling); two/three clients (T8).
- **No placeholders:** the pure units have full code and tests; the wiring steps name the file, the line range and the
  exact code; the scene test lists its five assertions (its helper is the existing one).
- **Names are consistent:** `PhaseTwoCutRules.{NoCut, CutTeam, CutZones, IsZoneCut, EffectiveTier, ZonesToNeutralise,
  ChooseCutTeam}`, `PhaseTwoCutGeometry.{Build, WallLine, Playable, Closed, WallRuns, HasRecess, BarrierCentre,
  BarrierYawDegrees, IsBehindWall}`, `ArenaPieceShapes.BarrierBlockingBox`, `ArenaSymmetry.{layout, FullBounds,
  UsePlayableBounds, BlocksGroupName, BarriersGroupName, SceneryGroupName}`, `ArenaPhaseTwoCut.{Active, AppliedCutTeam,
  Geometry}`, `MatchDirector.{CutTeamKey, CutTeam}`, `BuildingManager.{BaseTierOf, ResetCaptureOf}`,
  `BuildingCapture.EffectiveTier`, `TowerLook.ApplyColumnsAtRuntime`, `NetworkedDeployable.{DestroyOwnedWhere,
  FollowsCaster}`, `MinimapCutMask.{Pixel, Classify, Paint}`, `UiTheme.{minimapCutAreaColor, minimapCutWallColor,
  minimapCutWallWidth}`, `ArenaLayout.{PhaseTwoWallDistance, PhaseTwoRecessWidth, PhaseTwoRecessDepth,
  PhaseTwoRecessBarrierSize}`.
