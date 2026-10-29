using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The crosshair's decisions (CrosshairView) and the designer-only aim cone's (AimConeView). OuterHalfAngle is shared by
    /// both, so the spread hexagon and the cone lines can never disagree about how far a shot strays. Sizes are in canvas units.
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

        /// <summary>Distance from the crosshair's centre to the inside of the spread hexagon's sides: the rest gap plus a fixed size per degree of stray,
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

        /// <summary>Shots per second over roughly the last window: every shot adds one window's worth and the total fades with the
        /// window as its time constant, so a steady gun reads its own rate and the number eases down once the trigger is let go.</summary>
        public static float ShotRate(float rate, bool shotThisFrame, float deltaTime, float windowSeconds)
        {
            float window = Mathf.Max(0.01f, windowSeconds);
            return rate * Mathf.Exp(-Mathf.Max(0f, deltaTime) / window) + (shotThisFrame ? 1f / window : 0f);
        }

        /// <summary>The resting spin plus a fixed amount for every shot per second, never below the resting spin nor above the maximum
        /// (a maximum below the rest gives the rest).</summary>
        public static float SpinDegreesPerSecond(float shotsPerSecond, float idleDegreesPerSecond, float degreesPerShot, float maxDegreesPerSecond)
        {
            float speed = idleDegreesPerSecond + Mathf.Max(0f, shotsPerSecond) * degreesPerShot;
            return Mathf.Clamp(speed, idleDegreesPerSecond, Mathf.Max(idleDegreesPerSecond, maxDegreesPerSecond));
        }

        /// <summary>Turns clockwise (the angle falls), kept within one sixth of a turn: a hexagon looks the same every 60 degrees.</summary>
        public static float SpinAngle(float angle, float degreesPerSecond, float deltaTime) =>
            Mathf.Repeat(angle - degreesPerSecond * deltaTime, 60f);

        /// <summary>The heat the hexagon shows, 0 to 1: the meter itself, or full while overheated, because the gun stays locked
        /// until the meter has cooled all the way down.</summary>
        public static float HeatShown(float normalisedHeat, bool silenced) => silenced ? 1f : Mathf.Clamp01(normalisedHeat);

        /// <summary>While the vent window is open (R would vent now) the ring takes the vent colour instead of the heat's.</summary>
        public static Color RingColour(Color heatColour, bool ventWindowOpen, Color ventColour) => ventWindowOpen ? ventColour : heatColour;

        /// <summary>Cold, warm, hot, full in equal steps from no heat to <paramref name="fullAt"/>, full beyond it.</summary>
        public static Color HeatColor(float heat, Color cold, Color warm, Color hot, Color full, float fullAt)
        {
            float t = fullAt > 0f ? Mathf.Clamp01(heat / fullAt) : heat > 0f ? 1f : 0f;
            float scaled = t * 3f;
            if (scaled <= 1f)
                return Color.Lerp(cold, warm, scaled);
            return scaled <= 2f ? Color.Lerp(warm, hot, scaled - 1f) : Color.Lerp(hot, full, scaled - 2f);
        }
    }
}
