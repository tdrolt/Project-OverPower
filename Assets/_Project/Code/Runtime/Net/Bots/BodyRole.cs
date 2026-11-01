using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;

namespace Overpower.Net
{
    /// <summary>
    /// Two questions photonView.IsMine used to answer at once. On the host it is true for the human's own body AND for every
    /// bot body (the host controls them), so:
    /// - IsMine still means "this client simulates this body" (movement, firing, applying damage, the respawn timer);
    /// - IsLocalHuman means "this is the body of the person at this keyboard" (input, camera, HUD, minimap, shop screen, the
    ///   local sight texture, sounds and pops that are for their screen only).
    /// </summary>
    public static class BodyRole
    {
        public static bool IsBot(PhotonView view) => view != null && view.Owner is BotPlayer;

        public static bool IsLocalHuman(PhotonView view) => view != null && view.IsMine && !(view.Owner is BotPlayer);

        /// <summary>Every body this client simulates: its own human's (if it has one) and, on the host, each bot's. For the
        /// match-wide edges (go-live fresh start, round start, sudden death, the shrink) that used to touch "the local player"
        /// only. Cleared first.</summary>
        public static void ControlledBodies(List<PhotonView> into)
        {
            into.Clear();
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;
            if (PhotonNetwork.LocalPlayer != null)
            {
                PhotonView own = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
                if (own != null && own.IsMine) into.Add(own);
            }
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (!(player is BotPlayer)) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                if (body != null && body.IsMine) into.Add(body);
            }
        }
    }
}
