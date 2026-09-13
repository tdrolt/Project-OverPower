using System;

namespace Overpower.Combat
{
    /// <summary>
    /// Pure static events - no Photon, no MonoBehaviour - that fire ONLY on the machine of the
    /// player who actually earned the damage or the takedown, never on a victim's or a bystander's
    /// client. That is what makes this safe to be static: unlike PlayerHealth.Damaged (which fires
    /// on the victim, once per victim per client), there is exactly one machine per hit that should
    /// ever raise these, so there is no cross-player ambiguity a singleton would normally risk.
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

        public static void RaiseDamageDealt(float amount) => LocalDamageDealt?.Invoke(amount);

        public static void RaiseTakedown(bool isKill) => LocalTakedown?.Invoke(isKill);
    }
}
