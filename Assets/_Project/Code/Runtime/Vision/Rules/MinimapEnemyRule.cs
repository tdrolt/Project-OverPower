namespace Overpower.Vision
{
    /// <summary>Which players get a red dot on the minimap (D10): an enemy, alive, that my team sees right now, and only
    /// while the fog is on. No last-seen marks: the moment nobody sees them, the dot is gone.</summary>
    public static class MinimapEnemyRule
    {
        public static bool ShowDot(bool friendly, bool alive, bool seen, bool fogOn)
        {
            return fogOn && !friendly && alive && seen;
        }
    }
}
