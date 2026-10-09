namespace Overpower.Telemetry
{
    /// <summary>
    /// The admission rule Ctrl+B goes through (BugMarkerKey is the only caller): a mark is refused while the reporter is typing in
    /// chat, or within cooldownSeconds of the last one this client accepted, so holding the key cannot spam a screenshot every frame.
    /// The quit pop-up's gate is added at BugMarkerKey's call site once QuitConfirmPanel exists.
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
