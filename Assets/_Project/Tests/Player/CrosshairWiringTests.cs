using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
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
        private const string PlayerPrefab = "Assets/_Project/Resources/Multiplayer Player.prefab";
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
