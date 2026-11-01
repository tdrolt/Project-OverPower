using Photon.Pun;
using UnityEngine;

namespace Overpower.Net
{
    /// <summary>
    /// On the player prefab, first of all its components (execution order): a body the host made for a bot carries the bot's
    /// actor in its instantiation data, and this hands both of the body's PhotonViews to that BotPlayer on every client before
    /// any other Awake asks who owns the body. The owner is then the bot (Teams, PlayerLookup, damage credit all see the bot)
    /// and PUN makes the master the controller of an inactive owner's view, so photonView.IsMine is true on the host only.
    /// The prefab's views keep OwnershipTransfer = Fixed: nobody can take a body over the network; each client sets this
    /// locally from the same data.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class BotSeat : MonoBehaviour
    {
        /// <summary>InstantiationData: [0] the team (PlayerLifecycle reads it), [1] the bot actor (bodies of bots only).</summary>
        public const int ActorIndex = 1;

        public bool IsBot { get; private set; }
        public int Actor { get; private set; }
        /// <summary>The team the body was made for (-1 for a human's body), before the bot's team property arrives from the room.</summary>
        public int Team { get; private set; } = -1;

        /// <summary>The bot actor a body's instantiation data names, or false for a human's body.</summary>
        public static bool TryReadActor(object[] data, out int actor)
        {
            actor = 0;
            if (data == null || data.Length <= ActorIndex || !(data[ActorIndex] is int value) || !BotRosterRules.IsBotActor(value)) return false;
            actor = value;
            return true;
        }

        private void Awake()
        {
            PhotonView view = GetComponent<PhotonView>();
            if (!TryReadActor(view != null ? view.InstantiationData : null, out int actor)) return;
            BotPlayer bot = BotRoster.Ensure(actor);
            if (bot == null)
            {
                Debug.LogWarning($"[BOTS] body {view.ViewID} names bot {actor} outside a room - left as the creator's");
                return;
            }
            foreach (PhotonView part in GetComponentsInChildren<PhotonView>(true))
            {
                // The setter returns early on an unchanged number, which would leave Owner null if it was set before the bot existed.
                if (part.OwnerActorNr == actor && part.Owner == null) part.OwnerActorNr = part.CreatorActorNr;
                part.OwnerActorNr = actor;
                part.ControllerActorNr = actor;
            }
            IsBot = true;
            Actor = actor;
            Team = view.InstantiationData.Length > 0 && view.InstantiationData[0] is int team ? team : -1;
        }
    }
}
