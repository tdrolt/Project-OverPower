namespace Overpower.UI
{
    /// <summary>
    /// Playtest extras P6 (2026-09-26): the pure rule behind QuitConfirmPanel's Escape handling.
    /// Component Update() order is not fixed in Unity, and both LoadoutScreen (LoadoutScreen.cs:390-
    /// 394) and the chat panel (chatmanager.cs's Escape block) close THEMSELVES on Escape, in the
    /// SAME frame the key is pressed. Checking only "is the shop/chat open right now, this frame"
    /// would miss ownership on whichever frame the other component's Update() happened to run FIRST
    /// (it would already have closed itself before QuitConfirmPanel gets to look). Checking one
    /// frame back too always catches it, from whichever side of that ordering race actually happened.
    /// </summary>
    public static class EscapeOwnershipRule
    {
        /// <summary>True if this Escape press belongs to the shop or chat (so QuitConfirmPanel must
        /// do nothing) - either one was open THIS frame (about to consume/already consuming the key
        /// itself) or LAST frame (it just closed itself on this exact key press, and QuitConfirmPanel's
        /// own Update() happened to run after it).</summary>
        public static bool BelongsToShopOrChat(bool shopOpenThisFrame, bool shopOpenLastFrame,
                                                bool chatOpenThisFrame, bool chatOpenLastFrame) =>
            shopOpenThisFrame || shopOpenLastFrame || chatOpenThisFrame || chatOpenLastFrame;
    }
}
