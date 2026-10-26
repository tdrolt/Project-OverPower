using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.UI;
using Overpower.Vision;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// The plate over a player's head as the game builds it: on the canvas the fog hides (EnemyVisibility's own lookup, so an enemy's
    /// plate vanishes in the fog exactly as before), placed from NameplateLayout, coloured from what PlayerNameTag hands it; and the
    /// components that must ask these rules. The rig mirrors the prefab: a screen-space canvas first, then the world-space one with
    /// the bar (Track, Health Fill, Shield Fill) under it.
    /// </summary>
    public class NameplateViewTests
    {
        private GameObject player;
        private PlayerHealth health;
        private Canvas overhead;
        private RectTransform bar;
        private UiTheme theme;
        private Sprite sprite;

        [SetUp]
        public void CreateRig()
        {
            player = new GameObject("Nameplate Rig");
            var screenGo = new GameObject("Win Lose Canvas", typeof(RectTransform));
            screenGo.transform.SetParent(player.transform, false);
            screenGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var canvasGo = new GameObject("HealthBarCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(player.transform, false);
            overhead = canvasGo.AddComponent<Canvas>();
            overhead.renderMode = RenderMode.WorldSpace;

            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform, false);
            bar = barGo.GetComponent<RectTransform>();

            health = player.AddComponent<PlayerHealth>();
            Set("overheadTrackImage", Child(bar, "Track"));
            Set("healthFillImage", Child(bar, "Health Fill"));
            Set("shieldFillImage", Child(bar, "Shield Fill"));

            sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.barSprite = sprite;
            theme.nameplateNameSize = 4f;
            theme.nameplateBarSize = new Vector2(20f, 3f);
            theme.nameplatePadding = new Vector2(1f, 0.5f);
            theme.nameplateRowGap = 0.5f;
            theme.nameplateMaxWidth = 30f;
            theme.nameplateBottomY = -2f;
            theme.nameplateEdgeThickness = 0.25f;
            theme.nameplateBackingColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            Set("theme", theme);
        }

        [TearDown]
        public void DestroyRig()
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(theme);
            Object.DestroyImmediate(sprite);
        }

        private static Image Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        private void Set(string field, object value)
        {
            FieldInfo f = typeof(PlayerHealth).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f, field);
            f.SetValue(health, value);
        }

        private static void AssertPlaced(Rect expected, RectTransform rt, string what)
        {
            Assert.AreEqual(expected.center.x, rt.anchoredPosition.x, 1e-3f, what + " x");
            Assert.AreEqual(expected.center.y, rt.anchoredPosition.y, 1e-3f, what + " y");
            Assert.AreEqual(expected.width, rt.sizeDelta.x, 1e-3f, what + " width");
            Assert.AreEqual(expected.height, rt.sizeDelta.y, 1e-3f, what + " height");
        }

        [Test]
        public void TheFogRuleFindsTheWorldSpaceCanvasNotTheScreenOne()
        {
            Assert.AreSame(overhead, EnemyVisibility.FindOverheadCanvas(player.transform));
        }

        [Test]
        public void ThePlateIsBuiltOnTheCanvasTheFogHides()
        {
            NameplateView plate = NameplateView.Ensure(health);
            Assert.NotNull(plate);
            plate.SetName("Ana");
            Graphic[] graphics = plate.Root.GetComponentsInChildren<Graphic>(true);
            Assert.Greater(graphics.Length, 0);
            foreach (Graphic g in graphics)
                Assert.AreSame(overhead, g.GetComponentInParent<Canvas>(true), g.name + " must sit on the canvas the fog switches off");
            Assert.IsTrue(plate.NameText.transform.IsChildOf(overhead.transform));
            Assert.IsTrue(bar.IsChildOf(plate.Root), "the bar is part of the plate");
        }

        [Test]
        public void ThePlateAddsNoCanvasOfItsOwn()
        {
            NameplateView.Ensure(health).SetName("Ana");
            // A nested canvas would draw on its own, outside the switch the fog flips.
            Assert.AreEqual(1, overhead.GetComponentsInChildren<Canvas>(true).Length);
        }

        [Test]
        public void ThePlateIsBuiltOnce()
        {
            NameplateView first = NameplateView.Ensure(health);
            NameplateView second = NameplateView.Ensure(health);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, overhead.GetComponents<NameplateView>().Length);
        }

        [Test]
        public void ThePlateAsksTheFogRuleForItsCanvas()
        {
            MethodInfo find = typeof(EnemyVisibility).GetMethod(nameof(EnemyVisibility.FindOverheadCanvas));
            Assert.IsTrue(IlWiring.Uses(typeof(NameplateView), nameof(NameplateView.Ensure), find),
                "the plate must use the canvas the fog hides, found by the fog's own lookup");
            Assert.IsTrue(IlWiring.Uses(typeof(EnemyVisibility), "Awake", find), "the fog hides the canvas its lookup finds");
        }

        [Test]
        public void TheNameIsCrispTextThatTakesNoClicks()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            Assert.IsInstanceOf<TextMeshProUGUI>(plate.NameText);
            Assert.AreEqual("Ana", plate.NameText.text);
            Assert.IsFalse(plate.NameText.raycastTarget);
            Assert.AreEqual(Vector3.one, plate.NameText.rectTransform.localScale, "no stretched glyphs");
            // The bar's own images are PlayerHealth.ApplyTheme's to clear; these are the plate's.
            Assert.IsFalse(plate.Backing.raycastTarget, "the backing would swallow a shot aimed past a player's head");
            foreach (Image e in plate.Edges)
                Assert.IsFalse(e.raycastTarget, e.name + " would swallow a shot aimed past a player's head");
        }

        [Test]
        public void TheNameIsActuallyDrawn()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            plate.NameText.ForceMeshUpdate();
            Assert.AreEqual(3, plate.NameText.textInfo.characterCount, "a line taller than its row must not make the name vanish");
        }

        [Test]
        public void AVeryLongNameIsCutShortNotDropped()
        {
            NameplateView plate = NameplateView.Ensure(health);
            string longName = new string('W', 40);
            plate.SetName(longName);
            plate.NameText.ForceMeshUpdate();
            int drawn = plate.NameText.textInfo.characterCount;
            Assert.Greater(drawn, 0);
            Assert.Less(drawn, longName.Length);
        }

        [Test]
        public void ThePlateIsPlacedFromTheLayout()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            float width = plate.NameText.GetPreferredValues("Ana").x;
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), width, true);
            AssertPlaced(f.plate, plate.Backing.rectTransform, "backing");
            AssertPlaced(f.name, plate.NameText.rectTransform, "name");
            AssertPlaced(f.bar, bar, "bar");
        }

        [Test]
        public void HidingTheBarShrinksThePlateToTheName()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            plate.SetBarShown(false);
            float width = plate.NameText.GetPreferredValues("Ana").x;
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), width, false);
            AssertPlaced(f.plate, plate.Backing.rectTransform, "backing");
            AssertPlaced(f.name, plate.NameText.rectTransform, "name");
        }

        [Test]
        public void TheEdgeWearsTheEdgeColourAndTheNameTheNameColour()
        {
            NameplateView plate = NameplateView.Ensure(health);
            var name = new Color(0.2f, 0.9f, 0.3f, 1f);
            var edge = new Color(0.7f, 0.3f, 1f, 1f);
            plate.SetColours(name, edge);
            Assert.AreEqual(name, plate.NameText.color);
            Assert.AreEqual(4, plate.Edges.Length, "an edge on every side");
            foreach (Image e in plate.Edges)
                Assert.AreEqual(edge, e.color);
            Assert.AreEqual(theme.nameplateBackingColor, plate.Backing.color);
        }

        [Test]
        public void TheEdgesRunRoundTheBackingAtTheirThickness()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            foreach (Image e in plate.Edges)
            {
                Assert.IsTrue(e.transform.IsChildOf(plate.Backing.transform));
                Vector2 size = e.rectTransform.rect.size;
                Assert.AreEqual(0.25f, Mathf.Min(size.x, size.y), 1e-3f, e.name);
            }
        }

        [Test]
        public void TheStatusLabelSitsAboveThePlateInCrispText()
        {
            PlayerStatusEffects status = player.AddComponent<PlayerStatusEffects>();
            theme.nameplateStatusTextSize = 3f;
            theme.nameplateStatusGap = 1f;
            theme.nameplateStatusBarSize = new Vector2(20f, 1f);
            theme.nameplateStatusLabelGap = 0.5f;
            Assert.NotNull(StatusLabelOverhead.TryCreate(status, theme, overhead));

            Transform root = overhead.transform.Find("Status Label");
            Assert.NotNull(root);
            var label = root.Find("Text").GetComponent<TextMeshProUGUI>();
            Assert.NotNull(label, "the label is TextMeshPro like the name");
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), 0f, true);
            AssertPlaced(f.statusLabel, label.rectTransform, "label");
            AssertPlaced(f.statusBar, (RectTransform)root.Find("Bar"), "countdown bar");
        }

        [Test]
        public void TheTextBuilderUsesTheSizeAndPlaceItIsGiven()
        {
            var rect = new Rect(-5f, 2f, 10f, 3f);
            TextMeshProUGUI text = NameplateView.BuildText(overhead.transform, "Probe", theme, 3f, rect);
            Assert.AreEqual(3f, text.fontSize, 1e-4f);
            AssertPlaced(rect, text.rectTransform, "probe");
            Assert.IsFalse(text.raycastTarget);
            Assert.AreEqual(Vector3.one, text.rectTransform.localScale);
        }

        private static MethodInfo M(System.Type type, string name) => type.GetMethod(name);

        [Test]
        public void TheNamesColourComesFromTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "UpdateNameTagColour", M(typeof(NameplateRules), nameof(NameplateRules.NameColour))));

        [Test]
        public void TheColoursReachThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "UpdateNameTagColour", M(typeof(NameplateView), nameof(NameplateView.SetColours))));

        [Test]
        public void TheNameTagFindsThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", M(typeof(NameplateView), nameof(NameplateView.Ensure))));

        [Test]
        public void TheNameTagNamesThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", M(typeof(NameplateView), nameof(NameplateView.SetName))));

        [Test]
        public void ThePlayersHealthBuildsThePlateBeforeANameArrives() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "Awake", M(typeof(NameplateView), nameof(NameplateView.Ensure))));

        [Test]
        public void ACorpsesPlateShrinksToItsName() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), nameof(PlayerHealth.SetOverheadBarVisible),
                M(typeof(NameplateView), nameof(NameplateView.SetBarShown))));

        [Test]
        public void TheStatusRuleDecidesWhetherTheLabelShows() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(StatusLabelOverhead), nameof(StatusLabelOverhead.Tick),
                M(typeof(StatusLabelRule), nameof(StatusLabelRule.Look))));

        [Test]
        public void WastedIsCrispTextLikeTheName() =>
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "SetWasted", M(typeof(NameplateView), nameof(NameplateView.BuildText))));
    }
}
