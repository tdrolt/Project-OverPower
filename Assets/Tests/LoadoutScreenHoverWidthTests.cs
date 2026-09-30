using System.Reflection;
using NUnit.Framework;
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

        [SetUp]
        public void BuildScreen()
        {
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

        private void OpenPopUp(string key, string text)
        {
            InvokePrivate("HideTooltip");
            var source = (System.Func<string>)(() => text);
            InvokePrivate("TooltipPointerAt", key, source, new Vector2(400f, 300f));
            InvokePrivate("TickTooltip", 10f);
        }

        [Test]
        public void ThePanelWidthNeverChangesWhileAPopUpIsOpenOnAnyWeaponOrAbility()
        {
            float baseline = MeasuredPanelWidth();
            Assert.Greater(baseline, 0f, "sanity: the panel should have a real width before any hover starts");

            foreach (WeaponDefinition weapon in weapons.Weapons)
            {
                if (weapon == null)
                    continue;
                OpenPopUp("w:" + weapon.Id, weapon.DisplayName + "\n" + weapon.Description + "\n" + ShopItemNumbers.Weapon(weapon));
                Assert.IsTrue(screen.TooltipVisible, $"pop-up for weapon '{weapon.name}'");
                Assert.AreEqual(baseline, MeasuredPanelWidth(), WidthTolerance, $"panel width changed while the pop-up for weapon '{weapon.name}' is open");
            }

            foreach (AbilityDefinition ability in abilities.Abilities)
            {
                if (ability == null)
                    continue;
                OpenPopUp("a:" + ability.Id, ability.DisplayName + "\n" + ability.Description + "\n" + ShopItemNumbers.Ability(ability));
                Assert.IsTrue(screen.TooltipVisible, $"pop-up for ability '{ability.name}'");
                Assert.AreEqual(baseline, MeasuredPanelWidth(), WidthTolerance, $"panel width changed while the pop-up for ability '{ability.name}' is open");
            }
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
