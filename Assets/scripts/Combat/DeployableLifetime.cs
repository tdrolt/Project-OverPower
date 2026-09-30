namespace Overpower.Combat
{
    /// <summary>
    /// Whether a networked deployable's OWNER should schedule its own lifetime-destroy timer - pulled
    /// out of NetworkedDeployable.OnPhotonInstantiate (code review finding, Task 2.1a-era pass) so
    /// "this decision never depends on whether the object's own current Age already reads as expired"
    /// is provable without PhotonNetwork or a scene.
    ///
    /// THE BUG THIS REPLACED: the destroy schedule used to be gated by an else-if against the
    /// IsExpired-hide branch - schedule the destroy UNLESS this client already thinks the object is
    /// past its Lifetime Seconds. That reads reasonable until the OWNER's own copy is the one that
    /// computes IsExpired true (a Lifetime Seconds under one frame's worth of real time, or
    /// InitializeAfterServerTimeIsReady's own calibration wait stalling past it) - then NOTHING
    /// schedules that owner's destroy at all, and Mine/ElectricFence have no other path off IsExpired
    /// either (their own FixedUpdate also early-returns on it), so the object leaks forever: hidden,
    /// collider-less, and never destroyed.
    ///
    /// This method's own signature is the fix, not just its body: it takes ONLY lifetimeSeconds and
    /// isOwnerClient - there is no isExpired parameter to accidentally wire back in. A deployable with
    /// a real Lifetime Seconds always gets its owner-side destroy scheduled; whether THIS read of Age
    /// already clears that lifetime only changes the wait to zero (NetworkedDeployable clamps
    /// remaining at Mathf.Max(0f, ...)), never whether the timer exists at all.
    /// </summary>
    public static class DeployableLifetime
    {
        public static bool ShouldScheduleOwnerDestroy(float lifetimeSeconds, bool isOwnerClient) =>
            lifetimeSeconds > 0f && isOwnerClient;
    }
}
