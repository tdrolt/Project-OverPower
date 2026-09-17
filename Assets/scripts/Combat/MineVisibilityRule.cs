namespace Overpower.Combat
{
    /// <summary>What a mine looks like to one particular viewer, right now (A5, Tudor 2026-09-17 evening: mines turn
    /// invisible 1 s after placement, as the GDD always said).</summary>
    public enum MineVisibility
    {
        /// <summary>Full colour, to everyone - before Invisible After Seconds elapses.</summary>
        Visible,

        /// <summary>A faded, translucent version, seen only by the owner's own team once the mine would otherwise be
        /// invisible - so teammates can still tell where their own mines are.</summary>
        Ghost,

        /// <summary>Nothing drawn at all - what everyone who is not the owner's own team sees once Invisible After
        /// Seconds elapses.</summary>
        Hidden
    }

    /// <summary>
    /// Pure timing/team logic for MineVisibility - no Renderer, no PhotonNetwork, no scene - the same reasoning as
    /// MineDetonationState: "does this particular viewer still see the mine" is provable without either.
    ///
    /// secondsSincePlaced is deliberately a plain float the caller already computed (Mine.SecondsSincePlaced: Age,
    /// a one-time network-agreed snapshot, plus real time elapsed on THIS client since it arrived) rather than
    /// anything this rule reads itself - every client evaluates the identical comparison against the identical
    /// network-agreed placement time, so every client switches at the same moment regardless of its own lag. No
    /// RPC, no timestamp of this rule's own.
    /// </summary>
    public static class MineVisibilityRule
    {
        /// <summary>localTeam &lt; 0 (no team assigned yet, or a spectator) is never "the owner's own team" - it
        /// counts as an enemy, exactly like any other team id that isn't ownerTeam.</summary>
        public static MineVisibility For(int localTeam, int ownerTeam, float secondsSincePlaced, float invisibleAfterSeconds)
        {
            if (secondsSincePlaced < invisibleAfterSeconds)
                return MineVisibility.Visible;

            bool ownersOwnTeam = localTeam >= 0 && localTeam == ownerTeam;
            return ownersOwnTeam ? MineVisibility.Ghost : MineVisibility.Hidden;
        }
    }
}
