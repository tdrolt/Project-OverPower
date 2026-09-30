namespace Overpower.Combat
{
    /// <summary>
    /// The three kinds of request PlayerDisplacement (Task 1.6a) can be asked to run, in the order
    /// a designer would explain them: something the player chose (a dash, a zip pull), an instant
    /// jump with no travel time (a blink), and something done TO the player (a knockback).
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
    /// request. Kept free of MonoBehaviour/Rigidbody, the same reason CastGate and
    /// DamageReductionStack are plain C#: "does a knockback interrupt a dash" is a rule that should
    /// be provable without a scene, a Rigidbody or a physics step.
    ///
    /// THE RULE. Forced (knockback) always wins and is only ever replaced by another Forced -
    /// nothing a player chooses to do should be able to shrug off being shoved. Voluntary and
    /// Teleport never outrank each other or a Forced move in progress, but freely replace a
    /// Voluntary move already running (a second dash cuts the first one short) since neither of
    /// them is "something done to the player" that deserves protecting from the player's own next
    /// choice.
    /// </summary>
    public static class DisplacementPriority
    {
        /// <summary>
        /// True if a request of `incoming` kind may start right now. `current` is null when
        /// nothing is running. Only a Forced move blocks anything, and only another Forced move
        /// can get past it.
        /// </summary>
        public static bool Accepts(DisplaceKind? current, DisplaceKind incoming)
        {
            return current != DisplaceKind.Forced || incoming == DisplaceKind.Forced;
        }

        /// <summary>
        /// True if starting `incoming` cuts short whatever `current` move is already running - i.e.
        /// Accepts is true AND there was something running to cut short. A caller uses this to know
        /// whether to report Cancelled to the move it is about to replace before starting the new one.
        /// </summary>
        public static bool CancelsCurrent(DisplaceKind? current, DisplaceKind incoming)
        {
            return current.HasValue && Accepts(current, incoming);
        }
    }
}
