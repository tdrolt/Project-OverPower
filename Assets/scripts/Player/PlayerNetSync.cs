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
            float receivedHealth = (float)stream.ReceiveNext();
            float receivedArmor = (float)stream.ReceiveNext();

            playerMotor.SetNetworkTarget(NetworkPosition, NetworkRotation);
            playerHealth.SetHealthFromNetwork(receivedHealth, receivedArmor);
        }
    }
}
