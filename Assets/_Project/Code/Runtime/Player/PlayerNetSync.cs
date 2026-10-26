using Photon.Pun;
using UnityEngine;

/// <summary>
/// The player's entire network wire format: each serialize tick sends rb.position, transform.rotation,
/// health and armor, in that order; on receive hands them to PlayerMotor and PlayerHealth. This is
/// the ONE and ONLY IPunObservable on the player.
///
/// That singularity is load-bearing: the PhotonView uses AutoFindAll, which searches children too, so
/// a second IPunObservable anywhere on this GameObject would silently join the view's serialization
/// and send a second block per tick. PlayerHealth, PlayerMotor and the rest are NOT observable for
/// this reason.
///
/// AutoFindAll does NOT run at runtime for this prefab: PhotonNetwork.Instantiate sets the ViewID
/// before PhotonView.Awake, which then skips its search, so only the Observed Components list SAVED
/// in the prefab is used, and only the PhotonView Inspector rewrites it. A stale saved list means
/// nothing is sent and every remote player sits frozen at spawn. NetworkPrefabObservablesTests
/// fails if the saved list drifts from what the search finds.
///
/// Wire order is position, rotation, health, armor - change nothing here without updating the read
/// side. PUN has no version tag on this payload, so a mismatch does not error, it silently assigns
/// each value to the wrong field on every receiving client.
/// </summary>
public class PlayerNetSync : MonoBehaviour, IPunObservable
{
    private PlayerMotor playerMotor;
    private PlayerHealth playerHealth;
    private Rigidbody rb;

    public Vector3 NetworkPosition { get; private set; }

    public Quaternion NetworkRotation { get; private set; }

    /// <summary>True once the first update from the owner arrived. Until then NetworkPosition and
    /// NetworkRotation are defaults (the world origin), not where the player is. Not sent on the wire.</summary>
    public bool HasReceivedFromOwner { get; private set; }

    private void Awake()
    {
        playerMotor = GetComponent<PlayerMotor>();
        playerHealth = GetComponent<PlayerHealth>();
        rb = GetComponent<Rigidbody>();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // The body's physics position, not the transform's: the transform trails rb.position by up
            // to a physics step, and right after a respawn or teleport that is the spot just left.
            stream.SendNext(rb != null ? rb.position : transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(playerHealth.Health);
            stream.SendNext(playerHealth.Armor);
        }
        else
        {
            // Same order as sent: PhotonStream has no field names, only a queue.
            NetworkPosition = (Vector3)stream.ReceiveNext();
            NetworkRotation = (Quaternion)stream.ReceiveNext();
            HasReceivedFromOwner = true;
            float receivedHealth = (float)stream.ReceiveNext();
            float receivedArmor = (float)stream.ReceiveNext();

            playerMotor.SetNetworkTarget(NetworkPosition, NetworkRotation, info.SentServerTimestamp);

            // receivedArmor can clamp down for one frame if the armor-level Custom Property (which
            // changes ArmorState.Capacity) has not arrived yet: two separate channels. Self-healing,
            // see ArmorState.SetFromNetwork.
            playerHealth.SetHealthFromNetwork(receivedHealth, receivedArmor);
        }
    }
}
