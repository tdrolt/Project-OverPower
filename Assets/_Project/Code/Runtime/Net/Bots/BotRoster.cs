using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Net
{
    /// <summary>
    /// Keeps this client's injected BotPlayers in step with the room (BotRosterRules): injects a bot the roster names, mirrors
    /// its b{actor}.* properties into it, drops one the roster no longer names, and raises OnPlayerEnteredRoom /
    /// OnPlayerPropertiesUpdate / OnPlayerLeftRoom for it as the server would for a real player. So seats, teams, the scoreboard
    /// and every property reader see bots without knowing about them.
    ///
    /// RoomManager adds it before every other room component: callbacks run in the order components registered, so a bot is in
    /// the player list before anything reads its seat. When the host leaves, the new master takes every bot out.
    /// </summary>
    public sealed class BotRoster : MonoBehaviourPunCallbacks
    {
        // Announced per Room object: a join or rejoin builds a new Room (no bots in it yet), a scene load keeps the same one.
        private static Room announcedFor;
        private static readonly HashSet<int> announced = new HashSet<int>();
        private static FieldInfo callbackTargetsField;

        private readonly List<int> present = new List<int>();
        private readonly List<int> toAdd = new List<int>();
        private readonly List<int> toRemove = new List<int>();

        public override void OnJoinedRoom() => Reconcile(null);

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (Touches(changed)) Reconcile(changed);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (PhotonNetwork.IsMasterClient) TakeAllBotsOut();
        }

        /// <summary>The BotPlayer for actor, made from the room's record now when a bot body arrives before the roster update
        /// (it is announced when that update comes). Null outside a room or for a real actor.</summary>
        public static BotPlayer Ensure(int actor)
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null || !BotRosterRules.IsBotActor(actor)) return null;
            if (room.Players.TryGetValue(actor, out Player existing)) return existing as BotPlayer;
            return Create(room, actor);
        }

        private static bool Touches(Hashtable changed)
        {
            if (changed == null) return false;
            if (changed.ContainsKey(BotRosterRules.RosterKey)) return true;
            foreach (var pair in changed)
                if (pair.Key is string key && BotRosterRules.TryParsePropKey(key, out _, out _)) return true;
            return false;
        }

        private static BotPlayer Create(Room room, int actor)
        {
            Hashtable props = room.CustomProperties;
            string nick = props.TryGetValue(BotRosterRules.PropKey(actor, BotRosterRules.NickKey), out object rawNick) && rawNick is string n ? n : "Player";
            int build = props.TryGetValue(BotRosterRules.PropKey(actor, BotRosterRules.BuildKey), out object rawBuild) && rawBuild is int b ? b : 0;
            var bot = new BotPlayer(nick, actor, build);
            room.StorePlayer(bot);
            return bot;
        }

        private void Reconcile(Hashtable changed)
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return;
            if (!ReferenceEquals(announcedFor, room))
            {
                announcedFor = room;
                announced.Clear();
            }

            Hashtable all = room.CustomProperties;
            int[] roster = BotRosterRules.Roster(all);
            present.Clear();
            foreach (Player p in room.Players.Values)
                if (p is BotPlayer) present.Add(p.ActorNumber);
            BotRosterRules.Plan(roster, present, toAdd, toRemove);

            IInRoomCallbacks[] targets = Targets();
            foreach (int actor in toRemove)
            {
                if (!room.Players.TryGetValue(actor, out Player gone)) continue;
                room.Players.Remove(actor);
                announced.Remove(actor);
                Debug.Log($"[BOTS] {gone.NickName} ({actor}) left the room");
                Raise(targets, t => t.OnPlayerLeftRoom(gone));
            }
            foreach (int actor in toAdd)
                Create(room, actor);

            Dictionary<int, Hashtable> full = BotRosterRules.SplitByBot(all);
            Dictionary<int, Hashtable> delta = changed == null ? full : BotRosterRules.SplitByBot(changed);
            foreach (int actor in roster)
            {
                if (!(room.GetPlayer(actor) is BotPlayer bot)) continue;
                if (announced.Add(actor))
                {
                    full.TryGetValue(actor, out Hashtable everything);
                    bot.Mirror(everything);
                    Debug.Log($"[BOTS] {bot.NickName} ({actor}, build {bot.Build}) is in the room");
                    Raise(targets, t => t.OnPlayerEnteredRoom(bot));
                    if (everything != null && everything.Count > 0) Raise(targets, t => t.OnPlayerPropertiesUpdate(bot, everything));
                }
                else if (delta.TryGetValue(actor, out Hashtable changes))
                {
                    bot.Mirror(changes);
                    Raise(targets, t => t.OnPlayerPropertiesUpdate(bot, changes));
                }
            }
        }

        /// <summary>The host left: a dropped host's bot bodies stay in the room (PUN destroys a leaver's objects only on a real
        /// leave) and are now this client's to destroy; then the roster is emptied, which takes every BotPlayer out everywhere
        /// and lets the seat sweep free their seats.</summary>
        private static void TakeAllBotsOut()
        {
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null) return;
            var bodies = new List<PhotonView>();
            foreach (Player p in room.Players.Values)
            {
                if (!(p is BotPlayer)) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(p.ActorNumber);
                if (body != null) bodies.Add(body);
            }
            foreach (PhotonView body in bodies)
            {
                // A dropped host is still these views' controller until PUN rebuilds its caches after this callback, and PUN removes only a
                // body this client controls: the new master claims each one first.
                foreach (PhotonView part in body.GetComponentsInChildren<PhotonView>(true))
                    if (!part.IsMine) part.ControllerActorNr = PhotonNetwork.LocalPlayer.ActorNumber;
                PhotonNetwork.Destroy(body);
            }
            if (BotRosterRules.Roster(room.CustomProperties).Length == 0 && bodies.Count == 0) return;
            Debug.Log($"[BOTS] the host left: taking {BotRosterRules.Roster(room.CustomProperties).Length} bot(s) out");
            room.SetCustomProperties(BotRosterRules.ClearAllWrite(room.CustomProperties));
        }

        /// <summary>Everyone listening to room callbacks except PUN's own handler (it would hand a "leaving" bot's views to the
        /// master and send ownership updates to an actor the server does not have) and this component. A copy, so a callback
        /// that adds or removes a listener cannot break the loop.</summary>
        private IInRoomCallbacks[] Targets()
        {
            if (callbackTargetsField == null)
                callbackTargetsField = typeof(LoadBalancingClient).GetField("InRoomCallbackTargets", BindingFlags.Instance | BindingFlags.NonPublic);
            var list = callbackTargetsField?.GetValue(PhotonNetwork.NetworkingClient) as List<IInRoomCallbacks>;
            if (list == null) return Array.Empty<IInRoomCallbacks>();
            var copy = new List<IInRoomCallbacks>(list.Count);
            foreach (IInRoomCallbacks target in list)
                if (target != null && !(target is PhotonHandler) && !ReferenceEquals(target, this)) copy.Add(target);
            return copy.ToArray();
        }

        private static void Raise(IInRoomCallbacks[] targets, Action<IInRoomCallbacks> call)
        {
            foreach (IInRoomCallbacks target in targets)
            {
                if (target is UnityEngine.Object unityObject && unityObject == null) continue;
                try { call(target); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
