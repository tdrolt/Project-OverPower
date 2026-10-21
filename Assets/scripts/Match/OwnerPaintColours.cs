using Overpower.UI;
using UnityEngine;

namespace Overpower.Match
{
    /// <summary>Turns an OwnerPaint into the colour to draw. One home for "which theme colour, and how it pulses",
    /// shared by the capture ring's edge (neutral = the ring's dim white) and a tower's crown and caps (neutral =
    /// Tower Neutral Colour), so the two can never drift apart.</summary>
    public static class OwnerPaintColours
    {
        public static Color For(OwnerPaint paint, UiTheme theme, Color neutral, float timeSeconds)
        {
            Color colour = paint.Base == OwnerPaintBase.OutOfPlay ? theme.outOfPlayZoneColor
                         : paint.Base == OwnerPaintBase.Team ? theme.ShotColorFor(paint.Team) : neutral;
            if (paint.PulseTeam >= 0)
                return Pulse(colour, theme.ShotColorFor(paint.PulseTeam), CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed),
                             theme.captureRingPulseDip);
            if (paint.PulsesToWarning)
                return Pulse(colour, theme.captureRingWarningColor, CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed),
                             theme.captureRingPulseDip);
            return colour;
        }

        /// <summary>From one colour to the other: the first dims to <paramref name="dip"/> of its brightness, the second comes up from there.
        /// Never a mix of the two (purple and red mix to pink, white and red to salmon); opacity moves straight across.</summary>
        public static Color Pulse(Color from, Color to, float pulse01, float dip)
        {
            Color shown = pulse01 < 0.5f ? from * Mathf.Lerp(1f, dip, pulse01 * 2f) : to * Mathf.Lerp(dip, 1f, pulse01 * 2f - 1f);
            shown.a = Mathf.Lerp(from.a, to.a, pulse01);
            return shown;
        }
    }
}
