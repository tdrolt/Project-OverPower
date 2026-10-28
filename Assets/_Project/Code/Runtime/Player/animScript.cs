using UnityEngine;
using Photon.Pun;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Weapons;

// After PhotonAnimatorView (default order 0): on a remote copy its Update writes the streamed shoot bool, and the pose held after a shot
// this client saw must win over a stale value in the same frame.
[DefaultExecutionOrder(100)]
public class animScript : MonoBehaviourPun
{
    private static readonly int RunParam = Animator.StringToHash("run");
    private static readonly int IdleParam = Animator.StringToHash("idle");
    private static readonly int ShootParam = Animator.StringToHash("shoot");
    // Local only: PhotonAnimatorView must not list it, each client works it out from the shot it saw.
    private static readonly int ShootSpeedParam = Animator.StringToHash("shootSpeed");
    private static readonly int ShootState = Animator.StringToHash("Shoot_SingleShot_AR");

    private Animator anim;
    public WeaponFiring weaponFiring;
    private PlayerInputRouter input;
    private PlayerLifecycle lifecycle;
    private PlayerStatusEffects statusEffects;
    private PhotonView photonView;
    private float lastShotSeen = float.NegativeInfinity;
    private float shotPoseSeconds;

    [SerializeField, Tooltip("Where the shortest shoot pose after a shot and the shoot motion's speed-up and blend come from: " +
             "Assets/_Project/Design/Settings/GameplayConfig.asset. Left empty, a shot holds the shoot pose only for the length of the " +
             "shoot motion, and a fast gun's motion is not sped up.")]
    private GameplayConfig gameplayConfig;

    [SerializeField, Tooltip("The clip the Shoot state plays; its length sets how much a fast gun speeds the shoot motion up.")]
    private AnimationClip shootClip;

    void Awake()
    {
        anim = GetComponent<Animator>();
        // In the parent, not on this object: this script lives on the character mesh child, while WeaponFiring
        // sits on the player root with PlayerAim, PlayerOverheat and the input router it needs.
        weaponFiring = GetComponentInParent<WeaponFiring>();
        input = GetComponentInParent<PlayerInputRouter>();
        lifecycle = GetComponentInParent<PlayerLifecycle>();
        statusEffects = GetComponentInParent<PlayerStatusEffects>();
        photonView = GetComponent<PhotonView>();

        if (gameplayConfig == null)
            Debug.LogError($"[animScript] {name}: GameplayConfig is not assigned - the shoot pose has no minimum and a fast gun's motion is not sped up.");
    }

    void OnEnable()
    {
        if (weaponFiring != null)
            weaponFiring.ShotShown += OnShotShown;
    }

    void OnDisable()
    {
        if (weaponFiring != null)
            weaponFiring.ShotShown -= OnShotShown;
    }

    void Update()
    {
        // The owner works the bools out; remote copies play the ones PhotonAnimatorView streams in.
        if (photonView.IsMine)
            ApplyParams();
        else
            HoldRemoteShotPose();
    }

    private void ApplyParams()
    {
        // The held trigger is not read on purpose: a gun that cannot fire yet stays lowered.
        Vector2 move = input != null ? input.MoveAxis : Vector2.zero;
        float sinceShot = weaponFiring != null ? Time.time - weaponFiring.LastShotTime : float.PositiveInfinity;

        MechAnimParams p = MechAnimParams.For(move, MayShowShot(), sinceShot, shotPoseSeconds);
        anim.SetBool(RunParam, p.Run);
        anim.SetBool(IdleParam, p.Idle);
        anim.SetBool(ShootParam, p.Shoot);
    }

    // The owner's pose and a remote copy's held pose ask the same gate, so a stun or death cuts the pose on every screen.
    private bool MayShowShot() => MechAnimParams.MayShowShot(lifecycle == null || lifecycle.IsAlive,
                                                             weaponFiring != null && weaponFiring.enabled,
                                                             statusEffects != null && statusEffects.ShowsStunned);

    // The shot's RPC usually lands before the streamed shoot bool; without this the restarted motion blends straight back out.
    private void HoldRemoteShotPose()
    {
        if (!MayShowShot())
            anim.SetBool(ShootParam, false);
        else if (ShootMotion.HoldsPose(Time.time - lastShotSeen, shotPoseSeconds))
            anim.SetBool(ShootParam, true);
    }

    private void OnShotShown(float fireIntervalSeconds)
    {
        lastShotSeen = Time.time;
        float clipSeconds = shootClip != null ? shootClip.length : 0f;
        float maxSpeed = gameplayConfig != null ? gameplayConfig.ShootMotionMaxSpeed : 1f;
        float blend = gameplayConfig != null ? gameplayConfig.ShootMotionBlendSeconds : 0f;
        float minimumPose = gameplayConfig != null ? gameplayConfig.ShootPoseMinimumSeconds : 0f;
        float speed = ShootMotion.Speed(clipSeconds, fireIntervalSeconds, maxSpeed);
        shotPoseSeconds = ShootMotion.HoldSeconds(clipSeconds, speed, minimumPose);

        anim.SetFloat(ShootSpeedParam, speed);
        anim.CrossFadeInFixedTime(ShootState, blend, 0, 0f);
    }
}
