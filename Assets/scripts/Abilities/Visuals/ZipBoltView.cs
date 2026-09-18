using Overpower.Combat;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The zip gun's hook in flight (ability visuals step 7; Tudor: "too small and round - at least a square"): a cube
    /// head facing its flight, and a rope back to the shooter's muzzle. Every client already spawns this projectile
    /// locally from the cast RPC, so every client draws the same.
    ///
    /// A4 (Tudor 2026-09-17 evening, gameplay change): the hook's hit size grows by 50% - Zip Gun.prefab's own
    /// Projectile Radius goes from 0.15 to 0.225 m - and the head below is sized to match the new 0.45 m hit
    /// diameter exactly, so the look finally tells the truth about the hit instead of being a purely cosmetic
    /// oversize. The hit itself is still ProjectileMotor's sphere of that same Projectile Radius field
    /// (ProjectileMotor.cs:179) - this class never reads or touches it.
    ///
    /// Visual only beyond that: an IProjectileBehaviour that never keeps a shot flying and never touches the sweep.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZipBoltView : MonoBehaviour, IProjectileBehaviour
    {
        [SerializeField, Tooltip("Colour of the hook head, its rope and the square anchor where it bites (the zip gun " +
                 "reads it from here). Not a team colour: ability projectiles keep one fixed look.")]
        private Color hookColor = new Color(1f, 0.8f, 0.2f, 1f);

        [SerializeField, Tooltip("The cube head. Its size is its transform scale on this prefab - matches the real " +
                 "hit diameter (A4): Zip Gun.prefab's Projectile Radius x2.")]
        private Renderer head;

        [SerializeField, Tooltip("The rope from the shooter's muzzle to the head while it flies.")]
        private LineRenderer rope;

        [SerializeField, Range(0f, 1f), Tooltip("Rope opacity.")]
        private float ropeOpacity = 0.8f;

        private WeaponFiring shooterWeapon;
        private Vector3 fallbackStart;

        public Color HookColor => hookColor;

        public void OnSpawned(ProjectileMotor motor, ProjectileContext context)
        {
            fallbackStart = transform.position;
            PhotonView shooter = PlayerLookup.GetPhotonViewFor(context.ShooterActorNumber);
            shooterWeapon = shooter != null ? shooter.GetComponent<WeaponFiring>() : null;

            VisualTint.SetMeshColor(head, new MaterialPropertyBlock(), hookColor);
            VisualTint.SetLineColor(rope, VisualTint.WithAlpha(hookColor, ropeOpacity));
            UpdateRope();
        }

        private void LateUpdate() => UpdateRope();

        private void UpdateRope()
        {
            if (rope == null)
                return;
            rope.SetPosition(0, shooterWeapon != null ? shooterWeapon.MuzzlePosition : fallbackStart);
            rope.SetPosition(1, transform.position);
        }

        public ProjectileHitResponse OnHit(ProjectileMotor motor, ProjectileContext context, RaycastHit hit, IDamageable victim) =>
            ProjectileHitResponse.Despawn;

        public void OnExpired(ProjectileMotor motor, ProjectileContext context) { }
    }
}
