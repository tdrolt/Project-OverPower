using System;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure static events - no Photon, no MonoBehaviour - that fire ONLY on the machine of the
    /// player who actually earned the damage or the takedown, never on a victim's or a bystander's
    /// client. That is what makes this safe to be static: unlike PlayerHealth.Damaged (which fires
    /// on the victim, once per victim per client), there is exactly one machine per hit that should
    /// ever raise these, so there is no cross-player ambiguity a singleton would normally risk.
    /// UnityEngine is used only for Transform/Vector3 below - still no Photon and no MonoBehaviour.
    ///
    /// Two paths raise these today:
    ///  - PlayerCombatCredit.RPC_DamageCredit, on the attacker's own machine, once the victim's
    ///    client has told it (over the network) how much it actually dealt - see that file for why
    ///    the attacker cannot know this any other way (PlayerHealth.ApplyDamage is victim-side).
    ///  - DummyTarget, single-client only: the local player's own hits on a test-range dummy need
    ///    no network round trip at all, so it raises these directly.
    ///
    /// Task 1.5 (armor recharge is already wired through NoteDealtDamage, not these events), 1.7
    /// (zip gun cooldown reset on takedown) and 1.11 (ultimate charge) are the intended subscribers.
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

        /// <summary>Mark plan step 2: raised on the attacker's machine with the damage that actually
        /// LANDED on `victim` (the victim's own truth) - `cashedMark` is false until mark step 4 wires
        /// a real mark. Two raisers: PlayerCombatCredit.RPC_DamageCredit (a real, networked victim,
        /// `transform` being that victim as THIS attacker sees it - the RPC runs on the victim's own
        /// object) and DummyTarget (single-client, its own transform). DamageNumberView is the one
        /// subscriber: this is "how much", paired with LocalImpactSeen below for "where".</summary>
        public static event Action<Transform, float, bool> LocalHitReported;

        /// <summary>Mark plan step 2 (Tudor's override, answer 2): raised on the SHOOTER's own machine
        /// the instant its own shot is simulated landing on `victim` - before any credit message could
        /// possibly have arrived, since that's a separate round trip. A real player's PlayerHealth
        /// raises this on a copy it does NOT own, from inside ApplyDamage, BEFORE that method's
        /// IsMine/isDead return - the one place the shooter's own client ever sees where its shot hit.
        /// A DummyTarget raises it unconditionally (it has no owner to be "not mine" about). Not the
        /// same information as LocalHitReported: this carries no damage amount, only a point, and can
        /// arrive on its own with nothing to report yet (a shot that gets blocked, or against a target
        /// no local weapon actually damaged).</summary>
        public static event Action<Transform, Vector3> LocalImpactSeen;

        public static void RaiseDamageDealt(float amount) => LocalDamageDealt?.Invoke(amount);

        public static void RaiseTakedown(bool isKill) => LocalTakedown?.Invoke(isKill);

        public static void RaiseHitReported(Transform victim, float amount, bool cashedMark) =>
            LocalHitReported?.Invoke(victim, amount, cashedMark);

        public static void RaiseImpactSeen(Transform victim, Vector3 hitPoint) =>
            LocalImpactSeen?.Invoke(victim, hitPoint);
    }
}
