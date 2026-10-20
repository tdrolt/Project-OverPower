using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The zip gun's hook in flight: a small silver harpoon facing its flight, and a straight segmented silver cable back to the shooter's
    /// muzzle whose chevrons run toward the target (ZipCableLook). Every client already spawns this projectile locally from the cast RPC,
    /// so every client draws the same. The harpoon spans the real hit sphere along its flight (its back at the sphere's back, its point at
    /// the front), so the look tells the truth about the hit; the hit itself is ProjectileMotor's `radius`, which this class never reads or
    /// touches. Visual only beyond that: an IProjectileBehaviour that never keeps a shot flying and never touches the sweep.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZipBoltView : MonoBehaviour, IProjectileBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Tooltip("Where the harpoon's and cable's colour (the Zip Gun row of the look table), the cable's material and its " +
                 "sizes come from: Assets/Gameplay/Config/UiTheme.asset and its Arena Look. Not a team colour: movement abilities are silver.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The harpoon head. Its origin is the back of its shaft, where the cable ties on; the cable ends there.")]
        private Renderer head;

        [SerializeField, Tooltip("The cable from the shooter's muzzle to the back of the harpoon while it flies.")]
        private LineRenderer rope;

        private WeaponFiring shooterWeapon;
        private Vector3 fallbackStart;
        private ArenaLook look;
        private Color colour = Color.white;
        private MaterialPropertyBlock block;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            fallbackStart = transform.position;
            PhotonView shooter = PlayerLookup.GetPhotonViewFor(context.ShooterActorNumber);
            shooterWeapon = shooter != null ? shooter.GetComponent<WeaponFiring>() : null;

            look = theme != null ? theme.arenaLook : null;
            if (theme != null)
                colour = LookColours.ForAbility(theme, context.AbilityId, context.ShooterTeamId, LookFamily.Silver);
            if (block == null)
                block = new MaterialPropertyBlock();
            if (head != null)
            {
                float glow = look != null ? look.abilityShotGlow : 1f;
                head.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                block.SetColor(EmissionColorId, new Color(colour.r * glow, colour.g * glow, colour.b * glow, 1f));
                head.SetPropertyBlock(block);
            }
            ZipCableLook.Configure(rope, look);
            UpdateRope();
        }

        private void LateUpdate() => UpdateRope();

        private void UpdateRope()
        {
            if (rope == null)
                return;
            rope.SetPosition(0, shooterWeapon != null ? shooterWeapon.MuzzlePosition : fallbackStart);
            rope.SetPosition(1, head != null ? head.transform.position : transform.position);
            float scroll = look != null ? ZipCableLook.Scroll(Time.time, look.zipChevronSpeed, look.zipChevronSpacing) : 0f;
            if (block == null)
                block = new MaterialPropertyBlock();
            ZipCableLook.Paint(rope, block, colour, scroll);
        }

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim) =>
            ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
