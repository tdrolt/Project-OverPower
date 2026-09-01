using Photon.Pun;
using UnityEngine;
using Photon.Realtime;

public class AoEAbility : MonoBehaviourPunCallbacks
{
    [Header("AoE Settings")]
    public GameObject aoePrefab;      // Networked prefab with AoEEffect attached
    public float aoeDamage = 15f;     // Damage per tick
    public float aoeDuration = 3f;    // Duration the AoE effect lasts (in seconds)
    public float aoeRadius = 6f;      // Radius of the AoE effect
    public AudioClip SpellCastingAudio;

    [Header("Cooldown Settings")]
    public float cooldown = 5f;       // Cooldown time in seconds (editable in the inspector)
    private float nextAvailableTime = 0f;  // Time when ability is ready again

    public void TriggerAoE()
    {
        if (!photonView.IsMine)
            return;

        // Check if the ability is off cooldown
        if (Time.time < nextAvailableTime)
        {
            Debug.Log($"[AoEAbility] Cooldown active. Wait {(nextAvailableTime - Time.time):F1} seconds.");
            return;
        }

        // Set the next available time based on cooldown
        nextAvailableTime = Time.time + cooldown;

        // Raycast from the mouse position to the ground
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        float rayDistance;
        if (groundPlane.Raycast(ray, out rayDistance))
        {
            Vector3 targetPos = ray.GetPoint(rayDistance);

            // Spawn the AoE effect prefab via Photon
            GameObject aoeObj = PhotonNetwork.Instantiate(aoePrefab.name, targetPos, Quaternion.identity);
            
            // Configure the AoE effect with the desired parameters
            AoEEffect effect = aoeObj.GetComponent<AoEEffect>();
            if (effect != null)
            {
                effect.damage = aoeDamage;
                effect.duration = aoeDuration;
                effect.radius = aoeRadius;
                effect.caster = photonView.Owner;
                Debug.Log($"[AoEAbility] AoE triggered at {targetPos} by {photonView.Owner.NickName}");
                AudioManager.Instance.Play3D(SpellCastingAudio, transform.position);
            }
        }
    }
    void OnEnable()
{
    Debug.Log($"{GetType().Name} enabled");
}

void OnDisable()
{
    Debug.Log($"{GetType().Name} disabled");
}
}
