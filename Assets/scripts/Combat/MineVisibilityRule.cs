namespace Overpower.Combat
{
    /// <summary>What a mine looks like to one particular viewer, right now (A5: mines turn invisible a set time
    /// after placement).</summary>
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
    /// Pure timing/team logic for MineVisibility, provable without a Renderer, PhotonNetwork or scene.
    /// secondsSincePlaced is a plain float the caller already computed (Mine.SecondsSincePlaced: Age, a
    /// one-time network-agreed snapshot, plus real time elapsed on THIS client since it arrived), so
    /// every client evaluates the same comparison against the same placement time and switches at the
    /// same moment regardless of its own lag. No RPC, no timestamp of this rule's own.
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
