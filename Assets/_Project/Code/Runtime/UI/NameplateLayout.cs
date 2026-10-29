using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// Where everything over a player's head goes, in overhead-canvas units (y up, x centred on the player): the plate with the
    /// name above the health-and-armour bar (your own plate: no name, the shield bar above a separate HP bar), and above the plate
    /// the STUNNED / SLOWED countdown bar, its label and the shield's "Wasted" text. Pure: NameplateView, StatusLabelOverhead and
    /// the shield all place their pieces from one Frame.
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
            public float markSize;
            public float ownRowGap;
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
            /// <summary>Your own plate's two rows inside the bar (shield on top); on anyone else's plate both are the bar itself.</summary>
            public Rect shieldRow;
            public Rect healthRow;
            /// <summary>The armour-upgrade mark's room: centred on the shield; empty with no bar.</summary>
            public Rect marks;
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
            markSize = theme.nameplateArmourMarkSize,
            ownRowGap = theme.nameplateOwnRowGap,
        };

        /// <summary>The plate is as wide as the longer of its bar and the name (plus padding), up to the widest allowed, and never
        /// narrower than its bar. Its top is fixed by a living player's full height, so a corpse's plate (barShown false) loses the
        /// bar row from the bottom and the name and everything above it stay put. Your own plate (ownPlate) has no name: under the
        /// same top it holds the shield row, then the HP row, and a corpse's own plate is empty. The armour mark is centred on the
        /// shield row (the whole bar on anyone else's plate).</summary>
        public static Frame Compute(Settings s, float nameWidth, bool barShown, bool ownPlate = false)
        {
            float barOnly = s.barSize.x + 2f * s.padding.x;
            float top = s.bottomY + s.padding.y + s.barSize.y + s.rowGap + s.nameSize + s.padding.y;

            float statusBarBottom = top + s.statusGap;
            float labelBottom = statusBarBottom + s.statusBarSize.y + s.statusLabelGap;
            float wastedBottom = labelBottom + s.statusTextSize + s.statusGap;

            var f = new Frame
            {
                statusBar = Centred(s.statusBarSize.x, statusBarBottom, s.statusBarSize.y),
                statusLabel = Centred(s.statusBarSize.x, labelBottom, s.statusTextSize),
                wasted = Centred(s.statusBarSize.x, wastedBottom, s.wastedTextSize),
                edge = s.edge,
            };

            if (ownPlate)
            {
                float shieldBottom = top - s.padding.y - s.barSize.y;
                float healthBottom = shieldBottom - s.ownRowGap - s.barSize.y;
                float bottom = barShown ? healthBottom - s.padding.y : top;
                f.plate = Centred(barOnly, bottom, top - bottom);
                f.name = Centred(0f, top, 0f);
                f.shieldRow = Centred(s.barSize.x, shieldBottom, s.barSize.y);
                f.healthRow = Centred(s.barSize.x, healthBottom, s.barSize.y);
                f.bar = Centred(s.barSize.x, healthBottom, 2f * s.barSize.y + s.ownRowGap);
            }
            else
            {
                float width = Mathf.Max(barOnly, Mathf.Min(Mathf.Max(s.barSize.x, nameWidth) + 2f * s.padding.x, s.maxWidth));
                float nameBottom = top - s.padding.y - s.nameSize;
                float bottom = barShown ? s.bottomY : nameBottom - s.padding.y;
                f.plate = Centred(width, bottom, top - bottom);
                f.name = Centred(width - 2f * s.padding.x, nameBottom, s.nameSize);
                f.bar = Centred(s.barSize.x, s.bottomY + s.padding.y, s.barSize.y);
                f.shieldRow = f.bar;
                f.healthRow = f.bar;
            }

            if (barShown)
                f.marks = new Rect(f.shieldRow.center.x - s.markSize * 0.5f, f.shieldRow.center.y - s.markSize * 0.5f, s.markSize, s.markSize);
            return f;
        }

        private static Rect Centred(float width, float bottom, float height) => new Rect(-width * 0.5f, bottom, width, height);
    }
}
