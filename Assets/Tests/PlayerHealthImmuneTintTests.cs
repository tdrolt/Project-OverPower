using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Mark plan step 1, Tudor's override (the table at the top of the plan, answer 6):
    /// "make it so there's an overlay so it doesn't mess with the shield" - the yellow look is a
    /// translucent OVERLAY drawn over the shared health/shield rect, not a recolour of either fill.
    /// These tests assert the overlay switches on with Immune Bar Colour and off again, and that
    /// healthFillImage/shieldFillImage never move off the theme's own Health/Shield Colour regardless -
    /// the overlay is the ONLY thing that changes. Same rig as PlayerHealthOverheadBarTests, plus a
    /// real UiTheme instance (Awake needs one to theme the overhead bar at all).</summary>
    public class PlayerHealthImmuneTintTests
    {
        private GameObject playerGo;
        private PlayerHealth health;
        private Image healthFill;
        private Image shieldFill;
        private UiTheme theme;
        private Sprite barSprite;

        [SetUp]
        public void CreateRig()
        {
            playerGo = new GameObject("TestPlayerHealth");
            health = playerGo.AddComponent<PlayerHealth>();

            GameObject canvasGo = new GameObject("HealthBarCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(playerGo.transform);

            GameObject barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform);

            healthFill = CreateImageChild(barGo.transform, "Health Fill");
            shieldFill = CreateImageChild(barGo.transform, "Shield Fill");
            SetField("healthFillImage", healthFill);
            SetField("shieldFillImage", shieldFill);
            SetField("overheadTrackImage", CreateImageChild(barGo.transform, "Track"));

            barSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.barSprite = barSprite;
            theme.healthColor = new Color(0.1f, 0.9f, 0.1f, 1f);
            theme.shieldColor = new Color(0.1f, 0.1f, 0.9f, 1f);
            theme.immuneBarColor = new Color(1f, 0.86f, 0.1f, 0.45f);
            SetField("theme", theme);

            // gameplayConfig/armorConfig are deliberately left unassigned (as PlayerHealthOverheadBarTests
            // already does) - a theme IS assigned here, unlike that rig, so no UiTheme error is expected.
            LogAssert.Expect(LogType.Error, new Regex("GameplayConfig is not assigned"));
            LogAssert.Expect(LogType.Error, new Regex("ArmorConfig is not assigned"));
            InvokeAwake();
        }

        [TearDown]
        public void DestroyRig()
        {
            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(theme);
            Object.DestroyImmediate(barSprite);
        }

        private static Image CreateImageChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent);
            return go.AddComponent<Image>();
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PlayerHealth).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, name);
            field.SetValue(health, value);
        }

        private void InvokeAwake()
        {
            MethodInfo awake = typeof(PlayerHealth).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(awake, "PlayerHealth.Awake");
            awake.Invoke(health, null);
        }

        private Image FindOverlay()
        {
            FieldInfo field = typeof(PlayerHealth).GetField("overheadImmuneOverlay", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "overheadImmuneOverlay");
            return (Image)field.GetValue(health);
        }

        [Test]
        public void ShowImmuneLookActivatesTheOverlayWithTheImmuneColourAndLeavesTheFillsAlone()
        {
            health.ShowImmuneLook(4f);

            Image overlay = FindOverlay();
            Assert.NotNull(overlay, "the overlay is built the first time it's actually needed");
            Assert.IsTrue(overlay.gameObject.activeSelf);
            Assert.AreEqual(theme.immuneBarColor, overlay.color);

            Assert.AreEqual(theme.healthColor, healthFill.color, "the health fill must stay untouched");
            Assert.AreEqual(theme.shieldColor, shieldFill.color, "the shield fill must stay untouched");
            Assert.IsTrue(health.ShowsImmuneLook);
        }

        [Test]
        public void ClearImmuneLookHidesTheOverlayAndTheFillsStayUntouched()
        {
            health.ShowImmuneLook(4f);
            health.ClearImmuneLook();

            Image overlay = FindOverlay();
            Assert.IsFalse(overlay.gameObject.activeSelf);
            Assert.AreEqual(theme.healthColor, healthFill.color);
            Assert.AreEqual(theme.shieldColor, shieldFill.color);
            Assert.IsFalse(health.ShowsImmuneLook);
        }
    }
}
