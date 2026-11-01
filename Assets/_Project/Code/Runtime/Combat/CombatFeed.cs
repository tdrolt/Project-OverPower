using System;
using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// What one body earned in a fight, keyed by its actor number, heard by that body's own components (ultimate meter, Zip
    /// refill, scoreboard tally, respawn shield). On the host the human and every bot are all controlled here, so one shared
    /// "local" event would charge them all from each other's hits. CombatEvents keeps only what the screen of the human at this
    /// keyboard shows (damage numbers, marks, blocked pops).
    /// </summary>
    public sealed class CombatFeed
    {
        private static readonly Dictionary<int, CombatFeed> feeds = new Dictionary<int, CombatFeed>();

        /// <summary>Armour + health actually removed from an enemy.</summary>
        public event Action<float> DamageDealt;
        /// <summary>true = the killing blow, false = an assist.</summary>
        public event Action<bool> Takedown;
        /// <summary>The scoreboard's share: damage and the takedown code (0 none, 1 kill, 2 assist).</summary>
        public event Action<float, int> Credit;
        /// <summary>An enemy was hit or affected: (the victim was shielded, every hit came from an effect set before the respawn).</summary>
        public event Action<bool, bool> EnemyAffected;

        public static CombatFeed For(int actor)
        {
            if (!feeds.TryGetValue(actor, out CombatFeed feed))
                feeds[actor] = feed = new CombatFeed();
            return feed;
        }

        /// <summary>Test seam: drops every feed and its listeners.</summary>
        public static void ForgetAll() => feeds.Clear();

        public void RaiseDamageDealt(float amount) => DamageDealt?.Invoke(amount);
        public void RaiseTakedown(bool isKill) => Takedown?.Invoke(isKill);
        public void RaiseCredit(float amount, int takedown) => Credit?.Invoke(amount, takedown);
        public void RaiseEnemyAffected(bool victimShielded, bool fromBeforeRespawn) => EnemyAffected?.Invoke(victimShielded, fromBeforeRespawn);
    }
}
