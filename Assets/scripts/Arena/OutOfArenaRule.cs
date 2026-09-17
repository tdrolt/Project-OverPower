namespace Overpower.Arena
{
    /// <summary>What the out-of-arena safety net does this physics step.</summary>
    public enum OutOfArenaAction
    {
        /// <summary>Nothing: near an edge, in the air, or outside with nowhere known to go back to.</summary>
        None,

        /// <summary>Standing on floor with a player's width inside the outline: remember this spot.</summary>
        RememberAsSafe,

        /// <summary>The player's centre is past the outline - through a boundary wall's inner face - so put them
        /// back.</summary>
        ReturnToLastSafe,
    }

    /// <summary>
    /// The out-of-arena safety net's decision (movement step 4), kept pure so it is tested without a scene. Movement
    /// steps 2 and 3 close every way out anyone has found; this catches the ones nobody has found yet, on the owner's
    /// own client, next to the kill-height check. It is not a respawn key: nothing the player presses reaches it.
    ///
    /// Two thresholds on purpose:
    ///  - a spot is REMEMBERED only with a whole player's width to spare, so the spot you are put back on never
    ///    overlaps a wall;
    ///  - the net only FIRES once the centre is past a wall's inner face, which a player merely pressed against a wall
    ///    (about 0.1 m in) never is, so hugging a wall can't trigger it.
    /// </summary>
    public static class OutOfArenaRule
    {
        public static OutOfArenaAction Decide(float signedDistance, float playerRadius, bool grounded, bool hasLastSafe)
        {
            if (signedDistance < 0f)
                return hasLastSafe ? OutOfArenaAction.ReturnToLastSafe : OutOfArenaAction.None;

            if (grounded && signedDistance >= playerRadius)
                return OutOfArenaAction.RememberAsSafe;

            return OutOfArenaAction.None;
        }
    }
}
