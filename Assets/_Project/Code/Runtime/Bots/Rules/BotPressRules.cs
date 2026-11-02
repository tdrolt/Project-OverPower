namespace Overpower.Bots
{
    /// <summary>When a bot may press an ability key again: an ability can refuse a press without spending its charge (a blocked Cover
    /// spot, no Blink landing), and pressing again every think would hammer the key.</summary>
    public static class BotPressRules
    {
        public static bool MayPressAgain(float secondsSinceLastPress, float retrySeconds) => secondsSinceLastPress >= retrySeconds;
    }
}
