using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Net
{
    /// <summary>
    /// A seat the host's game plays. Every client puts one into CurrentRoom.Players (BotRoster), so PlayerList, Teams, the
    /// scoreboard and photonView.Owner treat it as a player. The server never hears of it: its properties come from the room
    /// (BotRosterRules), never from SetCustomProperties (PlayerProps routes those).
    ///
    /// Inactive on purpose: PUN makes the master the controller of a view whose owner is inactive, so the host simulates the
    /// bot's body and nobody else does. The game must never read that as "dropped": Presence.IsDropped knows the difference.
    /// </summary>
    public sealed class BotPlayer : Player
    {
        public BotPlayer(string nickName, int actorNumber, int build) : base(nickName, actorNumber, false)
        {
            IsInactive = true;
            Build = build;
        }

        /// <summary>Which of the three bot builds it plays (BotBuilds).</summary>
        public int Build { get; }

        /// <summary>Applies property changes locally, as a server update would; a null value deletes the key.</summary>
        public void Mirror(Hashtable changed)
        {
            if (changed == null || changed.Count == 0) return;
            InternalCacheProperties(changed);
        }
    }
}
