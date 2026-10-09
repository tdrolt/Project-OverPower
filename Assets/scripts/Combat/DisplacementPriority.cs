namespace Overpower.Combat
{
    /// <summary>
    /// The three kinds of request PlayerDisplacement can be asked to run: something the player chose
    /// (a dash, a zip pull), an instant jump with no travel time (a blink), and something done TO the
    /// player (a knockback).
    /// </summary>
    public enum DisplaceKind
    {
        /// <summary>The player's own choice - a dash, a zip pull. Travels over time.</summary>
        Voluntary,

        /// <summary>A blink - an instant reposition with no travel time to interrupt.</summary>
        Teleport,

        /// <summary>Something done to the player - a knockback. Always wins.</summary>
        Forced
    }

    /// <summary>
    /// Pure priority rules for one shared mover (PlayerDisplacement) juggling three kinds of
    /// request, plain C# so "does a knockback interrupt a dash" is provable without a scene or a
    /// physics step. Forced (knockback) always wins and is only replaced by another Forced: nothing a
    /// player chooses may shrug off being shoved. Voluntary and Teleport never outrank each other or
    /// a Forced move, but freely replace a running Voluntary (a second dash cuts the first short).
    /// </summary>
    public static class DisplacementPriority
    {
        /// <summary>
        /// True if a request of kind incoming may start now; current is null when nothing runs.
        /// </summary>
        public static bool Accepts(DisplaceKind? current, DisplaceKind incoming)
        {
            return current != DisplaceKind.Forced || incoming == DisplaceKind.Forced;
        }

        /// <summary>
        /// True if starting incoming cuts short a running current move, so the caller reports
        /// Cancelled to the move it replaces before starting the new one.
        /// </summary>
        public static bool CancelsCurrent(DisplaceKind? current, DisplaceKind incoming)
        {
            return current.HasValue && Accepts(current, incoming);
        }
    }
}
