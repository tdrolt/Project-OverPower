using Overpower.Lobby;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Everything about one zone that changes its floor tiles. Capture progress is not in it: tiles change on an owner flip.</summary>
    public struct ZoneState
    {
        /// <summary>ZoneOwnerShown.Of: what the minimap shows, so a zone this team does not know looks the same on both.</summary>
        public int OwnerShown;
        public bool Spawn;
        public bool OutOfPlay;
        /// <summary>The live capture radius (the centre's circle shrinks while a corner is cut).</summary>
        public float Radius;
        public Vector2 Centre;
        /// <summary>False until the zone's tower has registered; a never-painted slot is false too, so it always differs from a real zone.</summary>
        public bool Registered;
    }

    /// <summary>When the floor's owner tiles must be repainted: never before every zone is registered (a late joiner paints on the first
    /// frame they all are), then on any change of a zone's state or when an event marked it; never while nothing changed.</summary>
    public static class OwnershipSignature
    {
        public static bool NeedsRepaint(ZoneState[] painted, ZoneState[] now, int count, bool marked)
        {
            if (count <= 0)
                return false;
            for (int i = 0; i < count; i++)
                if (!now[i].Registered)
                    return false;
            if (marked)
                return true;
            for (int i = 0; i < count; i++)
                if (!Same(painted[i], now[i]))
                    return true;
            return false;
        }

        /// <summary>Leaving a room: nothing counts as painted any more, so the next room paints on its first frame with every zone.</summary>
        public static void Forget(ZoneState[] painted)
        {
            if (painted != null)
                System.Array.Clear(painted, 0, painted.Length);
        }

        /// <summary>The floor shows owners from the warm-up on, as the towers do; in the room's lobby before the start, and with no
        /// territory state at all, the arena draws plain. A live room counts even without a lobby stage (a room made without a lobby).</summary>
        public static bool ShowsOwnerTiles(bool hasSnapshot, bool live, int lobbyStage) =>
            hasSnapshot && (live || lobbyStage >= LobbySeatRules.LobbyWarmup);

        // Exact compares on purpose: a radius or centre only changes by a real event (a cut, a moved tower), never by drift.
        private static bool Same(ZoneState a, ZoneState b) =>
            a.Registered == b.Registered && a.OwnerShown == b.OwnerShown && a.Spawn == b.Spawn && a.OutOfPlay == b.OutOfPlay
            && a.Radius == b.Radius && a.Centre == b.Centre;
    }
}
