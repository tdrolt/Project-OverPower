namespace Overpower.Match
{
    /// <summary>
    /// The pure decision behind BuildingCapture.ComputeCurrentProgress (ring/minimap step 2 review fix, 2026-09-17):
    /// what CaptureProgress a zone should publish this frame, worked out from the same fields
    /// CalculateCaptureProgress/HandleCapturedState just updated. No MonoBehaviour, no Photon, no per-frame caching
    /// of its own - every branch (a moving capture, a contested or link-blocked hold, a moving or paused drain,
    /// idle/cooldown/captured-and-settled) is reachable directly from a test, which the method it replaced never
    /// was: reverting either Held branch back to Idle used to still pass every test in the project.
    /// </summary>
    public static class CaptureProgressPublishRule
    {
        /// <param name="isCaptured">The zone's own isCaptured flag.</param>
        /// <param name="isDecaying">An enemy is draining an owned zone.</param>
        /// <param name="isDrainPaused">The drain is holding because its drainers' way in is under attack - see DrainRule.</param>
        /// <param name="captureSeconds">One player's seconds to capture this zone's tier; captureProgress is tracked in the same units.</param>
        /// <param name="decaySeconds">Seconds for a full drain, tier-independent.</param>
        /// <param name="isOnCooldown">A just-neutralised zone nobody may capture yet.</param>
        /// <param name="capturingID">The team capturing, draining, or (captureFadeSpeed) fading/refilling; -1 =
        /// nobody has ever claimed this zone (the isOnCooldown/capturingID guard below catches that case).</param>
        /// <param name="eligibleCount">Players in the zone on capturingID's team (0 when the capturing team has
        /// nobody here - a completely empty zone, or only another team - which now fades rather than holding or
        /// resetting; see the eligibleCount &lt;= 0 branch below).</param>
        /// <param name="enemyPresent">Any player of another team is in the zone right now.</param>
        /// <param name="mayCaptureNow">TerritoryMap.MayCapture for capturingID this frame (a link-blocked capture reads false).</param>
        /// <param name="captureProgress">One-player-seconds banked so far.</param>
        /// <param name="nowMs">PhotonNetwork.ServerTimestamp.</param>
        /// <param name="fadeRatePerSecond">captureFadeSpeed - the tooltip's captureFadeSpeed x
        /// ProgressPerPlayerPerSecond, one-player-seconds per real second. 0 means "holds where it was" (published
        /// as an ordinary Held/Paused, not a moving-but-zero fade) - see BuildingCapture.FadeRatePerSecond.</param>
        public static CaptureProgress Decide(bool isCaptured, bool isDecaying, bool isDrainPaused, float captureSeconds,
                                              float decaySeconds, bool isOnCooldown, int capturingID, int eligibleCount,
                                              bool enemyPresent, bool mayCaptureNow, float captureProgress, int nowMs,
                                              float fadeRatePerSecond = 0f)
        {
            if (captureSeconds <= 0f)
                return CaptureProgress.Idle;

            if (isCaptured)
            {
                if (!isDecaying)
                {
                    // Not draining: either genuinely settled at full, or refilling back toward full after the
                    // drainers left instead of the old snap-to-full (captureFadeSpeed, [C], 2026-09-24) - Team is
                    // capturingID, the same stale-but-present "last drainer" field HandleCapturedState already
                    // carries through the stop (see its own comment on Step.Stop/None not clearing it).
                    if (captureProgress >= captureSeconds)
                        return CaptureProgress.Idle;

                    float refillProgress01 = captureProgress / captureSeconds;
                    float refillRate01 = fadeRatePerSecond / captureSeconds;
                    if (refillRate01 <= 0f)
                        return CaptureProgress.Held(capturingID, refillProgress01, nowMs);
                    return new CaptureProgress(capturingID, refillProgress01, refillRate01, nowMs, fading: true);
                }

                float decayProgress01 = captureProgress / captureSeconds;
                // A paused drain (its drainers' way in is under attack - see DrainRule) keeps its team and the
                // owner's remaining hold at rate 0, so every client's capture ring can show it paused. It used to
                // go Idle, which hid how far the drain had got.
                if (isDrainPaused)
                    return CaptureProgress.Held(capturingID, decayProgress01, nowMs);

                float decayRate = decaySeconds > 0f ? -1f / decaySeconds : 0f;
                return new CaptureProgress(capturingID, decayProgress01, decayRate, nowMs);
            }

            if (isOnCooldown || capturingID == -1)
                return CaptureProgress.Idle;

            float progress01 = captureProgress / captureSeconds;

            // The capturing team has nobody in the zone right now (empty, or only another team) - fades toward 0
            // instead of holding (captureFadeSpeed, [C], 2026-09-24). Checked before enemyPresent/mayCaptureNow:
            // eligibleCount <= 0 already covers "only another team present" (not contested - contested needs an
            // eligible player too), and mayCaptureNow is meaningless with nobody of capturingID's team here to ask
            // it about.
            if (eligibleCount <= 0)
            {
                if (progress01 <= 0f)
                    return CaptureProgress.Idle;
                float fadeRate01 = fadeRatePerSecond / captureSeconds;
                if (fadeRate01 <= 0f)
                    return CaptureProgress.Held(capturingID, progress01, nowMs);
                return new CaptureProgress(capturingID, progress01, -fadeRate01, nowMs, fading: true);
            }

            // Mirrors CalculateCaptureProgress's own eligibility check: only "N of my team, nobody else, and still
            // allowed to capture" actually moves the bar - otherwise CalculateCaptureProgress itself is not
            // advancing captureProgress this frame either, so the bar must not claim it is.
            if (enemyPresent || !mayCaptureNow)
                // Held, not Idle: a contested or link-blocked capture keeps what it has banked, and the ring shows
                // it paused. Capturers who LEAVE now fade instead (see the eligibleCount <= 0 branch above), so
                // this is Idle then: Held returns Idle when nothing is banked.
                return CaptureProgress.Held(capturingID, progress01, nowMs);

            float rate = eligibleCount / captureSeconds;
            return new CaptureProgress(capturingID, progress01, rate, nowMs);
        }
    }
}
