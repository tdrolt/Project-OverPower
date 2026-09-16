# Arena Symmetry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Game Scene`'s arena three identical thirds with a re-runnable Editor tool, put Tier 4 at the true centre
in place of the fountain, and open each capital's cramped corner into a pocket.

**Architecture:**
- Pure rotation maths lives in `RadialSymmetry` (runtime assembly, edit-mode tested).
- A data-only `ArenaSymmetry` MonoBehaviour in the scene names the Source third, the two generated thirds, the objects
  to snap (towers, carpets, spawns) and the objects to centre.
- A new Editor assembly holds `ArenaSymmetryBuilder` (Rebuild / Validate), its Inspector buttons and menu items.
- A one-time migration script (run from the scratchpad, not committed) moves today's arena into that structure and
  builds the capital pockets.
- A scene test guards the result.

**Tech Stack:** Unity 6000.0.70f1 (URP, Mono), C#, NUnit edit-mode tests, `unity` CLI (Editor command server).

**Spec:** `docs/superpowers/specs/2026-09-16-arena-symmetry-design.md` (read it first).

**Deviation from the spec, decided while planning [C]:** the spec's `towerTriplets` of `BuildingCapture` plus a
separate spawn triplet become one generic `snappedTriplets` list of Transforms (towers, their flag carpets and spawn
points are all rows). It's one mechanism instead of three, and the tool no longer needs to know what a tower is. The
scene test still proves every T1/T2/T3 tower and its carpet is in a triplet.

**Source third border [C, measured]:** map angles **[25°, 145°)**, not the spec's nominal [30°, 150°). The right
wall's midpoint piece sits at 29.4°, so a 30° border would drop it and double the 148.4° piece. Starting at 25° puts
each wall's midpoint piece in exactly one third.

---

## Rules for every task (from HANDOFF §5–6 — each one has cost time)

1. Branch `limit-testing` only; never touch or push `main`. Push after each task's commits.
2. `unity command editor_status` must answer before editing. CLI form: `unity command <name> -- --flag value`.
3. **Never run tests, recompile, build, or edit `.cs` while in Play Mode.** After `editor_stop`, poll `editor_status`
   until `playMode: "stopped"` and `compiling: false`.
4. `recompile_status` is the only compile truth: `unity command recompile`, poll `unity command recompile_status` until
   it is completed with an empty `errors` array.
5. **Tests only async:** `unity command run_tests -- --mode editor --async_tests true`, then poll
   `unity command test_status`.
6. **Dirty scene → modal "save?" dialog → the Editor hangs silently.**
   - Before tests, recompile, build or scene open, eval
     `return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;`.
   - If dirty and not intended, reload from disk:
     `UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Game Scene.unity",
     UnityEditor.SceneManagement.OpenSceneMode.Single)`.
   - If `editor_status` times out, **stop and report**. Don't force-restart and don't click dialogs.
7. Eval code writes `UnityEngine.Object`, never bare `Object`. Scratch files go in the session scratchpad
   `C:\Users\bascu\AppData\Local\Temp\claude\C--UniStuff-Y3-MinorSkilled-GitAccess-Project-OverPower\8bc97fd1-4270-4ef4-9013-ae2fb391f99d\scratchpad`,
   never under `Assets/`.
8. **Look at every render/capture yourself** (Read the PNG) before claiming anything about it.
9. `AssetDatabase.SaveAssets()` flushes every dirty asset. After any save, run `git status` and revert strays.
   `Assets/Gameplay/Config/GameplayConfig.asset` must stay unchanged.
10. Never move a player via `transform.position`; use `PlayerDisplacement.TeleportTo`.
11. Every designer-facing value: `[SerializeField]`/public field + a plain-language `[Tooltip]`. Comments explain *why*
    for a designer reader.
12. Commit messages end with your own `Co-Authored-By:` line. No number in a commit message unless you measured it.
13. Append judgement calls to `C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\assumptions-for-tudor.md` under a
    new heading `## Arena symmetry (2026-09-16)` as short [C] lines. That file is outside the repo, so don't commit it.

## File map

| File | Responsibility | Task |
|---|---|---|
| `Assets/scripts/Arena/RadialSymmetry.cs` | Pure maths: rotate points/rotations by thirds, map angle, which third | A1 |
| `Assets/Tests/RadialSymmetryTests.cs` | Tests for the above | A1 |
| `Assets/scripts/Arena/ArenaSymmetry.cs` | Scene component: the setup the tool reads (no runtime behaviour) | A2 |
| `Assets/scripts/Editor/Overpower.Editor.asmdef` | New Editor-only assembly | A2 |
| `Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs` | Rebuild + Validate | A2 |
| `Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs` | Inspector buttons + `OverPower/Arena` menu | A2 |
| `Assets/Tests/Overpower.Tests.asmdef` (modify) | Reference `Overpower.Editor` | A2 |
| `Assets/Tests/ArenaSymmetryBuilderTests.cs` | Builder tests in a preview scene | A2 |
| `Assets/Scenes/Game Scene.unity` (modify) | The migrated arena | A3 |
| `Assets/Tests/ArenaSymmetrySceneTests.cs` | Guards the saved scene | A3 |
| scratchpad `ArenaMigration.cs`, `ArenaRender.cs` | One-time migration + top-down render (not committed) | A3 |

---

### Task A1: `RadialSymmetry` maths

**Files:**
- Create: `Assets/scripts/Arena/RadialSymmetry.cs`
- Test: `Assets/Tests/RadialSymmetryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    public class RadialSymmetryTests
    {
        private static readonly Vector3 Centre = new Vector3(65.05f, 0f, 53.34f);

        private static void AssertClose(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F4} but was {actual:F4}");
        }

        [Test]
        public void OneThirdTurnMovesAPointCounterClockwiseSeenFromAbove()
        {
            // A point due +X of the centre (map angle 0°) ends at map angle 120°. Height is kept.
            Vector3 moved = RadialSymmetry.RotatePoint(Centre + new Vector3(10f, 2f, 0f), Centre, 1);
            AssertClose(Centre + new Vector3(-5f, 2f, 8.660254f), moved);
        }

        [Test]
        public void TheTopCapitalsAxisTurnsOntoTheBottomLeftThenTheBottomRight()
        {
            // Map angle 90° (the top capital) -> 210° (bottom-left, team 0) -> 330° (bottom-right, team 1).
            Vector3 top = Centre + new Vector3(0f, 0f, 57.66f);
            AssertClose(Centre + new Vector3(-49.935f, 0f, -28.83f), RadialSymmetry.RotatePoint(top, Centre, 1));
            AssertClose(Centre + new Vector3(49.935f, 0f, -28.83f), RadialSymmetry.RotatePoint(top, Centre, 2));
        }

        [Test]
        public void ThreeThirdTurnsReturnToTheStart()
        {
            Vector3 start = new Vector3(80f, 1f, 90f);
            AssertClose(start, RadialSymmetry.RotatePoint(start, Centre, 3));
        }

        [Test]
        public void AnObjectFacingAwayFromTheCentreStillFacesAwayAfterTheTurn()
        {
            // Facing +Z (map angle 90°) turns to face map angle 210°.
            Quaternion turned = RadialSymmetry.RotateRotation(Quaternion.identity, 1);
            AssertClose(new Vector3(-0.8660254f, 0f, -0.5f), turned * Vector3.forward);
        }

        [Test]
        public void MapAngleIsCounterClockwiseFromPlusXInDegrees()
        {
            Assert.AreEqual(0f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.right, Centre), 1e-3f);
            Assert.AreEqual(90f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.forward, Centre), 1e-3f);
            Assert.AreEqual(180f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.left, Centre), 1e-3f);
            Assert.AreEqual(270f, RadialSymmetry.MapAngleDegrees(Centre + Vector3.back, Centre), 1e-3f);
        }

        [Test]
        public void ThirdIndexIncludesTheStartBorderAndExcludesTheEnd()
        {
            Vector3 At(float degrees) => Centre + new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad)) * 20f;
            Assert.AreEqual(0, RadialSymmetry.ThirdIndex(At(25f), Centre, 25f));
            Assert.AreEqual(0, RadialSymmetry.ThirdIndex(At(144.9f), Centre, 25f));
            Assert.AreEqual(1, RadialSymmetry.ThirdIndex(At(145f), Centre, 25f));
            Assert.AreEqual(2, RadialSymmetry.ThirdIndex(At(265f), Centre, 25f));
            Assert.AreEqual(2, RadialSymmetry.ThirdIndex(At(24.9f), Centre, 25f));
        }
    }
}
```

- [ ] **Step 2: Recompile and run tests to verify they fail**

Run: `unity command recompile`, then poll `unity command recompile_status`.
Expected: compile **errors** naming `Overpower.Arena` / `RadialSymmetry` (the type doesn't exist). That is the failing
state. Don't run tests against a broken compile.

- [ ] **Step 3: Write the implementation**

```csharp
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The maths behind a three-way symmetric arena: one third is authored, and the other two are that third turned
    /// 120° and 240° about the arena's centre.
    ///
    /// Angles here read like a top-down map: counter-clockwise from +X, seen from above. That is the OPPOSITE sign of a
    /// Unity yaw (a positive Unity Y rotation turns clockwise seen from above). Keeping that conversion in this one
    /// class is its whole point: getting the sign wrong mirrors the copies instead of turning them, which looks almost
    /// right on a near-symmetric map and is easy to miss.
    /// </summary>
    public static class RadialSymmetry
    {
        public const float ThirdDegrees = 120f;

        /// <summary>The rotation that turns something <paramref name="thirds"/> × 120° counter-clockwise seen from
        /// above, about the vertical axis.</summary>
        public static Quaternion ThirdTurn(int thirds) => Quaternion.AngleAxis(-ThirdDegrees * thirds, Vector3.up);

        /// <summary>Turns a world point about <paramref name="centre"/>'s vertical axis. Height is unchanged.</summary>
        public static Vector3 RotatePoint(Vector3 point, Vector3 centre, int thirds) =>
            centre + ThirdTurn(thirds) * (point - centre);

        /// <summary>Turns an object's facing by the same amount RotatePoint turns its position.</summary>
        public static Quaternion RotateRotation(Quaternion rotation, int thirds) => ThirdTurn(thirds) * rotation;

        /// <summary>Map angle of a point around the centre, in [0, 360): 0 = +X, 90 = +Z.</summary>
        public static float MapAngleDegrees(Vector3 point, Vector3 centre)
        {
            float degrees = Mathf.Atan2(point.z - centre.z, point.x - centre.x) * Mathf.Rad2Deg;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        /// <summary>Which third a point is in: 0 for map angles [start, start+120), 1 for the next 120°, 2 for the
        /// last. The start border belongs to third 0, so every point is in exactly one third.</summary>
        public static int ThirdIndex(Vector3 point, Vector3 centre, float firstThirdStartDegrees)
        {
            float fromStart = MapAngleDegrees(point, centre) - firstThirdStartDegrees;
            fromStart = ((fromStart % 360f) + 360f) % 360f;
            return Mathf.Min(2, Mathf.FloorToInt(fromStart / ThirdDegrees));
        }
    }
}
```

- [ ] **Step 4: Recompile, run tests, verify they pass**

Run: `unity command recompile` → poll `recompile_status` (expect completed, `errors: []`). Check the scene isn't dirty
(rule 6). Then `unity command run_tests -- --mode editor --async_tests true`, and poll `unity command test_status` until
`completed`.
Expected: all tests pass. The total is the previous total + 6. Report both numbers.

- [ ] **Step 5: Commit**

```bash
git add Assets/scripts/Arena Assets/scripts/Arena.meta Assets/Tests/RadialSymmetryTests.cs Assets/Tests/RadialSymmetryTests.cs.meta
git commit -m "feat(arena): RadialSymmetry maths for three-way rotational symmetry" -m "Co-Authored-By: <your model line>"
git push
```

---

### Task A2: `ArenaSymmetry` component, Editor assembly, builder and Inspector

**Files:**
- Create: `Assets/scripts/Arena/ArenaSymmetry.cs`
- Create: `Assets/scripts/Editor/Overpower.Editor.asmdef`
- Create: `Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs`
- Create: `Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs`
- Modify: `Assets/Tests/Overpower.Tests.asmdef` (add `"Overpower.Editor"` to `references`)
- Test: `Assets/Tests/ArenaSymmetryBuilderTests.cs`

- [ ] **Step 1: Create the component** (needed so the tests can compile against it; it has no behaviour to test)

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Makes the arena three identical thirds, turned 120° apart about Centre.
    ///
    /// HOW TO EDIT THE ARENA: change only the objects under Source. Then press "Rebuild thirds" on this component
    /// (or OverPower > Arena > Rebuild thirds), look at the result, and save the scene. The two generated thirds are
    /// deleted and copied again from Source on every rebuild, so an edit made directly to them is thrown away. The
    /// Inspector greys them out to make that obvious.
    ///
    /// Towers, their flag carpets and spawn points are NOT copied: they are networked or carry ids that must stay
    /// unique. They are listed in Snapped Triplets instead. You place the Source one, and the rebuild moves its two
    /// partners to match. Objects in Centred are moved onto the centre (their height is kept).
    ///
    /// This component does nothing during play or in a build. It only stores the setup that the Editor tool
    /// (Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs) reads.
    /// </summary>
    public class ArenaSymmetry : MonoBehaviour
    {
        [Serializable]
        public class SnappedTriplet
        {
            [Tooltip("The one you place by hand, in the Source third.")]
            public Transform source;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 120° about the centre. Don't place it by hand.")]
            public Transform at120;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 240° about the centre. Don't place it by hand.")]
            public Transform at240;
        }

        [Tooltip("The point the thirds turn about, in world space (only X and Z matter). Moving it moves every " +
                 "generated object and snapped partner on the next rebuild.")]
        public Vector3 centre = new Vector3(65.05f, 0f, 53.34f);

        [Tooltip("Drawing aid only: the map angle (degrees counter-clockwise from +X, seen from above) where the Source " +
                 "third's lines are drawn in the Scene view. Rebuild copies everything under Source regardless.")]
        public float sourceStartDegrees = 25f;

        [Tooltip("The only third you edit. Everything under it is copied into the two generated thirds.")]
        public Transform source;

        [Tooltip("Rebuilt from Source turned 120°. Never edit its children by hand.")]
        public Transform generated120;

        [Tooltip("Rebuilt from Source turned 240°. Never edit its children by hand.")]
        public Transform generated240;

        [Tooltip("Networked or id-carrying objects kept symmetric by moving, not copying: towers, their flag carpets, " +
                 "spawn points.")]
        public List<SnappedTriplet> snappedTriplets = new List<SnappedTriplet>();

        [Tooltip("Objects moved onto the centre by Rebuild thirds, keeping their height (the Tier 4 tower and its " +
                 "carpet).")]
        public List<Transform> centred = new List<Transform>();

        private void OnDrawGizmosSelected()
        {
            // The three dividing lines, so a designer can see where the Source third ends.
            Gizmos.color = Color.yellow;
            for (int i = 0; i < 3; i++)
            {
                float radians = (sourceStartDegrees + RadialSymmetry.ThirdDegrees * i) * Mathf.Deg2Rad;
                Gizmos.DrawLine(centre, centre + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * 80f);
            }
            Gizmos.DrawWireSphere(centre, 1f);
        }
    }
}
```

- [ ] **Step 2: Create the Editor assembly and reference it from the tests**

`Assets/scripts/Editor/Overpower.Editor.asmdef`:

```json
{
  "name": "Overpower.Editor",
  "rootNamespace": "Overpower.EditorTools",
  "references": [
    "Overpower.Runtime",
    "PhotonUnityNetworking"
  ],
  "includePlatforms": [ "Editor" ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

(The namespace is `Overpower.EditorTools`, not `Overpower.Editor`: a namespace called `Editor` would shadow
`UnityEditor.Editor` inside it and break every `CustomEditor` class.)

In `Assets/Tests/Overpower.Tests.asmdef`, change `references` to:

```json
  "references": [
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "Overpower.Runtime",
    "Overpower.Editor",
    "PhotonUnityNetworking",
    "PhotonRealtime"
  ],
```

- [ ] **Step 3: Write the failing builder tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Runs the arena tool against a tiny arena built in a PREVIEW scene. A preview scene is never saved and never
    /// marks Game Scene dirty. A dirty Game Scene makes the Editor raise a modal "save?" dialog that hangs the command
    /// server, which is why these tests don't just create objects in the open scene.
    /// </summary>
    public class ArenaSymmetryBuilderTests
    {
        private static readonly Vector3 Centre = new Vector3(10f, 0f, 20f);
        private Scene scene;
        private ArenaSymmetry arena;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            arena = MakeRoot("Arena").AddComponent<ArenaSymmetry>();
            arena.centre = Centre;
            arena.source = MakeChild("Source", arena.transform);
            arena.generated120 = MakeChild("Generated 120", arena.transform);
            arena.generated240 = MakeChild("Generated 240", arena.transform);
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        private GameObject MakeRoot(string name)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private Transform MakeChild(string name, Transform parent)
        {
            GameObject go = MakeRoot(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void AssertClose(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), $"expected {expected:F3} but was {actual:F3}");

        [Test]
        public void RebuildPlacesATurnedCopyOfEachSourceObjectInBothGeneratedThirds()
        {
            Transform crate = MakeChild("Crate", arena.source);
            crate.position = Centre + new Vector3(0f, 1f, 10f);          // map angle 90°
            crate.localScale = new Vector3(2f, 1f, 1f);

            Assert.IsEmpty(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));

            Assert.AreEqual(1, arena.generated120.childCount);
            Transform at120 = arena.generated120.GetChild(0);
            Assert.AreEqual("Crate", at120.name);
            AssertClose(Centre + new Vector3(-8.660254f, 1f, -5f), at120.position);   // map angle 210°
            AssertClose(new Vector3(-0.8660254f, 0f, -0.5f), at120.forward);
            AssertClose(new Vector3(2f, 1f, 1f), at120.localScale);

            AssertClose(Centre + new Vector3(8.660254f, 1f, -5f), arena.generated240.GetChild(0).position); // 330°
        }

        [Test]
        public void RebuildingTwiceReplacesTheCopiesInsteadOfAddingMore()
        {
            MakeChild("Crate", arena.source).position = Centre + new Vector3(0f, 0f, 10f);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            Assert.AreEqual(1, arena.generated120.childCount);
            Assert.AreEqual(1, arena.generated240.childCount);
        }

        [Test]
        public void GeneratedCopiesAndTheirChildrenAreNotEditable()
        {
            Transform house = MakeChild("House", arena.source);
            MakeChild("Roof", house);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Transform copy = arena.generated120.GetChild(0);
            Assert.AreNotEqual(0, (int)(copy.gameObject.hideFlags & HideFlags.NotEditable));
            Assert.AreNotEqual(0, (int)(copy.GetChild(0).gameObject.hideFlags & HideFlags.NotEditable));
        }

        [Test]
        public void RebuildMovesSnappedPartnersAndCentredObjectsWithoutCopyingThem()
        {
            Transform towerSource = MakeRoot("Tower A").transform;
            towerSource.position = Centre + new Vector3(0f, 0f, 30f);
            Transform tower120 = MakeRoot("Tower B").transform;
            Transform tower240 = MakeRoot("Tower C").transform;
            arena.snappedTriplets.Add(new ArenaSymmetry.SnappedTriplet { source = towerSource, at120 = tower120, at240 = tower240 });
            Transform middle = MakeRoot("Middle").transform;
            middle.position = new Vector3(3f, 5f, 3f);
            arena.centred.Add(middle);

            Assert.IsEmpty(ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false));

            AssertClose(Centre + new Vector3(-25.98076f, 0f, -15f), tower120.position);
            AssertClose(Centre + new Vector3(25.98076f, 0f, -15f), tower240.position);
            AssertClose(new Vector3(Centre.x, 5f, Centre.z), middle.position);
            Assert.AreEqual(0, arena.generated120.childCount);
        }

        [Test]
        public void RebuildRefusesATowerUnderSourceAndBuildsNothing()
        {
            Transform tower = MakeChild("Tower 8", arena.source);
            tower.gameObject.AddComponent<BuildingCapture>();

            List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);

            Assert.IsNotEmpty(problems);
            StringAssert.Contains("Tower 8", string.Join("\n", problems));
            Assert.AreEqual(0, arena.generated120.childCount);
        }

        [Test]
        public void ValidateReportsACopyMovedByHand()
        {
            MakeChild("Crate", arena.source).position = Centre + new Vector3(0f, 0f, 10f);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            arena.generated120.GetChild(0).position += new Vector3(1f, 0f, 0f);

            List<string> problems = ArenaSymmetryBuilder.Validate(arena);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("Crate", problems[0]);
        }

        [Test]
        public void ValidateReportsASourceObjectAddedWithoutARebuild()
        {
            MakeChild("Crate", arena.source);
            ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
            MakeChild("Barrel", arena.source);

            Assert.IsNotEmpty(ArenaSymmetryBuilder.Validate(arena));
        }
    }
}
```

- [ ] **Step 4: Recompile to verify the failing state**

Run: `unity command recompile` → poll `recompile_status`.
Expected: errors naming `ArenaSymmetryBuilder` (not yet written). No other errors. If the asmdef reference is wrong,
the error names `Overpower.EditorTools`; fix that first.

- [ ] **Step 5: Write the builder**

`Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs`:

```csharp
using System.Collections.Generic;
using Overpower.Arena;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// The arena tool behind ArenaSymmetry's "Rebuild thirds" and "Validate" buttons. See ArenaSymmetry's class
    /// comment for the designer workflow; this class is the how.
    /// </summary>
    public static class ArenaSymmetryBuilder
    {
        /// <summary>How far a copy or snapped partner may sit from where its source puts it before Validate reports
        /// it. Tight on purpose: the tool places copies exactly, so anything bigger means a hand edit or a forgotten
        /// rebuild.</summary>
        public const float PositionToleranceMetres = 0.05f;
        public const float AngleToleranceDegrees = 0.5f;

        private const string UndoName = "Rebuild arena thirds";

        /// <summary>Deletes both generated thirds, copies Source into them turned 120° and 240°, moves snapped
        /// partners and centred objects, and returns Validate's findings. If the setup is wrong it changes nothing
        /// and returns what is wrong. It never saves the scene: the designer looks first, then saves.</summary>
        public static List<string> Rebuild(ArenaSymmetry arena, bool recordUndo)
        {
            List<string> problems = CheckSetup(arena);
            if (problems.Count > 0)
                return problems;

            int undoGroup = Undo.GetCurrentGroup();
            if (recordUndo)
                Undo.SetCurrentGroupName(UndoName);

            Transform[] targets = { arena.generated120, arena.generated240 };
            for (int t = 0; t < targets.Length; t++)
            {
                Transform target = targets[t];
                for (int i = target.childCount - 1; i >= 0; i--)
                {
                    GameObject old = target.GetChild(i).gameObject;
                    if (recordUndo) Undo.DestroyObjectImmediate(old);
                    else UnityEngine.Object.DestroyImmediate(old);
                }

                int thirds = t + 1;
                for (int i = 0; i < arena.source.childCount; i++)
                {
                    Transform original = arena.source.GetChild(i);
                    GameObject copy = UnityEngine.Object.Instantiate(original.gameObject, target);
                    copy.name = original.name;
                    copy.transform.SetPositionAndRotation(
                        RadialSymmetry.RotatePoint(original.position, arena.centre, thirds),
                        RadialSymmetry.RotateRotation(original.rotation, thirds));
                    copy.transform.localScale = original.localScale;
                    foreach (Transform part in copy.GetComponentsInChildren<Transform>(true))
                        part.gameObject.hideFlags |= HideFlags.NotEditable;
                    if (recordUndo)
                        Undo.RegisterCreatedObjectUndo(copy, UndoName);
                }
            }

            foreach (ArenaSymmetry.SnappedTriplet triplet in arena.snappedTriplets)
            {
                Place(triplet.at120, RadialSymmetry.RotatePoint(triplet.source.position, arena.centre, 1),
                      RadialSymmetry.RotateRotation(triplet.source.rotation, 1), recordUndo);
                Place(triplet.at240, RadialSymmetry.RotatePoint(triplet.source.position, arena.centre, 2),
                      RadialSymmetry.RotateRotation(triplet.source.rotation, 2), recordUndo);
            }

            foreach (Transform middle in arena.centred)
                Place(middle, new Vector3(arena.centre.x, middle.position.y, arena.centre.z), middle.rotation, recordUndo);

            if (recordUndo)
                Undo.CollapseUndoOperations(undoGroup);
            if (!EditorSceneManager.IsPreviewScene(arena.gameObject.scene))
                EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);

            return Validate(arena);
        }

        /// <summary>Everything that doesn't match its source, one line each. Empty means symmetric.</summary>
        public static List<string> Validate(ArenaSymmetry arena)
        {
            List<string> problems = CheckSetup(arena);
            if (problems.Count > 0)
                return problems;

            Transform[] targets = { arena.generated120, arena.generated240 };
            for (int t = 0; t < targets.Length; t++)
            {
                Transform target = targets[t];
                if (target.childCount != arena.source.childCount)
                {
                    problems.Add($"{target.name} has {target.childCount} objects but Source has {arena.source.childCount}: press Rebuild thirds.");
                    continue;
                }
                for (int i = 0; i < arena.source.childCount; i++)
                {
                    Transform original = arena.source.GetChild(i);
                    Transform copy = target.GetChild(i);
                    if (copy.name != original.name)
                        problems.Add($"{target.name}: object {i} is '{copy.name}' but Source's is '{original.name}': press Rebuild thirds.");
                    Compare(original, copy, arena.centre, t + 1, problems);
                    if ((copy.localScale - original.localScale).sqrMagnitude > 1e-6f)
                        problems.Add($"{copy.name} ({(t + 1) * 120}°) has a different scale from its Source.");
                }
            }

            foreach (ArenaSymmetry.SnappedTriplet triplet in arena.snappedTriplets)
            {
                Compare(triplet.source, triplet.at120, arena.centre, 1, problems);
                Compare(triplet.source, triplet.at240, arena.centre, 2, problems);
            }

            foreach (Transform middle in arena.centred)
            {
                float off = new Vector2(middle.position.x - arena.centre.x, middle.position.z - arena.centre.z).magnitude;
                if (off > PositionToleranceMetres)
                    problems.Add($"{middle.name} is {off:0.00} m from the centre.");
            }

            return problems;
        }

        private static List<string> CheckSetup(ArenaSymmetry arena)
        {
            var problems = new List<string>();
            if (arena == null) { problems.Add("No ArenaSymmetry given."); return problems; }
            if (arena.source == null || arena.generated120 == null || arena.generated240 == null)
            {
                problems.Add("Source, Generated 120 and Generated 240 must all be assigned.");
                return problems;
            }
            if (arena.source == arena.generated120 || arena.source == arena.generated240 || arena.generated120 == arena.generated240
                || arena.generated120.IsChildOf(arena.source) || arena.generated240.IsChildOf(arena.source)
                || arena.source.IsChildOf(arena.generated120) || arena.source.IsChildOf(arena.generated240))
                problems.Add("Source and the two generated thirds must be three separate objects, none inside another.");

            foreach (Transform parent in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                // Copies are placed in world space but keep Source's local scale, which is only the same thing when
                // these parents have no position, rotation or scale of their own.
                if (parent.position != Vector3.zero || parent.rotation != Quaternion.identity || parent.lossyScale != Vector3.one)
                    problems.Add($"{parent.name} must sit at position 0, rotation 0, scale 1 (it doesn't).");
            }

            foreach (PhotonView view in arena.source.GetComponentsInChildren<PhotonView>(true))
                problems.Add($"'{view.name}' under Source has a PhotonView. Networked objects can't be copied (their view ids must stay unique): move it out of Source and add it to Snapped Triplets instead.");
            foreach (BuildingCapture tower in arena.source.GetComponentsInChildren<BuildingCapture>(true))
                problems.Add($"'{tower.name}' under Source is a capture tower. Towers can't be copied (their ids must stay unique): move it out of Source and add it to Snapped Triplets instead.");

            for (int i = 0; i < arena.snappedTriplets.Count; i++)
            {
                ArenaSymmetry.SnappedTriplet triplet = arena.snappedTriplets[i];
                if (triplet == null || triplet.source == null || triplet.at120 == null || triplet.at240 == null)
                {
                    problems.Add($"Snapped Triplet {i} has an empty slot.");
                    continue;
                }
                foreach (Transform member in new[] { triplet.source, triplet.at120, triplet.at240 })
                    if (IsUnderArenaThirds(arena, member))
                        problems.Add($"'{member.name}' (Snapped Triplet {i}) is inside Source or a generated third, so a rebuild would copy or delete it.");
            }
            foreach (Transform middle in arena.centred)
            {
                if (middle == null) problems.Add("Centred has an empty slot.");
                else if (IsUnderArenaThirds(arena, middle)) problems.Add($"'{middle.name}' (Centred) is inside Source or a generated third.");
            }
            return problems;
        }

        private static bool IsUnderArenaThirds(ArenaSymmetry arena, Transform t) =>
            t.IsChildOf(arena.source) || t.IsChildOf(arena.generated120) || t.IsChildOf(arena.generated240);

        private static void Compare(Transform original, Transform copy, Vector3 centre, int thirds, List<string> problems)
        {
            float drift = Vector3.Distance(RadialSymmetry.RotatePoint(original.position, centre, thirds), copy.position);
            if (drift > PositionToleranceMetres)
                problems.Add($"{copy.name} ({thirds * 120}°) is {drift:0.00} m from where {original.name} puts it.");
            float turn = Quaternion.Angle(RadialSymmetry.RotateRotation(original.rotation, thirds), copy.rotation);
            if (turn > AngleToleranceDegrees)
                problems.Add($"{copy.name} ({thirds * 120}°) is turned {turn:0.0}° away from where {original.name} puts it.");
        }

        private static void Place(Transform target, Vector3 position, Quaternion rotation, bool recordUndo)
        {
            if (recordUndo)
                Undo.RecordObject(target, UndoName);
            target.SetPositionAndRotation(position, rotation);
            // Towers 1/2/4/5/7/8 are prefab instances: without this a scripted move can be lost on save.
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
```

- [ ] **Step 6: Write the Inspector and menu items**

`Assets/scripts/Editor/Arena/ArenaSymmetryInspector.cs`:

```csharp
using System.Collections.Generic;
using Overpower.Arena;
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>Adds the Rebuild thirds / Validate buttons to ArenaSymmetry, and the same two actions under the
    /// OverPower > Arena menu. Results go to the Console.</summary>
    [CustomEditor(typeof(ArenaSymmetry))]
    public class ArenaSymmetryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit only the objects under Source, then press Rebuild thirds, check the arena, and save the scene. " +
                "The two generated thirds are rebuilt from Source every time, so edits made to them are thrown away.",
                MessageType.Info);
            DrawDefaultInspector();

            var arena = (ArenaSymmetry)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild thirds"))
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
            if (GUILayout.Button("Validate"))
                Report("Validate", ArenaSymmetryBuilder.Validate(arena));
        }

        [MenuItem("OverPower/Arena/Rebuild thirds")]
        private static void RebuildFromMenu()
        {
            ArenaSymmetry arena = FindArena();
            if (arena != null)
                Report("Rebuild thirds", ArenaSymmetryBuilder.Rebuild(arena, recordUndo: true));
        }

        [MenuItem("OverPower/Arena/Validate")]
        private static void ValidateFromMenu()
        {
            ArenaSymmetry arena = FindArena();
            if (arena != null)
                Report("Validate", ArenaSymmetryBuilder.Validate(arena));
        }

        private static ArenaSymmetry FindArena()
        {
            ArenaSymmetry arena = UnityEngine.Object.FindFirstObjectByType<ArenaSymmetry>(FindObjectsInactive.Include);
            if (arena == null)
                Debug.LogWarning("[Arena] There is no ArenaSymmetry in the open scene.");
            return arena;
        }

        private static void Report(string action, List<string> problems)
        {
            if (problems.Count == 0)
                Debug.Log($"[Arena] {action}: every generated object and snapped partner matches its source.");
            else
                Debug.LogWarning($"[Arena] {action}: {problems.Count} problem(s):\n" + string.Join("\n", problems));
        }
    }
}
```

- [ ] **Step 7: Recompile, run tests, verify they pass**

Run: `recompile` → `recompile_status` (completed, `errors: []`). Rule 6 dirty check. Run the tests async and poll.
Expected: all pass; the total is Task A1's total + 7.
**Then check that the tests did not dirty the scene** (eval `isDirty`, expect `False`). If it is dirty, the preview
scene approach leaked objects into Game Scene. Stop and report instead of saving.

- [ ] **Step 8: Commit**

```bash
git add Assets/scripts/Arena Assets/scripts/Editor Assets/scripts/Editor.meta Assets/Tests/Overpower.Tests.asmdef Assets/Tests/ArenaSymmetryBuilderTests.cs Assets/Tests/ArenaSymmetryBuilderTests.cs.meta
git status   # nothing else staged or modified
git commit -m "feat(arena): ArenaSymmetry tool with Rebuild thirds and Validate" -m "Co-Authored-By: <your model line>"
git push
```

---

### Task A3: Migrate `Game Scene` into the tool, open the capital pockets, centre Tier 4

**Files:**
- Modify: `Assets/Scenes/Game Scene.unity`
- Create: `Assets/Tests/ArenaSymmetrySceneTests.cs`
- Scratch (not committed): `<scratchpad>/ArenaRender.cs`, `<scratchpad>/ArenaMigration.cs`

**Measured facts this task relies on (2026-09-16):**
- Wall pieces (`Wall_01`, `Wall_02`) are 7.00 m long along local X and 5.98 m tall. Their pivot line is the wall's
  outer face; the 0.72 m thickness sits on local +Z, which faces into the arena. Pivot y = 0.06.
- The wall triangle's centroid is (65.05, 53.34). Each wall's pivot line is 31.04 m from it.
- Terrain tiles are `Terrain1`…`Terrain1 (3)`, 64 m square, covering x 0–128 and z 0–128, all sharing
  `Terrain_FPS2.asset`.
- Corner crates live under `Enviorment/Props/Markets/Box/bocs (n)`, with the group pivots far from the crates. Palms
  are under `Enviorment/Nature/Tress/PalmTree_02 (15)`.

**Pocket geometry (source third, capital axis +Z) [C]:**
- The converging side walls keep 4 pieces from each wall's midpoint (ending 24.5 m along the wall), which puts the
  pocket mouth at z = 90.08, x = 65.05 ± 14.63.
- The side walls then run parallel to the axis for 5 pieces (35 m) to the back wall at z = 125.08.
- The back wall is 4 pieces stretched ×1.045 to span 29.26 m.
- Result: the capital (#8 at z = 111.00, radius 10) has 4.6 m clearance each side and 14.1 m behind it.
- The two bottom pockets reach x −4.4 and x 134.5, so two terrain tiles are added at x −64 and x 128.

- [ ] **Step 1: Render the arena before any change**

Write `<scratchpad>/ArenaRender.cs`:

```csharp
using UnityEditor;
using UnityEngine;

public static class ArenaRender
{
    // Orthographic top-down render of the open scene to a PNG. The camera is HideAndDontSave, so Game Scene is not
    // dirtied (measured 2026-09-16). 10 px per metre; covers x -7..137, z -7..137.
    public static string Run(string path)
    {
        GameObject go = EditorUtility.CreateGameObjectWithHideFlags("ArenaRenderCam", HideFlags.HideAndDontSave, typeof(Camera));
        var cam = go.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 72f;
        cam.transform.SetPositionAndRotation(new Vector3(65f, 150f, 65f), Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        var rt = new RenderTexture(1440, 1440, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1440, 1440, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1440, 1440), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        UnityEngine.Object.DestroyImmediate(go);
        return "wrote " + path + " dirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty;
    }
}
```

Run:
`unity command run_script -- --file "<scratchpad>/ArenaRender.cs" --entry ArenaRender.Run --args '["<scratchpad>/arena-before.png"]' --timeout_ms 120000`
If `--args` is rejected, check `unity command --query run_script --detail full`, or hard-code the path in `Run()` with
no parameter. Expected: `dirty=False`. Read the PNG. Image up is +Z, and pixel (px, py) = world
(px/10 − 7, 137 − py/10).

- [ ] **Step 2: Write the migration script**

`<scratchpad>/ArenaMigration.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Overpower.Arena;
using Overpower.EditorTools;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// One-time: moves Game Scene's arena into ArenaSymmetry. Not committed; the result (the scene) is.
public static class ArenaMigration
{
    private static readonly Vector3 C = new Vector3(65.05f, 0f, 53.34f);
    private const float InRadius = 31.04f;          // centre to each wall's pivot line
    private const float SourceStartDegrees = 25f;    // Source third = map angles [25°, 145°)
    private const float WallLength = 7f;
    private const float WallY = 0.06f;
    private const float PocketHalfWidth = 14.63f;    // pocket side walls at x = C.x ± this (source third)
    private const float PocketMouthU = 36.74f;       // distance from C along the capital axis where the pocket starts
    private const float PocketBackU = 71.74f;        // PocketMouthU + 5 × 7 m
    private static readonly float[] CapitalAxes = { 90f, 210f, 330f };

    public static string Run()
    {
        var log = new StringBuilder();
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Game Scene.unity") return "ABORT: active scene is " + scene.path;
        if (scene.isDirty) return "ABORT: scene is dirty before migration - reload it from disk first";

        Transform env = GameObject.Find("Enviorment").transform;
        if (env.position != Vector3.zero || env.rotation != Quaternion.identity || env.lossyScale != Vector3.one)
            return "ABORT: Enviorment is not at identity";
        if (env.Find("Arena") != null) return "ABORT: Enviorment/Arena already exists";

        Dictionary<int, BuildingCapture> towers = UnityEngine.Object
            .FindObjectsByType<BuildingCapture>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .ToDictionary(b => b.buildingID);
        Transform spawns = GameObject.Find("Spawn Points").transform;

        // 1. Structure.
        var arenaGo = new GameObject("Arena");
        arenaGo.transform.SetParent(env, false);
        var arena = arenaGo.AddComponent<ArenaSymmetry>();
        arena.centre = C;
        arena.sourceStartDegrees = SourceStartDegrees;
        arena.source = NewChild("Source (Team 2 third)", arenaGo.transform);
        arena.generated120 = NewChild("Generated 120°", arenaGo.transform);
        arena.generated240 = NewChild("Generated 240°", arenaGo.transform);
        foreach (int[] ids in new[] { new[] { 8, 6, 7 }, new[] { 2, 0, 1 }, new[] { 5, 3, 4 } })
        {
            AddTriplet(arena, towers[ids[0]].transform, towers[ids[1]].transform, towers[ids[2]].transform);
            AddTriplet(arena, towers[ids[0]].flagRenderer.transform, towers[ids[1]].flagRenderer.transform, towers[ids[2]].flagRenderer.transform);
        }
        AddTriplet(arena, spawns.Find("team (2)"), spawns.Find("team (1)"), spawns.Find("team"));
        arena.centred.Add(towers[9].transform);
        arena.centred.Add(towers[9].flagRenderer.transform);

        // 2. Source towers exactly on their axes, keeping their distance from the centre; carpets move with them.
        MoveWithCarpet(towers[8], AtMapAngle(90f, 57.66f));
        MoveWithCarpet(towers[2], AtMapAngle(90f, 34.02f));
        MoveWithCarpet(towers[5], AtMapAngle(150f, 20.44f));
        MoveWithCarpet(towers[9], C);
        var t9 = new SerializedObject(towers[9]);
        t9.FindProperty("captureRadius").floatValue = 8f;
        t9.ApplyModifiedPropertiesWithoutUndo();
        Transform spawn2 = spawns.Find("team (2)");
        spawn2.position = new Vector3(C.x, spawn2.position.y, spawn2.position.z);
        Record(spawn2);
        log.AppendLine($"source towers placed; tower 9 radius {towers[9].captureRadius}");

        // 3. Placement units: objects with their own Renderer/Collider, or groups no wider than 30 m.
        var units = new List<(Transform unit, string group)>();
        foreach (string group in new[] { "Houses", "Buildings", "Nature", "Props" })
            Collect(env.Find(group), group, units);
        log.AppendLine($"units found: {units.Count}");

        // Prefab children can't be moved or destroyed; unpack their outermost instance first.
        foreach ((Transform unit, string _) in units)
        {
            if (unit != null && PrefabUtility.IsPartOfPrefabInstance(unit.gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(unit.gameObject))
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(unit.gameObject);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                log.AppendLine($"unpacked prefab {root.name}");
            }
        }

        var groups = new Dictionary<string, Transform>();
        int moved = 0, deleted = 0, scenery = 0;
        foreach ((Transform unit, string group) in units)
        {
            if (unit == null) continue;
            Vector3 p = UnitPosition(unit);
            bool inArena = InsideTriangle(p, 3f) || CapitalAxes.Any(axis => InPocket(p, axis));
            if (!inArena) { scenery++; continue; }

            bool nearCentre = Flat(p - C).magnitude < 6f;                     // the fountain: Tier 4 replaces it
            bool inSource = RadialSymmetry.ThirdIndex(p, C, SourceStartDegrees) == 0;
            bool inSourcePocket = InPocket(p, 90f);                            // capital corner: cleared
            if (nearCentre || !inSource || inSourcePocket)
            {
                log.AppendLine($"delete {Path(unit)} at ({p.x:0.0}, {p.z:0.0}){(nearCentre ? " [centre]" : inSourcePocket ? " [pocket]" : "")}");
                UnityEngine.Object.DestroyImmediate(unit.gameObject);
                deleted++;
                continue;
            }
            if (!groups.TryGetValue(group, out Transform target))
                groups[group] = target = NewChild(group, arena.source);
            unit.SetParent(target, true);
            moved++;
        }
        log.AppendLine($"moved into Source: {moved}, deleted: {deleted}, scenery left outside: {scenery}");

        // 4. Walls: replace every wall piece with the Source third's layout (templates captured first).
        Transform boundry = env.Find("Boundry");
        GameObject wall1 = boundry.Find("Wall_01 (34)").gameObject;
        GameObject wall2 = boundry.Find("Wall_02 (16)").gameObject;
        Transform walls = NewChild("Boundry", arena.source);
        Vector3 vertex = AtMapAngle(90f, 2f * InRadius);
        Vector3 midRight = AtMapAngle(30f, InRadius), midLeft = AtMapAngle(150f, InRadius);
        Vector3 alongRight = (vertex - midRight).normalized, alongLeft = (vertex - midLeft).normalized;
        // Right wall: its midpoint piece belongs to this third (turned 120°, it is also the left wall's midpoint).
        for (int k = 0; k < 4; k++)
            PlaceWall(k == 2 ? wall2 : wall1, walls, $"Wall R {k * 7}m", midRight + alongRight * (WallLength * k), 240f, 1f);
        for (int k = 1; k < 4; k++)
            PlaceWall(k == 2 ? wall2 : wall1, walls, $"Wall L {k * 7}m", midLeft + alongLeft * (WallLength * k), 120f, 1f);
        float mouthZ = C.z + PocketMouthU, backZ = C.z + PocketBackU;
        for (int j = 0; j < 5; j++)
        {
            float z = mouthZ + WallLength * (j + 0.5f);
            PlaceWall(j == 2 ? wall2 : wall1, walls, $"Pocket R {j}", new Vector3(C.x + PocketHalfWidth, 0f, z), 270f, 1f);
            PlaceWall(j == 2 ? wall2 : wall1, walls, $"Pocket L {j}", new Vector3(C.x - PocketHalfWidth, 0f, z), 90f, 1f);
        }
        float backWidth = 2f * PocketHalfWidth;
        for (int j = 0; j < 4; j++)
            PlaceWall(j % 2 == 1 ? wall2 : wall1, walls, $"Pocket Back {j}",
                      new Vector3(C.x - PocketHalfWidth + backWidth * (j + 0.5f) / 4f, 0f, backZ), 180f, backWidth / (4f * WallLength));
        int oldWalls = boundry.childCount;
        UnityEngine.Object.DestroyImmediate(boundry.gameObject);
        log.AppendLine($"old wall pieces removed: {oldWalls}; new Source wall pieces: {walls.childCount}");

        // 5. Ground under the two bottom pockets.
        GameObject terrain = env.Find("Terrain1").gameObject;
        foreach ((string name, float x) in new[] { ("Terrain1 (4)", -64f), ("Terrain1 (5)", 128f) })
        {
            GameObject tile = UnityEngine.Object.Instantiate(terrain, env);
            tile.name = name;
            tile.transform.position = new Vector3(x, 0f, 0f);
        }

        // 6. Empty groups left behind.
        foreach (string group in new[] { "Houses", "Buildings", "Nature", "Props" })
            RemoveEmpty(env.Find(group), log);

        // 7. Build the other two thirds.
        List<string> problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
        log.AppendLine(problems.Count == 0 ? "rebuild: symmetric" : "REBUILD PROBLEMS:\n" + string.Join("\n", problems));

        // 8. Anything but terrain under a capital pocket's floor (mountains, leftovers)?
        Physics.SyncTransforms();
        foreach (float axis in CapitalAxes)
        {
            Vector3 u = AxisDir(axis), v = new Vector3(-u.z, 0f, u.x);
            var blockers = new HashSet<string>();
            for (float a = PocketMouthU + 1f; a <= PocketBackU - 1.5f; a += 2f)
                for (float b = -(PocketHalfWidth - 1.5f); b <= PocketHalfWidth - 1.5f; b += 2f)
                {
                    Vector3 point = C + u * a + v * b + Vector3.up * 60f;
                    if (Physics.Raycast(point, Vector3.down, out RaycastHit hit, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && !(hit.collider is TerrainCollider)
                        && hit.collider.GetComponentInParent<BuildingCapture>() == null   // the capital house itself
                        && hit.collider.GetComponentInParent<PhotonView>() == null)       // its flag carpet
                        blockers.Add(Path(hit.collider.transform));
                }
            log.AppendLine($"pocket {axis}° blockers: {(blockers.Count == 0 ? "none" : string.Join(", ", blockers))}");
        }

        if (problems.Count == 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("scene saved");
        }
        else
        {
            log.AppendLine("NOT SAVED - reload the scene from disk before doing anything else");
        }
        return log.ToString();
    }

    private static Transform NewChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void AddTriplet(ArenaSymmetry arena, Transform a, Transform b, Transform c) =>
        arena.snappedTriplets.Add(new ArenaSymmetry.SnappedTriplet { source = a, at120 = b, at240 = c });

    private static Vector3 AxisDir(float degrees) =>
        new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad));

    private static Vector3 AtMapAngle(float degrees, float radius) => C + AxisDir(degrees) * radius;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static void Record(UnityEngine.Object o)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(o))
            PrefabUtility.RecordPrefabInstancePropertyModifications(o);
    }

    private static void MoveWithCarpet(BuildingCapture tower, Vector3 flatTarget)
    {
        Vector3 delta = new Vector3(flatTarget.x, tower.transform.position.y, flatTarget.z) - tower.transform.position;
        tower.transform.position += delta;
        Record(tower.transform);
        if (tower.flagRenderer != null)
        {
            tower.flagRenderer.transform.position += delta;
            Record(tower.flagRenderer.transform);
        }
    }

    private static void Collect(Transform parent, string group, List<(Transform, string)> units)
    {
        foreach (Transform child in parent.Cast<Transform>().ToList())
        {
            bool isTower = child.GetComponent<BuildingCapture>() != null || child.GetComponent<PhotonView>() != null;
            if (isTower) continue;
            bool holdsTower = child.GetComponentInChildren<BuildingCapture>(true) != null || child.GetComponentInChildren<PhotonView>(true) != null;
            if (holdsTower) { Collect(child, group, units); continue; }
            if (child.GetComponent<Renderer>() != null || child.GetComponent<Collider>() != null) { units.Add((child, group)); continue; }
            Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            float minX = renderers.Min(r => r.transform.position.x), maxX = renderers.Max(r => r.transform.position.x);
            float minZ = renderers.Min(r => r.transform.position.z), maxZ = renderers.Max(r => r.transform.position.z);
            if (Mathf.Max(maxX - minX, maxZ - minZ) <= 30f) units.Add((child, group));
            else Collect(child, group, units);
        }
    }

    // A single object's own pivot; a group's average child position (group pivots can be far from their contents).
    private static Vector3 UnitPosition(Transform unit)
    {
        if (unit.GetComponent<Renderer>() != null || unit.GetComponent<Collider>() != null)
            return unit.position;
        Renderer[] renderers = unit.GetComponentsInChildren<Renderer>(true);
        Vector3 sum = Vector3.zero;
        foreach (Renderer r in renderers) sum += r.transform.position;
        return sum / renderers.Length;
    }

    private static bool InsideTriangle(Vector3 p, float margin) =>
        new[] { 30f, 150f, 270f }.All(a => Vector3.Dot(Flat(p - C), AxisDir(a)) <= InRadius + margin);

    private static bool InPocket(Vector3 p, float axisDegrees)
    {
        Vector3 u = AxisDir(axisDegrees), v = new Vector3(-u.z, 0f, u.x);
        float along = Vector3.Dot(Flat(p - C), u), across = Vector3.Dot(Flat(p - C), v);
        return along >= PocketMouthU - 0.5f && along <= PocketBackU + 1.5f && Mathf.Abs(across) <= PocketHalfWidth + 1f;
    }

    private static void PlaceWall(GameObject template, Transform parent, string name, Vector3 position, float yaw, float scaleX)
    {
        GameObject piece = UnityEngine.Object.Instantiate(template, parent);
        piece.name = name;
        piece.transform.SetPositionAndRotation(new Vector3(position.x, WallY, position.z), Quaternion.Euler(0f, yaw, 0f));
        piece.transform.localScale = new Vector3(scaleX, 1f, 1f);
    }

    private static void RemoveEmpty(Transform t, StringBuilder log)
    {
        foreach (Transform child in t.Cast<Transform>().ToList())
            RemoveEmpty(child, log);
        if (t.childCount == 0 && t.GetComponents<Component>().Length == 1)
        {
            log.AppendLine($"remove empty {Path(t)}");
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }
    }

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
```

- [ ] **Step 3: Run the migration**

Rule 6: confirm the scene isn't dirty and play mode is stopped. Then:
`unity command run_script -- --file "<scratchpad>/ArenaMigration.cs" --entry ArenaMigration.Run --timeout_ms 120000`
If the in-memory compile can't find `Overpower.Arena`, `Overpower.EditorTools` or Photon types, pass
`--references '["Overpower.Runtime","Overpower.Editor","PhotonUnityNetworking"]'` (check the exact parameter shape with
`unity command --query run_script --detail full`).
Expected log:
- `rebuild: symmetric` and `scene saved`;
- `moved into Source` roughly 20–40 and `deleted` roughly 60–100 (report the real numbers);
- the pocket blocker lines.

**The check samples only the pocket interior (1.5 m inside the walls), so any blocker listed is a finding.** If a `Mountains` piece blocks a pocket, move that
mountain piece straight outward along that pocket's axis until the check reports none. Mountains are scenery and not
part of the symmetry. Then save and report which pieces you moved and by how much.

If anything aborted or failed to save: reload the scene from disk (rule 6), `git checkout -- "Assets/Scenes/Game
Scene.unity"` only if the file on disk changed, fix the script, and re-run.

- [ ] **Step 4: Render after, and look**

Run `ArenaRender.Run` again to `<scratchpad>/arena-after.png`. Read both PNGs. Check and report each:
1. The three thirds look identical when turned (compare the V-shaped cluster, the houses, the wall barriers).
2. No gap and no doubled wall piece at the three seams (map angles 25°, 145°, 265°), or along any wall.
3. Each pocket is closed: side walls meet the converging walls and the back wall with no walk-through gap. A gap
   narrower than a player (~1 m) still counts.
4. The fountain is gone, and the Tier 4 house sits at the centre.
5. No crates or palms inside the pockets.
6. Nothing is floating over the black background (the terrain covers every pocket floor).

If a seam or corner is wrong, fix it **in Source only** (move or add a piece under `Source (Team 2 third)`), press
Rebuild (eval `Overpower.EditorTools.ArenaSymmetryBuilder.Rebuild(UnityEngine.Object.FindFirstObjectByType<Overpower.Arena.ArenaSymmetry>(), false)`),
save the scene, and render again.

- [ ] **Step 5: Write the scene test**

`Assets/Tests/ArenaSymmetrySceneTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    /// <summary>
    /// Guards the saved arena: every generated object and snapped partner must match the Source third, and every
    /// capital, T2 and T3 tower (plus its flag carpet) must be kept symmetric by the tool. It fails when someone edits a
    /// generated third by hand, forgets to press Rebuild thirds after editing Source, or adds a tower the tool doesn't
    /// know about.
    /// </summary>
    public class ArenaSymmetrySceneTests
    {
        private const string ScenePath = "Assets/Scenes/Game Scene.unity";

        [Test]
        public void TheArenaMatchesItsSourceThird()
        {
            WithGameScene(scene =>
            {
                List<string> problems = ArenaSymmetryBuilder.Validate(Find<ArenaSymmetry>(scene).Single());
                Assert.IsEmpty(problems,
                    "Open Game Scene, select Enviorment/Arena and press Rebuild thirds, then save:\n" + string.Join("\n", problems));
            });
        }

        [Test]
        public void EveryCapitalT2AndT3TowerAndItsCarpetIsKeptSymmetric()
        {
            WithGameScene(scene =>
            {
                ArenaSymmetry arena = Find<ArenaSymmetry>(scene).Single();
                var snapped = new HashSet<Transform>(arena.snappedTriplets.SelectMany(t => new[] { t.source, t.at120, t.at240 }));
                List<BuildingCapture> towers = Find<BuildingCapture>(scene).ToList();

                foreach (int tier in new[] { 1, 2, 3 })
                {
                    List<BuildingCapture> ofTier = towers.Where(b => b.tier == tier).ToList();
                    Assert.AreEqual(3, ofTier.Count, $"Tier {tier} should have one tower per team.");
                    foreach (BuildingCapture tower in ofTier)
                    {
                        Assert.IsTrue(snapped.Contains(tower.transform), $"Tower {tower.buildingID} (tier {tier}) is not in a Snapped Triplet.");
                        Assert.IsTrue(tower.flagRenderer != null && snapped.Contains(tower.flagRenderer.transform),
                                      $"Tower {tower.buildingID}'s flag carpet is not in a Snapped Triplet.");
                    }
                }

                BuildingCapture centre = towers.Single(b => b.tier == 4);
                Assert.IsTrue(arena.centred.Contains(centre.transform), "The Tier 4 tower is not in Centred.");
            });
        }

        // Uses Game Scene if it's already open (the normal case: read-only checks on what's loaded), otherwise opens it
        // additively and closes it again. Never opens it Single, which would prompt to save a dirty scene.
        private static void WithGameScene(Action<Scene> body)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try { body(scene); }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static IEnumerable<T> Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
```

- [ ] **Step 6: Prove the scene test can fail, then pass**

1. Recompile (clean), then run tests async. Expected: both scene tests pass; total = A2's total + 2.
2. Negative check (don't save): eval
   `var a = UnityEngine.Object.FindFirstObjectByType<Overpower.Arena.ArenaSymmetry>(); a.generated120.GetChild(0).position += UnityEngine.Vector3.right; return Overpower.EditorTools.ArenaSymmetryBuilder.Validate(a).Count;`
   Expected ≥ 1. Then reload the scene from disk (rule 6) and eval `isDirty` → `False`.

- [ ] **Step 7: Play-mode checks (one client)**

`editor_play`, join a room (the Editor joins through `JoinGameUI`/`RoomManager` exactly as in
`C:\UniStuff\Y3\MinorSkilled\Resources\loops\Limit Test\two-client-harness.md` §5, Editor side), then:
1. Teleport (via `PlayerDisplacement.TeleportTo`) to each capital pocket's interior: (65.05, 0, 118) for the top, and
   the same point turned 120° and 240° (use `RadialSymmetry.RotatePoint`). Read back the player's position after 1 s
   of game time: it must stay inside the walls and not fall. Report each.
2. Capture a 616×576 Game view (`capture_game_view`; copy to the scratchpad, delete it from `Assets/Temp`) from inside
   your own capital pocket, and one standing at the Tier 4 centre. Read both.
3. Stand 7.5 m from the centre (inside the new 8 m radius) and confirm `BuildingManager.TryGetZoneAt` returns zone 9.
   At 8.5 m, confirm it doesn't.
4. Walk-through check at the three seams: teleport to a point 1 m inside each seam's wall (map angles 25°, 145°, 265°
   at radius 29.5), push the player outward for 1 s (move input or `PlayerMotor`), and confirm they're still inside.
5. If the test range is on (`GameplayConfig`), confirm the dummies spawned outside wall colliders (list their positions).

`editor_stop`, poll until stopped. `git status` must show only `Assets/Scenes/Game Scene.unity` and the new test
(`GameplayConfig.asset` unchanged).

- [ ] **Step 8: Two clients**

Rebuild the development Player (`two-client-harness.md` §3, since the scene changed) and launch it (§4). Get both
clients into one room (§5), then:
1. Read tower 9's and tower 7's world positions on the Player through the Runtime server (§6 reflection). They must
   match the Editor's to 1 cm.
2. The Editor's player captures Tier 4 at the centre. The Player reads `BuildingManager` owner of zone 9 = the Editor's
   team.

Shut the Player down (§9).

- [ ] **Step 9: Commit**

```bash
git add "Assets/Scenes/Game Scene.unity" Assets/Tests/ArenaSymmetrySceneTests.cs Assets/Tests/ArenaSymmetrySceneTests.cs.meta
git status   # nothing else modified
git commit -m "feat(arena): symmetric arena from the Team 2 third, capital pockets, Tier 4 at the centre" -m "Co-Authored-By: <your model line>"
git push
```

Report the before/after render paths in the scratchpad (the controller sends them to Tudor), the migration log's
counts, every check's measured value, and the final test total.

---

### Task A4: Shrink each capital pocket to wrap the capture circle (Tudor, 2026-09-16)

Tudor's feedback on the A3 renders: **"the pocket size should be the size of the whole T1 capture area"** — the pocket
walls wrap the capital's 10 m capture circle; the radius stays 10 m [T].

**Files:**
- Modify: `Assets/Scenes/Game Scene.unity` (Source third's pocket walls; Rebuild)
- Scratch (not committed): `<scratchpad>/ArenaPocketResize.cs`

**Geometry (source third, capital axis +Z, centre C = (65.05, 53.34)) [C]:**
- Converging walls gain one more whole piece each (`Wall R 28m`, `Wall L 28m`) and end 31.5 m from each wall's
  midpoint, at z = 96.14, x = 65.05 ± 11.13. That is the first piece boundary where the lane is narrower than 23 m.
- **Pocket side walls** at x = C.x ± 11.13 (pivot = outer face). The inner face is 10.40 m from the axis, so the 10 m
  circle has 0.40 m to spare on each side.
- **Back wall** pivot at z = 53.34 (C.z) + 57.66 (capital) + 10 (radius) + 0.40 (spare) + 0.73 (thickness) =
  **122.13**, so its inner face is 10.40 m behind the capital.
- Side walls run 25.99 m (4 pieces, scale X 0.9283). The back wall is 22.26 m (3 pieces, scale X 1.0602).
- The terrain tiles at x −64 / 128 stay (the bottom-right back corner still reaches x ≈ 130). The mountains moved in
  A3 stay.

- [ ] **Step 1: Write the resize script**

```csharp
using System.Linq;
using System.Text;
using Overpower.Arena;
using Overpower.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// One-time: replaces the Source third's capital pocket with one that wraps the 10 m capture circle.
public static class ArenaPocketResize
{
    private static readonly Vector3 C = new Vector3(65.05f, 0f, 53.34f);
    private const float InRadius = 31.04f;
    private const float WallLength = 7f;
    private const float WallY = 0.06f;
    private const float WallThickness = 0.73f;     // mesh occupies local z 0.01..0.73 from the pivot (outer face)
    private const float CapitalDistance = 57.66f;  // tower 8 from the centre along +Z
    private const float CaptureRadius = 10f;

    public static string Run()
    {
        var log = new StringBuilder();
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying) return "ABORT: play mode";
        if (scene.isDirty) return "ABORT: scene is dirty - reload it from disk first";

        ArenaSymmetry arena = UnityEngine.Object.FindFirstObjectByType<ArenaSymmetry>();
        Transform walls = arena.source.Find("Boundry");
        GameObject wall1 = walls.Find("Wall R 0m").gameObject;
        GameObject wall2 = walls.Find("Wall R 14m").gameObject;

        // 1. Remove the old pocket.
        int removed = 0;
        foreach (Transform piece in walls.Cast<Transform>().Where(t => t.name.StartsWith("Pocket ")).ToList())
        {
            UnityEngine.Object.DestroyImmediate(piece.gameObject);
            removed++;
        }

        // 2. One more whole converging piece on each side (the lane narrows to the pocket's width there).
        Vector3 vertex = At(90f, 2f * InRadius);
        Vector3 midRight = At(30f, InRadius), midLeft = At(150f, InRadius);
        Vector3 alongRight = (vertex - midRight).normalized, alongLeft = (vertex - midLeft).normalized;
        Place(wall1, walls, "Wall R 28m", midRight + alongRight * 28f, 240f, 1f);
        Place(wall1, walls, "Wall L 28m", midLeft + alongLeft * 28f, 120f, 1f);

        Vector3 corner = midRight + alongRight * 31.5f;           // where the pocket's right wall starts
        float halfWidth = corner.x - C.x;                          // pivot (outer face) distance from the axis
        float spare = (halfWidth - WallThickness) - CaptureRadius; // room between circle and inner face
        float mouthZ = corner.z;
        float backZ = C.z + CapitalDistance + CaptureRadius + spare + WallThickness;

        // 3. Pocket side walls: 4 pieces stretched to fit exactly.
        float sideLength = backZ - mouthZ;
        float sideScale = sideLength / (4f * WallLength);
        for (int j = 0; j < 4; j++)
        {
            float z = mouthZ + sideLength * (j + 0.5f) / 4f;
            Place(j == 2 ? wall2 : wall1, walls, $"Pocket R {j}", new Vector3(C.x + halfWidth, 0f, z), 270f, sideScale);
            Place(j == 2 ? wall2 : wall1, walls, $"Pocket L {j}", new Vector3(C.x - halfWidth, 0f, z), 90f, sideScale);
        }

        // 4. Back wall: 3 pieces stretched to fit exactly.
        float backWidth = 2f * halfWidth;
        float backScale = backWidth / (3f * WallLength);
        for (int j = 0; j < 3; j++)
            Place(j == 1 ? wall2 : wall1, walls, $"Pocket Back {j}",
                  new Vector3(C.x - halfWidth + backWidth * (j + 0.5f) / 3f, 0f, backZ), 180f, backScale);

        log.AppendLine($"removed {removed} old pocket pieces; halfWidth {halfWidth:0.000}, spare {spare:0.000}, mouthZ {mouthZ:0.000}, backZ {backZ:0.000}, side scale {sideScale:0.0000}, back scale {backScale:0.0000}");

        var problems = ArenaSymmetryBuilder.Rebuild(arena, recordUndo: false);
        log.AppendLine(problems.Count == 0 ? "rebuild: symmetric" : "REBUILD PROBLEMS:\n" + string.Join("\n", problems));

        // 5. Nothing but terrain inside any capital's capture circle (walls must be outside it).
        Physics.SyncTransforms();
        foreach (float axis in new[] { 90f, 210f, 330f })
        {
            Vector3 capital = At(axis, CapitalDistance);
            int blocked = 0;
            for (int i = 0; i < 72; i++)
            {
                float a = i * 5f * Mathf.Deg2Rad;
                Vector3 edge = capital + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (CaptureRadius - 0.05f);
                // A horizontal ray at waist height from 6 m out (outside the capital house) to the circle's edge.
                Vector3 from = capital + (edge - capital).normalized * 6f + Vector3.up * 1f;
                Vector3 to = edge + Vector3.up * 1f;
                if (Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.collider.name.Contains("Wall"))
                    blocked++;
            }
            log.AppendLine($"capital {axis}°: circle edge rays blocked by a wall: {blocked} of 72");
        }

        if (problems.Count == 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine("scene saved");
        }
        else log.AppendLine("NOT SAVED - reload the scene from disk");
        return log.ToString();
    }

    private static Vector3 At(float degrees, float radius) =>
        C + new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

    private static void Place(GameObject template, Transform parent, string name, Vector3 position, float yaw, float scaleX)
    {
        GameObject piece = UnityEngine.Object.Instantiate(template, parent);
        piece.name = name;
        piece.transform.SetPositionAndRotation(new Vector3(position.x, WallY, position.z), Quaternion.Euler(0f, yaw, 0f));
        piece.transform.localScale = new Vector3(scaleX, 1f, 1f);
    }
}
```

- [ ] **Step 2: Run it**

Check the scene isn't dirty and play mode is stopped. Run:
`unity command --timeout 240 run_script -- --file "<scratchpad>/ArenaPocketResize.cs" --entry ArenaPocketResize.Run --timeout_ms 200000`

Expected:
- `halfWidth 11.132`, `spare 0.402`, `backZ 122.13` (±0.01)
- `rebuild: symmetric`
- all three `blocked: 0 of 72`
- `scene saved`

- [ ] **Step 3: Render and look.** Run `ArenaRender` to `<scratchpad>/arena-after-a4.png`. Read it. For each of the three
pockets, check and report:
- it is closed (the corners meet);
- it is visibly about as wide as the capture circle;
- the T2 sits outside, before the mouth;
- there are no leftover old pocket pieces.

Then zoom-render one pocket.

- [ ] **Step 4: Tests.** Recompile isn't needed (no code). Run the tests async: all pass, including both
`ArenaSymmetrySceneTests`. The scene isn't dirty afterwards.

- [ ] **Step 5: Play mode.**
- Join; teleport to 1 m inside each back corner of your own capital pocket, i.e. (C.x ± 9.4, 0, 121.3) turned for your
  team's axis. After 1 s of game time, read `Rigidbody.position`: the player is still inside.
- Stand on the capture circle's edge (9.8 m from the capital, toward a side wall). `BuildingManager.TryGetZoneAt`
  returns the capital's zone and the player isn't blocked there.
- Stop play mode.

- [ ] **Step 6: Commit** the scene only:
`feat(arena): capital pockets wrap the capture circle (Tudor feedback)`. Push. Report the log, the render paths, the
play-mode numbers, and the test total.

---

## Self-review against the spec

| Spec requirement | Task |
|---|---|
| `ArenaSymmetry` with centre, source, generated thirds, towers/spawns snapped, T4 centred | A2 (generic `snappedTriplets`, see deviation) |
| Inspector buttons + menu `OverPower › Arena › Rebuild thirds` (+ Validate) | A2 step 6 |
| Refuse PhotonView / BuildingCapture under Source, listing offenders | A2 step 5 (`CheckSetup`), test `RebuildRefusesATowerUnderSourceAndBuildsNothing` |
| Delete + copy rotated, plain copies, NotEditable | A2 step 5, tests |
| Snap towers + carpets + spawns; centre T4 + carpet | A2 step 5; A3 step 2 wiring |
| Marks dirty, never saves | A2 `Rebuild` |
| Validate 5 cm / 0.5°, run at end of Rebuild | A2 |
| `RadialSymmetry` pure + tests | A1 |
| Scene test (additive open, Validate, three towers per tier 120° apart) | A3 step 5 (snapped towers + Validate imply exact 120°) |
| Migration: structure, source towers on axes, units rule, [source] moved / others deleted / fountain deleted / scenery kept / empty groups deleted | A3 step 2 |
| Seams without gaps or doubles | A3 steps 2 (walls rebuilt on exact lines, 25° border) + 4 (render check) |
| T4 at centre, radius 8 m | A3 step 2 |
| Capital pocket ≥ 3 m clearance, inside terrain, crates/palms removed, capital distance kept | A3 step 2 (4.6 m / 14.1 m; terrain tiles added) + step 4 |
| Before/after renders to Tudor; 616×576 captures in a pocket and at T4 | A3 steps 1, 4, 7 |
| Play mode: spawn/walk pocket, capture T4 edge, no stuck seams | A3 step 7 |
| `git status` only scene/scripts/tests | A2 step 8, A3 steps 7, 9 |
| Two clients: towers and capture match | A3 step 8 |
| Test range dummies not inside geometry | A3 step 7.5 |
