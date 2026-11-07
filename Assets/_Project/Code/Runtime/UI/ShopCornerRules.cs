namespace Overpower.UI
{
    /// <summary>The block in the bottom-right corner: the shop button, and above it the gold readout where the mode has gold, on one panel.</summary>
    public static class ShopCornerRules
    {
        /// <summary>The panel's height: the button, the gold readout and its gap when the mode shows gold, and the padding above and below.</summary>
        public static float PanelHeight(bool showsGold, float buttonHeight, float goldGap, float goldHeight, float padding) =>
            2f * padding + buttonHeight + (showsGold ? goldGap + goldHeight : 0f);
    }
}
