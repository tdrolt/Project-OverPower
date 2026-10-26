using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The crosshair's decisions (CrosshairView) and the designer-only aim cone's (AimConeView). OuterHalfAngle is shared by
    /// both, so the reticle and the cone lines can never disagree about how far a shot strays. Sizes are in canvas units.
    /// </summary>
    public static class CrosshairRules
    {
        /// <summary>The farthest a shot can stray from the aim, in degrees. A shotgun's fixed fan is centred on the aim and every pellet
        /// is jittered by the cone on top (WeaponFiring.BuildShots), so its edge is fan half + cone half.</summary>
        public static float OuterHalfAngle(bool simultaneous, float spreadDegrees, float effectiveConeAngle)
        {
            float coneHalf = effectiveConeAngle / 2f;
            return simultaneous && spreadDegrees > 0f ? spreadDegrees / 2f + coneHalf : coneHalf;
        }

        /// <summary>Distance from the crosshair's centre to the inner end of each tick: the rest gap plus a fixed size per degree of stray,
        /// never inside the rest gap and never past the maximum (a maximum below the rest gap gives the rest gap).</summary>
        public static float ReticleRadius(float outerHalfAngle, float restRadius, float unitsPerDegree, float maxRadius)
        {
            float radius = restRadius + Mathf.Max(0f, outerHalfAngle) * unitsPerDegree;
            return Mathf.Clamp(radius, restRadius, Mathf.Max(restRadius, maxRadius));
        }

        public static bool IsCharging(bool chargeHeld, float maxChargeSeconds) => chargeHeld && maxChargeSeconds > 0f;

        /// <summary>0 to 1; empty when not charging, so a leftover fraction from a released trigger never shows.</summary>
        public static float ChargeFill(bool charging, float chargeFraction) => charging ? Mathf.Clamp01(chargeFraction) : 0f;

        /// <summary>The crosshair takes the arrow's place only where a click would fire (PlayerInputRouter.PointerFireBlocked is false):
        /// over the shop, a menu, the chat or while dead the arrow comes back.</summary>
        public static bool ShowCrosshair(bool fireBlocked, bool hasWeapon) => !fireBlocked && hasWeapon;

        public static bool DrawAimCone(bool designerSwitch, bool alive, bool inputSuppressed, bool loadoutOpen, bool hasWeapon) =>
            designerSwitch && alive && !inputSuppressed && !loadoutOpen && hasWeapon;

        /// <summary>Eases toward the target, frame-rate independent; 0 seconds snaps.</summary>
        public static float Approach(float current, float target, float deltaTime, float smoothSeconds)
        {
            if (smoothSeconds <= 0f)
                return target;
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-deltaTime / smoothSeconds));
        }
    }
}
