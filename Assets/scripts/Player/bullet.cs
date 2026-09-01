using Photon.Pun;
using UnityEngine;

public class PlayerShooting : MonoBehaviourPun
{
    [Header("Bullet Settings")]
    public GameObject bulletPrefab;
    public Transform bulletPosition;
    public GameObject bulletFiringEffect;
    public float fireRate = 0.75f;
    public AudioClip playerShootingAudio;
    
    private float nextFire;

    void Update()
    {
        if (!photonView.IsMine) return;

        if (Input.GetMouseButton(0) && Time.time > nextFire)
        {
            nextFire = Time.time + fireRate;
            photonView.RPC("Fire", RpcTarget.AllViaServer);
        }
    }

    [PunRPC]
    void Fire()
    {
        GameObject bullet = Instantiate(bulletPrefab, bulletPosition.position, Quaternion.identity);
        bullet.GetComponent<MultiplayerBulletController>()?.InitializeBullet(
            transform.rotation * Vector3.forward, 
            photonView.Owner
        );

        AudioManager.Instance.Play3D(playerShootingAudio, transform.position);
        VFXManager.Instance.PlayVFX(bulletFiringEffect, bulletPosition.position);
    }
}
