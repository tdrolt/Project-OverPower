namespace Overpower.Match
{
    /// <summary>What the warm-up bar shows for one message: whether End warm-up is there and pressable, and whether the line naming the team that
    /// has nobody is there.</summary>
    public readonly struct WarmupBarView
    {
        public readonly bool ButtonShown;
        public readonly bool ButtonEnabled;
        public readonly bool ReasonShown;
        public readonly bool Countdown;

        public WarmupBarView(bool buttonShown, bool buttonEnabled, bool reasonShown, bool countdown)
        {
            ButtonShown = buttonShown;
            ButtonEnabled = buttonEnabled;
            ReasonShown = reasonShown;
            Countdown = countdown;
        }
    }

    /// <summary>Pure mapping from the bar's message to what is shown, greyed or explained.</summary>
    public static class WarmupBarRules
    {
        public static WarmupBarView ViewFor(WarmupMessage message, int blockedTeam, int playersNow)
        {
            bool hostButton = message == WarmupMessage.HostMayEnd || message == WarmupMessage.HostBlocked;
            // The reason names a team that has nobody. Right after Start the team properties have not landed: nobody is counted yet, and the host's own
            // team would be named empty. The line waits until at least one player is counted.
            bool reason = message == WarmupMessage.HostBlocked && blockedTeam >= 0 && playersNow > 0;
            return new WarmupBarView(hostButton, message == WarmupMessage.HostMayEnd, reason, message == WarmupMessage.Countdown);
        }
    }
}
