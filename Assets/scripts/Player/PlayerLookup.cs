using System.Collections.Generic;
using Photon.Pun;

public static class PlayerLookup
{
    static Dictionary<int, PhotonView> _lookup = new Dictionary<int, PhotonView>();

    public static void Register(int actorNumber, PhotonView view)
    {
        _lookup[actorNumber] = view;
    }

    public static void Unregister(int actorNumber)
    {
        _lookup.Remove(actorNumber);
    }

    /// <summary>Task 9f: nothing left over when the scene is rebuilt for a new match.</summary>
    public static void Clear() => _lookup.Clear();

    public static PhotonView GetPhotonViewFor(int actorNumber)
    {
        _lookup.TryGetValue(actorNumber, out var view);
        return view;
    }
}
