using System.Collections;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.TestRange
{
    /// <summary>
    /// A stationary stand-in for a player, so weapon damage can be MEASURED rather than asserted.
    ///
    /// Every balance number in the design derives from one figure: how long the baseline weapon
    /// takes to kill a full-health player. Working that out on paper gives an answer that quietly
    /// assumes the fire interval, the armor rule and the damage order are all what you think they
    /// are. This target prints the real number, and every later weapon is tuned against it.
    ///
    /// It takes damage through the same DamageResolver and the same ArmorState a real player does,
    /// with health and armor read from the same two config assets - so a change to either config
    /// moves the dummy and the player together, and the measurement stays honest.
    ///
    /// Not networked, and deliberately so: it exists to be shot at in a single Editor session.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DummyTarget : MonoBehaviour, IDamageable
    {
        [SerializeField, Tooltip("Match tuning asset. Max Health comes from here, so the dummy has " +
                 "exactly as much health as a real player.")]
        private GameplayConfig gameplayConfig;

        [SerializeField, Tooltip("Armor tiers asset. The dummy wears the tier below, using the same " +
                 "absorb value a player of that tier would have.")]
        private ArmorConfig armorConfig;

        [SerializeField, Tooltip("Which armor tier this dummy wears. 0 is the tier everyone starts " +
                 "a match on, which is what the baseline time-to-kill is measured against.")]
        private int armorTier = 0;

        [SerializeField, Tooltip("The team this dummy belongs to. It must be a number no real team " +
                 "ever uses, or the no-friendly-fire rule would make it unshootable for whoever " +
                 "happened to share its team. Real teams are 0, 1 and 2.")]
        private int teamId = 99;

        [SerializeField, Tooltip("Seconds to lie dead before healing back to full and becoming a " +
                 "target again, so repeated measurements need no scene reload.")]
        private float resetDelaySeconds = 2f;

        private float health;
        private ArmorState armor;
        private bool isDead;

        // Measurement state, running from the FIRST hit rather than from spawn - the clock should
        // time the kill, not however long the tester spent lining the shot up.
        private float firstHitTime;
        private int hits;

        public bool IsAlive => !isDead;
        public int TeamId => teamId;

        /// <summary>Not a real player, so it owns no actor number. -1 can never collide with a
        /// Photon actor number, which start at 1.</summary>
        public int ActorNumber => -1;

        public float Health => health;
        public float Armor => armor.Current;

        private void Awake() => ResetToFull();

        private void ResetToFull()
        {
            health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
            armor = new ArmorState(armorConfig != null ? armorConfig.AbsorbFor(armorTier) : 0f,
                                    armorConfig != null ? armorConfig.RechargeSecondsFor(armorTier) : 6f);
            isDead = false;
            firstHitTime = 0f;
            hits = 0;
        }

        /// <summary>
        /// The same order of operations a player takes damage in, because it is literally the same
        /// resolver: vulnerability, then reduction, then armor, then health. The dummy carries
        /// neither buff, so both multipliers are passed as zero.
        /// </summary>
        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (isDead)
                return default;

            if (hits == 0)
                firstHitTime = Time.time;
            hits++;

            DamageResult result = DamageResolver.Resolve(info.Amount, info.IgnoresArmor, health,
                                                          armor.Current, 0f, 0f);
            armor.Absorb(result.ArmorAbsorbed);
            health -= result.HealthLost;

            if (result.Lethal)
            {
                isDead = true;

                // The line the whole test range exists to print.
                Debug.Log($"[TTK] killed in {Time.time - firstHitTime:F2}s after {hits} hits");

                StartCoroutine(ResetAfterDelay());
            }

            return result;
        }

        private IEnumerator ResetAfterDelay()
        {
            yield return new WaitForSeconds(resetDelaySeconds);
            ResetToFull();
        }
    }
}
