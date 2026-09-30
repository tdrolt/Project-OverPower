using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Task 13: the shop on two pages (Weapons, Abilities &amp; Armor) and the hover pop-up that replaced the
    /// description strip: the page memory, where the pop-up sits, what it says, and the numbers each kind of item shows.</summary>
    public class ShopPagesAndPopupTests
    {
        // ---- last page ----

        [Test]
        public void TheShopOpensOnTheWeaponsPageTheFirstTime()
        {
            Assert.AreEqual(ShopPage.Weapons, new ShopPageMemory().Last);
        }

        [Test]
        public void TheShopReopensOnThePageYouLastUsed()
        {
            var memory = new ShopPageMemory();
            memory.Remember(ShopPage.AbilitiesAndArmor);
            Assert.AreEqual(ShopPage.AbilitiesAndArmor, memory.Last);
            memory.Remember(ShopPage.Weapons);
            Assert.AreEqual(ShopPage.Weapons, memory.Last);
        }

        [Test]
        public void AnImpossiblePageFallsBackToWeapons()
        {
            var memory = new ShopPageMemory();
            memory.Remember(ShopPage.AbilitiesAndArmor);
            memory.Remember((ShopPage)99);
            Assert.AreEqual(ShopPage.Weapons, memory.Last);
        }

        // ---- delay ----

        [Test]
        public void ThePopUpDelayIsHalfASecondInTheCodeDefaultAndInTheAsset()
        {
            var fresh = ScriptableObject.CreateInstance<UiTheme>();
            try { Assert.AreEqual(0.5f, fresh.loadoutTooltipDelaySeconds, 0.0001f); }
            finally { Object.DestroyImmediate(fresh); }

            var asset = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.NotNull(asset);
            Assert.AreEqual(0.5f, asset.loadoutTooltipDelaySeconds, 0.0001f);
        }

        [Test]
        public void ThePopUpShowsAtHalfASecondAndNotBefore()
        {
            var timer = new HoverTooltipTimer();
            timer.Enter("w:1");
            timer.Tick(0.49f);
            Assert.IsFalse(timer.IsShown(0.5f));
            timer.Tick(0.01f);
            Assert.IsTrue(timer.IsShown(0.5f));
        }

        [Test]
        public void TheTabNamesAreTheOnesTudorAskedFor()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            Assert.AreEqual("Weapons", asset.loadoutTabWeaponsText);
            Assert.AreEqual("Abilities & Armor", asset.loadoutTabAbilitiesArmorText);
        }

        // ---- Tudor: the right-click slot is called Attachment (and no longer Equipment) wherever the player reads it ----

        [Test]
        public void TheRightClickSlotIsCalledAttachmentOnTheShopColumnAndTheKeysTip()
        {
            var heading = typeof(LoadoutScreen).GetMethod("SlotHeading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(heading);
            string column = (string)heading.Invoke(null, new object[] { AbilitySlot.Attachment });
            StringAssert.Contains("Attachment", column);
            StringAssert.DoesNotContain("Equipment", column);

            var asset = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/Gameplay/Config/UiTheme.asset");
            StringAssert.Contains("Attachment", asset.nameTipsKeysText);
            StringAssert.DoesNotContain("Equipment", asset.nameTipsKeysText);
            var fresh = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                StringAssert.Contains("Attachment", fresh.nameTipsKeysText);
                StringAssert.DoesNotContain("Equipment", fresh.nameTipsKeysText);
            }
            finally { Object.DestroyImmediate(fresh); }
        }

        // ---- placement ----

        private static readonly Vector2 Half = new Vector2(960f, 540f);
        private static readonly Vector2 Offset = new Vector2(16f, -18f);
        private const float Gap = 8f;

        private static Vector2 Place(float px, float py, float w, float h) =>
            ShopPopupPlacement.TopLeft(new Vector2(px, py), new Vector2(w, h), Half, Offset, Gap);

        [Test]
        public void ThePopUpSitsBelowAndRightOfTheCursorWhenThereIsRoom()
        {
            Vector2 p = Place(0f, 0f, 300f, 200f);
            Assert.AreEqual(16f, p.x, 0.001f);
            Assert.AreEqual(-18f, p.y, 0.001f);
        }

        [Test]
        public void ThePopUpFlipsLeftOfTheCursorNearTheRightEdge()
        {
            Vector2 p = Place(900f, 0f, 300f, 200f);
            Assert.AreEqual(900f - Gap - 300f, p.x, 0.001f);
        }

        [Test]
        public void ThePopUpFlipsAboveTheCursorNearTheBottomEdge()
        {
            Vector2 p = Place(0f, -500f, 300f, 200f);
            Assert.AreEqual(-500f + Gap + 200f, p.y, 0.001f);
        }

        [Test]
        public void ThePopUpStaysOnScreenInTheBottomRightCorner()
        {
            AssertFullyOnScreen(Place(950f, -530f, 300f, 200f), 300f, 200f);
        }

        [Test]
        public void ThePopUpStaysOnScreenAtEveryCornerAndEdge()
        {
            foreach (float px in new[] { -960f, -900f, 0f, 900f, 960f })
                foreach (float py in new[] { -540f, -500f, 0f, 500f, 540f })
                    AssertFullyOnScreen(Place(px, py, 400f, 260f), 400f, 260f);
        }

        [Test]
        public void APopUpBiggerThanTheScreenIsPinnedToTheTopLeft()
        {
            Vector2 p = Place(0f, 0f, 2000f, 1500f);
            Assert.AreEqual(-960f, p.x, 0.001f);
            Assert.AreEqual(540f, p.y, 0.001f);
        }

        private static void AssertFullyOnScreen(Vector2 topLeft, float w, float h)
        {
            Assert.GreaterOrEqual(topLeft.x, -Half.x - 0.001f, "left edge");
            Assert.LessOrEqual(topLeft.x + w, Half.x + 0.001f, "right edge");
            Assert.LessOrEqual(topLeft.y, Half.y + 0.001f, "top edge");
            Assert.GreaterOrEqual(topLeft.y - h, -Half.y - 0.001f, "bottom edge");
        }

        // ---- panel scale on narrow canvases ----

        [Test]
        public void APanelThatFitsTheCanvasIsNotScaled()
        {
            Assert.AreEqual(1f, ShopPanelScale.For(1920f, 1740f), 0.0001f);
            Assert.AreEqual(1f, ShopPanelScale.For(1740f, 1740f), 0.0001f);
        }

        [Test]
        public void APanelWiderThanTheCanvasIsScaledDownToFitEvenly()
        {
            Assert.AreEqual(1440f / 1740f, ShopPanelScale.For(1440f, 1740f), 0.0001f);
            Assert.AreEqual(0.5f, ShopPanelScale.For(870f, 1740f), 0.0001f);
        }

        [Test]
        public void ADegenerateCanvasOrPanelWidthLeavesTheScaleAtOne()
        {
            Assert.AreEqual(1f, ShopPanelScale.For(0f, 1740f), 0.0001f);
            Assert.AreEqual(1f, ShopPanelScale.For(-5f, 1740f), 0.0001f);
            Assert.AreEqual(1f, ShopPanelScale.For(1920f, 0f), 0.0001f);
        }

        // ---- text ----

        [Test]
        public void ThePopUpTextHasNameThenDescriptionThenNumbers()
        {
            string text = ShopPopupText.Compose("Rocket", "Slow, heavy rockets.", "Damage 20", "b8bccc");
            Assert.AreEqual("<b>Rocket</b>\nSlow, heavy rockets.\n<color=#b8bccc>Damage 20</color>", text);
        }

        [Test]
        public void ThePopUpTextLeavesOutWhatIsMissing()
        {
            Assert.AreEqual("<b>Rocket</b>\n<color=#b8bccc>Damage 20</color>", ShopPopupText.Compose("Rocket", "", "Damage 20", "b8bccc"));
            Assert.AreEqual("<b>Rocket</b>\nSlow.", ShopPopupText.Compose("Rocket", "Slow.", null, "b8bccc"));
            Assert.AreEqual("", ShopPopupText.Compose("", null, "", "b8bccc"));
        }

        [Test]
        public void NumbersAreAtMostTwoDecimalsWithADotWhateverTheCulture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL");
                Assert.AreEqual("3.13", ShopNumberFormat.Compact(3.13333f));
                Assert.AreEqual("5", ShopNumberFormat.Compact(5f));
                Assert.AreEqual("0.32", ShopNumberFormat.Compact(0.32f));
            }
            finally { Thread.CurrentThread.CurrentCulture = saved; }
        }

        [Test]
        public void LinesSkipEmptyOnesAndJoinTheRestWithABreak()
        {
            Assert.AreEqual("a\nb", ShopNumberFormat.Lines("a", "", null, "b"));
            Assert.AreEqual("", ShopNumberFormat.Lines("", null));
        }

        // ---- armour rows ----

        private const string AbsorbNow = "Now: level {0}, soaks {1} damage";
        private const string AbsorbNext = "Next: level {0}, soaks {1} damage ({2})";
        private const string RechargeNow = "Now: level {0}, refills after {1}s out of combat";
        private const string RechargeNext = "Next: level {0}, refills after {1}s out of combat ({2})";
        private const string NoNext = "Next: no upgrade left";

        private ArmorConfig config;

        [SetUp]
        public void CreateConfig() => config = ScriptableObject.CreateInstance<ArmorConfig>();

        [TearDown]
        public void DestroyConfig() => Object.DestroyImmediate(config);

        [Test]
        public void TheAbsorbRowShowsTheCurrentLevelAndTheNextUpgradeWithItsPrice()
        {
            string text = ArmorPopupText.Numbers(true, config, 0, 0, "1400", AbsorbNow, AbsorbNext, NoNext);
            string N(float v) => ShopNumberFormat.Compact(v);
            Assert.AreEqual($"Now: level 0, soaks {N(config.AbsorbFor(0))} damage\nNext: level 1, soaks {N(config.AbsorbFor(1))} damage (1400)", text);
        }

        [Test]
        public void TheRechargeRowShowsTheCurrentLevelAndTheNextUpgradeWithItsPrice()
        {
            string text = ArmorPopupText.Numbers(false, config, 0, 1, "1800", RechargeNow, RechargeNext, NoNext);
            string N(float v) => ShopNumberFormat.Compact(v);
            Assert.AreEqual($"Now: level 1, refills after {N(config.RechargeSecondsFor(1))}s out of combat\nNext: level 2, refills after {N(config.RechargeSecondsFor(2))}s out of combat (1800)", text);
        }

        [Test]
        public void ARowWithNoUpgradeLeftSaysSo()
        {
            // The shared limit (ArmorConfig.MaxArmorUpgrades) is spent: the row has no upgrade left (or its own top level, whichever is first).
            int level = Mathf.Min(config.MaxArmorUpgrades, config.AbsorbLevelCount - 1);
            string text = ArmorPopupText.Numbers(true, config, level, 0, "-", AbsorbNow, AbsorbNext, NoNext);
            Assert.AreEqual($"Now: level {level}, soaks {ShopNumberFormat.Compact(config.AbsorbFor(level))} damage\nNext: no upgrade left", text);
        }

        // ---- what each item shows (read off the real assets) ----

        private static WeaponDefinition WeaponNamed(string assetFile) =>
            AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Gameplay/Weapons/" + assetFile);

        private static AbilityDefinition AbilityNamed(string assetFile) =>
            AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Gameplay/Abilities/" + assetFile);

        [Test]
        public void EveryWeaponShowsDamageFireIntervalRangeAndOverheat()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>("Assets/Gameplay/Weapons/WeaponCatalogue.asset");
            Assert.NotNull(catalogue);
            foreach (WeaponDefinition def in catalogue.Weapons)
            {
                string text = ShopItemNumbers.Weapon(def);
                StringAssert.Contains("Damage", text, def.DisplayName);
                StringAssert.Contains("Fire interval", text, def.DisplayName);
                StringAssert.Contains("Range", text, def.DisplayName);
                StringAssert.Contains("Overheat", text, def.DisplayName);
            }
        }

        [Test]
        public void TheRocketSaysHowBigItsBlastIs()
        {
            StringAssert.Contains("Blast", ShopItemNumbers.Weapon(WeaponNamed("02 Rocket.asset")));
        }

        [Test]
        public void TheBounceWeaponSaysHowManyTimesItBouncesAndWhatEachAdds()
        {
            string text = ShopItemNumbers.Weapon(WeaponNamed("07 Burst - Bounce.asset"));
            // The numbers are Tudor's to tune, so the test pins the wording, not the values.
            StringAssert.IsMatch(@"Bounces \d+ times, \+\d+(\.\d+)?% damage per bounce", text);
        }

        [Test]
        public void TheMarkWeaponSaysWhatAMarkDoes()
        {
            StringAssert.Contains("mark", ShopItemNumbers.Weapon(WeaponNamed("12 Laser - Mark.asset")));
        }

        [Test]
        public void EveryShopAbilityShowsItsCooldownOrChargeRule()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<AbilityCatalogue>("Assets/Gameplay/Config/AbilityCatalogue.asset");
            Assert.NotNull(catalogue);
            int checkedCount = 0;
            foreach (AbilityDefinition def in catalogue.Abilities)
            {
                if (def == null || def.Id >= 900) continue;
                string text = ShopItemNumbers.Ability(def);
                Assert.IsFalse(string.IsNullOrEmpty(text), def.DisplayName);
                checkedCount++;
            }
            Assert.Greater(checkedCount, 10);
        }

        [Test]
        public void AbilitiesShowTheirOwnNumbersBesidesTheCooldown()
        {
            StringAssert.Contains("cooldown", ShopItemNumbers.Ability(AbilityNamed("15 Blink M.asset")));
            // Tudor tunes the values, so these pin the wording (which number the pop-up shows), not the number itself.
            StringAssert.IsMatch(@"Range \d+(\.\d+)?m", ShopItemNumbers.Ability(AbilityNamed("15 Blink M.asset")));
            StringAssert.IsMatch(@"Distance \d+(\.\d+)?m", ShopItemNumbers.Ability(AbilityNamed("14 Dash M.asset")));
            StringAssert.IsMatch(@"Burn \d+(\.\d+)? damage/s for \d+(\.\d+)?s", ShopItemNumbers.Ability(AbilityNamed("21 Flamethrower A.asset")));
            StringAssert.IsMatch(@"Damage \d+(\.\d+)? · blast radius \d+(\.\d+)?m", ShopItemNumbers.Ability(AbilityNamed("19 Mines A.asset")));
            StringAssert.IsMatch(@"Radius \d+(\.\d+)?m · \d+(\.\d+)? damage per pass", ShopItemNumbers.Ability(AbilityNamed("26 Electric Fence U.asset")));
            StringAssert.IsMatch(@"Lasts \d+(\.\d+)?s", ShopItemNumbers.Ability(AbilityNamed("27 AoE Zone U.asset")));
            StringAssert.IsMatch(@"Armed \d+(\.\d+)?s · Invulnerable \d+(\.\d+)?s", ShopItemNumbers.Ability(AbilityNamed("25 Invulnerability U.asset")));
        }

        [Test]
        public void AnUltimateStillSaysItChargesFromKillsNotFromACooldown()
        {
            string text = ShopItemNumbers.Ability(AbilityNamed("27 AoE Zone U.asset"));
            StringAssert.Contains("Charges from kills", text);
            StringAssert.DoesNotContain("0s cooldown", text);
        }
    }
}
