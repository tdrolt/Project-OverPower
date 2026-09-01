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

    public static PhotonView GetPhotonViewFor(int actorNumber)
    {
        _lookup.TryGetValue(actorNumber, out var view);
        return view;
    }
}
