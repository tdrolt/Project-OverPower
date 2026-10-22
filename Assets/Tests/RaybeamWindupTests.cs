using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Vision;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Raybeam's wind-up: from the press the beams wait, then fire from wherever the gun is and points at that moment; a stun,
    /// an overheat or death first cancels them and the ultimate meter stays full. Meanwhile every client draws a red hexagon with the caster's
    /// team edge shrinking at the caster's gun. The body colour comes from the look table's row (Gold here, so a hard-coded red is caught).</summary>
    public class RaybeamWindupTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int RaybeamId = 24;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color[] TeamColours = { new Color(0.9f, 0.3f, 0.1f, 1f), new Color(0.6f, 0.2f, 0.9f, 1f), new Color(0.1f, 0.8f, 0.9f, 1f) };

        // ---- the rules ----

        [Test]
        public void TheBeamsFireAtTheEndOfTheWindupAndAStunOrDeathCancelsThem()
        {
            Assert.AreEqual(RaybeamWindupStep.Waiting, RaybeamWindup.Step(0f, 0.4f, true), "just pressed");
            Assert.AreEqual(RaybeamWindupStep.Waiting, RaybeamWindup.Step(0.39f, 0.4f, true), "still charging");
            Assert.AreEqual(RaybeamWindupStep.Fire, RaybeamWindup.Step(0.4f, 0.4f, true), "the wind-up is over: fire");
            Assert.AreEqual(RaybeamWindupStep.Fire, RaybeamWindup.Step(0.47f, 0.4f, true), "a slow frame fires a little late, never skips the shot");
            Assert.AreEqual(RaybeamWindupStep.Cancel, RaybeamWindup.Step(0.2f, 0.4f, false), "stunned, overheated or dead while charging");
            Assert.AreEqual(RaybeamWindupStep.Cancel, RaybeamWindup.Step(0.4f, 0.4f, false), "a stun on the last frame still cancels: a stunned player never fires");
            Assert.AreEqual(RaybeamWindupStep.Fire, RaybeamWindup.Step(0f, 0f, true), "no wind-up set: it fires at once");
        }

        [Test]
        public void ACancelledWindupKeepsTheUltimateFull()
        {
            Assert.IsTrue(RaybeamWindup.SpendsUltimate(RaybeamWindupStep.Fire), "the meter is spent when the beams fire");
            Assert.IsFalse(RaybeamWindup.SpendsUltimate(RaybeamWindupStep.Cancel), "a cancelled wind-up spends nothing");
            Assert.IsFalse(RaybeamWindup.SpendsUltimate(RaybeamWindupStep.Waiting));
            Assert.IsTrue(RaybeamWindup.IsReady(true, false), "a full meter and nothing charging");
            Assert.IsFalse(RaybeamWindup.IsReady(true, true), "a second press while charging starts no second wind-up");
            Assert.IsFalse(RaybeamWindup.IsReady(false, false), "the meter must be full");
        }

        [Test]
        public void TheChargeHexagonShrinksOverTheWindupAndHoldsUntilTheBeamsFire()
        {
            Assert.AreEqual(1.2f, RaybeamWindup.ChargeRadius(0f, 0.4f, 1.2f, 0.2f), 1e-5f, "full size at the press");
            Assert.AreEqual(0.7f, RaybeamWindup.ChargeRadius(0.2f, 0.4f, 1.2f, 0.2f), 1e-5f, "halfway through, halfway down");
            Assert.AreEqual(0.2f, RaybeamWindup.ChargeRadius(0.4f, 0.4f, 1.2f, 0.2f), 1e-5f, "its end size as the beams fire");
            Assert.AreEqual(0.2f, RaybeamWindup.ChargeRadius(0.55f, 0.4f, 1.2f, 0.2f), 1e-5f, "held at its end size while the fire message travels");
            Assert.AreEqual(1.2f, RaybeamWindup.ChargeRadius(-0.05f, 0.4f, 1.2f, 0.2f), 1e-5f, "a clock a little behind never grows it");
            Assert.AreEqual(0.2f, RaybeamWindup.ChargeRadius(0f, 0f, 1.2f, 0.2f), 1e-5f, "no wind-up: the end size");

            Assert.IsTrue(RaybeamWindup.ChargeShown(0f, 0.4f));
            Assert.IsTrue(RaybeamWindup.ChargeShown(0.4f + RaybeamWindup.MissingEndSeconds - 0.01f, 0.4f), "held while the fire message is on its way");
            Assert.IsFalse(RaybeamWindup.ChargeShown(0.4f + RaybeamWindup.MissingEndSeconds + 0.01f, 0.4f),
                "a fire or cancel that never comes (the caster left) never leaves the hexagon on screen");
        }

        [Test]
        public void TheTeamEdgeRunsAGapOutsideTheHexagonsRedLine()
        {
            // The band is centred on the corner path; an outline a distance d outside a hexagon's sides has its corners d / cos 30 further out.
            float d = (0.14f + 0.05f) * 0.5f + 0.04f;
            Assert.AreEqual(1f + d / Mathf.Cos(30f * Mathf.Deg2Rad), RaybeamLook.HexEdgeRadius(1f, 0.14f, 0.05f, 0.04f), 1e-5f);
            Assert.AreEqual(0.5f + 0.07f / Mathf.Cos(30f * Mathf.Deg2Rad), RaybeamLook.HexEdgeRadius(0.5f, 0.14f, 0f, -1f), 1e-5f, "a negative gap is no gap");
        }

        // ---- the hexagon on screen ----

        [Test]
        public void TheChargeIsARedHexagonWithTheCastersTeamEdgeShrinkingAtTheGun()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                SetChargeLook(kit);
                GameObject body = kit.NewObject("caster");
                body.transform.position = new Vector3(3f, 0f, 4f);
                RaybeamChargeView view = RaybeamChargeView.TryCreate(kit.Theme, RaybeamId, null, null, body.transform);
                Assert.IsNotNull(view);
                kit.Make(view.gameObject);
                GameObject hex = view.transform.Find(RaybeamChargeView.HexChildName).gameObject;
                LineRenderer band = hex.transform.Find("Band").GetComponent<LineRenderer>();
                LineRenderer edge = hex.transform.Find("Edge").GetComponent<LineRenderer>();
                Assert.IsFalse(hex.activeSelf, "nothing shows until a wind-up starts");

                view.Begin(Time.time, 0.4f, 2);
                Assert.IsTrue(hex.activeSelf);
                Assert.IsTrue(view.Running);
                Assert.AreEqual(body.transform.position, view.transform.position, "at the caster (the gun, when there is one)");
                Assert.AreEqual(6, band.positionCount, "a hexagon");
                Assert.IsTrue(band.loop);
                Assert.AreEqual(1.2f, band.GetPosition(0).magnitude, 1e-4f, "Raybeam Charge Start Radius at the press");
                Assert.AreEqual(0.15f, band.widthMultiplier, 1e-5f, "Raybeam Charge Width");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, band.startColor, "the hexagon wears the Raybeam's row in the look table");
                Assert.AreEqual(0.8f, band.startColor.a, 1f / 255f, "Raybeam Charge Alpha");
                Assert.AreEqual(1.6f, GlowOf(band).r, 1e-4f, "Raybeam Charge Glow");
                Assert.AreEqual(6, edge.positionCount);
                Assert.IsTrue(edge.loop);
                Assert.AreEqual(RaybeamLook.HexEdgeRadius(1.2f, 0.15f, 0.05f, 0.04f), edge.GetPosition(0).magnitude, 1e-4f, "the edge runs a gap outside the red line");
                Assert.AreEqual(0.05f, edge.widthMultiplier, 1e-5f, "Raybeam Edge Width");
                MovementLookTestKit.AssertRgb(TeamColours[2], edge.startColor, "the edge says whose Raybeam is charging");
                Assert.AreEqual(0.7f, edge.startColor.a, 1f / 255f, "Raybeam Edge Alpha");
                Assert.AreEqual(1.3f, GlowOf(edge).r, 1e-4f, "Raybeam Edge Glow");

                view.Begin(Time.time - 0.2f, 0.4f, 2);
                body.transform.position = new Vector3(5f, 0f, 1f);
                LateUpdate(view);
                Assert.AreEqual(0.7f, band.GetPosition(0).magnitude, 1e-3f, "halfway through the wind-up, halfway down");
                Assert.AreEqual(body.transform.position, view.transform.position, "it follows the caster, who may move while charging");

                view.End();
                Assert.IsFalse(hex.activeSelf, "the beams fired or the wind-up was cancelled");
                Assert.IsFalse(view.Running);

                view.Begin(Time.time - 0.4f - RaybeamWindup.MissingEndSeconds - 0.5f, 0.4f, 2);
                LateUpdate(view);
                Assert.IsFalse(hex.activeSelf, "a wind-up whose end never arrived goes away by itself");

                view.Begin(Time.time, 0.4f, -1);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, edge.startColor, "no team known: the edge keeps the hexagon's own colour");
            }
        }

        [Test]
        public void TheChargeViewMakesNoMeshOrMaterial()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                SetChargeLook(kit);
                int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length, materials = Resources.FindObjectsOfTypeAll<Material>().Length;
                RaybeamChargeView view = RaybeamChargeView.TryCreate(kit.Theme, RaybeamId, null, null, kit.NewObject("caster").transform);
                kit.Make(view.gameObject);
                view.Begin(Time.time, 0.4f, 1);
                LateUpdate(view);
                Assert.AreEqual(meshes, Resources.FindObjectsOfTypeAll<Mesh>().Length);
                Assert.AreEqual(materials, Resources.FindObjectsOfTypeAll<Material>().Length, "colours through line colours and a property block");
                foreach (LineRenderer line in view.GetComponentsInChildren<LineRenderer>(true))
                {
                    Assert.AreSame(kit.Fx, line.sharedMaterial, line.name + ": the shared FX Alpha");
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, line.shadowCastingMode);
                }
                Assert.IsEmpty(view.GetComponentsInChildren<Collider>(true), "a look never adds a collider");
            }
        }

        [Test]
        public void WithoutAnArenaLookThereIsNoHexagon()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                kit.Theme.arenaLook = null;
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("RaybeamChargeView"));
                Assert.IsNull(RaybeamChargeView.TryCreate(kit.Theme, RaybeamId, null, null, kit.NewObject("caster").transform));
            }
        }

        // ---- the wiring ----

        [Test]
        public void TheRaybeamWaitsForTheWindupAndItsHexagonFollowsTheRules()
        {
            MethodInfo step = typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.Step));
            MethodInfo create = typeof(RaybeamChargeView).GetMethod(nameof(RaybeamChargeView.TryCreate));
            MethodInfo spend = typeof(UltimateCharge).GetMethod(nameof(UltimateCharge.Spend));
            AssertAll(
                (!IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.TryBuildCast), spend), "the press spends nothing, so a cancelled wind-up keeps the meter full"),
                (IlWiring.Uses(typeof(RaybeamAbility), "EndWindup", spend), "the meter is spent where the beams fire"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.ExecuteCast), typeof(RaybeamAbility).GetMethod("ShowCharge", Any)),
                    "every client shows the hexagon from a cast phase"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.ExecuteCast), typeof(RaybeamAbility).GetMethod("FireBeams", Any)),
                    "every client fires the beams from a cast phase"),
                (IlWiring.Uses(typeof(RaybeamAbility), "ShowCharge", typeof(AbilityOwner).GetField(nameof(AbilityOwner.Weapon))), "the hexagon is handed the caster's gun"),
                (IlWiring.Uses(typeof(RaybeamChargeView), "Place", typeof(WeaponFiring).GetProperty(nameof(WeaponFiring.MuzzlePosition)).GetMethod),
                    "the hexagon sits at the gun's muzzle"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.OwnerTick), step), "the owner's frame asks the wind-up rule"),
                (IlWiring.Uses(typeof(RaybeamAbility), nameof(RaybeamAbility.Interrupt), typeof(RaybeamAbility).GetMethod("EndWindup", Any)),
                    "a stun or death ends the wind-up the moment it lands"),
                (IlWiring.ResultDecidesABranch(typeof(RaybeamAbility), "EndWindup", typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.SpendsUltimate))),
                    "the meter is spent only when the rule says the beams fire"),
                (IlWiring.Uses(typeof(RaybeamAbility), "EndWindup", typeof(AbilityModule).GetMethod("CurrentContext", Any)),
                    "the beams fire from the gun and aim of the moment they fire, not of the press"),
                (IlWiring.Uses(typeof(RaybeamAbility), "get_" + nameof(RaybeamAbility.IsReady), typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.IsReady))),
                    "no second press while charging"),
                (IlWiring.Uses(typeof(RaybeamAbility), "ShowCharge", create), "every client draws the hexagon from the wind-up phase"),
                (IlWiring.ResultDecidesABranch(typeof(RaybeamChargeView), "LateUpdate", typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.ChargeShown))),
                    "the hexagon leaves on its own only when the rule says"),
                (IlWiring.Uses(typeof(RaybeamChargeView), "Place", typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.ChargeRadius))), "its size is the rule's"),
                (IlWiring.Uses(typeof(RaybeamChargeView), "Place", typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.HexEdgeRadius))), "its edge sits where the rule puts it"),
                (IlWiring.Uses(typeof(RaybeamChargeView), nameof(RaybeamChargeView.Begin), typeof(LookColours).GetMethod(nameof(LookColours.Accent))),
                    "the edge is the caster's team accent"));
        }

        [Test]
        public void TheOwnersFrameHandsCanActToTheRuleAndActsOnItsAnswer()
        {
            // OwnerTick(deltaTime, held, canAct): canAct (ldarg.3) carries the runner's stun, overheat and death check into the rule; the step the
            // rule returns decides whether the wind-up ends and is what EndWindup is handed.
            MethodInfo tick = typeof(RaybeamAbility).GetMethod(nameof(RaybeamAbility.OwnerTick));
            MethodInfo step = typeof(RaybeamWindup).GetMethod(nameof(RaybeamWindup.Step));
            MethodInfo end = typeof(RaybeamAbility).GetMethod("EndWindup", Any);
            byte[] il = tick.GetMethodBody().GetILAsByteArray();
            var stepAt = IlWiring.CallOffsets(tick, step);
            var endAt = IlWiring.CallOffsets(tick, end);
            Assert.AreEqual(1, stepAt.Count, "one question a frame");
            Assert.AreEqual(1, endAt.Count);
            int local = StoredLocal(il, stepAt[0] + 5);
            AssertAll(
                (il[stepAt[0] - 1] == 0x05, "canAct is the rule's last argument, not a constant"),
                (IlWiring.ResultDecidesABranch(typeof(RaybeamAbility), nameof(RaybeamAbility.OwnerTick), step), "the rule's step decides whether the wind-up ends"),
                (local >= 0 && LoadsLocal(il, endAt[0], local), "and the step itself is what ends it (fire or cancel)"));
        }

        // The local a value is stored in right at il[at] (stloc.0-3 or stloc.s), or -1.
        private static int StoredLocal(byte[] il, int at)
        {
            if (il[at] >= 0x0A && il[at] <= 0x0D) return il[at] - 0x0A;
            return il[at] == 0x13 ? il[at + 1] : -1;
        }

        // Whether the instruction right before il[at] loads that local (ldloc.0-3 or ldloc.s).
        private static bool LoadsLocal(byte[] il, int at, int local) =>
            local <= 3 ? il[at - 1] == 0x06 + local : il[at - 2] == 0x11 && il[at - 1] == local;

        [Test]
        public void OnlyTheUltimateMeterGatesThePress()
        {
            var go = new GameObject("raybeam charge pool test");
            try
            {
                RaybeamAbility ability = go.AddComponent<RaybeamAbility>();
                bool spendsPool = (bool)typeof(AbilityModule).GetProperty("SpendsChargeWhenCast", Any).GetValue(ability);
                Assert.IsFalse(spendsPool, "the runner must never refuse a press the wind-up has already started on");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheHexagonIsHiddenInTheFogWithItsCasterAndBuiltBeforeTheGate()
        {
            MethodInfo create = typeof(RaybeamChargeView).GetMethod(nameof(RaybeamChargeView.TryCreate));
            var lineAt = IlWiring.CallOffsets(create, typeof(RaybeamChargeView).GetMethod("NewLine", Any));
            var attachAt = IlWiring.CallOffsets(create, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            Assert.AreEqual(2, lineAt.Count, "the red line and the team edge");
            Assert.AreEqual(1, attachAt.Count, "follows the caster's fog gate");
            Assert.Less(lineAt.Max(), attachAt[0], "the gate collects the renderers once: both lines must exist before it attaches");
        }

        private static void SetChargeLook(MovementLookTestKit kit)
        {
            kit.Theme.teamShotColors = (Color[])TeamColours.Clone();
            kit.Look.raybeamChargeStartRadius = 1.2f;
            kit.Look.raybeamChargeEndRadius = 0.2f;
            kit.Look.raybeamChargeWidth = 0.15f;
            kit.Look.raybeamChargeAlpha = 0.8f;
            kit.Look.raybeamChargeGlow = 1.6f;
            kit.Look.raybeamEdgeWidth = 0.05f;
            kit.Look.raybeamEdgeAlpha = 0.7f;
            kit.Look.raybeamEdgeGap = 0.04f;
            kit.Look.raybeamEdgeGlow = 1.3f;
        }

        private static void LateUpdate(RaybeamChargeView view) => typeof(RaybeamChargeView).GetMethod("LateUpdate", Any).Invoke(view, null);

        private static Color GlowOf(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(BaseColorId);
        }

        // Every failing check named in one run.
        private static void AssertAll(params (bool ok, string what)[] checks)
        {
            string[] failed = checks.Where(c => !c.ok).Select(c => c.what).ToArray();
            Assert.IsEmpty(failed, "failed: " + string.Join(" | ", failed));
        }
    }
}
