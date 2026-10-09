using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// "Keep the newest N by placement order": portals, and any future deployable needing the same
    /// rule (a mine cap, say). Pure so "placing a third portal destroys the FIRST one, never the
    /// second" is provable without a scene or PhotonNetwork.Destroy.
    ///
    /// Takes placement order as plain ints (a Seq counter, see Portal.Seq), so it never needs to know
    /// what a Portal, a PhotonView or a GameObject is; the caller maps the returned seqs back to
    /// objects to destroy.
    /// </summary>
    public static class DeployablePruning
    {
        /// <summary>The next placement number for an owner: never below what the owner's surviving
        /// deployables already hold. A module rebuilt after a rejoin restarts its counter at 0 while the
        /// room kept the old mines/portals (with higher Seqs), and "oldest" pruning would otherwise
        /// destroy the NEW one at once.</summary>
        public static int NextSeq(int current, IReadOnlyList<int> existingSeqs)
        {
            int next = current;
            for (int i = 0; i < existingSeqs.Count; i++)
                if (existingSeqs[i] + 1 > next)
                    next = existingSeqs[i] + 1;
            return next;
        }

        /// <summary>
        /// The OLDEST (smallest) seqs that must be removed to bring the count down to maxCount (seqs in
        /// any order, no duplicates expected); empty when nothing is over the cap. maxCount &lt;= 0 means
        /// 0 (destroy everything), not "no limit": a designer setting it to 0 by mistake should see the
        /// effect immediately.
        /// </summary>
        public static List<int> OverflowBySeq(IReadOnlyList<int> existingSeqs, int maxCount)
        {
            var sorted = new List<int>(existingSeqs);
            sorted.Sort();

            int keep = Mathf.Max(0, maxCount);
            int overflow = sorted.Count - keep;
            if (overflow <= 0)
                return new List<int>();

            return sorted.GetRange(0, overflow);
        }
    }
}
