using System;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure static events (no Photon, no MonoBehaviour) for the screen of the human at this keyboard only: damage numbers, the mark
    /// diamond, blocked pops. Raise an attacker's event only when the attacker is that human (actor == PhotonNetwork.LocalPlayer's):
    /// on the host every bot is controlled here too, and a bot's hit must not pop on the human's screen. What a body EARNED (damage
    /// dealt, takedowns, scoreboard credit, an enemy affected) is not here: it goes through CombatFeed.For(actor), so the host's
    /// human and each bot hear only their own. UnityEngine is used only for Transform/Vector3.
    ///
    /// Two paths raise the hit/mark events:
    ///  - PlayerCombatCredit.RPC_DamageCredit, on the attacker's own machine, once the victim's client
    ///    has told it how much it actually dealt (the attacker cannot know this any other way:
    ///    PlayerHealth.ApplyDamage is victim-side).
    ///  - DummyTarget, single-client only: the local player's hits on a test-range dummy need no
    ///    network round trip, so it raises these directly.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>Raised on the attacker's machine with the damage that actually LANDED on victim (the
        /// victim's own truth). cashedMark is true exactly when this hit used up the attacker's own mark
        /// (MarkOutcome.Cashed). Two raisers: PlayerCombatCredit.RPC_DamageCredit (a networked victim;
        /// transform is that victim as THIS attacker sees it, since the RPC runs on the victim's own
        /// object) and DummyTarget (single-client, its own transform). DamageNumberView is the one
        /// subscriber: this is "how much", paired with LocalImpactSeen for "where".</summary>
        public static event Action<Transform, float, bool> LocalHitReported;

        /// <summary>Raised on the SHOOTER's own machine the instant its own shot is simulated landing on
        /// victim, before any credit message could have arrived (that is a separate round trip). A real
        /// player's PlayerHealth raises it on a copy it does NOT own, from inside ApplyDamage BEFORE the
        /// IsMine/isDead return: the one place the shooter's client ever sees where its shot hit. Raised
        /// even when the hit is about to read as Blocked (LocalBlockedSeen fires right after), since a
        /// Blocked pop is drawn at the impact too and needs the same fresh anchor. A DummyTarget raises it
        /// too, guarded by its own local-actor-and-point-source check, so a dummy does not re-anchor from
        /// a shot fired by someone else's simulated copy. Not the same information as LocalHitReported:
        /// it carries no damage amount, only a point, and can arrive alone (a blocked shot, or a target
        /// no local weapon actually damaged).</summary>
        public static event Action<Transform, Vector3> LocalImpactSeen;

        /// <summary>Seconds YOUR mark on victim has left, from the victim's own truth (every credit
        /// message carries this, always, after the sender check); 0 means none (cashed, expired, or the
        /// victim died). Nothing subscribes yet; it waits on the mark diamond.</summary>
        public static event Action<Transform, float> LocalMarkReported;

        /// <summary>Raised on the SHOOTER's own machine, from the same spot LocalImpactSeen fires from,
        /// when this shot's target already shows the yellow immune look. A shooter-side guess with no
        /// network, not the victim's truth: the hit that SPRINGS the trap shows nothing (the yellow
        /// bubble arrives a round trip later), and the window's edges lag by that same round trip. Never
        /// raised for a teammate (friendly fire shows nothing, shielded or not); see
        /// PlayerHealth.ApplyDamage's guard.</summary>
        public static event Action<Transform> LocalBlockedSeen;

        public static void RaiseHitReported(Transform victim, float amount, bool cashedMark) =>
            LocalHitReported?.Invoke(victim, amount, cashedMark);

        public static void RaiseImpactSeen(Transform victim, Vector3 hitPoint) =>
            LocalImpactSeen?.Invoke(victim, hitPoint);

        public static void RaiseMarkReported(Transform victim, float markSecondsLeft) =>
            LocalMarkReported?.Invoke(victim, markSecondsLeft);

        public static void RaiseBlockedSeen(Transform victim) => LocalBlockedSeen?.Invoke(victim);

        /// <summary>This client's own player left an enemy vulnerable (the Raybeam), judged on this client's copy of the enemy, for how many
        /// seconds. Raised only on the attacker's machine; the enemy's own client holds the real status.</summary>
        public static event Action<Transform, float> LocalVulnerabilitySeen;

        public static void RaiseVulnerabilitySeen(Transform victim, float seconds) => LocalVulnerabilitySeen?.Invoke(victim, seconds);

        /// <summary>Dominion respawn shield: a hit on the shielded player was stopped (read from their dBlk stamp, so every client that can see
        /// them raises it, not only the shooter). The seconds are how long BLOCKED stays up. Distinct from LocalBlockedSeen, the shooter's own
        /// guess at the Invulnerability ultimate, so one hit can never pop twice.</summary>
        public static event Action<Transform, float> LocalShieldBlockedSeen;

        public static void RaiseShieldBlockedSeen(Transform victim, float popupSeconds) => LocalShieldBlockedSeen?.Invoke(victim, popupSeconds);
    }
}
