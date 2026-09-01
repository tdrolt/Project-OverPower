using Photon.Pun;
using UnityEngine;
using System.Collections;
using Photon.Realtime;
using TMPro;

public class AoEEffect : MonoBehaviourPunCallbacks
{
    public float damage = 20f;       // Damage per tick
    public float duration = 5f;      // Total duration of the effect
    public float radius = 5f;        // AoE radius
    public float tickInterval = 1f;  // Interval between damage ticks

    // The player who triggered this AoE (for awarding score on kills)
    public Player caster; 

    private float elapsedTime = 0f;

    void Start()
    {
        StartCoroutine(ApplyDamageOverTime());
    }

    IEnumerator ApplyDamageOverTime()
    {
        while (elapsedTime < duration)
        {
            Debug.Log($"[AoEEffect] Ticking at {transform.position} (Elapsed: {elapsedTime}s)");
            // Check all colliders within the AoE radius
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
            foreach (Collider hit in hitColliders)
            {
                Multiplayer player = hit.GetComponent<Multiplayer>();
                if (player != null && player.playerNameText.text != caster.NickName) //temp fix
                {
                    Debug.Log($"[AoEEffect] {player.playerNameText.text} hit! Applying {damage} damage.");
                    player.ApplyAoEDamage(damage, caster);
                }
            }
            yield return new WaitForSeconds(tickInterval);
            elapsedTime += tickInterval;
        }

        // Destroy this AoE effect over the network when done
        PhotonNetwork.Destroy(gameObject);
    }

    // Visualize the AoE radius in the editor for debugging
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
