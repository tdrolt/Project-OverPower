namespace Overpower.UI
{
    /// <summary>
    /// The Abilities &amp; Armor page's one start line: the ability columns (two cards across each) and the armour block above them are
    /// the same width and both centred on the page, so the armour paths begin where the first ability card begins. Canvas units.
    /// </summary>
    public static class ShopPageColumns
    {
        /// <summary>One ability column: two cards side by side with the card spacing between them.</summary>
        public static float ColumnWidth(float cardWidth, float cardSpacing) => 2f * cardWidth + cardSpacing;

        /// <summary>All the ability columns with the column gap between neighbours; the armour block is given this width too.</summary>
        public static float Width(int columns, float cardWidth, float cardSpacing, float columnGap) =>
            columns <= 0 ? 0f : columns * ColumnWidth(cardWidth, cardSpacing) + (columns - 1) * columnGap;
    }
}
