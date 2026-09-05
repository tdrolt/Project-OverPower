using Photon.Pun;
using UnityEngine;
using System.Collections;
using Photon.Realtime;
using TMPro;

public class AoEEffect : MonoBehaviourPunCallbacks, IPunInstantiateMagicCallback
{
    public float damage = 20f;       // Damage per tick
    public float duration = 5f;      // Total duration of the effect
    public float radius = 5f;        // AoE radius
    public float tickInterval = 1f;  // Interval between damage ticks

    // The player who triggered this AoE (for awarding score on kills)
    public Player caster; 

    private float elapsedTime = 0f;

    // Runs on every client the moment PhotonNetwork.Instantiate creates this object, before Start.
    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        object[] data = info.photonView.InstantiationData;
        if (data != null && data.Length > 0 && data[0] is int actorNumber)
            caster = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);

        if (caster == null)
            caster = info.Sender;
    }

    void Start()
    {
        StartCoroutine(ApplyDamageOverTime());
    }

    IEnumerator ApplyDamageOverTime()
    {
        while (elapsedTime < duration)
        {
            // Removed a Debug.Log here that ran EVERY tick. With tickInterval 0.1 over a 7s
            // duration that is ~70 lines per cast, on every client, each written synchronously to
            // Player.log in a build. It is the leading suspect for the freezes in the 9-player
            // playtest.

            // Check all colliders within the AoE radius
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
            foreach (Collider hit in hitColliders)
            {
                // Friend-or-foe used to be a nickname string comparison against a caster that was
                // null on every non-casting client. Multiplayer.ApplyAoEDamage now owns that
                // decision (ownership, self-hit and team), so there is one place that decides.
                Multiplayer player = hit.GetComponent<Multiplayer>();
                if (player != null)
                    player.ApplyAoEDamage(damage, caster);
            }
            yield return new WaitForSeconds(tickInterval);
            elapsedTime += tickInterval;
        }

        // This coroutine runs on EVERY client, but only the owner may network-destroy the object.
        // Without this guard the other eight clients each logged
        // "Failed to 'network-remove' GameObject" for every single cast.
        if (photonView.IsMine)
            PhotonNetwork.Destroy(gameObject);
    }

    // Visualize the AoE radius in the editor for debugging
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
