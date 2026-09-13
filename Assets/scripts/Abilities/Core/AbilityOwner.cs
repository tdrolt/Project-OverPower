using Photon.Pun;
using UnityEngine;
using Overpower.Net;
using Overpower.Weapons;

namespace Overpower.Abilities
{
    /// <summary>
    /// The player an ability belongs to, with every component a module might need looked up exactly
    /// once, when the player spawns. Modules read Owner.Motor instead of calling GetComponent: a
    /// flamethrower ticking every frame for nine players would otherwise search the player's
    /// components nine times a frame for something that never changes.
    ///
    /// One instance per player, shared by all three of that player's modules (AbilityRunner builds
    /// it). A class, not a struct, so every module sees the same cached references.
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

        /// <summary>Null until Task 1.6 adds PlayerDisplacement to the player root. Found here
        /// automatically once it exists, so no module or runner edit is needed then. A dash must
        /// check for null rather than assume it.</summary>
        public readonly IDisplaceable Displacement;

        // Task 1.11: the ultimate-charge component goes here as one more cached reference. Not
        // declared yet on purpose - the type does not exist, and a placeholder type invented now
        // would only have to be deleted then.

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
        }
    }
}
