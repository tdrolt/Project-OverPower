using System.Collections.Generic;

namespace Overpower.Combat
{
    /// <summary>
    /// One victim's running tally of "who has been hitting me and how much", kept on the victim's own
    /// client so it can periodically tell each attacker their share (PlayerCombatCredit is the only
    /// caller). Plain C# with no UnityEngine at all, unit tested without the engine.
    ///
    /// Self-damage is NOT filtered here: the ledger has no notion of whose it is, so PlayerCombatCredit
    /// filters it before Record by comparing the source actor to its owner's actor number. What IS
    /// filtered: an actor number that could never be a real attacker (zero or negative: damage nobody
    /// caused) and non-positive amounts, which would let a zeroed-out or fully-blocked hit credit an
    /// attacker for nothing.
    ///
    /// Per-actor last-hit time survives Drain() (only Clear() forgets it), because AssistersSince must
    /// still answer for an attacker whose damage an earlier Drain() already flushed: "how much do I
    /// still owe them" and "did they hit me recently enough for an assist" must not reset together.
    /// </summary>
    public sealed class DamageCreditLedger
    {
        private struct Entry
        {
            public float sum;
            public float lastHitTime;
            // ORs together across every Record call this actor makes before the next Drain, like sum:
            // one plain hit and one cashed hit in the same window still report "some of this was a
            // cashed mark", as their damage adds into one combined amount.
            public bool cashedMark;
            // True when at least one hit in this window came from something the attacker did AFTER its respawn (a direct hit, or a
            // mine/field/burn set up since); ORs together like cashedMark. The attacker's respawn shield ends only on such a hit (A26).
            public bool endsShield;
        }

        private readonly Dictionary<int, Entry> byActor = new Dictionary<int, Entry>();

        // PlayerCombatCredit's leading-edge LateUpdate flush asks "is there anything to send" every
        // frame without draining. Counting the actors with sum > 0, kept in step with Record/Drain/Clear,
        // makes that a field read instead of an allocation-per-frame scan of every entry (Decision 11).
        private int pendingCount;

        public bool HasPending => pendingCount > 0;

        /// <summary>Adds one hit's damage to its source actor's running total. Ignored for an actor
        /// number that cannot be a real attacker (<= 0) or an amount that could not have hurt anyone
        /// (<= 0); see the class comment for why self-damage is not checked here. cashedMark ORs into
        /// the actor's own flag, reset by Drain along with the sum.</summary>
        public void Record(int sourceActor, float amount, float now, bool cashedMark = false, bool endsShield = true)
        {
            if (sourceActor <= 0 || amount <= 0f)
                return;

            Entry entry = byActor.TryGetValue(sourceActor, out Entry existing) ? existing : default;
            bool wasPending = entry.sum > 0f;
            entry.sum += amount;
            entry.lastHitTime = now;
            entry.cashedMark |= cashedMark;
            entry.endsShield |= endsShield;
            byActor[sourceActor] = entry;
            if (!wasPending && entry.sum > 0f)
                pendingCount++;
        }

        /// <summary>Returns every actor with an un-flushed positive sum, then resets every sum (and
        /// cashedMark) to zero: the "tell them, then stop owing them" half of a flush. Last-hit times
        /// are kept (see the class comment), so repeated calls with no new Record calls in between
        /// return an empty list after the first.</summary>
        public IReadOnlyList<(int actor, float amount, bool cashedMark, bool endsShield)> Drain()
        {
            var drained = new List<(int actor, float amount, bool cashedMark, bool endsShield)>();

            // Collected into a separate list before writing back: mutating byActor's values while
            // enumerating it is invalid in C#.
            var actors = new List<int>(byActor.Keys);
            foreach (int actor in actors)
            {
                Entry entry = byActor[actor];
                if (entry.sum > 0f)
                {
                    drained.Add((actor, entry.sum, entry.cashedMark, entry.endsShield));
                    pendingCount--;
                }

                entry.sum = 0f;
                entry.cashedMark = false;
                entry.endsShield = false;
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
        public void Clear()
        {
            byActor.Clear();
            pendingCount = 0;
        }
    }
}
