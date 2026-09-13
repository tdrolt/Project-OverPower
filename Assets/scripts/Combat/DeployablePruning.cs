using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// "Keep the newest N by placement order" - portals today (maxPortals 2), and whatever future
    /// deployable needs the same rule (a mine cap, say). Pure so "placing a third portal destroys
    /// the FIRST one, never the second" is provable without a scene or PhotonNetwork.Destroy.
    ///
    /// Takes placement order as plain ints (a Seq counter - see Portal.Seq) rather than the
    /// deployables themselves, so it never needs to know what a Portal, a PhotonView or a
    /// GameObject is - the caller maps the returned seqs back to real objects to destroy.
    /// </summary>
    public static class DeployablePruning
    {
        /// <summary>
        /// Given every seq currently owned (in any order, no duplicates expected - a placement
        /// counter only ever increases), returns the OLDEST (smallest) ones that must be removed to
        /// bring the count down to maxCount. Empty when nothing is over the cap. maxCount &lt;= 0 is
        /// treated as 0 (destroy everything) rather than "no limit" - a designer setting it to 0 by
        /// mistake should see the effect immediately, not silently keep unlimited portals.
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
