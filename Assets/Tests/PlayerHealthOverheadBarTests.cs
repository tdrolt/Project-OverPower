using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Cleanup batch item 7: the overhead health bar used to stay active over a dead player for the
    /// whole ~5.7s death on other clients' screens - the fills read 0/0 correctly, but the "Bar"
    /// GameObject itself (Track + Health Fill + Shield Fill, a child of HealthBarCanvas - NOT the
    /// canvas itself, which also carries the floating name text and must stay visible on a corpse)
    /// never hid. PlayerLifecycle.ApplyAliveState runs on EVERY client (owner and remote alike - see
    /// its own class comment), so hooking SetOverheadBarVisible from there is what makes this work
    /// for a remote copy too, not just the owner's own screen.
    ///
    /// Edit-mode components never run Awake automatically (confirmed elsewhere in this suite, e.g.
    /// ScopeAbilityTests) - Awake is invoked here by reflection after wiring the three Image fields,
    /// the same way a designer wires them in the Inspector on the real prefab (Bar is their shared
    /// parent there too - confirmed against Assets/Resources/Multiplayer Player.prefab).
    /// </summary>
    public class PlayerHealthOverheadBarTests
    {
        private GameObject playerGo;
        private PlayerHealth health;
        private GameObject barGo;

        [SetUp]
        public void CreateRig()
        {
            playerGo = new GameObject("TestPlayerHealth");
            health = playerGo.AddComponent<PlayerHealth>();

            GameObject canvasGo = new GameObject("HealthBarCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(playerGo.transform);

            barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform);

            SetField("healthFillImage", CreateImageChild(barGo.transform, "Health Fill"));
            SetField("shieldFillImage", CreateImageChild(barGo.transform, "Shield Fill"));
            SetField("overheadTrackImage", CreateImageChild(barGo.transform, "Track"));

            // gameplayConfig/armorConfig/theme are deliberately left unassigned (as in every other
            // edit-mode PlayerHealth-adjacent rig in this suite) - Awake logs one error per missing
            // reference, expected below so the test does not fail on them.
            LogAssert.Expect(LogType.Error, new Regex("GameplayConfig is not assigned"));
            LogAssert.Expect(LogType.Error, new Regex("ArmorConfig is not assigned"));
            LogAssert.Expect(LogType.Error, new Regex("UiTheme is not assigned"));
            InvokeAwake();
        }

        [TearDown]
        public void DestroyRig() => Object.DestroyImmediate(playerGo);

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

        [Test]
        public void TheBarStartsVisible()
        {
            Assert.IsTrue(barGo.activeSelf);
        }

        [Test]
        public void SetOverheadBarVisibleFalseHidesTheBarRoot()
        {
            health.SetOverheadBarVisible(false);

            Assert.IsFalse(barGo.activeSelf);
        }

        [Test]
        public void SetOverheadBarVisibleTrueShowsItAgainAfterHiding()
        {
            health.SetOverheadBarVisible(false);
            health.SetOverheadBarVisible(true);

            Assert.IsTrue(barGo.activeSelf);
        }
    }
}
