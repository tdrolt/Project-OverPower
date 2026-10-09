namespace Overpower.UI
{
    /// <summary>
    /// The pure rule behind QuitConfirmPanel's Escape handling. Component Update() order is not fixed, and both
    /// LoadoutScreen and the chat panel (chatmanager.cs's Escape block) close THEMSELVES on Escape in the SAME frame
    /// the key is pressed. Checking only "open this frame" misses ownership whenever the other component's Update()
    /// ran first and already closed itself; checking one frame back too catches it from either side of that race.
    /// </summary>
    public static class EscapeOwnershipRule
    {
        /// <summary>True if this Escape press belongs to the shop, the chat or an open How to play / mode info page
        /// (a page closes itself on Escape), so QuitConfirmPanel must do nothing: it was open THIS frame (about to
        /// consume the key) or LAST frame (it just closed itself on this key press, before QuitConfirmPanel's Update).</summary>
        public static bool BelongsToShopOrChat(bool shopOpenThisFrame, bool shopOpenLastFrame,
                                                bool chatOpenThisFrame, bool chatOpenLastFrame,
                                                bool pageOpenThisFrame = false, bool pageOpenLastFrame = false) =>
            shopOpenThisFrame || shopOpenLastFrame || chatOpenThisFrame || chatOpenLastFrame || pageOpenThisFrame || pageOpenLastFrame;
    }
}
