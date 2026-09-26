using System;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every per-tier territory number: how long a zone takes to capture, what it pays the team
    /// that owns it, what it pays out as a bounty, and how fast it heals you. One asset, shared by
    /// every tower, is the whole tuning surface for territory - a designer changes a tier's row here
    /// instead of hunting down nine towers that each used to carry their own copy of the same number.
    ///
    /// Fields are [SerializeField] private with read-only properties for the same reason as the rest
    /// of the Data folder (see GameplayConfig): a ScriptableObject is one shared instance per
    /// process, so writing to one at runtime quietly edits the asset in the Editor and does nothing
    /// in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Territory Config", fileName = "TerritoryConfig")]
    public sealed class TerritoryConfig : ScriptableObject
    {
        [Serializable]
        public struct TierSettings
        {
            [Tooltip("Seconds for ONE player standing alone to capture a neutral zone of this tier. " +
                     "Two players take half as long, three a third.")]
            public float captureSeconds;

            [Tooltip("Gold per second this zone earns the team that owns it. Each player on that team receives " +
                     "this divided by Players Per Team.")]
            public int teamGoldPerSecond;

            [Tooltip("Gold paid to EACH player of a team that captures this zone after another team held it " +
                     "uninterrupted for Bounty Hold Seconds. 0 = no bounty on this tier.")]
            public int captureBounty;

            [Tooltip("Health per second you regain while standing in a zone of this tier that your team owns, " +
                     "once you have been out of combat long enough. 0 = no regen on this tier.")]
            public float healthRegenPerSecond;

            [Tooltip("How far from the tower, in metres, a player's centre can be and still count as in the zone: " +
                     "capturing, draining, under attack, health regen and the shop all use this one circle, and the " +
                     "ring on the ground is drawn at it. Read when the match scene starts, so a change shows on the " +
                     "next Play.")]
            public float captureRadius;

            [Tooltip("Seconds an enemy standing alone in a zone of this tier that your team owns takes to drain it back " +
                     "to neutral (a defender stepping in pauses it). Compare with Capture Seconds above: lower drains " +
                     "faster than it was captured. Per tier since Tudor, 2026-09-26.")]
            [Min(0.01f)] public float decaySeconds;
        }

        [Tooltip("One row per tier. Element 0 = Tier 1 (Capital), 1 = Tier 2, 2 = Tier 3, 3 = Tier 4 (Centre).")]
        [SerializeField] private TierSettings[] tiers =
        {
            new TierSettings { captureSeconds = 20f, teamGoldPerSecond = 0,  captureBounty = 0,    healthRegenPerSecond = 10f, captureRadius = 10f, decaySeconds = 3f },
            new TierSettings { captureSeconds = 15f, teamGoldPerSecond = 5,  captureBounty = 0,    healthRegenPerSecond = 4f,  captureRadius = 8f, decaySeconds = 3f },
            new TierSettings { captureSeconds = 10f, teamGoldPerSecond = 10, captureBounty = 900,  healthRegenPerSecond = 0f,  captureRadius = 5.4f, decaySeconds = 3f },
            new TierSettings { captureSeconds = 15f, teamGoldPerSecond = 8,  captureBounty = 1200, healthRegenPerSecond = 0f,  captureRadius = 8f, decaySeconds = 3f },
        };

        [Tooltip("How many players a team's territory income is shared between. The GDD balances income per team " +
                 "and divides by 3; keep it at 3 even in a smaller test so the economy feels the same.")]
        [SerializeField, Min(1)] private int playersPerTeam = 3;

        [Tooltip("Seconds a team must hold a Tier 3 or Tier 4 zone without losing it before capturing it pays the bounty.")]
        [SerializeField, Min(0f)] private float bountyHoldSeconds = 300f;

        [Tooltip("Gold every player starts the match with. The starting kit is already free.")]
        [SerializeField, Min(0)] private int startingGold = 0;

        [Tooltip("Seconds a zone that just went neutral cannot be captured by anyone.")]
        [SerializeField, Min(0f)] private float recaptureCooldownSeconds = 5f;

        [Tooltip("Seconds a zone still counts as under attack after the last enemy steps out of it. While your capital " +
                 "is under attack you respawn at its Tier 2 zone, and no zone counts as a way in for capturing while " +
                 "it's under attack. Stops both from flickering when someone steps on and off the edge.")]
        [SerializeField, Min(0f)] private float underAttackLingerSeconds = 3f;

        [Tooltip("How fast unfinished capture progress slides back when its team has left, as a fraction of " +
                 "one player's capture speed (1 = as fast as one player builds it; 0.5 = half as fast; 0 = it " +
                 "holds until someone acts). The same speed refills an owned zone that was partly drained once " +
                 "the attackers leave. An enemy standing in the zone alone always pushes it down at least as " +
                 "fast as it could capture.")]
        [SerializeField, Min(0f)] private float captureFadeSpeed = 1f;

        [Header("Sounds")]
        [Tooltip("The looping sound a zone plays while it is being captured or drained. Off since Tudor, 2026-09-26.")]
        [SerializeField] private bool playCaptureProgressSound = false;

        [Tooltip("The sound a zone plays when a team takes control of it (the same clip also marks a zone going " +
                 "neutral). Off since Tudor, 2026-09-26.")]
        [SerializeField] private bool playCapturedSound = false;

        public int PlayersPerTeam => playersPerTeam;
        public float BountyHoldSeconds => bountyHoldSeconds;
        public int StartingGold => startingGold;
        public float RecaptureCooldownSeconds => recaptureCooldownSeconds;
        public float UnderAttackLingerSeconds => underAttackLingerSeconds;
        public float CaptureFadeSpeed => captureFadeSpeed;
        public bool PlayCaptureProgressSound => playCaptureProgressSound;
        public bool PlayCapturedSound => playCapturedSound;
        public int TierCount => tiers != null ? tiers.Length : 0;

        // Set once an out-of-range tier has already logged, so a mistyped tower spams the console
        // only once per asset instead of once per frame it stays wrong.
        [NonSerialized] private bool loggedOutOfRangeTier;

        /// <summary>Settings for tier 1..4. An out-of-range tier logs once and reads as Tier 1 so a
        /// mistyped tower stays playable rather than throwing every frame.</summary>
        public TierSettings ForTier(int tier)
        {
            if (tiers == null || tiers.Length == 0)
                return default;
            int index = tier - 1;
            if (index < 0 || index >= tiers.Length)
            {
                if (!loggedOutOfRangeTier)
                {
                    Debug.LogError($"{name}: tier {tier} does not exist (1..{tiers.Length}) - using Tier 1.", this);
                    loggedOutOfRangeTier = true;
                }
                index = 0;
            }
            return tiers[index];
        }
    }
}
