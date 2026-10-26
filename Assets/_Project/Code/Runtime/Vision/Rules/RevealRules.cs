using System.Collections.Generic;

namespace Overpower.Vision
{
    /// <summary>When a hit on an enemy shows them to my team (X-Ray blind hits).</summary>
    public static class RevealOnHitRule
    {
        /// <summary>The shooter is on my team, the one hit is an enemy, and the weapon reveals for some time.</summary>
        public static bool ShouldReveal(bool shooterIsFriendly, bool targetIsEnemy, float seconds) =>
            shooterIsFriendly && targetIsEnemy && seconds > 0f;
    }

    /// <summary>Per-player "revealed until" times, so a blind hit shows the enemy for a short while. A later hit extends the
    /// time and never shortens it.</summary>
    public sealed class RevealTimers
    {
        private readonly Dictionary<int, float> until = new Dictionary<int, float>();

        public void Reveal(int actorNumber, float now, float seconds)
        {
            if (seconds <= 0f)
                return;
            float end = now + seconds;
            if (!until.TryGetValue(actorNumber, out float current) || end > current)
                until[actorNumber] = end;
        }

        public bool IsRevealed(int actorNumber, float now) => until.TryGetValue(actorNumber, out float end) && now < end;

        public void Clear() => until.Clear();
    }
}
