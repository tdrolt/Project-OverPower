using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The crosshair's rules: how wide it opens for a spread, how full its charge hexagon is, when it replaces the arrow,
    /// and when the designer-only aim cone draws. Literal values only; no asset numbers.</summary>
    public class CrosshairRulesTests
    {
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
                "a maximum set below the rest radius must not pull the ticks into the centre");
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
    }
}
