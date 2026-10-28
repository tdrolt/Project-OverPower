using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The death card's words and where it sits. Every format, size and rect here is a literal made up for the test (screen pixels, origin
    /// bottom-left); the shop rects follow the shop's look at 1920x1080 (top-anchored, about nine tenths wide and two thirds high, as measured in
    /// Play Mode) and a sweep of other sizes, so the rule is held to "never over the open shop" for any shop, not one layout.</summary>
    public class DeathCardRulesTests
    {
        private const string Format = "RESPAWN IN {0}";

        [TestCase(3.2f, "RESPAWN IN 4")]
        [TestCase(4f, "RESPAWN IN 4")]
        [TestCase(0.01f, "RESPAWN IN 1")]
        [TestCase(0f, "RESPAWN IN 0")]
        [TestCase(-2f, "RESPAWN IN 0")]
        public void CountdownRoundsUp(float secondsLeft, string expected)
        {
            Assert.AreEqual(expected, DeathCardRules.Text(Format, secondsLeft));
        }

        [Test]
        public void SecondsLeftOnlyWhileTheWaitRuns()
        {
            Assert.AreEqual(3.2f, DeathCardRules.SecondsLeft(true, 3.2f), 1e-6f);
            Assert.AreEqual(0f, DeathCardRules.SecondsLeft(true, -1f), "a wait that overran reads 0, never a negative count");
            Assert.AreEqual(0f, DeathCardRules.SecondsLeft(false, 5f), "a stale value after the wait stopped is not shown");
            Assert.AreEqual(4, DeathCardRules.WholeSeconds(3.2f));
            Assert.AreEqual(0, DeathCardRules.WholeSeconds(-2f));
        }

        [Test]
        public void ABrokenFormatStillShowsTheNumber()
        {
            Assert.AreEqual("WAIT {x 3", DeathCardRules.Text("WAIT {x", 2.5f));
        }

        [Test]
        public void WaitingTextReadsAsOneLine()
        {
            Assert.AreEqual("Waiting for team to capture Base", DeathCardRules.OneLine("Waiting for team to \n\ncapture Base "));
        }

        // ---- placement

        private static float Scale(Vector2 screen) => screen.y / 1080f;
        private static Vector2 Card(Vector2 screen) => new Vector2(520f, 110f) * Scale(screen);
        private static float Margin(Vector2 screen) => 16f * Scale(screen);
        private static Rect AbilityBar(Vector2 screen) => new Rect(screen.x * 0.37f, 0f, screen.x * 0.26f, 250f * Scale(screen));
        private static Rect RoundBar(Vector2 screen) => new Rect(screen.x * 0.4f, screen.y - 170f * Scale(screen), screen.x * 0.2f, 140f * Scale(screen));

        /// <summary>A top-anchored shop like the real one: centred, this share of the width, from the top margin down to this share of the height.</summary>
        private static Rect Shop(Vector2 screen, float widthShare, float heightShare)
        {
            float top = screen.y - 24f * Scale(screen);
            float height = screen.y * heightShare;
            return new Rect(screen.x * (1f - widthShare) / 2f, top - height, screen.x * widthShare, height);
        }

        private static bool OnScreen(Rect r, Vector2 screen) => r.xMin >= -0.01f && r.yMin >= -0.01f && r.xMax <= screen.x + 0.01f && r.yMax <= screen.y + 0.01f;

        private static void AssertClear(Rect placed, Rect shop, Rect band, Vector2 screen, string what)
        {
            if (placed.width <= 0f) return; // hidden: nothing to overlap
            Assert.IsFalse(placed.Overlaps(shop), what + ": over the shop " + placed + " / " + shop);
            Assert.IsFalse(band.width > 0f && placed.Overlaps(band), what + ": over the top band " + placed + " / " + band);
            Assert.IsTrue(OnScreen(placed, screen), what + ": off the screen " + placed);
        }

        [TestCase(1280f, 720f)]
        [TestCase(1920f, 1080f)]
        public void NeverOverlapsTheOpenShop(float w, float h)
        {
            var screen = new Vector2(w, h);
            Rect realShop = Shop(screen, 0.906f, 0.684f);
            Rect placed = DeathCardRules.Place(true, realShop, Rect.zero, AbilityBar(screen), screen, Card(screen), Margin(screen));
            Assert.Greater(placed.width, 0f, "with the shop as the game builds it there is room for the card");
            AssertClear(placed, realShop, Rect.zero, screen, "real shop");
            Assert.LessOrEqual(placed.yMax, realShop.yMin, "the card is below the shop (over the ability bar: there is no room between them)");

            Rect shortShop = Shop(screen, 0.75f, 0.5f);
            Rect roomy = DeathCardRules.Place(true, shortShop, Rect.zero, AbilityBar(screen), screen, Card(screen), Margin(screen));
            Assert.Greater(roomy.width, 0f, "with room between the shop and the ability bar the card shows");
            AssertClear(roomy, shortShop, Rect.zero, screen, "short shop");
            Assert.IsFalse(roomy.Overlaps(AbilityBar(screen)), "with room between the shop and the ability bar the card sits just above the bar");
            Assert.AreEqual(AbilityBar(screen).yMax + Margin(screen), roomy.yMin, 0.01f, "just above the bar");

            // The card the game draws while the shop is open is the small one: it fits between the real shop and the ability bar.
            Vector2 small = new Vector2(520f, 48f) * Scale(screen);
            float smallMargin = 6f * Scale(screen);
            Rect between = DeathCardRules.Place(true, realShop, Rect.zero, AbilityBar(screen), screen, small, smallMargin);
            Assert.Greater(between.width, 0f);
            AssertClear(between, realShop, Rect.zero, screen, "small card, real shop");
            Assert.IsFalse(between.Overlaps(AbilityBar(screen)), "the small card leaves the ability bar readable");

            for (float widthShare = 0.4f; widthShare <= 1.001f; widthShare += 0.1f)
                for (float heightShare = 0.1f; heightShare <= 0.98f; heightShare += 0.04f)
                {
                    Rect shop = Shop(screen, widthShare, heightShare);
                    AssertClear(DeathCardRules.Place(true, shop, Rect.zero, AbilityBar(screen), screen, Card(screen), Margin(screen)), shop, Rect.zero, screen,
                        "shop " + widthShare + " x " + heightShare);
                    AssertClear(DeathCardRules.Place(true, shop, RoundBar(screen), AbilityBar(screen), screen, Card(screen), Margin(screen)), shop, RoundBar(screen), screen,
                        "shop " + widthShare + " x " + heightShare + " in Dominion");
                }
        }

        [Test]
        public void TopCentreWhileTheShopIsClosed()
        {
            var screen = new Vector2(1920f, 1080f);
            Rect placed = DeathCardRules.Place(false, Shop(screen, 0.75f, 0.56f), Rect.zero, AbilityBar(screen), screen, Card(screen), Margin(screen));
            Assert.AreEqual(screen.x / 2f, placed.center.x, 0.01f, "centred");
            Assert.AreEqual(screen.y - Margin(screen), placed.yMax, 0.01f, "at the top");
        }

        [TestCase(1280f, 720f)]
        [TestCase(1920f, 1080f)]
        public void NeverOverlapsTheTopReservedBand(float w, float h)
        {
            var screen = new Vector2(w, h);
            for (float bandShare = 0.05f; bandShare <= 0.9f; bandShare += 0.05f)
            {
                var band = new Rect(screen.x * 0.25f, screen.y * (1f - bandShare), screen.x * 0.5f, screen.y * bandShare);
                Rect closed = DeathCardRules.Place(false, Rect.zero, band, AbilityBar(screen), screen, Card(screen), Margin(screen));
                AssertClear(closed, Rect.zero, band, screen, "shop closed, band " + bandShare);
                Rect shop = Shop(screen, 0.75f, 0.56f);
                AssertClear(DeathCardRules.Place(true, shop, band, AbilityBar(screen), screen, Card(screen), Margin(screen)), shop, band, screen, "shop open, band " + bandShare);
            }
            Rect underRoundBar = DeathCardRules.Place(false, Rect.zero, RoundBar(screen), AbilityBar(screen), screen, Card(screen), Margin(screen));
            Assert.Greater(underRoundBar.width, 0f);
            Assert.AreEqual(RoundBar(screen).yMin - Margin(screen), underRoundBar.yMax, 0.01f, "right under the round bar");
        }

        [TestCase(1280f, 720f)]
        [TestCase(1920f, 1080f)]
        public void StaysClearOfATopCentreToast(float w, float h)
        {
            var screen = new Vector2(w, h);
            // The HUD's toast: 590 x 52 reference pixels, 28 from the top, centred.
            var toast = new Rect((screen.x - 590f * Scale(screen)) / 2f, screen.y - 80f * Scale(screen), 590f * Scale(screen), 52f * Scale(screen));
            Rect placed = DeathCardRules.Place(false, Rect.zero, toast, AbilityBar(screen), screen, Card(screen), Margin(screen));
            Assert.Greater(placed.width, 0f);
            AssertClear(placed, Rect.zero, toast, screen, "toast");
            Rect both = ScreenRects.Union(RoundBar(screen), toast);
            Rect inDominion = DeathCardRules.Place(false, Rect.zero, both, AbilityBar(screen), screen, Card(screen), Margin(screen));
            AssertClear(inDominion, Rect.zero, toast, screen, "toast in Dominion");
            AssertClear(inDominion, Rect.zero, RoundBar(screen), screen, "round bar with a toast");
        }

        // ---- the game calls the rules

        [Test]
        public void TheCardIsPlacedAndWordedThroughTheRules()
        {
            var missing = new List<string>();
            Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", typeof(DeathCardRules).GetMethod(nameof(DeathCardRules.Place))), "Place");
            Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", typeof(DeathCardRules).GetMethod(nameof(DeathCardRules.Text))), "Text");
            Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", typeof(DeathCardRules).GetMethod(nameof(DeathCardRules.OneLine))), "the waiting card's text");
            Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", typeof(PlayerHud).GetProperty(nameof(PlayerHud.ToastRect)).GetMethod),
                "the HUD's toast is kept clear like the round bar");
            foreach (string name in new[] { nameof(UiTheme.deathCardShopOpenTitleSize), nameof(UiTheme.deathCardShopOpenPadding), nameof(UiTheme.deathCardShopOpenMargin) })
                Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", typeof(UiTheme).GetField(name)), name + ": the small card while the shop is open");
            Assert.IsEmpty(missing);
        }

        [Test]
        public void TheCountdownIsHandedOverFromTheRespawnWait()
        {
            FieldInfo waitLeft = typeof(PlayerLifecycle).GetField("respawnWaitLeft", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(waitLeft);
            var missing = new List<string>();
            Check(missing, IlWiring.Stores(typeof(PlayerLifecycle), "RespawnPlayer", waitLeft), "the wait writes what is left of it");
            MethodInfo secondsLeft = typeof(PlayerLifecycle).GetProperty(nameof(PlayerLifecycle.RespawnSecondsLeft)).GetMethod;
            Check(missing, IlWiring.Uses(typeof(PlayerLifecycle), secondsLeft.Name, typeof(DeathCardRules).GetMethod(nameof(DeathCardRules.SecondsLeft))), "read through the rule");
            Check(missing, IlWiring.Uses(typeof(PlayerLifecycle), secondsLeft.Name, waitLeft), "the rule is given the wait's own value");
            Check(missing, IlWiring.Uses(typeof(DeathCard), "LateUpdate", secondsLeft), "the card counts down from it");
            Assert.IsEmpty(missing);
        }

        [Test]
        public void MatchUiSkinsTheResultPanels()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), "Awake", typeof(ResultPanelSkin).GetMethod(nameof(ResultPanelSkin.Apply))));
        }

        [Test]
        public void MatchUiRoutesTheRespawnAndWaitingStatesToTheCard()
        {
            MethodInfo respawn = typeof(DeathCard).GetMethod(nameof(DeathCard.SetRespawn));
            MethodInfo waiting = typeof(DeathCard).GetMethod(nameof(DeathCard.SetWaiting));
            MethodInfo note = typeof(DeathCard).GetMethod(nameof(DeathCard.SetNote));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), nameof(MatchUi.SetRespawnPanelVisible), respawn));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), nameof(MatchUi.ShowWaitingPanel), waiting));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), nameof(MatchUi.HideWaitingPanel), waiting));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), nameof(MatchUi.SetRespawnNote), note));
            Assert.IsTrue(IlWiring.Uses(typeof(MatchUi), "RPC_HideWaitingPanelAll", typeof(MatchUi).GetMethod(nameof(MatchUi.HideWaitingPanel))),
                "an older client's hide still reaches the card, like the other kept RPCs");
        }

        [Test]
        public void TheDeathCardAndPopUpFieldsAreInTheYamlWithATooltip()
        {
            string yaml = System.IO.File.ReadAllText("Assets/_Project/Design/Settings/UiTheme.asset").Replace("\r\n", "\n");
            foreach (string name in new[] { "deathCardRespawnFormat", "deathCardWidth", "deathCardTitleSize", "deathCardPadding", "deathCardGap", "deathCardMargin",
                         "deathVignetteColor", "popupDimColor", "popupCardWidth", "popupTitleSize", "popupBodySize", "masterVolumeLabel", "masterVolumePercentFormat",
                         "masterVolumeRowHeight", "matchLogDismissText", "resultPanelCardWidth", "resultPanelTitleSize", "resultPanelButtonWidth",
                         "deathCardShopOpenTitleSize", "deathCardShopOpenPadding", "deathCardShopOpenMargin" })
            {
                FieldInfo f = typeof(UiTheme).GetField(name);
                Assert.NotNull(f, name);
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                Assert.NotNull(tip, name + " has no tooltip");
                Assert.Greater(tip.tooltip.Length, 40, name + " tooltip is not a full sentence");
                StringAssert.Contains("\n  " + name + ":", yaml, name + " is not in UiTheme.asset");
            }
        }

        private static void Check(List<string> missing, bool wired, string what)
        {
            if (!wired) missing.Add(what);
        }
    }
}
