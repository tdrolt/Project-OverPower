using System;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure static events (no Photon, no MonoBehaviour) that fire ONLY on the machine of the player who
    /// earned the damage or the takedown, never on a victim's or a bystander's client. That is what
    /// makes static safe: unlike PlayerHealth.Damaged (fires on the victim, once per victim per
    /// client), exactly one machine per hit raises these, so there is no cross-player ambiguity.
    /// UnityEngine is used only for Transform/Vector3.
    ///
    /// Two paths raise these:
    ///  - PlayerCombatCredit.RPC_DamageCredit, on the attacker's own machine, once the victim's client
    ///    has told it how much it actually dealt (the attacker cannot know this any other way:
    ///    PlayerHealth.ApplyDamage is victim-side).
    ///  - DummyTarget, single-client only: the local player's hits on a test-range dummy need no
    ///    network round trip, so it raises these directly.
    ///
    /// Intended subscribers: the zip gun cooldown reset (takedown) and the ultimate charge. Armor
    /// recharge is wired through NoteDealtDamage, not these events.
    /// </summary>
    public static class CombatEvents
    {
        /// <summary>Raised with the amount of damage this client's own player just dealt (armor +
        /// health actually removed from the victim), never with zero or negative amounts.</summary>
        public static event Action<float> LocalDamageDealt;

        /// <summary>Raised when this client's own player earned credit for ending a fight - true
        /// for the kill itself, false for an assist (damage within the assist window, but someone
        /// else landed the killing blow).</summary>
        public static event Action<bool> LocalTakedown;

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

        /// <summary>Raised ONLY by PlayerCombatCredit.RPC_DamageCredit - credit for hitting a real player, never a
        /// test-range dummy (DummyTarget raises the two events above directly). The scoreboard listens to this one.
        /// takedown: 0 = damage only, 1 = kill, 2 = assist.</summary>
        public static event Action<float, int> LocalPlayerCredit;

        public static void RaisePlayerCredit(float amount, int takedown) => LocalPlayerCredit?.Invoke(amount, takedown);

        public static void RaiseDamageDealt(float amount) => LocalDamageDealt?.Invoke(amount);

        public static void RaiseTakedown(bool isKill) => LocalTakedown?.Invoke(isKill);

        public static void RaiseHitReported(Transform victim, float amount, bool cashedMark) =>
            LocalHitReported?.Invoke(victim, amount, cashedMark);

        public static void RaiseImpactSeen(Transform victim, Vector3 hitPoint) =>
            LocalImpactSeen?.Invoke(victim, hitPoint);

        public static void RaiseMarkReported(Transform victim, float markSecondsLeft) =>
            LocalMarkReported?.Invoke(victim, markSecondsLeft);

        public static void RaiseBlockedSeen(Transform victim) => LocalBlockedSeen?.Invoke(victim);

        /// <summary>Dominion respawn shield: a hit on the shielded player was stopped (read from their dBlk stamp, so every client that can see
        /// them raises it, not only the shooter). The seconds are how long BLOCKED stays up. Distinct from LocalBlockedSeen, the shooter's own
        /// guess at the Invulnerability ultimate, so one hit can never pop twice.</summary>
        public static event Action<Transform, float> LocalShieldBlockedSeen;

        public static void RaiseShieldBlockedSeen(Transform victim, float popupSeconds) => LocalShieldBlockedSeen?.Invoke(victim, popupSeconds);

        /// <summary>Something THIS client's own player did reached an enemy (A25) - damage (read from the victim's credit
        /// message), a stun, slow, burn or vulnerability (this client's own simulation of the effect, on its copy of the victim) or a push (the
        /// same, from the pulse). Raised only on the machine of the player who did it, so no RPC carries it. victimShielded = the enemy's own
        /// respawn shield was up (the effect was stopped, so nothing was hit); fromBeforeRespawn = the effect was set up before the attacker's
        /// respawn (a mine, a fire field, a burn: A26). The attacker's RespawnShield is the one subscriber.</summary>
        public static event Action<bool, bool> LocalEnemyAffected;

        public static void RaiseEnemyAffected(bool victimShielded, bool fromBeforeRespawn) => LocalEnemyAffected?.Invoke(victimShielded, fromBeforeRespawn);
    }
}
