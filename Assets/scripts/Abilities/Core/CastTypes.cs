using UnityEngine;
using Overpower.Data;

namespace Overpower.Abilities
{
    /// <summary>
    /// Everything the caster's machine knew at the instant of the press. Built by AbilityRunner on
    /// the OWNER only, so no ability module ever reads Camera.main, the mouse or the keyboard itself -
    /// the one mistake WeaponFiring's class comment describes (reading "local" state inside an RPC,
    /// where local means the receiver) cannot be made from a module that is never handed input.
    /// </summary>
    public readonly struct CastContext
    {
        /// <summary>The player root's position.</summary>
        public readonly Vector3 Origin;

        /// <summary>Where the gun's muzzle sits - WeaponFiring.MuzzlePosition.</summary>
        public readonly Vector3 Muzzle;

        /// <summary>Flat, normalised, toward the cursor - PlayerAim.AimDirection.</summary>
        public readonly Vector3 AimDirection;

        /// <summary>Where the cursor meets the ground - PlayerAim.GroundPointUnderCursor.</summary>
        public readonly Vector3 TargetPoint;

        /// <summary>The WASD direction, camera-relative, at most length 1. Zero when standing still -
        /// since 921bb71 that is how a dash knows to fall back to the cursor first, and only to the
        /// aim direction once the cursor sits too close to the caster to mean anything
        /// (DashDirectionRule.Choose).</summary>
        public readonly Vector3 MoveDirection;

        public CastContext(Vector3 origin, Vector3 muzzle, Vector3 aimDirection, Vector3 targetPoint,
                           Vector3 moveDirection)
        {
            Origin = origin;
            Muzzle = muzzle;
            AimDirection = aimDirection;
            TargetPoint = targetPoint;
            MoveDirection = moveDirection;
        }
    }

    /// <summary>
    /// What crosses the wire for one cast, flattened into RPC_CastAbility's parameters. It is a
    /// struct here only for the modules' convenience: PUN cannot send a custom struct without
    /// PhotonPeer.RegisterType, and registering a type per ability is exactly the per-ability
    /// network plumbing this framework exists to avoid. Every module fits its cast into these six
    /// fields, or it has outgrown this framework.
    /// </summary>
    public struct CastPayload
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 Point;

        /// <summary>Any randomness the cast needs, rolled once by the caster so every client rolls
        /// the identical numbers - the same trick WeaponFiring uses for spread.</summary>
        public int Seed;

        /// <summary>Module-defined - whatever one extra whole number the module needs.</summary>
        public int IntArg;

        /// <summary>Module-defined - whatever one extra decimal number the module needs.</summary>
        public float FloatArg;
    }

    /// <summary>
    /// What ExecuteCast receives on every client, the caster included. Everything a module needs to
    /// know about WHO cast is resolved here from the RPC's own sender, never from
    /// PhotonNetwork.LocalPlayer, which inside an RPC body is the receiver.
    /// </summary>
    public readonly struct CastEvent
    {
        public readonly CastPayload Payload;

        /// <summary>0 = the cast itself. A module defines its own 1, 2, ... for later moments in
        /// the same cast: a channel completing, a channel cancelled, a sprint stopping.</summary>
        public readonly byte Phase;

        /// <summary>The caster's actor number, from PhotonMessageInfo.Sender.</summary>
        public readonly int CasterActor;

        /// <summary>The caster's team, or -1 if their team property has not arrived yet.</summary>
        public readonly int CasterTeam;

        /// <summary>True only on the caster's own machine. Anything that must happen exactly once
        /// for the whole room - spawning a networked mine, say - checks this first.</summary>
        public readonly bool IsCasterClient;

        /// <summary>How long ago the caster sent this, in seconds, never negative. Lets a projectile-
        /// like cast catch up to where it would be by now on a laggy receiver.</summary>
        public readonly float SecondsLate;

        public CastEvent(CastPayload payload, byte phase, int casterActor, int casterTeam,
                         bool isCasterClient, float secondsLate)
        {
            Payload = payload;
            Phase = phase;
            CasterActor = casterActor;
            CasterTeam = casterTeam;
            IsCasterClient = isCasterClient;
            SecondsLate = secondsLate;
        }
    }

    /// <summary>Why a module is being told to stop whatever it is doing.</summary>
    public enum InterruptReason
    {
        Died,
        Stunned,
        Silenced,

        /// <summary>The module is about to be destroyed because the loadout swapped it out.</summary>
        Unequipped
    }

    /// <summary>
    /// The HUD's (Task 1.12) entire view of one ability slot. Deliberately read-only and tiny, so the
    /// HUD can never reach into a module and change something while it is only meant to be drawing it.
    /// </summary>
    public interface IAbilityStatus
    {
        AbilityDefinition Definition { get; }
        int ChargesAvailable { get; }

        /// <summary>0 means this ability has no charges at all (a sprint that spends heat instead)
        /// - the HUD should draw no cooldown for it, not a permanently empty one.</summary>
        int MaxCharges { get; }

        /// <summary>0..1 toward the next charge; 0 when full.</summary>
        float RechargeProgress { get; }

        /// <summary>A channel, dash or sprint is running right now.</summary>
        bool IsActive { get; }
    }
}
