namespace Overpower.Telemetry
{
    /// <summary>
    /// Playtest extras P2 (2026-09-26): the pure admission rule Ctrl+B goes through - BugMarkerKey is
    /// the only caller. A mark is refused while the reporter is typing in chat, or within
    /// cooldownSeconds of the last one this client accepted (so holding the key down cannot spam a
    /// screenshot every frame). The quit pop-up's own gate (P6 - not part of this task, since
    /// QuitConfirmPanel does not exist yet) is added at BugMarkerKey's own call site once that panel
    /// exists, the same way LoadoutScreen's Escape gate grew a second condition without this rule
    /// needing to know about it.
    /// </summary>
    public static class BugMarkRule
    {
        /// <param name="lastMarkTime">The match-clock time of the last ACCEPTED mark, or a negative
        /// number if none has happened yet this match.</param>
        public static bool CanMark(bool isTypingInChat, double now, double lastMarkTime, double cooldownSeconds)
        {
            if (isTypingInChat)
                return false;

            return lastMarkTime < 0 || now - lastMarkTime >= cooldownSeconds;
        }
    }
}
