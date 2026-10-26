using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The white flash on a hit mech: built idle over the mech's own renderers, lit through a property block (never a material copy,
    /// never the team material changed), refreshed by every hit, a flicker (not a white blob) under damage over time, off on a dead player,
    /// no garbage per frame; and the game asks the rule from
    /// the one damage funnel on every client's copy, before only the victim's own client goes on. Literal looks, never the real asset's.</summary>
    public class HitFlashViewTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float Frame = 1f / 60f;
        private const float Length = 0.1f;
        private const float Gap = 0.3f;
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private UiTheme theme;
        private ArenaLook look;
        private Material team;
        private GameObject body;
        private SkinnedMeshRenderer[] parts;

        [SetUp]
        public void SetUp()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.flashWhiteColor = Color.white;
            look = ScriptableObject.CreateInstance<ArenaLook>();
            look.hitFlashSeconds = Length;
            look.hitFlashGlow = 1.5f;
            look.overTimeFlashGapSeconds = Gap;
            theme.arenaLook = look;
            team = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            body = new GameObject("HitFlashViewTests Body") { hideFlags = HideFlags.HideAndDontSave };
            parts = new SkinnedMeshRenderer[3];
            for (int i = 0; i < parts.Length; i++)
            {
                var part = new GameObject("Part " + i);
                part.transform.SetParent(body.transform, false);
                parts[i] = part.AddComponent<SkinnedMeshRenderer>();
                parts[i].sharedMaterial = team;
            }
            parts[2].gameObject.SetActive(false); // the mesh is hidden while dead and can come back flashing
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(body);
            Object.DestroyImmediate(team);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private HitFlashView Make() => HitFlashView.TryCreate(body, null, theme);

        private void AssertIdle(string why)
        {
            foreach (SkinnedMeshRenderer part in parts)
            {
                Assert.IsFalse(part.HasPropertyBlock(), why + ": " + part.name + " is back to its own look");
                Assert.AreSame(team, part.sharedMaterial, why + ": the team material is the one worn");
            }
        }

        [Test]
        public void BuiltIdleWithoutTouchingTheMech()
        {
            HitFlashView view = Make();
            Assert.NotNull(view);
            Assert.IsFalse(view.Flashing);
            Assert.IsFalse(view.enabled, "an idle flash costs nothing per frame");
            AssertIdle("nothing hit yet");
        }

        [Test]
        public void NoThemeLookNoFlash()
        {
            theme.arenaLook = null;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.IsNull(Make());
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void AHitLightsEveryPartWhiteThenTheMechGoesBack()
        {
            HitFlashView view = Make();
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            view.Flash(DamageSource.Projectile, 0f);
            Assert.IsTrue(view.enabled, "a hit wakes the flash");
            view.Step(true, Frame);
            Assert.IsTrue(view.Flashing);
            Assert.AreEqual(1f, view.CurrentStrength, 1e-5f, "full on the frame of the hit");
            var block = new MaterialPropertyBlock();
            foreach (SkinnedMeshRenderer part in parts)
            {
                Assert.IsTrue(part.HasPropertyBlock(), part.name + " flashes, hidden or not");
                part.GetPropertyBlock(block);
                Assert.AreSame(Texture2D.whiteTexture, block.GetTexture(EmissionMapId), "the whole body glows, not only the glow lines");
                Color glow = block.GetColor(EmissionColorId);
                Assert.That(glow.r, Is.GreaterThan(0.5f));
                Assert.AreEqual(glow.r, glow.g, 1e-4f, "white, not a team colour");
                Assert.AreEqual(glow.r, glow.b, 1e-4f, "white, not a team colour");
                Assert.AreSame(team, part.sharedMaterial, "the team material is never swapped");
            }
            Assert.AreEqual(materials, Resources.FindObjectsOfTypeAll<Material>().Length, "no material copy per hit");

            for (int i = 0; i < 10; i++)
                view.Step(true, Frame);
            Assert.IsFalse(view.Flashing, "over after its length");
            Assert.IsFalse(view.enabled, "asleep again");
            AssertIdle("after the flash");
        }

        [Test]
        public void TheFlashStaysFullWhileItShows()
        {
            HitFlashView view = Make();
            view.Flash(DamageSource.Projectile, 0f);
            view.Step(true, Frame);
            view.Step(true, Frame);
            view.Step(true, Frame);
            Assert.IsTrue(view.Flashing, "still showing");
            Assert.AreEqual(1f, view.CurrentStrength, 1e-5f, "never a dimmed grey that hides the team's glow lines");
            var block = new MaterialPropertyBlock();
            parts[0].GetPropertyBlock(block);
            Assert.AreEqual(1.5f, block.GetColor(EmissionColorId).r, 1e-4f, "the white at its full Hit Flash Glow");
        }

        [Test]
        public void ARepeatedHitRefreshesTheFlash()
        {
            HitFlashView view = Make();
            view.Flash(DamageSource.Projectile, 0f);
            for (int i = 0; i < 4; i++) view.Step(true, Frame); // most of the way out
            view.Flash(DamageSource.Projectile, 4 * Frame);
            view.Step(true, Frame);
            Assert.AreEqual(1f, view.CurrentStrength, 1e-5f, "a second hit starts it over at full");
            for (int i = 0; i < 4; i++) view.Step(true, Frame);
            Assert.IsTrue(view.Flashing, "past the first hit's end, still flashing for the second");
        }

        [Test]
        public void BurnTicksEveryFrameFlickerInsteadOfStayingWhite()
        {
            HitFlashView view = Make();
            int whiteFrames = 0, flashes = 0, longestWhite = 0, shortestDark = int.MaxValue, run = 0, dark = -1;
            bool wasWhite = false;
            const int Frames = 120; // two seconds of a burn ticking every frame
            for (int i = 0; i < Frames; i++)
            {
                view.Flash(DamageSource.Burn, i * Frame);
                view.Step(true, Frame);
                if (view.Flashing)
                {
                    whiteFrames++;
                    run++;
                    if (!wasWhite)
                    {
                        flashes++;
                        if (dark >= 0) shortestDark = Mathf.Min(shortestDark, dark);
                    }
                    longestWhite = Mathf.Max(longestWhite, run);
                    dark = 0;
                }
                else
                {
                    run = 0;
                    if (dark >= 0) dark++;
                }
                wasWhite = view.Flashing;
            }
            Assert.That(flashes, Is.InRange(6, 7), "one flash per gap over two seconds, not one per tick");
            Assert.That(longestWhite, Is.LessThanOrEqualTo(7), "each flash lasts its own length, never stretched by the ticks under it");
            Assert.That(shortestDark, Is.GreaterThanOrEqualTo(10), "the mech's own look shows between flashes");
            Assert.That(whiteFrames, Is.LessThan(Frames / 2), "a burning mech is mostly its own colour");
        }

        [Test]
        public void AHitWhileBurningFlashesAtOnce()
        {
            HitFlashView view = Make();
            view.Flash(DamageSource.Burn, 0f);
            for (int i = 0; i < 9; i++) view.Step(true, Frame); // 0.15 s: that flash is over, the gap is not
            Assert.IsFalse(view.Flashing);
            view.Flash(DamageSource.Burn, 9 * Frame);
            Assert.IsFalse(view.enabled, "a tick inside the gap starts nothing");
            view.Flash(DamageSource.Projectile, 9 * Frame);
            view.Step(true, Frame);
            Assert.IsTrue(view.Flashing, "a bullet flashes whatever the burn is doing");
        }

        [Test]
        public void ADeadPlayerDoesNotFlash()
        {
            HitFlashView view = Make();
            view.Flash(DamageSource.Projectile, 0f);
            view.Step(false, Frame);
            Assert.IsFalse(view.Flashing);
            AssertIdle("dead on the frame of the hit");

            view.Flash(DamageSource.Projectile, 0f);
            view.Step(true, Frame);
            Assert.IsTrue(view.Flashing);
            view.Step(false, Frame);
            Assert.IsFalse(view.Flashing, "dying mid-flash ends it");
            AssertIdle("died mid-flash");
        }

        [Test]
        public void ATeamColourArrivingMidFlashIsTheOneWornAfter()
        {
            HitFlashView view = Make();
            var other = new Material(team) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                view.Flash(DamageSource.Projectile, 0f);
                view.Step(true, Frame);
                foreach (SkinnedMeshRenderer part in parts)
                    part.sharedMaterial = other;
                for (int i = 0; i < 10; i++) view.Step(true, Frame);
                foreach (SkinnedMeshRenderer part in parts)
                {
                    Assert.AreSame(other, part.sharedMaterial);
                    Assert.IsFalse(part.HasPropertyBlock());
                }
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void NoGarbageWhileFlashing()
        {
            HitFlashView view = Make();
            view.Flash(DamageSource.Projectile, 0f);
            view.Step(true, Frame); // warm up
            int allocations = AllocationProbe.Count(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    if (i % 4 == 0) view.Flash(DamageSource.Projectile, i * Frame);
                    if (i % 4 == 2) view.Flash(DamageSource.Burn, i * Frame);
                    view.Step(true, Frame);
                }
                for (int i = 0; i < 20; i++)
                    view.Step(true, Frame);
            });
            Assert.AreEqual(0, allocations, "no allocation per hit or per frame");
        }

        // ---------------------------------------------------------------- the game calls the rule

        private static MethodInfo Rule(string name) => typeof(HitFlashRule).GetMethod(name, Any);
        private static MethodInfo ViewMethod(string name) => typeof(HitFlashView).GetMethod(name, Any);

        [Test]
        public void TheDamageFunnelAsksTheRuleAndFlashesOnItsAnswer()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(PlayerHealth), "ApplyDamage", Rule(nameof(HitFlashRule.FlashesOnHit))),
                "PlayerHealth.ApplyDamage branches on HitFlashRule.FlashesOnHit");
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "ApplyDamage", ViewMethod(nameof(HitFlashView.Flash))),
                "and flashes the mech");
        }

        [Test]
        public void TheFlashIsDecidedOnEveryCopyBeforeOnlyTheVictimGoesOn()
        {
            MethodInfo applyDamage = typeof(PlayerHealth).GetMethod(nameof(PlayerHealth.ApplyDamage), Any);
            MethodInfo isMine = typeof(Photon.Pun.PhotonView).GetProperty("IsMine").GetGetMethod();
            int flash = IlWiring.CallOffsets(applyDamage, Rule(nameof(HitFlashRule.FlashesOnHit))).DefaultIfEmpty(int.MaxValue).Min();
            int victimOnly = IlWiring.CallOffsets(applyDamage, isMine).DefaultIfEmpty(-1).Max();
            Assert.That(flash, Is.LessThan(victimOnly), "the rule is asked before the 'only the victim's own client goes on' return");
        }

        [Test]
        public void EveryFlashAsksTheGapRuleAndStartsOnItsAnswer()
        {
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(HitFlashView), "Flash", Rule(nameof(HitFlashRule.StartsFlash))),
                "HitFlashView.Flash branches on HitFlashRule.StartsFlash");
        }

        [Test]
        public void EveryPlayerIsBuiltWithAFlash() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerHealth), "Start", ViewMethod(nameof(HitFlashView.TryCreate))));

        [Test]
        public void TheViewAsksTheRuleEachFrame()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(HitFlashView), "Step", Rule(nameof(HitFlashRule.Strength))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(HitFlashView), "Step", Rule(nameof(HitFlashRule.Shows))));
            Assert.IsTrue(IlWiring.Uses(typeof(HitFlashView), "LateUpdate", ViewMethod(nameof(HitFlashView.Step))));
        }
    }
}
