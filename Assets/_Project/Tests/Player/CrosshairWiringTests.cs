using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>The crosshair and the aim cone ask the tested rules (CrosshairRulesTests), the router's fire gate is the one the crosshair reads,
    /// and the player prefab carries the crosshair. Each IL check fails with its wiring reverted.</summary>
    public class CrosshairWiringTests
    {
        private const string PlayerPrefab = "Assets/_Project/Design/Resources/Multiplayer Player.prefab";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo Rule(string name) => typeof(CrosshairRules).GetMethod(name, Any);
        private static MethodInfo Getter(System.Type type, string property) => type.GetProperty(property, Any).GetGetMethod(true);

        private static GameObject Player()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, $"Expected the player prefab at {PlayerPrefab} - if it moved, update this path.");
            return prefab;
        }

        [Test]
        public void TheRouterGatesEveryMouseButtonFireOnTheOneRule()
        {
            MethodInfo rule = typeof(PlayerInputRouter).GetMethod(nameof(PlayerInputRouter.FireBlocked), Any);
            MethodInfo gate = Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.PointerFireBlocked));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerInputRouter), "get_PointerFireBlocked", rule));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerInputRouter), "get_PrimaryHeld", gate), "Primary held must read the shared gate");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerInputRouter), "get_AttachmentHeld", gate), "Attachment held must read the shared gate");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerInputRouter), "EmitPointerGated", gate), "the click events must read the shared gate");
        }

        [Test]
        public void TheCrosshairShowsByTheRoutersFireGate()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate",
                Getter(typeof(PlayerInputRouter), nameof(PlayerInputRouter.PointerFireBlocked))), "the arrow must come back exactly where a click would not fire");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.ShowCrosshair))));
        }

        [Test]
        public void TheCrosshairOpensWithTheGunsRealSpreadAndFillsWithTheCharge()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Getter(typeof(PlayerAim), nameof(PlayerAim.EffectiveConeAngle))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.OuterHalfAngle))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.ReticleRadius))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.Approach))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.IsCharging))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.ChargeFill))));
        }

        [Test]
        public void TheSpreadHexagonSpinsWithTheShotRateAndColoursWithTheHeat()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Getter(typeof(WeaponFiring), nameof(WeaponFiring.LastShotTime))),
                "the spin must count the gun's real shots");
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.ShotRate))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.SpinDegreesPerSecond))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.SpinAngle))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Getter(typeof(PlayerOverheat), nameof(PlayerOverheat.Normalised))),
                "the colour must read the same heat meter that locks the gun");
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Getter(typeof(PlayerOverheat), nameof(PlayerOverheat.IsSilenced))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.HeatShown))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.HeatColor))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", Rule(nameof(CrosshairRules.RingColour))));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", typeof(PlayerOverheat).GetProperty(nameof(PlayerOverheat.IsVentWindowOpen)).GetGetMethod()));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", typeof(CrosshairView).GetMethod(nameof(CrosshairView.DrawSpreadRing), Any)));
        }

        [Test]
        public void TheSpreadHexagonTakesTheRadiusTheSpinAndTheColourItIsGiven()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var host = new GameObject("Crosshair Test Host");
            try
            {
                CrosshairView.Parts parts = CrosshairView.BuildCanvas(theme, host.transform);
                CrosshairView.DrawSpreadRing(parts, apothem: 40f, angleDegrees: 25f, colour: Color.red, lineWidth: 3f, outline: 1.5f);
                Assert.AreEqual(40f, parts.spreadLine.Apothem, 1e-4f, "the inside of the sides sits at the spread radius");
                Assert.AreEqual(3f, parts.spreadLine.LineWidth, 1e-4f);
                Assert.AreEqual(Color.red, parts.spreadLine.color);
                Assert.AreEqual(25f, parts.spreadHex.localEulerAngles.z, 1e-3f);
                Assert.AreEqual(38.5f, parts.spreadEdge.Apothem, 1e-4f, "the dark edge reaches outline further in");
                Assert.AreEqual(6f, parts.spreadEdge.LineWidth, 1e-4f, "and outline further out");
                Assert.IsTrue(parts.spreadEdge.transform.IsChildOf(parts.spreadHex), "the edge spins with the line");
                Assert.IsNull(host.transform.Find("Crosshair Canvas/Reticle/Tick 1"), "the four ticks are gone");
                // Without a CanvasRenderer a Graphic builds its mesh and nothing ever draws it.
                Assert.IsNotNull(parts.spreadLine.GetComponent<CanvasRenderer>(), "the spread hexagon has nothing to draw it");
                Assert.IsNotNull(parts.spreadEdge.GetComponent<CanvasRenderer>(), "the spread hexagon's edge has nothing to draw it");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void DrawingTheSpreadHexagonEveryFrameMakesNoGarbage()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var host = new GameObject("Crosshair Test Host");
            try
            {
                CrosshairView.Parts parts = CrosshairView.BuildCanvas(theme, host.transform);
                CrosshairView.DrawSpreadRing(parts, 20f, 0f, Color.white, 2f, 1f);
                int allocations = AllocationProbe.Count(() =>
                {
                    float rate = 0f, angle = 0f;
                    for (int frame = 0; frame < 30; frame++)
                    {
                        rate = CrosshairRules.ShotRate(rate, frame % 3 == 0, 0.016f, 0.5f);
                        angle = CrosshairRules.SpinAngle(angle, CrosshairRules.SpinDegreesPerSecond(rate, 30f, 40f, 600f), 0.016f);
                        Color c = CrosshairRules.HeatColor(CrosshairRules.HeatShown(frame / 30f, false), Color.white, Color.yellow, Color.red, Color.red, 0.8f);
                        CrosshairView.DrawSpreadRing(parts, 20f + frame, angle, c, 2f, 1f);
                    }
                });
                Assert.AreEqual(0, allocations);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheCrosshairHidesTheArrowOnlyWhileShownAndGivesItBack()
        {
            MethodInfo setVisible = typeof(Cursor).GetProperty(nameof(Cursor.visible)).GetSetMethod();
            Assert.IsTrue(IlWiring.CallsEndingWithBool(typeof(CrosshairView), "Reveal", setVisible, false));
            Assert.IsTrue(IlWiring.CallsEndingWithBool(typeof(CrosshairView), "Conceal", setVisible, true));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "LateUpdate", typeof(CrosshairView).GetMethod("Conceal", Any)));
            Assert.IsTrue(IlWiring.Uses(typeof(CrosshairView), "OnDisable", typeof(CrosshairView).GetMethod("Conceal", Any)),
                "leaving the match or losing the player must give the arrow back");
        }

        [Test]
        public void HidingTheCrosshairForgetsTheShotRateSoItComesBackSpinningAtRest()
        {
            // Edit mode: AddComponent runs no Awake, so the view needs no PhotonView or theme here.
            var host = new GameObject("CrosshairHost");
            try
            {
                var view = host.AddComponent<CrosshairView>();
                FieldInfo rate = typeof(CrosshairView).GetField("shotRate", Any);
                rate.SetValue(view, 12f);
                typeof(CrosshairView).GetMethod("Conceal", Any).Invoke(view, null);
                Assert.AreEqual(0f, (float)rate.GetValue(view),
                    "dying or opening the shop mid-spray must not bring the crosshair back spinning fast with nothing fired");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheAimConeAsksTheDesignerSwitchAndSharesTheSpreadRule()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(AimConeView), "LateUpdate", Rule(nameof(CrosshairRules.DrawAimCone))));
            Assert.IsTrue(IlWiring.Uses(typeof(AimConeView), "LateUpdate", typeof(UiTheme).GetField(nameof(UiTheme.showAimCone))));
            Assert.IsTrue(IlWiring.Uses(typeof(AimConeView), "LateUpdate", Rule(nameof(CrosshairRules.OuterHalfAngle))),
                "the cone and the crosshair must agree on how far a shot can stray");
        }

        [Test]
        public void TheCrosshairNeverTakesAClick()
        {
            // pointerOverUi counts any raycast target under a GraphicRaycaster: a crosshair that took clicks would sit under the
            // pointer forever and block every shot.
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var host = new GameObject("Crosshair Test Host");
            try
            {
                CrosshairView.BuildCanvas(theme, host.transform);
                Assert.IsEmpty(host.GetComponentsInChildren<GraphicRaycaster>(true), "no raycaster on the crosshair canvas");
                foreach (Graphic g in host.GetComponentsInChildren<Graphic>(true))
                    Assert.IsFalse(g.raycastTarget, g.name + " is a raycast target");
                Assert.IsNotEmpty(host.GetComponentsInChildren<Graphic>(true), "the builder drew nothing");
                Assert.AreEqual(UiLayers.Crosshair, host.GetComponentInChildren<Canvas>(true).sortingOrder);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheCrosshairDrawsOverEveryPanelItCanShowOver()
        {
            Assert.Greater(UiLayers.Crosshair, UiLayers.DominionHud);
            Assert.Greater(UiLayers.Crosshair, UiLayers.ResultPanels);
            Assert.Less(UiLayers.Crosshair, UiLayers.QuitConfirm);
        }

        [Test]
        public void ThePlayerCarriesACrosshairOnTheAimConesTheme()
        {
            var view = Player().GetComponent<CrosshairView>();
            Assert.IsNotNull(view, "The player prefab has no CrosshairView - only the arrow shows.");
            var theme = new SerializedObject(view).FindProperty("theme");
            Assert.IsNotNull(theme, "CrosshairView.theme was renamed - update this test.");
            var cone = new SerializedObject(Player().GetComponent<AimConeView>()).FindProperty("theme");
            Assert.IsNotNull(theme.objectReferenceValue, "CrosshairView.Theme is empty on the player prefab.");
            Assert.AreSame(cone.objectReferenceValue, theme.objectReferenceValue);
            Assert.IsFalse(view is IPunObservable, "owner-only look: never observed over the network");
        }
    }
}
