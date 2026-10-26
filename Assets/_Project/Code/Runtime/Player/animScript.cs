using UnityEngine;
using Photon.Pun;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Weapons;

public class animScript : MonoBehaviourPun
{
    private static readonly int RunParam = Animator.StringToHash("run");
    private static readonly int IdleParam = Animator.StringToHash("idle");
    private static readonly int ShootParam = Animator.StringToHash("shoot");

    private Animator anim;
    public WeaponFiring weaponFiring;
    private PlayerInputRouter input;
    private PhotonView photonView;

    [SerializeField, Tooltip("Where the shortest shoot pose after a shot comes from: Assets/_Project/Shared/Config/GameplayConfig.asset. " +
             "Left empty, the mech only shows the shoot pose while the trigger is held, and a charge weapon's shot shows no pose at all.")]
    private GameplayConfig gameplayConfig;

    void Awake()
    {
        anim = GetComponent<Animator>();
        // In the parent, not on this object: this script lives on the character mesh child, while WeaponFiring
        // sits on the player root with PlayerAim, PlayerOverheat and the input router it needs.
        weaponFiring = GetComponentInParent<WeaponFiring>();
        input = GetComponentInParent<PlayerInputRouter>();
        photonView = GetComponent<PhotonView>();

        if (gameplayConfig == null)
            Debug.LogError($"[animScript] {name}: GameplayConfig is not assigned - a quick tap or a charge shot shows no shoot pose.");
    }

    void Update()
    {
        // Remote copies play the bools PhotonAnimatorView streams in; only the owner works them out.
        if (!photonView.IsMine)
            return;

        ApplyParams();
    }

    private void ApplyParams()
    {
        bool held = input != null && input.PrimaryHeld;
        Vector2 move = input != null ? input.MoveAxis : Vector2.zero;
        bool mayFire = weaponFiring != null && weaponFiring.MayFire;
        bool canCharge = weaponFiring != null && weaponFiring.Weapon != null && weaponFiring.Weapon.CanCharge;
        float sinceShot = weaponFiring != null ? Time.time - weaponFiring.LastShotTime : float.PositiveInfinity;
        float pose = gameplayConfig != null ? gameplayConfig.ShootPoseMinimumSeconds : 0f;

        MechAnimParams p = MechAnimParams.For(held, move, mayFire, canCharge, sinceShot, pose);
        anim.SetBool(RunParam, p.Run);
        anim.SetBool(IdleParam, p.Idle);
        anim.SetBool(ShootParam, p.Shoot);
    }
}
