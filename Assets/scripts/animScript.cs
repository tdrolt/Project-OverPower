using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Overpower.Weapons;

public class animScript : MonoBehaviourPun
{
    private Animator anim;
    public WeaponFiring weaponFiring;
    private PhotonView photonView;

    void Awake()
    {
        anim = GetComponent<Animator>();
        // In the parent, not on this object: this script lives on the character mesh child, while WeaponFiring
        // sits on the player root with PlayerAim, PlayerOverheat and the input router it needs.
        weaponFiring = GetComponentInParent<WeaponFiring>();
        photonView = GetComponent<PhotonView>();
    }

    void Update()
    {
        if (!photonView.IsMine)
            return;

        CheckKey();
    }

    void CheckKey()
    {
        bool isRunning = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) ||
                         Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);

        anim.SetBool("run", isRunning);
        anim.SetBool("idle", !isRunning);

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
