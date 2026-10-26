using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The thin lines that cut a health or armour bar into equal steps of hit points (UiTheme.barSegmentSize), and the
    /// "current/max" number on your own HUD's bars. Pure: BarDividers draws the lines, PlayerHud writes the numbers.
    /// </summary>
    public static class BarSegments
    {
        // Damage and recharge leave float crumbs (35.0004): without this slack a crumb would read as one more hit point.
        private const float Crumb = 0.01f;

        /// <summary>Writes where each line falls, as fractions of the bar's length, into `into` and returns how many: one every
        /// segmentSize hit points, never on the bar's end, never more than `into` holds. No lines for an empty bar or a size of 0.</summary>
        public static int Dividers(float maxValue, float segmentSize, float[] into)
        {
            if (maxValue <= 0f || segmentSize <= 0f || into == null)
                return 0;
            int count = 0;
            while (count < into.Length && (count + 1) * segmentSize < maxValue - Crumb)
            {
                into[count] = (count + 1) * segmentSize / maxValue;
                count++;
            }
            return count;
        }

        /// <summary>A line drawn on a fill shows only where that fill reaches (the plate's armour lines, on a shield that is part empty).</summary>
        public static bool OnFill(float position, float fill) => position < fill;

        /// <summary>Whole hit points as shown: a part point counts as a whole one, so a living player never reads 0.</summary>
        public static int Shown(float value) => Mathf.Max(0, Mathf.CeilToInt(value - Crumb));

        public static string Number(float current, float max)
        {
            int shownMax = Shown(max);
            return Mathf.Min(Shown(current), shownMax) + "/" + shownMax;
        }
    }
}
