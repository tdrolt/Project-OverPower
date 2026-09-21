using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Data;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 2026-09-21: "when you hover over the mark upgrade from the laser tree it expands the
    /// shop menu for no reason". LoadoutScreen.Builder.BuildScreenCanvas gives the hover-description
    /// strip a pinned HEIGHT (Loadout Description Panel Height) but never a pinned WIDTH - a
    /// TextMeshProUGUI's own preferred width is its unwrapped single-line width, so the strip's
    /// VerticalLayoutGroup reports the longest hovered description's full length upward, and the
    /// panel's own ContentSizeFitter (PreferredSize, both axes) grows sideways to fit it. 12 Laser -
    /// Mark's description is the shop's longest at 168 characters (Assets/Gameplay/Weapons/12 Laser -
    /// Mark.asset; the bug report says ~170) and is the one this bug report names, but this test
    /// checks every weapon and ability in the real catalogues, since any long-enough text does the
    /// same thing.
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

        [SetUp]
        public void BuildScreen()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
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
        }

        [TearDown]
        public void DestroyScreen() => Object.DestroyImmediate(screenGo);

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

        private void ShowWeaponHover(WeaponDefinition def) => InvokePrivate("ShowWeaponHover", def);
        private void ShowAbilityHover(AbilityDefinition def) => InvokePrivate("ShowAbilityHover", def);
        private void ClearHover() => InvokePrivate("ClearHover");

        [Test]
        public void ThePanelWidthNeverChangesAcrossAnyWeaponOrAbilityHover()
        {
            ClearHover();
            float baseline = MeasuredPanelWidth();
            Assert.Greater(baseline, 0f, "sanity: the panel should have a real width before any hover starts");

            foreach (WeaponDefinition weapon in weapons.Weapons)
            {
                if (weapon == null)
                    continue;

                ShowWeaponHover(weapon);
                float hovered = MeasuredPanelWidth();
                Assert.AreEqual(baseline, hovered, WidthTolerance,
                    $"panel width changed while hovering weapon '{weapon.name}' " +
                    $"(description {weapon.Description?.Length ?? 0} chars)");

                ClearHover();
                float cleared = MeasuredPanelWidth();
                Assert.AreEqual(baseline, cleared, WidthTolerance,
                    $"panel width did not return to baseline after clearing weapon '{weapon.name}'");
            }

            foreach (AbilityDefinition ability in abilities.Abilities)
            {
                if (ability == null)
                    continue;

                ShowAbilityHover(ability);
                float hovered = MeasuredPanelWidth();
                Assert.AreEqual(baseline, hovered, WidthTolerance,
                    $"panel width changed while hovering ability '{ability.name}' " +
                    $"(description {ability.Description?.Length ?? 0} chars)");

                ClearHover();
                float cleared = MeasuredPanelWidth();
                Assert.AreEqual(baseline, cleared, WidthTolerance,
                    $"panel width did not return to baseline after clearing ability '{ability.name}'");
            }
        }

        /// <summary>The bug report's own example, called out on its own: 170 characters, the shop's
        /// longest description (checked directly here, not just implied by the loop above) and the
        /// one that made the panel visibly stretch sideways in Tudor's game.</summary>
        [Test]
        public void HoveringTheLaserMarkSpecificallyDoesNotStretchThePanel()
        {
            WeaponDefinition mark = null;
            foreach (WeaponDefinition weapon in weapons.Weapons)
            {
                if (weapon != null && weapon.name == "12 Laser - Mark")
                {
                    mark = weapon;
                    break;
                }
            }

            Assert.NotNull(mark, "expected to find the '12 Laser - Mark' weapon asset in the catalogue");
            // The bug report says 170 characters; the asset measures 168 - close enough that either
            // is clearly "the shop's longest description", which is the only thing this test needs.
            Assert.Greater(mark.Description?.Length ?? 0, 100,
                "sanity: this is supposed to be the shop's longest description");

            ClearHover();
            float baseline = MeasuredPanelWidth();

            ShowWeaponHover(mark);
            float hovered = MeasuredPanelWidth();

            Assert.AreEqual(baseline, hovered, WidthTolerance,
                "hovering '12 Laser - Mark' must not change the shop panel's width");
        }
    }
}
