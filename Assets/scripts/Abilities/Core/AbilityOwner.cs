using Photon.Pun;
using UnityEngine;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// The player an ability belongs to, every component a module needs looked up once at spawn, so a per-frame
    /// module (the flamethrower) reads Owner.Motor instead of calling GetComponent nine times a frame.
    /// One instance per player, shared by all three of that player's modules (AbilityRunner builds it); a class,
    /// not a struct, so every module sees the same cached references.
    /// </summary>
    public sealed class AbilityOwner
    {
        public readonly GameObject Root;
        public readonly PhotonView PhotonView;
        public readonly PlayerMotor Motor;
        public readonly PlayerAim Aim;
        public readonly PlayerHealth Health;
        public readonly PlayerOverheat Overheat;
        public readonly PlayerStatusEffects Status;
        public readonly PlayerLifecycle Lifecycle;
        public readonly WeaponFiring Weapon;

        /// <summary>The player root's PlayerDisplacement, found automatically; null on a root without one (a test rig), so a dash must
        /// check for null rather than assume it.</summary>
        public readonly IDisplaceable Displacement;

        /// <summary>The Space-bar meter every ultimate reads through IsReady and spends through
        /// TryBuildCast - see UltimateCharge's own class comment for why it is owner-only.</summary>
        public readonly UltimateCharge UltimateCharge;

        /// <summary>The owning player's actor number - fixed for the life of this player object.</summary>
        public int ActorNumber => PhotonView.OwnerActorNr;

        /// <summary>Read live, every time, rather than cached: the team Custom Property can arrive
        /// after the player has already spawned, and a copy taken at spawn would stay -1 forever.
        /// -1 while unknown.</summary>
        public int TeamId => Teams.TryGetTeam(PhotonView.Owner, out int teamId) ? teamId : -1;

        /// <summary>True on the machine this player belongs to - the only machine whose owner hooks
        /// (TryBuildCast, OwnerTick) ever run.</summary>
        public bool IsMine => PhotonView.IsMine;

        public AbilityOwner(GameObject root)
        {
            Root = root;
            PhotonView = root.GetComponent<PhotonView>();
            Motor = root.GetComponent<PlayerMotor>();
            Aim = root.GetComponent<PlayerAim>();
            Health = root.GetComponent<PlayerHealth>();
            Overheat = root.GetComponent<PlayerOverheat>();
            Status = root.GetComponent<PlayerStatusEffects>();
            Lifecycle = root.GetComponent<PlayerLifecycle>();
            Weapon = root.GetComponent<WeaponFiring>();
            Displacement = root.GetComponent<IDisplaceable>();
            UltimateCharge = root.GetComponent<UltimateCharge>();
        }
    }
}
