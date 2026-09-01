using Photon.Pun;
using UnityEngine;

public class MultiplayerBulletController : MonoBehaviourPun
{
    [Header("Bullet Settings")]
    public float bulletSpeed = 15f;
    public GameObject bulletImpactEffect;
    public AudioClip BulletHitAudio;
    public int damage = 10;
    
    [HideInInspector] public Photon.Realtime.Player owner;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void InitializeBullet(Vector3 direction, Photon.Realtime.Player shooter)
    {
        owner = shooter;
        transform.forward = direction;
        rb.linearVelocity = direction * bulletSpeed;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.Play3D(BulletHitAudio, transform.position);

        if (VFXManager.Instance != null)
            VFXManager.Instance.PlayVFX(bulletImpactEffect, transform.position);

        Destroy(gameObject);
    }
}