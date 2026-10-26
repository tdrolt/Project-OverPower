namespace Overpower.UI
{
    /// <summary>
    /// Where the shop's two armour paths and their steps sit: the paths stacked one above the other (Absorb first), each a row of the same
    /// height, and a path's steps spread evenly along its track. Distances in canvas units, measured down from the section's top.
    /// </summary>
    public static class ArmorPathLayout
    {
        /// <summary>Height of the whole section holding the two paths.</summary>
        public static float SectionHeight(float rowHeight, float rowGap) => RowTop(1, rowHeight, rowGap) + rowHeight;

        /// <summary>How far below the section's top the path at this index starts (0 = the upper path).</summary>
        public static float RowTop(int index, float rowHeight, float rowGap) => index * (rowHeight + rowGap);

        /// <summary>Centre of step index (of count) along a track from left to right, spaced evenly and centred in the track.</summary>
        public static float StepCentreX(int index, int count, float trackLeft, float trackRight, float spacing) =>
            (trackLeft + trackRight) * 0.5f + (index - (count - 1) * 0.5f) * spacing;
    }
}
