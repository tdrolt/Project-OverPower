using Photon.Pun;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// A wall of cover dropped by Deployable Cover. It blocks projectiles in BOTH directions, the caster's own included,
    /// because the hit points are the point: the enemy chooses between spending damage on the wall or on the player behind
    /// it. Destroyed at Hit Points absorbed or Lifetime Seconds, whichever comes first. DeployableCoverAbility decides
    /// WHEN and WHERE; everything else lives here, on the prefab.
    /// The Building-layer collider (no Rigidbody, like every arena wall) does the blocking for free for ProjectileMotor,
    /// Hitscan and player movement, so it blocks MOVEMENT too; revisit after a playtest if allies should walk through.
    /// A FRIENDLY SHOT STILL STOPS: ProjectileMotor.FliesThrough, ExplodeOnImpact.IsFriendly and BeamResolver.PassesThrough
    /// ask FriendlyFire.IsSelfOrTeammate against THIS OBJECT'S ActorNumber/TeamId, both -1 (never a real shooter or team),
    /// so every check reads "not friendly" and the shot acts as if it hit a plain wall. ApplyDamage then decides via
    /// OwnerTeam whether the stop costs HP: identity for "do you stop here", OwnerTeam for "does it cost HP".
    /// The through-walls laser (IgnoreWalls leaf) never asks Physics about the Building layer, so BeamResolver never sees
    /// this collider and it passes through on purpose; the base laser is blocked like a bullet and damages the cover.
    /// HP IS OWNER-AUTHORITATIVE, NOT SYNCED (like PlayerHealth): every client's local projectile calls ApplyDamage on its
    /// own copy (victim-side hit detection), but only the copy with photonView.IsMine spends HP or destroys, and
    /// PhotonNetwork.Destroy removes it for everyone. ACCEPTED TRADEOFF: a remote collider can briefly outlive the
    /// owner's destroyed copy, so a shot there may stop at cover that is a moment later gone.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CoverWall : NetworkedDeployable, IDamageable, IStructure
    {
        /// <summary>Metres this collider's bottom sits above the placement ground point, shared with
        /// DeployableCoverAbility's placement check so the "is this spot free" box and the blocking box agree. Not a
        /// design tunable (like TeleportAbility's BlockCheckBottom/Top): a few cm so it never clips an uneven floor.</summary>
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

        /// <summary>Hit Points, read-only - the shop's pop-up shows it.</summary>
        public float HitPoints => hitPoints;

        // ---- IDamageable ----------------------------------------------------------------------

        /// <summary>Fails open, on purpose - see the class comment. Never a real actor or a real
        /// team, so no shooter's FriendlyFire check can ever read this wall as "mine" or "my
        /// team".</summary>
        public int ActorNumber => -1;
        public int TeamId => -1;

        public bool IsAlive => damageState == null || !damageState.Destroyed;

        // ---- IStructure (marker only, no members - see its own class comment) -----------------
        // Without this, cover's fails-open -1/-1 identity let a mine trip and blast on it (MineTargeting) and a
        // piercing laser punch through it (BeamResolver); cover is a structure, not a combatant.

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
        /// Victim-side, like PlayerHealth.ApplyDamage: every client's local hit calls this, only the owner's copy
        /// (photonView.IsMine) acts. An own-team shot was already stopped by the collider; this decides only whether that
        /// stop ALSO costs HP, using the stored OwnerTeam rather than the fails-open identity above
        /// (FriendlyFire.IsSelfOrTeammate with the shooter and the wall's owner swapped into (shooter, target)).
        /// </summary>
        public DamageResult ApplyDamage(in DamageInfo info)
        {
            // Backstop (FAIL #15): a copy that arrived already past Lifetime Seconds must never absorb damage; IsExpired
            // already disabled the BoxCollider, this is the same belt-and-suspenders check Mine and ElectricFence use.
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

            // Through the shared guard, not PhotonNetwork.Destroy: this HP destroy and the base lifetime timer are
            // independent paths that can end this object in the same window (the race RequestDestroy guards; Mine does the same).
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
