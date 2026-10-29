using System.Linq;
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

        private static int Active(System.Collections.Generic.IReadOnlyList<Image> images) =>
            images == null ? 0 : images.Count(i => i.gameObject.activeSelf && i.enabled);

        [Test]
        public void TheArmourMarksAreDrawnAsTheRuleComposesThem()
        {
            theme.nameplateArmourMarkSize = 2f;
            theme.nameplateArmourNestedScale = 0.5f;
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            plate.ShowArmourMarks(2, 1, 10f);
            Assert.AreEqual(2, Active(plate.MarkSquares), "two Absorb squares");
            Assert.AreEqual(1, Active(plate.MarkChevrons), "one Recharge v");

            RectTransform outer = plate.MarkSquares[0].rectTransform, inner = plate.MarkSquares[1].rectTransform;
            Assert.AreEqual(outer.position, inner.position, "the second square is centred inside the first");
            Assert.AreEqual(2f, outer.rect.width, 1e-4f);
            Assert.AreEqual(1f, inner.rect.width, 1e-4f);
            Vector3 v = inner.InverseTransformPoint(plate.MarkChevrons[0].rectTransform.position);
            Assert.IsTrue(inner.rect.Contains(v), "the v sits inside the innermost square");
            foreach (Image i in plate.MarkSquares.Concat(plate.MarkChevrons))
                Assert.IsFalse(i.raycastTarget, i.name + " would swallow a shot aimed past a player's head");

            float width = plate.NameText.GetPreferredValues("Ana").x;
            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), width, true);
            AssertPlaced(f.marks, plate.Marks, "marks");
            AssertPlaced(f.name, plate.NameText.rectTransform, "name");
            Assert.IsTrue(plate.Marks.gameObject.activeSelf);
        }

        [Test]
        public void TheMarkGoesWithTheShieldAndComesBackAsItRecharges()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.ShowArmourMarks(1, 1, 10f);
            Assert.IsTrue(plate.Marks.gameObject.activeSelf);
            plate.ShowArmourMarks(1, 1, 0f);
            Assert.IsFalse(plate.Marks.gameObject.activeSelf, "shield at zero: the bar shows green HP and no mark");
            plate.ShowArmourMarks(1, 1, 0.2f);
            Assert.IsTrue(plate.Marks.gameObject.activeSelf, "back as soon as the shield recharges");
        }

        [Test]
        public void YourOwnPlateHasNoNameAndSplitsTheBar()
        {
            theme.nameplateOwnRowGap = 0.5f;
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            plate.SetOwnPlate(true);
            Assert.IsFalse(plate.NameText.gameObject.activeSelf);

            NameplateLayout.Frame f = NameplateLayout.Compute(NameplateLayout.From(theme), 0f, true, true);
            AssertPlaced(f.bar, plate.Bar, "bar");
            RectTransform shield = health.OverheadShieldFill.rectTransform, hp = health.OverheadHealthFill.rectTransform;
            Assert.Greater(shield.anchorMin.y, hp.anchorMax.y, "shield row above the HP row, with a gap");
            Assert.AreEqual(1f, shield.anchorMax.y, 1e-4f);
            Assert.AreEqual(0f, hp.anchorMin.y, 1e-4f);
            Assert.AreEqual(hp.anchorMax.y, health.OverheadTrack.rectTransform.anchorMax.y, 1e-4f, "the HP row keeps the old track");
            Image shieldTrack = plate.Bar.Find("Shield Track").GetComponent<Image>();
            Assert.AreEqual(shield.anchorMin.y, shieldTrack.rectTransform.anchorMin.y, 1e-4f);
            Assert.Less(shieldTrack.transform.GetSiblingIndex(), health.OverheadHealthFill.transform.GetSiblingIndex(), "tracks under the fills");

            plate.ShowShieldRow(false);
            Assert.IsFalse(shieldTrack.enabled, "no armour to hold: no shield row, as on the HUD");
        }

        [Test]
        public void YourOwnCorpseShowsNoPlate()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetOwnPlate(true);
            plate.SetBarShown(false);
            Assert.IsFalse(plate.Backing.enabled);
        }

        [Test]
        public void TwoRechargeLevelsStackTwoVsInOneMark()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.ShowArmourMarks(0, 2, 10f);
            Assert.AreEqual(0, Active(plate.MarkSquares));
            Assert.AreEqual(2, Active(plate.MarkChevrons));
            Vector3 a = plate.MarkChevrons[0].rectTransform.position, b = plate.MarkChevrons[1].rectTransform.position;
            Assert.AreEqual(a.x, b.x, 1e-3f, "stacked, not side by side");
            Assert.AreNotEqual(a.y, b.y);
        }

        [Test]
        public void SellingTheUpgradesTakesTheMarksAway()
        {
            NameplateView plate = NameplateView.Ensure(health);
            plate.SetName("Ana");
            plate.ShowArmourMarks(2, 2, 10f);
            plate.ShowArmourMarks(0, 0, 10f);
            Assert.AreEqual(0, Active(plate.MarkSquares));
            Assert.AreEqual(0, Active(plate.MarkChevrons));
            float width = plate.NameText.GetPreferredValues("Ana").x;
            AssertPlaced(NameplateLayout.Compute(NameplateLayout.From(theme), width, true).name, plate.NameText.rectTransform, "name");
        }

        [Test]
        public void HealthLinesSitUnderTheShieldAndArmourLinesOnItWhereItIsFilled()
        {
            theme.barSegmentSize = 25f;
            NameplateView plate = NameplateView.Ensure(health);
            plate.ShowSegments(100f, 50f, 1f);
            Assert.AreEqual(3, plate.HealthLines.Count);
            foreach (Image line in plate.HealthLines.Lines)
                Assert.AreSame(health.OverheadHealthFill.transform, line.transform.parent, "under the shield, which is drawn after health");
            Assert.AreEqual(1, plate.ArmourLines.Count);
            Assert.AreSame(health.OverheadShieldFill.transform, plate.ArmourLines.Lines[0].transform.parent);
            Assert.AreEqual(1, Active(plate.ArmourLines.Lines));

            plate.ShowSegments(100f, 50f, 0.4f);
            Assert.AreEqual(0, Active(plate.ArmourLines.Lines), "no armour line past the shield's fill");
        }

        private static MethodInfo M(System.Type type, string name) => type.GetMethod(name);

        [Test]
        public void TheNamesColourComesFromTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "UpdateNameTagColour", M(typeof(NameplateRules), nameof(NameplateRules.NameColour))));

        // A bare "it calls the rule" would still pass with the name colour or a constant handed to the edge.
        [Test]
        public void TheTeamColourGoesToThePlatesEdge()
        {
            MethodInfo update = typeof(PlayerNameTag).GetMethod("UpdateNameTagColour", BindingFlags.Instance | BindingFlags.NonPublic);
            var edgeAt = IlWiring.CallOffsets(update, M(typeof(NameplateRules), nameof(NameplateRules.EdgeColour)));
            var setAt = IlWiring.CallOffsets(update, M(typeof(NameplateView), nameof(NameplateView.SetColours)));
            Assert.IsTrue(edgeAt.Any(at => setAt.Contains(at + 5)), "the rule's answer is SetColours' last argument, the edge (a call is 5 bytes)");
        }

        [Test]
        public void TheColoursReachThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "UpdateNameTagColour", M(typeof(NameplateView), nameof(NameplateView.SetColours))));

        [Test]
        public void TheNameTagFindsThePlate() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", M(typeof(NameplateView), nameof(NameplateView.Ensure))));

        [Test]
        public void TheNameTagTellsThePlateWhetherItIsYours()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", M(typeof(NameplateRules), nameof(NameplateRules.ShowsOwnBars))));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerNameTag), "Start", M(typeof(NameplateView), nameof(NameplateView.SetOwnPlate))));
        }

        [Test]
        public void TheShieldRowFollowsWhetherThereIsArmourToHold() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "UpdateOverheadBar", M(typeof(NameplateView), nameof(NameplateView.ShowShieldRow))));

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
