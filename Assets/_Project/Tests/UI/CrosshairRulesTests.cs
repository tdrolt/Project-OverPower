using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The crosshair's rules: how wide its hexagon opens for a spread, how fast it spins and how hot it looks, how full its
    /// charge hexagon is, when it replaces the arrow, and when the designer-only aim cone draws. Literal values only; no asset numbers.</summary>
    public class CrosshairRulesTests
    {
        private static void SameColour(Color expected, Color actual, string message = null)
        {
            Assert.AreEqual(0f, ((Vector4)expected - (Vector4)actual).magnitude, 1e-4f, message ?? $"expected {expected}, got {actual}");
        }

        [Test]
        public void ASingleShotGunOpensToHalfItsCone()
        {
            Assert.AreEqual(3f, CrosshairRules.OuterHalfAngle(simultaneous: false, spreadDegrees: 0f, effectiveConeAngle: 6f), 1e-5f);
        }

        [Test]
        public void AShotgunOpensToItsFanPlusItsCone()
        {
            // Every pellet is jittered by the cone on top of its place in the fan, so the farthest one lands at fan half + cone half.
            Assert.AreEqual(14f + 2f, CrosshairRules.OuterHalfAngle(simultaneous: true, spreadDegrees: 28f, effectiveConeAngle: 4f), 1e-5f);
        }

        [Test]
        public void ABurstWithAFanSettingButNotSimultaneousIgnoresTheFan()
        {
            Assert.AreEqual(2f, CrosshairRules.OuterHalfAngle(simultaneous: false, spreadDegrees: 28f, effectiveConeAngle: 4f), 1e-5f);
        }

        [Test]
        public void TheReticleSitsAtRestWithNoSpreadAndOpensPerDegree()
        {
            Assert.AreEqual(20f, CrosshairRules.ReticleRadius(0f, restRadius: 20f, unitsPerDegree: 5f, maxRadius: 100f), 1e-5f);
            Assert.AreEqual(35f, CrosshairRules.ReticleRadius(3f, restRadius: 20f, unitsPerDegree: 5f, maxRadius: 100f), 1e-5f);
        }

        [Test]
        public void TheReticleNeverOpensPastItsMaximumNorClosesInsideItsRest()
        {
            Assert.AreEqual(100f, CrosshairRules.ReticleRadius(40f, restRadius: 20f, unitsPerDegree: 5f, maxRadius: 100f), 1e-5f);
            Assert.AreEqual(20f, CrosshairRules.ReticleRadius(-3f, restRadius: 20f, unitsPerDegree: 5f, maxRadius: 100f), 1e-5f);
            Assert.AreEqual(20f, CrosshairRules.ReticleRadius(10f, restRadius: 20f, unitsPerDegree: 5f, maxRadius: 10f), 1e-5f,
                "a maximum set below the rest radius must not pull the hexagon into the centre");
        }

        [Test]
        public void ABloomedGunOpensWiderThanARecoveredOne()
        {
            float recovered = CrosshairRules.ReticleRadius(CrosshairRules.OuterHalfAngle(false, 0f, 1.5f), 20f, 5f, 120f);
            float bloomed = CrosshairRules.ReticleRadius(CrosshairRules.OuterHalfAngle(false, 0f, 9f), 20f, 5f, 120f);
            Assert.Greater(bloomed, recovered);
        }

        [Test]
        public void TheChargeHexShowsOnlyWhileAChargeIsHeld()
        {
            Assert.IsTrue(CrosshairRules.IsCharging(chargeHeld: true, maxChargeSeconds: 1f));
            Assert.IsFalse(CrosshairRules.IsCharging(chargeHeld: false, maxChargeSeconds: 1f));
            Assert.IsFalse(CrosshairRules.IsCharging(chargeHeld: true, maxChargeSeconds: 0f), "a charge that takes no time has nothing to fill");
        }

        [Test]
        public void TheChargeHexFillsWithTheChargeAndIsEmptyWhenNotCharging()
        {
            Assert.AreEqual(0.4f, CrosshairRules.ChargeFill(charging: true, chargeFraction: 0.4f), 1e-5f);
            Assert.AreEqual(1f, CrosshairRules.ChargeFill(charging: true, chargeFraction: 1.7f), 1e-5f);
            Assert.AreEqual(0f, CrosshairRules.ChargeFill(charging: true, chargeFraction: -0.2f), 1e-5f);
            Assert.AreEqual(0f, CrosshairRules.ChargeFill(charging: false, chargeFraction: 0.8f), 1e-5f, "a leftover fraction must not show without a held charge");
        }

        [Test]
        public void TheCrosshairReplacesTheArrowOnlyWhereFireIsNotBlocked()
        {
            Assert.IsTrue(CrosshairRules.ShowCrosshair(fireBlocked: false, hasWeapon: true));
            Assert.IsFalse(CrosshairRules.ShowCrosshair(fireBlocked: true, hasWeapon: true), "over the shop or a menu the arrow comes back");
            Assert.IsFalse(CrosshairRules.ShowCrosshair(fireBlocked: false, hasWeapon: false), "no gun, nothing to aim");
        }

        [Test]
        public void TheAimConeDrawsOnlyWhenTheDesignerSwitchIsOn()
        {
            Assert.IsFalse(CrosshairRules.DrawAimCone(designerSwitch: false, alive: true, inputSuppressed: false, loadoutOpen: false, hasWeapon: true));
            Assert.IsTrue(CrosshairRules.DrawAimCone(designerSwitch: true, alive: true, inputSuppressed: false, loadoutOpen: false, hasWeapon: true));
        }

        [Test]
        public void TheAimConeKeepsItsOldHideConditions()
        {
            Assert.IsFalse(CrosshairRules.DrawAimCone(true, alive: false, inputSuppressed: false, loadoutOpen: false, hasWeapon: true));
            Assert.IsFalse(CrosshairRules.DrawAimCone(true, alive: true, inputSuppressed: true, loadoutOpen: false, hasWeapon: true));
            Assert.IsFalse(CrosshairRules.DrawAimCone(true, alive: true, inputSuppressed: false, loadoutOpen: true, hasWeapon: true));
            Assert.IsFalse(CrosshairRules.DrawAimCone(true, alive: true, inputSuppressed: false, loadoutOpen: false, hasWeapon: false));
        }

        [Test]
        public void TheRadiusEasesTowardItsTargetAndSnapsWithNoSmoothing()
        {
            Assert.AreEqual(40f, CrosshairRules.Approach(20f, 40f, deltaTime: 0.016f, smoothSeconds: 0f), 1e-5f);
            float eased = CrosshairRules.Approach(20f, 40f, deltaTime: 0.016f, smoothSeconds: 0.05f);
            Assert.Greater(eased, 20f);
            Assert.Less(eased, 40f);
            Assert.AreEqual(40f, CrosshairRules.Approach(20f, 40f, deltaTime: 10f, smoothSeconds: 0.05f), 1e-3f);
        }

        [Test]
        public void FireIsBlockedWhileInputIsSuppressedOrThePointerIsOverClickableUi()
        {
            Assert.IsFalse(PlayerInputRouter.FireBlocked(inputSuppressed: false, pointerOverUi: false));
            Assert.IsTrue(PlayerInputRouter.FireBlocked(inputSuppressed: true, pointerOverUi: false));
            Assert.IsTrue(PlayerInputRouter.FireBlocked(inputSuppressed: false, pointerOverUi: true));
        }

        [Test]
        public void TheHexRingSpriteIsARingNotADisc()
        {
            const int size = 128;
            const float band = 10f;
            Assert.AreEqual(0f, GeneratedSprites.HexRingAlpha(size / 2f, size / 2f, size, band), 1e-5f, "the centre is hollow");
            Assert.AreEqual(0f, GeneratedSprites.HexRingAlpha(0.5f, 0.5f, size, band), 1e-5f, "the sprite's corner is outside the hexagon");
            // Pointy-top: the top corner sits near the sprite's top edge, so a point just under it is on the line.
            Assert.AreEqual(1f, GeneratedSprites.HexRingAlpha(size / 2f, size - 6f, size, band), 1e-5f);
            // Its flat sides face left and right, inside the sprite's edges by the apothem.
            Assert.AreEqual(0f, GeneratedSprites.HexRingAlpha(3f, size / 2f, size, band), 1e-5f);
        }

        [Test]
        public void EachShotRaisesTheShotRateAndItFallsBackOnceFiringStops()
        {
            float afterShot = CrosshairRules.ShotRate(0f, shotThisFrame: true, deltaTime: 0.016f, windowSeconds: 0.5f);
            Assert.AreEqual(2f, afterShot, 1e-4f, "one shot adds one shot per window");
            float later = CrosshairRules.ShotRate(afterShot, shotThisFrame: false, deltaTime: 0.5f, windowSeconds: 0.5f);
            Assert.AreEqual(afterShot * Mathf.Exp(-1f), later, 1e-4f);
            Assert.AreEqual(0f, CrosshairRules.ShotRate(0f, shotThisFrame: false, deltaTime: 0.016f, windowSeconds: 0.5f), 1e-6f);
        }

        [Test]
        public void ASteadyGunsShotRateReadsItsShotsPerSecond()
        {
            // Ten shots a second at 100 frames a second; once settled, the rate averages ten.
            float rate = 0f, sum = 0f;
            for (int frame = 0; frame < 300; frame++)
            {
                rate = CrosshairRules.ShotRate(rate, frame % 10 == 0, 0.01f, 0.5f);
                if (frame >= 200)
                    sum += rate;
            }
            Assert.AreEqual(10f, sum / 100f, 0.5f);
        }

        [Test]
        public void TheHexagonSpinsSlowlyAtRestAndFasterWithEveryShotPerSecond()
        {
            Assert.AreEqual(30f, CrosshairRules.SpinDegreesPerSecond(0f, idleDegreesPerSecond: 30f, degreesPerShot: 40f, maxDegreesPerSecond: 600f), 1e-5f);
            Assert.AreEqual(30f + 5f * 40f, CrosshairRules.SpinDegreesPerSecond(5f, 30f, 40f, 600f), 1e-5f);
            Assert.AreEqual(600f, CrosshairRules.SpinDegreesPerSecond(50f, 30f, 40f, 600f), 1e-5f);
            Assert.AreEqual(30f, CrosshairRules.SpinDegreesPerSecond(-2f, 30f, 40f, 600f), 1e-5f, "never slower than at rest");
            Assert.AreEqual(30f, CrosshairRules.SpinDegreesPerSecond(5f, 30f, 40f, 10f), 1e-5f, "a maximum below the rest speed keeps the rest speed");
        }

        [Test]
        public void TheSpinTurnsClockwiseAndWrapsEverySixthOfATurn()
        {
            // A hexagon looks the same every 60 degrees, so the angle stays small however long the match runs.
            Assert.AreEqual(5f, CrosshairRules.SpinAngle(10f, degreesPerSecond: 100f, deltaTime: 0.05f), 1e-4f);
            Assert.AreEqual(57f, CrosshairRules.SpinAngle(2f, degreesPerSecond: 100f, deltaTime: 0.05f), 1e-4f);
            float a = CrosshairRules.SpinAngle(0f, 7000f, 1f);
            Assert.GreaterOrEqual(a, 0f);
            Assert.Less(a, 60f);
        }

        [Test]
        public void TheHexagonRunsWhiteYellowOrangeRedWithTheHeat()
        {
            Color white = Color.white, yellow = Color.yellow, orange = new Color(1f, 0.5f, 0f), red = Color.red;
            SameColour(white, CrosshairRules.HeatColor(0f, white, yellow, orange, red, fullAt: 0.9f));
            SameColour(yellow, CrosshairRules.HeatColor(0.3f, white, yellow, orange, red, 0.9f));
            SameColour(orange, CrosshairRules.HeatColor(0.6f, white, yellow, orange, red, 0.9f));
            SameColour(red, CrosshairRules.HeatColor(0.9f, white, yellow, orange, red, 0.9f));
            SameColour(red, CrosshairRules.HeatColor(1f, white, yellow, orange, red, 0.9f));
            Color between = CrosshairRules.HeatColor(0.15f, white, yellow, orange, red, 0.9f);
            Assert.AreEqual(Color.Lerp(white, yellow, 0.5f).b, between.b, 1e-4f);
            SameColour(red, CrosshairRules.HeatColor(0.01f, white, yellow, orange, red, 0f), "with no heat range set, any heat at all reads full");
            SameColour(white, CrosshairRules.HeatColor(0f, white, yellow, orange, red, 0f));
        }

        [Test]
        public void AnOverheatedGunShowsFullHeatUntilItCanFireAgain()
        {
            Assert.AreEqual(0.4f, CrosshairRules.HeatShown(normalisedHeat: 0.4f, silenced: false), 1e-5f);
            Assert.AreEqual(1f, CrosshairRules.HeatShown(normalisedHeat: 0.4f, silenced: true), 1e-5f, "cooling while locked out still reads red");
            Assert.AreEqual(0f, CrosshairRules.HeatShown(-0.1f, false), 1e-5f);
        }

        [Test]
        public void TheSpreadHexagonsSidesSitAtItsRadiusWhateverItsSize()
        {
            foreach (float apothem in new[] { 20f, 90f })
            {
                Vector2 top = HexRingGraphic.Corner(0, apothem);
                Assert.AreEqual(0f, top.x, 1e-4f, "pointy top");
                Assert.Greater(top.y, apothem);
                Vector2 midSide = (HexRingGraphic.Corner(0, apothem) + HexRingGraphic.Corner(1, apothem)) / 2f;
                Assert.AreEqual(apothem, midSide.magnitude, 1e-3f, "the inside of each side is exactly the spread radius from the centre");
                Assert.AreEqual(HexRingGraphic.Corner(0, apothem).magnitude, HexRingGraphic.Corner(3, apothem).magnitude, 1e-3f);
            }
        }

        [Test]
        public void TheRingTurnsTheVentColourWhileRWouldVent()
        {
            Color heat = new Color(1f, 0.1f, 0.1f, 1f), vent = new Color(0.2f, 1f, 0.3f, 1f);
            SameColour(vent, CrosshairRules.RingColour(heat, true, vent), "the vent window is open");
            SameColour(heat, CrosshairRules.RingColour(heat, false, vent), "any other time the heat colour");
        }
    }
}
