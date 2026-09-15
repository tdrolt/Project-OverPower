using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// A wall of cover, dropped by the Equipment slot's Deployable Cover ability - Tudor's decision
    /// of 2026-09-12: it blocks projectiles in BOTH directions, including the caster's own, because
    /// the hit points are the point - the enemy decides whether to spend damage on the wall or on
    /// the player standing behind it. Destroyed at 100 damage absorbed or 10 seconds old, whichever
    /// comes first (Task 1.8 addendum); DeployableCoverAbility only decides WHEN and WHERE one gets
    /// placed, everything about what it does once it exists lives here, on the prefab.
    ///
    /// THE COLLIDER DOES THE BLOCKING FOR FREE. This sits on the Building layer, no Rigidbody - the
    /// exact layer every wall in the arena already sits on - so ProjectileMotor's sweep, Hitscan's
    /// ray and a player's own movement all stop at it without one new line in any of those systems,
    /// the plan's own "existing wall-blocking logic applies to it for free". That also means it
    /// blocks player MOVEMENT, not only shots - a Building collider does not know the difference -
    /// which Tudor can revisit after a playtest if allies should be able to walk through their own
    /// cover.
    ///
    /// WHY A FRIENDLY SHOT STILL STOPS HERE, DESPITE "BOTH DIRECTIONS" SOUNDING LIKE THE OPPOSITE OF
    /// FRIENDLY FIRE. ProjectileMotor.FliesThrough, ExplodeOnImpact.IsFriendly and
    /// BeamResolver.PassesThrough all ask FriendlyFire.IsSelfOrTeammate against THIS OBJECT'S OWN
    /// ActorNumber/TeamId, both -1 below - a value that can never equal a real shooter's actor number
    /// or a real team, so every one of those checks reads "not friendly" and the shot behaves exactly
    /// like it hit a plain wall. That is the fails-open trick that makes even the wall's own owner's
    /// bullets stop at it. ApplyDamage then separately decides, using OwnerTeam, whether that stopped
    /// shot actually costs the wall any HP. Identity for "do you stop here", OwnerTeam for "does it
    /// cost HP" - two different questions, on purpose, per the Task 1.8 addendum.
    ///
    /// THE THROUGH-WALLS LASER (13) PASSES THROUGH COVER, ON PURPOSE - it is on the Building layer
    /// like every other wall, and that laser leaf (IgnoreWalls) never asks Physics about the Building
    /// layer at all, so BeamResolver never even sees this collider. The base laser (11/12) has no
    /// such leaf, so it is blocked here exactly like a bullet, and damages the cover through the same
    /// Hitscan.Fire -> ApplyDamage call every other Projectile-source hit already uses.
    ///
    /// HP IS OWNER-AUTHORITATIVE, NOT SYNCED - the same rule PlayerHealth follows. Every client's own
    /// local projectile simulation calls ApplyDamage on its own local copy of this object (victim-
    /// side hit detection, see ProjectileMotor's class comment), but only the copy where
    /// photonView.IsMine is true ever spends HP or destroys anything; PhotonNetwork.Destroy then
    /// removes it for everyone. Nobody but the owner ever reads the HP, so nothing needs to
    /// replicate it. ACCEPTED TRADEOFF, worth stating rather than hiding: a remote client's own
    /// collider can briefly outlive the owner's already-destroyed copy, so a shot on that machine can
    /// be seen stopping at cover that is, a moment later, gone.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CoverWall : NetworkedDeployable, IDamageable, IStructure
    {
        /// <summary>How far, in metres, this wall's own collider bottom sits above the ground point
        /// it was placed at - shared with DeployableCoverAbility's own placement check (as
        /// CoverWall.GroundLift), so the box that decides "is this spot free" and the box that
        /// actually blocks things agree on the same geometry. Not a design tunable, the same
        /// reasoning as TeleportAbility's BlockCheckBottom/Top: a few centimetres of clearance so the
        /// collider never clips into a sloped or slightly uneven floor, nothing a designer would ever
        /// balance gameplay against.</summary>
        public const float GroundLift = 0.1f;

        [Header("Cover")]
        [SerializeField, Tooltip("Metres wide, across the wall's own face - how much of a lane it " +
                 "blocks. Tudor's spec: 3m.")]
        private float width = 3f;

        [SerializeField, Tooltip("Metres tall, measured up from its lifted base. Tudor's spec: 2m - " +
                 "taller than a player, so nothing shoots over it.")]
        private float height = 2f;

        [SerializeField, Tooltip("Metres thick, front to back. Not one of Tudor's numbers - purely " +
                 "how solid the wall reads and how much clearance DeployableCoverAbility's placement " +
                 "check has near the caster's own body; thin enough that it never eats into the " +
                 "arena.")]
        private float thickness = 0.4f;

        [SerializeField, Tooltip("Total Projectile/Splash damage this cover can absorb before it is " +
                 "destroyed - Tudor's spec: 100. A Burn or a Zone tick washing over it does nothing " +
                 "(see ApplyDamage) - this is cover from bullets, not a punching bag for a DoT.")]
        private float hitPoints = 100f;

        private BoxCollider box;
        private CoverDamageState damageState;

        /// <summary>Read by DeployableCoverAbility.IsBlocked so its placement check uses the exact
        /// same size this prefab actually blocks with - one home for the number, not a second copy
        /// on the ability.</summary>
        public float Width => width;
        public float Height => height;
        public float Thickness => thickness;

        // ---- IDamageable ----------------------------------------------------------------------

        /// <summary>Fails open, on purpose - see the class comment. Never a real actor or a real
        /// team, so no shooter's FriendlyFire check can ever read this wall as "mine" or "my
        /// team".</summary>
        public int ActorNumber => -1;
        public int TeamId => -1;

        public bool IsAlive => damageState == null || !damageState.Destroyed;

        // ---- IStructure (marker only, no members - see its own class comment) -----------------
        // Task 1.8b review fix: without this, cover's fails-open -1/-1 identity (above) let a mine
        // treat it as just another enemy to trip and blast (MineTargeting), and let a piercing laser
        // treat it as just another target to punch through (BeamResolver) - both wrong, since cover
        // is a structure, not a combatant.

        /// <summary>Same rule PlayerHealth exposes: only the owner's own machine is authoritative
        /// over this object's HP.</summary>
        public bool HasLocalAuthority => photonView.IsMine;

        private void Awake()
        {
            box = GetComponent<BoxCollider>();
            ApplyDimensions();
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.1f, width);
            height = Mathf.Max(0.1f, height);
            thickness = Mathf.Max(0.05f, thickness);
            hitPoints = Mathf.Max(1f, hitPoints);

            if (box == null)
                box = GetComponent<BoxCollider>();
            ApplyDimensions();
        }

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            damageState = new CoverDamageState(hitPoints);
            LogPlaced();
        }

        /// <summary>
        /// Victim-side, exactly like PlayerHealth.ApplyDamage - every client's own local hit calls
        /// this, and only the owner's own copy (photonView.IsMine) does anything at all. The own-
        /// side check runs before CoverDamageState ever sees the hit: an own-team shot has already
        /// been stopped by the collider itself (see the class comment), so this is only deciding
        /// whether that stop ALSO costs the wall HP, using the wall's stored OwnerTeam rather than
        /// its own fails-open IDamageable identity above - FriendlyFire.IsSelfOrTeammate is reused
        /// here with the shooter and the wall's owner swapped into its (shooter, target) shape.
        /// </summary>
        public DamageResult ApplyDamage(in DamageInfo info)
        {
            // Defensive backstop (NetworkedDeployable's own class comment, FAIL #15): a copy that
            // arrived already past Lifetime Seconds - the narrow cache-removal/destroy race, not the
            // normal path - must never absorb damage. IsExpired already disabled this wall's own
            // BoxCollider, which should already keep any real shot from reaching this call at all;
            // this is the same belt-and-suspenders check Mine.FixedUpdate and ElectricFence.FixedUpdate
            // use for their own per-frame logic.
            if (IsExpired)
                return default;

            if (!photonView.IsMine || damageState == null || damageState.Destroyed)
                return default;

            bool ownSide = FriendlyFire.IsSelfOrTeammate(info.SourceActorNumber, OwnerActor,
                                                          info.SourceTeamId, OwnerTeam);
            if (ownSide)
                return default; // Blocked outright; no HP cost from your own side of the fight.

            float healthLost = damageState.ApplyDamage(info.Amount, info.Source);
            if (healthLost <= 0f)
                return default; // Wrong source (Burn/Zone) or nothing left to absorb.

            // Through the shared guard, not PhotonNetwork.Destroy directly: this HP-triggered
            // destroy and the base class's own lifetime timer (Lifetime Seconds, 10s) are two
            // independent paths that know nothing of each other and can both decide to end this
            // object in the same window - the exact race RequestDestroy exists to guard (see its own
            // class comment, and Mine's identical fix).
            if (damageState.Destroyed)
                RequestDestroy();

            return new DamageResult(0f, healthLost, false, damageState.Destroyed);
        }

        /// <summary>Width/Height/Thickness are the one home for this wall's size - applied to the
        /// real BoxCollider (and the Visual child, if one exists, so what you see matches what
        /// blocks you) every time they change, rather than leaving the collider's own Size a second,
        /// driftable copy of the same numbers.</summary>
        private void ApplyDimensions()
        {
            if (box == null)
                return;

            box.center = new Vector3(0f, GroundLift + height * 0.5f, 0f);
            box.size = new Vector3(width, height, thickness);

            Transform visual = transform.Find("Visual");
            if (visual != null)
            {
                visual.localPosition = box.center;
                visual.localScale = box.size;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlaced()
        {
            Debug.Log($"[COVER] placed by actor {OwnerActor} (team {OwnerTeam}) at {transform.position}");
        }
    }
}
