using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

public class animScript : MonoBehaviourPun
{
    private Animator anim;
    public PlayerShooting playerShooting; // Reference to the PlayerShooting component
    private PhotonView photonView;

    void Awake()
    {
        anim = GetComponent<Animator>();
        playerShooting = GetComponent<PlayerShooting>();
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

        // Only process shoot input if the PlayerShooting script is active/enabled.
        if (playerShooting != null && playerShooting.enabled)
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
