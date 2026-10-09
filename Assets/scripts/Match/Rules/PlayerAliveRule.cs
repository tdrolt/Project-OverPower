namespace Overpower.Match
{
    /// <summary>What PlayerHealth.IsAlive says. PlayerHealth latches its own "dead" flag only on the machine that owns the
    /// player (damage is decided there), so on every other client that flag never turns on. The replicated alive state (the `alive`
    /// Player Property, carried by PlayerLifecycle.IsAlive on every client) is the truth everyone shares; a player is alive only when
    /// the local latch is clear AND the shared state says alive. A body with no lifecycle has no shared state, so the latch decides.</summary>
    public static class PlayerAliveRule
    {
        public static bool IsAlive(bool latchedDead, bool hasLifecycle, bool lifecycleAlive) =>
            !latchedDead && (!hasLifecycle || lifecycleAlive);
    }
}
