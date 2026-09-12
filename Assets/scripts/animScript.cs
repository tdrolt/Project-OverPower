using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Overpower.Weapons;

public class animScript : MonoBehaviourPun
{
    private Animator anim;
    public WeaponFiring weaponFiring; // Reference to the weapon component (was PlayerShooting).
    private PhotonView photonView;

    void Awake()
    {
        anim = GetComponent<Animator>();
        // In the parent, not on this object: this script lives on the character mesh child, while
        // WeaponFiring sits on the player root with PlayerAim, PlayerOverheat and the input router
        // it needs. The component this replaced was a sibling here, which is why the old lookup was
        // a plain GetComponent.
        weaponFiring = GetComponentInParent<WeaponFiring>();
        photonView = GetComponent<PhotonView>();
    }

    void Update()
    {
        // Only animate if this is the local player
        if (!photonView.IsMine)
            return;

        CheckKey();
    }

    void CheckKey()
    {
        // Movement animations
        bool isRunning = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                         Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);

        anim.SetBool("run", isRunning);
        anim.SetBool("idle", !isRunning);

        // Only process shoot input if the weapon component is active/enabled.
        if (weaponFiring != null && weaponFiring.enabled)
        {
            if (Input.GetMouseButtonDown(0))
            {
                anim.SetBool("shoot", true);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                anim.SetBool("shoot", false);
            }
        }
    }
}
