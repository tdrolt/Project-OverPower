using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// The shop as the game builds it (the LoadoutScreenHoverWidthTests rig: the construction methods called directly, real assets) shows the
    /// two armour upgrades as two paths, Absorb above Recharge, each with one step per level between its text and its +, and the page and the
    /// panel still fit at 1920x1080 and 1280x720.
    /// </summary>
    public class ShopArmorPathsLayoutTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject screenGo;
        private LoadoutScreen screen;
        private UiTheme theme;
        private ArmorConfig armor;
        private RectTransform panelRect;
        private bool createdEventSystem;

        private static void ResetPageMemory() =>
            ((ShopPageMemory)typeof(LoadoutScreen).GetField("pageMemory", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Remember(ShopPage.Weapons);

        [SetUp]
        public void BuildScreen()
        {
            ResetPageMemory();
            createdEventSystem = EventSystem.current == null;
            theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Shared/Config/UiTheme.asset");
            armor = AssetDatabase.LoadAssetAtPath<ArmorConfig>("Assets/_Project/Shared/Config/ArmorConfig.asset");
            Assert.NotNull(theme, "UiTheme.asset");
            Assert.NotNull(armor, "ArmorConfig.asset");

            screenGo = new GameObject("TestLoadoutScreen");
            screen = screenGo.AddComponent<LoadoutScreen>();
            Set("theme", theme);
            Set("armorConfig", armor);
            Set("weapons", AssetDatabase.LoadAssetAtPath<WeaponCatalogue>("Assets/_Project/Features/Weapons/Data/WeaponCatalogue.asset"));
            Set("abilities", AssetDatabase.LoadAssetAtPath<AbilityCatalogue>("Assets/_Project/Shared/Config/AbilityCatalogue.asset"));
            typeof(LoadoutScreen).GetMethod("BuildWeaponTree", Private).Invoke(screen, null);
            typeof(LoadoutScreen).GetMethod("BuildUi", Private).Invoke(screen, null);

            panelRect = (RectTransform)((GameObject)Get("screenRoot")).transform.Find("Panel");
            screen.ShowPage(ShopPage.AbilitiesAndArmor);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }

        [TearDown]
        public void DestroyScreen()
        {
            Object.DestroyImmediate(screenGo);
            ResetPageMemory();
            if (createdEventSystem && EventSystem.current != null)
                Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private void Set(string name, object value) => typeof(LoadoutScreen).GetField(name, Private).SetValue(screen, value);
        private object Get(string name) => typeof(LoadoutScreen).GetField(name, Private).GetValue(screen);

        private object Path(string field)
        {
            object ui = Get(field);
            Assert.NotNull(ui, field);
            return ui;
        }

        private static T Part<T>(object ui, string name) => (T)ui.GetType().GetField(name).GetValue(ui);

        /// <summary>World-space bounds (x right, y up) of a built element over all four corners: the step diamonds are turned 45 degrees,
        /// so two opposite corners alone give a rect with no width.</summary>
        private static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float xMin = Mathf.Min(c[0].x, c[1].x, c[2].x, c[3].x), xMax = Mathf.Max(c[0].x, c[1].x, c[2].x, c[3].x);
            float yMin = Mathf.Min(c[0].y, c[1].y, c[2].y, c[3].y), yMax = Mathf.Max(c[0].y, c[1].y, c[2].y, c[3].y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        [Test]
        public void AStepDiamondIsMeasuredAsWideAsItIsTall()
        {
            Image step = Part<Image[]>(Path("absorbPath"), "stepRings")[0];
            Rect r = WorldRect(step.rectTransform);
            Assert.Greater(r.width, 1f, "a turned diamond still has a width");
            Assert.AreEqual(r.height, r.width, 0.5f);
        }

        private static bool Inside(Rect inner, Rect outer, float slack = 0.5f) =>
            inner.xMin >= outer.xMin - slack && inner.xMax <= outer.xMax + slack && inner.yMin >= outer.yMin - slack && inner.yMax <= outer.yMax + slack;

        [Test]
        public void TheAbsorbPathStandsAboveTheRechargePathWithoutOverlap()
        {
            Rect absorb = WorldRect(Part<RectTransform>(Path("absorbPath"), "root"));
            Rect recharge = WorldRect(Part<RectTransform>(Path("rechargePath"), "root"));
            Assert.Greater(absorb.height, 1f, "absorb path has a size");
            Assert.Greater(recharge.height, 1f, "recharge path has a size");
            Assert.IsFalse(absorb.Overlaps(recharge), $"the paths overlap: {absorb} / {recharge}");
            Assert.Greater(absorb.yMin, recharge.yMax, "Absorb is above Recharge, with a gap between");
            Assert.AreEqual(absorb.xMin, recharge.xMin, 0.5f, "both paths start at the same left edge");
            Assert.AreEqual(absorb.width, recharge.width, 0.5f, "both paths are as wide");
        }

        [Test]
        public void EachPathHasOneStepPerLevelBetweenItsTextAndItsPlus()
        {
            AssertSteps("absorbPath", armor.AbsorbLevelCount);
            AssertSteps("rechargePath", armor.RechargeLevelCount);
        }

        private void AssertSteps(string field, int levels)
        {
            object ui = Path(field);
            Rect row = WorldRect(Part<RectTransform>(ui, "root"));
            Rect text = WorldRect(Part<TMPro.TextMeshProUGUI>(ui, "label").rectTransform);
            Rect plus = WorldRect((RectTransform)Part<Button>(ui, "plus").transform);
            Image[] steps = Part<Image[]>(ui, "stepRings");
            TMPro.TextMeshProUGUI[] values = Part<TMPro.TextMeshProUGUI[]>(ui, "stepValues");
            Assert.AreEqual(levels, steps.Length, field + ": one step per level, the start included");
            Assert.AreEqual(levels, values.Length, field + ": one value per step");
            float lastX = float.MinValue;
            for (int i = 0; i < steps.Length; i++)
            {
                Rect step = WorldRect(steps[i].rectTransform);
                Assert.IsTrue(Inside(step, row), $"{field} step {i} inside its path: {step} / {row}");
                Assert.GreaterOrEqual(step.xMin, text.xMax - 0.5f, $"{field} step {i} right of the text");
                Assert.LessOrEqual(step.xMax, plus.xMin + 0.5f, $"{field} step {i} left of the +");
                Assert.Greater(step.center.x, lastX, $"{field} steps run left to right");
                lastX = step.center.x;

                Rect value = WorldRect(values[i].rectTransform);
                Assert.IsTrue(Inside(value, row), $"{field} step {i}'s value inside its path: {value} / {row}");
                Assert.LessOrEqual(value.xMax, plus.xMin + 0.5f, $"{field} step {i}'s value left of the +");
            }
            Assert.IsTrue(Inside(plus, row), field + ": the + inside its path");
        }

        [Test]
        public void ThePathsAndResetArmorSitInsideTheAbilitiesPage()
        {
            var page = (GameObject)Get("abilitiesPageRoot");
            Rect pageRect = WorldRect((RectTransform)page.transform);
            Assert.IsTrue(Inside(WorldRect(Part<RectTransform>(Path("absorbPath"), "root")), pageRect), "absorb inside the page");
            Assert.IsTrue(Inside(WorldRect(Part<RectTransform>(Path("rechargePath"), "root")), pageRect), "recharge inside the page");
            Rect reset = WorldRect(((Image)Get("resetArmorImage")).rectTransform);
            Assert.IsTrue(Inside(reset, pageRect), "Reset Armor inside the page");
            Assert.IsFalse(reset.Overlaps(WorldRect(Part<RectTransform>(Path("absorbPath"), "root"))), "Reset Armor clear of Absorb");
            Assert.IsFalse(reset.Overlaps(WorldRect(Part<RectTransform>(Path("rechargePath"), "root"))), "Reset Armor clear of Recharge");
            Assert.LessOrEqual(page.GetComponent<VerticalLayoutGroup>().preferredHeight, theme.loadoutPageHeight, "the page's content fits Loadout Page Height");
        }

        [TestCase(1920f, 1080f)]
        [TestCase(1280f, 720f)]
        public void TheShopPanelFitsTheScreen(float w, float h)
        {
            float scale = HudScreenLayout.CanvasScaleFactor(theme.referenceResolution, theme.matchWidthOrHeight, w, h);
            Vector2 canvas = new Vector2(w, h) / scale;
            float panelWidth = theme.loadoutPageWidth + 2f * Mathf.RoundToInt(theme.loadoutPanelPadding);
            float panelScale = ShopPanelScale.For(canvas.x, panelWidth);
            Assert.LessOrEqual(panelRect.rect.width * panelScale, canvas.x + 0.5f, "panel width on screen");
            Assert.LessOrEqual(theme.loadoutPanelTopMargin + panelRect.rect.height * panelScale, canvas.y, "the panel ends above the bottom of the screen");
            Rect panel = WorldRect(panelRect);
            Assert.IsTrue(Inside(WorldRect(Part<RectTransform>(Path("rechargePath"), "root")), panel), "the lower path inside the panel");
        }

        [Test]
        public void TheShopBuildsAndPaintsThePathsThroughTheRules()
        {
            System.Type steps = typeof(ArmorPathSteps), layout = typeof(ArmorPathLayout);
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "RefreshArmor", steps.GetMethod("For")), "steps from the rule");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "StyleArmorPath", steps.GetMethod("Border")), "ring colour");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "StyleArmorPath", steps.GetMethod("Fill")), "fill colour");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "StyleArmorPath", steps.GetMethod("LineInto")), "line colour");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "BuildArmorSection", layout.GetMethod("SectionHeight")), "section height");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "BuildArmorSection", layout.GetMethod("RowTop")), "path rows");
            Assert.IsTrue(IlWiring.Uses(typeof(LoadoutScreen), "BuildArmorPath", layout.GetMethod("StepCentreX")), "step places");
        }
    }
}
