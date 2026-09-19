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
                return Color.Lerp(colour, theme.ShotColorFor(paint.PulseTeam),
                                  CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed));
            if (paint.PulsesToWarning)
                return Color.Lerp(colour, theme.captureRingWarningColor,
                                  CaptureRingGeometry.Pulse01(timeSeconds, theme.captureRingPulseSpeed));
            return colour;
        }
    }
}
