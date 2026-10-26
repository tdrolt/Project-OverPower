using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>Territory income maths from the GDD balancing sheet (p.36-38): a team earns the sum of
    /// its zones' tier income, and each player receives that divided by the team size.</summary>
    public static class GoldMath
    {
        /// <param name="ownerByZone">Owner per zone id (Neutral = nobody).</param>
        /// <param name="tierByZone">Tier 1..4 per zone id; 0 = not a zone.</param>
        /// <param name="teamGoldByTier">Element 0 = Tier 1's team gold per second.</param>
        public static int TeamIncomePerSecond(int teamId, IReadOnlyList<int> ownerByZone,
                                              IReadOnlyList<int> tierByZone, IReadOnlyList<int> teamGoldByTier)
        {
            if (teamId < 0 || ownerByZone == null || tierByZone == null || teamGoldByTier == null)
                return 0;
            int total = 0;
            int zones = Math.Min(ownerByZone.Count, tierByZone.Count);
            for (int zone = 0; zone < zones; zone++)
            {
                if (ownerByZone[zone] != teamId) continue;
                int index = tierByZone[zone] - 1;
                if (index >= 0 && index < teamGoldByTier.Count)
                    total += teamGoldByTier[index];
            }
            return total;
        }

        public static double PlayerIncomePerSecond(int teamIncomePerSecond, int playersPerTeam) =>
            teamIncomePerSecond / (double)Math.Max(1, playersPerTeam);

        /// <summary>What selling back gives you: the rate of what you paid, rounded down.</summary>
        public static int Refund(int goldSpent, double refundRate)
        {
            if (goldSpent <= 0 || refundRate <= 0) return 0;
            return (int)Math.Floor(goldSpent * Math.Min(1.0, refundRate));
        }
    }

    /// <summary>
    /// A gold balance that earns a fractional rate every frame but only ever shows whole gold. The
    /// fraction is carried rather than dropped, so 7.67 gold/sec really pays 23 gold every 3 seconds.
    /// </summary>
    public sealed class GoldAccrual
    {
        // Floating-point sums of many tiny frames land a hair under a whole number (0.99999...).
        private const double WholeTolerance = 1e-6;

        private double carry;

        public int Balance { get; private set; }

        public GoldAccrual(int startingBalance)
        {
            Balance = Math.Max(0, startingBalance);
        }

        public void Accrue(double goldPerSecond, double seconds)
        {
            if (goldPerSecond <= 0 || seconds <= 0) return;
            carry += goldPerSecond * seconds;
            int whole = (int)Math.Floor(carry + WholeTolerance);
            if (whole <= 0) return;
            Balance += whole;
            carry = Math.Max(0, carry - whole);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || amount > Balance) return false;
            Balance -= amount;
            return true;
        }

        public void Add(int amount)
        {
            if (amount > 0) Balance += amount;
        }
    }
}
