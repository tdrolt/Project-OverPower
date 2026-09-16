using Photon.Pun;
using UnityEngine;

/// <summary>
/// The player's entire network wire format: every serialize tick it sends transform.position,
/// transform.rotation, health and armor, in that order, and on receive hands them straight to
/// PlayerMotor and PlayerHealth. Split out of Multiplayer.cs (Task 0.11a) so this is the ONE and
/// ONLY IPunObservable on the player.
///
/// That singularity is load-bearing, not stylistic: the player's PhotonView uses AutoFindAll,
/// which searches children too, so a second IPunObservable anywhere on this GameObject would be
/// silently added to the same view's serialization and start sending a second block per network
/// tick with nobody having touched the Inspector. PlayerHealth, PlayerMotor and every other player
/// component are deliberately NOT observable for this reason - see their own class comments.
///
/// AutoFindAll does NOT run at runtime for this prefab. PhotonNetwork.Instantiate sets the ViewID
/// before PhotonView.Awake, which then skips its search - only the Observed Components list SAVED
/// in the prefab is used, and only the PhotonView Inspector rewrites it. From 32e2b1a until
/// 2026-09-15 that saved list still pointed at the deleted Multiplayer component, so this class
/// never sent anything and every remote player sat frozen at spawn at full health.
/// NetworkPrefabObservablesTests now fails if the saved list drifts from what the search finds.
///
/// Wire order is position, rotation, health, armor - do not reorder or change a type here without
/// updating the read side to match. PUN has no version tag on this payload, so a mismatch does not
/// error, it just silently assigns each value to the wrong field on every receiving client.
/// </summary>
public class PlayerNetSync : MonoBehaviour, IPunObservable
{
    private PlayerMotor playerMotor;
    private PlayerHealth playerHealth;

    /// <summary>Last position received from the owner over the network.</summary>
    public Vector3 NetworkPosition { get; private set; }

    /// <summary>Last rotation received from the owner over the network.</summary>
    public Quaternion NetworkRotation { get; private set; }

    /// <summary>True once this copy has received anything from its owner. Until then NetworkPosition and
    /// NetworkRotation are still their defaults (the world origin), not where the player is. Nothing extra is sent for
    /// this: it only records that the first update arrived.</summary>
    public bool HasReceivedFromOwner { get; private set; }

    private void Awake()
    {
        playerMotor = GetComponent<PlayerMotor>();
        playerHealth = GetComponent<PlayerHealth>();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(playerHealth.Health);
            stream.SendNext(playerHealth.Armor);
        }
        else
        {
            // Read in the exact order they were sent - PhotonStream has no field names, only a
            // queue.
            NetworkPosition = (Vector3)stream.ReceiveNext();
            NetworkRotation = (Quaternion)stream.ReceiveNext();
            HasReceivedFromOwner = true;
            float receivedHealth = (float)stream.ReceiveNext();
            float receivedArmor = (float)stream.ReceiveNext();

            playerMotor.SetNetworkTarget(NetworkPosition, NetworkRotation);

            // receivedArmor can legitimately clamp down for one frame here if an armor-level
            // Custom Property (which changes ArmorState.Capacity) hasn't arrived on this client
            // yet - this stream and Custom Properties are two separate channels. Harmless and
            // self-healing: see ArmorState.SetFromNetwork.
            playerHealth.SetHealthFromNetwork(receivedHealth, receivedArmor);
        }
    }
}
