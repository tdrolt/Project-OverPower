using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// One victim's running tally of "who has been hitting me and how much", kept on the victim's
    /// own client so it can periodically tell each attacker their share (PlayerCombatCredit is the
    /// only caller). Plain C# - no UnityEngine beyond nothing at all here, not even Mathf - for the
    /// same reason as the rest of Combat: unit tested without touching the engine.
    ///
    /// Self-damage is NOT filtered here: this ledger has no notion of whose ledger it is, so it
    /// cannot tell a self-hit from anyone else's. PlayerCombatCredit filters that before ever
    /// calling Record, by comparing the incoming source actor to its own owner's actor number. What
    /// IS filtered here is any actor number that could never be a real attacker - zero or negative,
    /// which PlayerHealth.ApplyDamage would only ever produce for damage nobody caused (e.g. a
    /// misconfigured source) - and non-positive amounts, which would otherwise let a zeroed-out or
    /// fully-blocked hit "credit" an attacker for nothing.
    ///
    /// Per-actor last-hit time survives Drain() (only Clear() forgets it), because
    /// AssistersSince must still answer correctly for an attacker whose damage was already flushed
    /// out by an earlier periodic Drain() - the two questions ("how much do I still owe them" and
    /// "did they hit me recently enough for an assist") are independent and must not reset together.
    /// </summary>
    public sealed class DamageCreditLedger
    {
        private struct Entry
        {
            public float sum;
            public float lastHitTime;
        }

        private readonly Dictionary<int, Entry> byActor = new Dictionary<int, Entry>();

        /// <summary>Adds one hit's damage to its source actor's running total. Ignored outright for
        /// an actor number that cannot be a real attacker (<= 0) or an amount that could not have
        /// hurt anyone (<= 0) - see the class comment for why self-damage is not checked here.</summary>
        public void Record(int sourceActor, float amount, float now)
        {
            if (sourceActor <= 0 || amount <= 0f)
                return;

            Entry entry = byActor.TryGetValue(sourceActor, out Entry existing) ? existing : default;
            entry.sum += amount;
            entry.lastHitTime = now;
            byActor[sourceActor] = entry;
        }

        /// <summary>Returns every actor with an un-flushed positive sum, then resets every sum to
        /// zero - the "tell them, then stop owing them" half of a flush. Last-hit times are kept
        /// (see the class comment), so calling this repeatedly with no new Record calls in between
        /// returns an empty list every time after the first.</summary>
        public IReadOnlyList<(int actor, float amount)> Drain()
        {
            var drained = new List<(int actor, float amount)>();

            // Collected into a separate list before writing back: mutating byActor's values while
            // enumerating it is invalid in C#.
            var actors = new List<int>(byActor.Keys);
            foreach (int actor in actors)
            {
                Entry entry = byActor[actor];
                if (entry.sum > 0f)
                    drained.Add((actor, entry.sum));

                entry.sum = 0f;
                byActor[actor] = entry;
            }

            return drained;
        }

        /// <summary>Every actor (other than excludeActor, normally the killer) whose most recent
        /// hit landed within window seconds of now - an assist takedown's own definition. An actor
        /// whose damage was already sent by an earlier Drain() still qualifies, since only the
        /// timing of the hit matters here, not whether it has been paid out yet.</summary>
        public IEnumerable<int> AssistersSince(float now, float window, int excludeActor)
        {
            foreach (KeyValuePair<int, Entry> kvp in byActor)
            {
                if (kvp.Key == excludeActor)
                    continue;

                if (now - kvp.Value.lastHitTime <= window)
                    yield return kvp.Key;
            }
        }

        /// <summary>Forgets every attacker entirely, sums and last-hit times alike - called once a
        /// death has been fully resolved, so the next life starts owing nobody and crediting
        /// nobody for an assist window that closed with the last fight.</summary>
        public void Clear() => byActor.Clear();
    }
}
