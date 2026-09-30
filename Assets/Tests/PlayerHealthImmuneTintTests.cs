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
    /// "make it so there's an overlay so it doesn't mess with the shield". Carry-over C supersedes the
    /// ORIGINAL look (a translucent overlay over the whole bar): a montage of real screen captures
    /// (captures/immune-overlay-alpha-montage.png) showed a wash could not read yellow over the blue
    /// shield fill at any usable alpha, and on the HUD armour bar the FILLED part read PALER than the
    /// EMPTY part - backwards. The look is now a solid yellow FRAME (four thin edge Images) round the
    /// shared health/shield rect, plus an optional faint WASH under it (off by default). These tests
    /// assert the frame switches on with Immune Bar Colour and off again, that a non-zero wash alpha
    /// tints the wash and a zero one leaves it invisible, and that healthFillImage/shieldFillImage
    /// never move off the theme's own Health/Shield Colour regardless - the frame (and wash) are the
    /// ONLY things that change. Same rig as PlayerHealthOverheadBarTests, plus a real UiTheme instance
    /// (Awake needs one to theme the overhead bar at all).</summary>
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
            theme.immuneBarColor = new Color(1f, 0.86f, 0.1f, 1f);
            theme.immuneOverheadFrameThickness = 0.6f;
            theme.immuneBarWashAlpha = 0f;
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

        /// <summary>Carry-over C: the private nested ImmuneFrame instance PlayerHealth builds lazily -
        /// reached the same way FindOverlay used to reach the old single overlay Image, just one hop
        /// further in (the frame object itself, then one of ITS fields).</summary>
        private object FindFrame()
        {
            FieldInfo field = typeof(PlayerHealth).GetField("overheadImmuneFrame", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, "overheadImmuneFrame");
            object frame = field.GetValue(health);
            Assert.NotNull(frame, "the frame is built the first time it's actually needed");
            return frame;
        }

        private GameObject FindFrameRoot()
        {
            object frame = FindFrame();
            FieldInfo rootField = frame.GetType().GetField("root", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(rootField, "ImmuneFrame.root");
            return (GameObject)rootField.GetValue(frame);
        }

        /// <summary>fieldName is one of "top", "bottom", "left", "right" (the four edge strips) or
        /// "wash" (the optional faint reinforcement under them) - see PlayerHealth.ImmuneFrame.</summary>
        private Image FindFramePart(string fieldName)
        {
            object frame = FindFrame();
            FieldInfo partField = frame.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(partField, fieldName);
            return (Image)partField.GetValue(frame);
        }

        [Test]
        public void ShowImmuneLookActivatesTheFrameWithTheImmuneColourAndLeavesTheFillsAlone()
        {
            health.ShowImmuneLook(4f);

            Assert.IsTrue(FindFrameRoot().activeSelf);
            Assert.AreEqual(theme.immuneBarColor, FindFramePart("top").color, "the frame's four edges all carry Immune Bar Colour");
            Assert.AreEqual(theme.immuneBarColor, FindFramePart("bottom").color);
            Assert.AreEqual(theme.immuneBarColor, FindFramePart("left").color);
            Assert.AreEqual(theme.immuneBarColor, FindFramePart("right").color);

            Assert.AreEqual(theme.healthColor, healthFill.color, "the health fill must stay untouched");
            Assert.AreEqual(theme.shieldColor, shieldFill.color, "the shield fill must stay untouched");
            Assert.IsTrue(health.ShowsImmuneLook);
        }

        [Test]
        public void ClearImmuneLookHidesTheFrameAndTheFillsStayUntouched()
        {
            health.ShowImmuneLook(4f);
            health.ClearImmuneLook();

            Assert.IsFalse(FindFrameRoot().activeSelf);
            Assert.AreEqual(theme.healthColor, healthFill.color);
            Assert.AreEqual(theme.shieldColor, shieldFill.color);
            Assert.IsFalse(health.ShowsImmuneLook);
        }

        [Test]
        public void DyingHidesTheImmuneFrameEvenBeforeItsOwnFourSecondsAreUp()
        {
            // Item 3, 2026-09-20 (Tudor: the bar over a dead player hides with the body): the frame is
            // a CHILD of overheadBarRoot, so SetOverheadBarVisible(false) already takes it out of the
            // hierarchy visually. REVIEW 2026-09-21 (opus): its own activeSelf being forced off here is
            // a harmless SECOND GUARD, not a fix for a real race - on death, on every client,
            // PlayerLifecycle.ApplyAliveState(false) raises AliveChanged, and
            // AbilityRunner.HandleAliveChanged -> Interrupt(Died) -> InvulnerabilityAbility.ClearShield
            // -> PlayerHealth.ClearImmuneLook() already clears the frame before SetOverheadBarVisible
            // is even reached, and the shortest respawn (5s) outlasts the longest immune look (4s).
            // This test still pins SetOverheadBarVisible(false) clearing the frame on its own, since
            // that guard is real code this class relies on, even though the real path clears it first.
            health.ShowImmuneLook(4f);
            Assert.IsTrue(FindFrameRoot().activeSelf, "arm the frame first, or this test proves nothing");

            health.SetOverheadBarVisible(false);

            Assert.IsFalse(FindFrameRoot().activeSelf, "a dead player must not carry the immune frame into their next life");
            Assert.IsFalse(health.ShowsImmuneLook);
        }

        [Test]
        public void ShowingTheOverheadBarNeverClearsARunningImmuneLook()
        {
            // Review follow-up, 2026-09-21: SetOverheadBarVisible(false) clears the frame (a harmless
            // second guard - see that method's own comment), but the same method must not also clear
            // it on the OPPOSITE call. Showing the bar back (a respawn, or a corpse's bar reappearing)
            // must never itself end a look that is still legitimately running.
            health.ShowImmuneLook(4f);
            Assert.IsTrue(FindFrameRoot().activeSelf, "arm the frame first, or this test proves nothing");

            health.SetOverheadBarVisible(true);

            Assert.IsTrue(FindFrameRoot().activeSelf, "showing the bar must never clear a running look");
            Assert.IsTrue(health.ShowsImmuneLook);
        }

        [Test]
        public void AZeroWashAlphaLeavesTheWashInvisible()
        {
            theme.immuneBarWashAlpha = 0f;
            health.ShowImmuneLook(4f);

            Assert.AreEqual(0f, FindFramePart("wash").color.a, 1e-4f, "this test forces the frame-only setting (Immune Bar Wash Alpha 0) - carry-over C");
        }

        [Test]
        public void ANonZeroWashAlphaTintsTheWashInImmuneBarColourAtThatAlpha()
        {
            theme.immuneBarWashAlpha = 0.15f;
            health.ShowImmuneLook(4f);

            Color wash = FindFramePart("wash").color;
            Assert.AreEqual(theme.immuneBarColor.r, wash.r, 1e-4f);
            Assert.AreEqual(theme.immuneBarColor.g, wash.g, 1e-4f);
            Assert.AreEqual(theme.immuneBarColor.b, wash.b, 1e-4f);
            Assert.AreEqual(0.15f, wash.a, 1e-4f, "the wash's OWN alpha, independent of Immune Bar Colour's own alpha (now 1)");
        }
    }
}
