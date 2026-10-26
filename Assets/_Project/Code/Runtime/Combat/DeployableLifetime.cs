namespace Overpower.Combat
{
    /// <summary>
    /// Whether a networked deployable's OWNER should schedule its own lifetime-destroy timer, so
    /// "this never depends on whether Age already reads as expired" is provable without PhotonNetwork.
    ///
    /// THE TRAP: gating the schedule on the IsExpired-hide branch (destroy UNLESS already past
    /// Lifetime Seconds) leaks the object when the OWNER's own copy computes IsExpired true (a tiny
    /// Lifetime Seconds, or InitializeAfterServerTimeIsReady's calibration wait stalling past it):
    /// nothing schedules the destroy, and Mine/ElectricFence have no other path off IsExpired (their
    /// FixedUpdate also early-returns on it), so it stays hidden, collider-less and never destroyed.
    ///
    /// The signature is the guard: it takes ONLY lifetimeSeconds and isOwnerClient, with no isExpired
    /// to wire back in. An already-expired read only shortens the wait to zero (NetworkedDeployable
    /// clamps remaining at Mathf.Max(0f, ...)), never whether the timer exists.
    /// </summary>
    public static class DeployableLifetime
    {
        public static bool ShouldScheduleOwnerDestroy(float lifetimeSeconds, bool isOwnerClient) =>
            lifetimeSeconds > 0f && isOwnerClient;
    }
}
