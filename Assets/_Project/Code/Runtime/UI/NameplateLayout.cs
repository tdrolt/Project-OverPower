using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where everything over a player's head goes, in overhead-canvas units (y up, x centred on the player): the plate with the
    /// name above the health-and-armour bar, and above the plate the STUNNED / SLOWED countdown bar, its label and the shield's
    /// "Wasted" text. Pure: NameplateView, StatusLabelOverhead and the shield all place their pieces from one Frame.
    /// </summary>
    public static class NameplateLayout
    {
        /// <summary>The theme's numbers, gathered so the rule is testable with literals.</summary>
        public struct Settings
        {
            public float nameSize;
            public Vector2 barSize;
            public Vector2 padding;
            public float rowGap;
            public float maxWidth;
            public float bottomY;
            public float edge;
            public float statusTextSize;
            public float statusGap;
            public Vector2 statusBarSize;
            public float statusLabelGap;
            public float wastedTextSize;
        }

        /// <summary>Rects in overhead-canvas units (Rect.center is the anchored position of a centre-pivoted child).</summary>
        public struct Frame
        {
            public Rect plate;
            public Rect name;
            public Rect bar;
            public Rect statusBar;
            public Rect statusLabel;
            public Rect wasted;
            public float edge;
        }

        public static Settings From(UiTheme theme) => new Settings
        {
            nameSize = theme.nameplateNameSize,
            barSize = theme.nameplateBarSize,
            padding = theme.nameplatePadding,
            rowGap = theme.nameplateRowGap,
            maxWidth = theme.nameplateMaxWidth,
            bottomY = theme.nameplateBottomY,
            edge = theme.nameplateEdgeThickness,
            statusTextSize = theme.nameplateStatusTextSize,
            statusGap = theme.nameplateStatusGap,
            statusBarSize = theme.nameplateStatusBarSize,
            statusLabelGap = theme.nameplateStatusLabelGap,
            wastedTextSize = theme.shieldWastedTextSize,
        };

        /// <summary>The plate is as wide as the longer of its bar and the name (plus padding), up to the widest allowed, and never
        /// narrower than its bar. Its top is fixed by a living player's full height, so a corpse's plate (barShown false) loses the
        /// bar row from the bottom and the name and everything above it stay put.</summary>
        public static Frame Compute(Settings s, float nameWidth, bool barShown)
        {
            float barOnly = s.barSize.x + 2f * s.padding.x;
            float width = Mathf.Max(barOnly, Mathf.Min(Mathf.Max(s.barSize.x, nameWidth) + 2f * s.padding.x, s.maxWidth));

            float top = s.bottomY + s.padding.y + s.barSize.y + s.rowGap + s.nameSize + s.padding.y;
            float nameBottom = top - s.padding.y - s.nameSize;
            float bottom = barShown ? s.bottomY : nameBottom - s.padding.y;

            float statusBarBottom = top + s.statusGap;
            float labelBottom = statusBarBottom + s.statusBarSize.y + s.statusLabelGap;
            float wastedBottom = labelBottom + s.statusTextSize + s.statusGap;

            return new Frame
            {
                plate = Centred(width, bottom, top - bottom),
                name = Centred(width - 2f * s.padding.x, nameBottom, s.nameSize),
                bar = Centred(s.barSize.x, s.bottomY + s.padding.y, s.barSize.y),
                statusBar = Centred(s.statusBarSize.x, statusBarBottom, s.statusBarSize.y),
                statusLabel = Centred(s.statusBarSize.x, labelBottom, s.statusTextSize),
                wasted = Centred(s.statusBarSize.x, wastedBottom, s.wastedTextSize),
                edge = s.edge,
            };
        }

        private static Rect Centred(float width, float bottom, float height) => new Rect(-width * 0.5f, bottom, width, height);
    }
}
