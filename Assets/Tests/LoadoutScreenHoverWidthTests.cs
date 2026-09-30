using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 2026-09-21: "when you hover over the mark upgrade from the laser tree it expands the
    /// shop menu for no reason". That was the hover-description strip; Task 13 replaced the strip with a pop-up
    /// beside the cursor and put the shop on two fixed-size pages, and this class now pins the same promise for the
    /// new design: hovering never resizes the panel (the pop-up is not part of the panel), both pages give the panel
    /// the SAME size (switching a tab never jumps the panel), and each page's content fits inside its fixed
    /// area (the pages have no mask, so overflow would silently draw over what sits below).
    ///
    /// LoadoutScreen.Awake() bails for every non-owner copy behind photonView.IsMine (see the class
    /// comment), which PUN only ever sets true through a live room's controller-cache rebuild - it
    /// has no meaning without a connected Photon room, the same reasoning
    /// ReactiveInvulnerabilityStateTests gives for DisarmReactiveInvulnerability. Rather than fabricate
    /// ownership, this test skips Awake entirely and calls the two private CONSTRUCTION methods
    /// (BuildWeaponTree, BuildUi) directly by reflection - the same "call the method the Inspector-
    /// wired path already reaches" trick PlayerHealthOverheadBarTests uses for Awake. Neither method
    /// touches photonView, PlayerHealth, GoldWallet or any of the other owner-only wiring Awake sets
    /// up - this bug lives entirely in layout, which is exactly what those two methods build.
    ///
    /// Real project assets (UiTheme.asset, WeaponCatalogue.asset, AbilityCatalogue.asset) are loaded
    /// straight off disk with AssetDatabase rather than built with ScriptableObject.CreateInstance
    /// (CatalogueTests' own style) - the whole point is pinning the width against the GAME'S OWN
    /// descriptions, not a synthetic stand-in, so this test would not have caught the real bug if it
    /// used fake ones.
    /// </summary>
    public class LoadoutScreenHoverWidthTests
    {
        // Layout float math (word-wrap point rounding, TMP metrics) is not bit-exact between rebuilds -
        // this is generous enough to catch the bug (which grows the panel by tens to hundreds of
        // canvas units) while tolerating that noise.
        private const float WidthTolerance = 0.5f;

        private GameObject screenGo;
        private LoadoutScreen screen;
        private RectTransform panelRect;
        private WeaponCatalogue weapons;
        private AbilityCatalogue abilities;
        private UiTheme theme;
        private GameObject weaponsPage;
        private GameObject abilitiesPage;

        // G2 (review follow-up, 2026-09-21): BuildUi -> EnsureEventSystem creates an EventSystem root
        // in the active scene when none exists there yet - true for an edit-mode test run with no
        // scene of its own. Recorded here so TearDown can clean up only the one THIS test created,
        // never an EventSystem some other test (or the real scene) already had.
        private bool createdEventSystem;

        /// <summary>The page memory is static (one per session), so a test that switches pages must not leak into the next.</summary>
        private static void ResetPageMemory() =>
            ((Overpower.Match.ShopPageMemory)typeof(LoadoutScreen).GetField("pageMemory", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
                .Remember(Overpower.Match.ShopPage.Weapons);

        [SetUp]
        public void BuildScreen()
        {
            ResetPageMemory();
            createdEventSystem = EventSystem.current == null;

            theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            weapons = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>("Assets/Gameplay/Weapons/WeaponCatalogue.asset");
            abilities = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>("Assets/Gameplay/Config/AbilityCatalogue.asset");
            Assert.NotNull(theme, "UiTheme.asset");
            Assert.NotNull(weapons, "WeaponCatalogue.asset");
            Assert.NotNull(abilities, "AbilityCatalogue.asset");

            screenGo = new GameObject("TestLoadoutScreen");
            screen = screenGo.AddComponent<LoadoutScreen>();

            SetField("theme", theme);
            SetField("weapons", weapons);
            SetField("abilities", abilities);

            InvokePrivate("BuildWeaponTree");
            InvokePrivate("BuildUi");

            var screenRoot = (GameObject)GetField("screenRoot");
            Assert.NotNull(screenRoot, "BuildUi should have created screenRoot");

            Transform panel = screenRoot.transform.Find("Panel");
            Assert.NotNull(panel, "BuildScreenCanvas should have created a child named 'Panel'");
            panelRect = (RectTransform)panel;

            Assert.IsNull(panel.Find("Description Panel"), "the description strip was removed in Task 13");
            weaponsPage = (GameObject)GetField("weaponsPageRoot");
            abilitiesPage = (GameObject)GetField("abilitiesPageRoot");
            Assert.NotNull(weaponsPage, "weapons page");
            Assert.NotNull(abilitiesPage, "abilities page");
        }

        [TearDown]
        public void DestroyScreen()
        {
            Object.DestroyImmediate(screenGo);
            ResetPageMemory();

            // G2: only destroy the EventSystem this test's own BuildUi call created - never one that
            // was already there (the real scene, or another test's own rig) before this test ran.
            if (createdEventSystem && EventSystem.current != null)
                Object.DestroyImmediate(EventSystem.current.gameObject);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(LoadoutScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, name);
            field.SetValue(screen, value);
        }

        private object GetField(string name)
        {
            FieldInfo field = typeof(LoadoutScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, name);
            return field.GetValue(screen);
        }

        private void InvokePrivate(string name, params object[] args)
        {
            MethodInfo method = typeof(LoadoutScreen).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, name);
            method.Invoke(screen, args);
        }

        private float MeasuredPanelWidth()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            return panelRect.rect.width;
        }

        /// <summary>Rests the (simulated) pointer on a built item through its real HoverRelay, the way the EventSystem
        /// would, and lets the delay pass.</summary>
        private void HoverItem(string itemObjectName)
        {
            InvokePrivate("HideTooltip");
            Transform item = null;
            foreach (Transform t in ((GameObject)GetField("screenRoot")).GetComponentsInChildren<Transform>(true))
                if (t.name == itemObjectName) { item = t; break; }
            Assert.NotNull(item, itemObjectName);
            Component relay = null;
            foreach (Component comp in item.GetComponents<Component>())
                if (comp != null && comp.GetType().Name == "HoverRelay") relay = comp;
            Assert.NotNull(relay, "HoverRelay on " + itemObjectName);
            var ev = new PointerEventData(EventSystem.current) { position = new Vector2(400f, 300f) };
            relay.GetType().GetMethod("OnPointerEnter").Invoke(relay, new object[] { ev });
            InvokePrivate("TickTooltip", 10f);
        }

        private string PopUpBuilt(string builder, object def) =>
            (string)typeof(LoadoutScreen).GetMethod(builder, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(screen, new[] { def });

        /// <summary>The pop-up is a child of the canvas, not of the panel (so it can never resize the panel), and what
        /// it shows is what the real pop-up builders make for that item.</summary>
        [Test]
        public void ThePopUpSitsOutsideThePanelAndShowsTheRealBuildersText()
        {
            float baseline = MeasuredPanelWidth();
            int shown = 0;

            foreach (WeaponDefinition weapon in weapons.Weapons)
            {
                if (weapon == null) continue;
                screen.ShowPage(Overpower.Match.ShopPage.Weapons);
                HoverItem($"Weapon Node {weapon.Id}");
                Assert.IsTrue(screen.TooltipVisible, $"pop-up for weapon '{weapon.name}'");
                Assert.AreEqual(PopUpBuilt("WeaponPopUpText", weapon), screen.TooltipShownText, weapon.name);
                AssertPopUpOutsidePanel(baseline, weapon.name);
                shown++;
            }

            foreach (AbilityDefinition ability in abilities.Abilities)
            {
                if (ability == null || ability.Id >= 900) continue;
                screen.ShowPage(Overpower.Match.ShopPage.AbilitiesAndArmor);
                HoverItem($"Ability Card {ability.Id}");
                Assert.IsTrue(screen.TooltipVisible, $"pop-up for ability '{ability.name}'");
                Assert.AreEqual(PopUpBuilt("AbilityPopUpText", ability), screen.TooltipShownText, ability.name);
                AssertPopUpOutsidePanel(baseline, ability.name);
                shown++;
            }
            Assert.Greater(shown, 20);
        }

        private void AssertPopUpOutsidePanel(float baselineWidth, string what)
        {
            Transform popUp = ((GameObject)GetField("screenRoot")).transform.Find("Shop Tooltip");
            Assert.NotNull(popUp, "Shop Tooltip object");
            Assert.IsFalse(popUp.IsChildOf(panelRect), $"{what}: the pop-up must not be inside the panel");
            Assert.AreEqual(baselineWidth, MeasuredPanelWidth(), WidthTolerance, $"{what}: panel width changed with the pop-up open");
            Assert.LessOrEqual(screen.TooltipBox.width, theme.loadoutTooltipMaxWidth + 0.5f, $"{what}: pop-up wider than Loadout Tooltip Max Width");
        }

        [Test]
        public void MovingOffAnItemHidesThePopUpAtOnce()
        {
            HoverItem("Weapon Node " + weapons.Weapons[0].Id);
            Assert.IsTrue(screen.TooltipVisible);
            InvokePrivate("TooltipPointerLeft", "w:" + weapons.Weapons[0].Id);
            Assert.IsFalse(screen.TooltipVisible, "hidden in the same call, not on the next frame");
        }

        /// <summary>Open() lands on the page last used. Needs a PhotonView on the rig for the shop-gate read Open does.</summary>
        [Test]
        public void OpenLandsOnTheRememberedPage()
        {
            screenGo.AddComponent<PhotonView>();
            screen.ShowPage(Overpower.Match.ShopPage.AbilitiesAndArmor);
            screen.Open();
            try { Assert.AreEqual(Overpower.Match.ShopPage.AbilitiesAndArmor, screen.CurrentPage); }
            finally { screen.Close(); }

            screen.ShowPage(Overpower.Match.ShopPage.Weapons);
            screen.Open();
            try { Assert.AreEqual(Overpower.Match.ShopPage.Weapons, screen.CurrentPage); }
            finally { screen.Close(); }
        }

        /// <summary>A canvas narrower than the panel (4:3, 5:4) scales the panel down by the rule; a wide one leaves it at 1.</summary>
        [Test]
        public void ThePanelIsScaledByTheRuleForTheCanvasItSitsOn()
        {
            InvokePrivate("FitPanelToCanvas");
            float canvasWidth = ((RectTransform)((GameObject)GetField("screenRoot")).transform).rect.width;
            float panelWidth = theme.loadoutPageWidth + 2f * Mathf.RoundToInt(theme.loadoutPanelPadding);
            Assert.AreEqual(Overpower.Match.ShopPanelScale.For(canvasWidth, panelWidth), panelRect.localScale.x, 0.0001f);
            Assert.AreEqual(panelRect.localScale.x, panelRect.localScale.y, 0.0001f, "scaled evenly");
        }

        [Test]
        public void ThePanelHasTheSameSizeOnBothPages()
        {
            screen.ShowPage(Overpower.Match.ShopPage.Weapons);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            Vector2 onWeapons = panelRect.rect.size;

            screen.ShowPage(Overpower.Match.ShopPage.AbilitiesAndArmor);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            Vector2 onAbilities = panelRect.rect.size;

            Assert.AreEqual(onWeapons.x, onAbilities.x, WidthTolerance, "panel width differs between the pages");
            Assert.AreEqual(onWeapons.y, onAbilities.y, WidthTolerance, "panel height differs between the pages");
        }

        [Test]
        public void EachPagesContentFitsInsideTheFixedPageArea()
        {
            screen.ShowPage(Overpower.Match.ShopPage.Weapons);
            AssertPageFits("weapons page", weaponsPage);
            screen.ShowPage(Overpower.Match.ShopPage.AbilitiesAndArmor);
            AssertPageFits("abilities & armor page", abilitiesPage);
        }

        /// <summary>Tudor, Task 13: on the Weapons page Baseline is centred on top, the four families sit in one row
        /// under it, and under each family its two upgrades sit side by side in one row (not stacked).</summary>
        [Test]
        public void TheWeaponTreeHasTheFamiliesInOneRowAndEachFamilysUpgradesSideBySide()
        {
            screen.ShowPage(Overpower.Match.ShopPage.Weapons);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);

            var nodes = (System.Collections.IDictionary)GetField("weaponNodes");
            System.Func<int, Vector2> centre = id =>
            {
                object ui = nodes[id];
                var outer = (Image)ui.GetType().GetField("outer").GetValue(ui);
                var corners = new Vector3[4];
                outer.rectTransform.GetWorldCorners(corners);
                return (corners[0] + corners[2]) * 0.5f;
            };

            WeaponDefinition root = null;
            foreach (WeaponDefinition w in weapons.Weapons)
                if (w != null && w.Parent == null) root = w;
            Assert.NotNull(root);

            var families = new System.Collections.Generic.List<WeaponDefinition>();
            foreach (WeaponDefinition w in weapons.Weapons)
                if (w != null && w.Parent == root) families.Add(w);
            Assert.AreEqual(4, families.Count, "four weapon families under the baseline");

            Vector2 rootAt = centre(root.Id);
            float familyRowY = centre(families[0].Id).y;
            foreach (WeaponDefinition family in families)
            {
                Vector2 familyAt = centre(family.Id);
                Assert.AreEqual(familyRowY, familyAt.y, 0.5f, $"{family.DisplayName} is in the families' row");
                Assert.Less(familyAt.y, rootAt.y, $"{family.DisplayName} is below the baseline");

                var upgrades = new System.Collections.Generic.List<Vector2>();
                foreach (WeaponDefinition w in weapons.Weapons)
                    if (w != null && w.Parent == family) upgrades.Add(centre(w.Id));
                Assert.AreEqual(2, upgrades.Count, $"{family.DisplayName} has two upgrades");
                Assert.AreEqual(upgrades[0].y, upgrades[1].y, 0.5f, $"{family.DisplayName}: the two upgrades share one row");
                Assert.Less(upgrades[0].y, familyAt.y, $"{family.DisplayName}: the upgrades are below it");
                Assert.Greater(Mathf.Abs(upgrades[0].x - upgrades[1].x), 1f, $"{family.DisplayName}: the upgrades are side by side");
                Assert.AreEqual(familyAt.x, (upgrades[0].x + upgrades[1].x) * 0.5f, 0.5f, $"{family.DisplayName} sits centred above its two upgrades");
            }

            float rootX = rootAt.x, sum = 0f;
            foreach (WeaponDefinition family in families) sum += centre(family.Id).x;
            Assert.AreEqual(rootX, sum / families.Count, 0.5f, "the baseline is centred above the four families");
        }

        /// <summary>The capture found each ability column's heading starting left of its cards (the column grew wider than
        /// its two cards): the heading and the first card must share one left edge.</summary>
        [Test]
        public void EachAbilityColumnsHeadingAndCardsShareOneLeftEdge()
        {
            screen.ShowPage(Overpower.Match.ShopPage.AbilitiesAndArmor);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)abilitiesPage.transform);

            int columns = 0;
            foreach (Transform t in abilitiesPage.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.EndsWith(" Ability Grid")) continue;
                columns++;
                var corners = new Vector3[4];
                ((RectTransform)t.GetChild(0)).GetWorldCorners(corners);
                float cardLeft = corners[0].x;
                ((RectTransform)t.parent.GetChild(0)).GetWorldCorners(corners);
                float headingLeft = corners[0].x;
                Assert.AreEqual(headingLeft, cardLeft, 0.5f, $"{t.name}: heading and first card start at the same x");
            }
            Assert.AreEqual(3, columns, "Mobility, Attachment and Ultimate columns");
        }

        private void AssertPageFits(string label, GameObject page)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            var rt = (RectTransform)page.transform;
            var group = page.GetComponent<VerticalLayoutGroup>();
            Assert.LessOrEqual(group.preferredHeight, theme.loadoutPageHeight,
                $"{label}: its content needs {group.preferredHeight:F1} units of height, more than Loadout Page Height ({theme.loadoutPageHeight:F1}) - it would draw past the page's own box");
            Assert.LessOrEqual(group.preferredWidth, theme.loadoutPageWidth,
                $"{label}: its content needs {group.preferredWidth:F1} units of width, more than Loadout Page Width ({theme.loadoutPageWidth:F1})");
            Assert.AreEqual(theme.loadoutPageWidth, rt.rect.width, WidthTolerance, $"{label}: page width");
        }
    }
}
