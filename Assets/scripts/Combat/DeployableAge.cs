namespace Overpower.Combat
{
    /// <summary>
    /// How old a networked deployable really is, from a placement timestamp that travelled in
    /// instantiationData - pulled out of NetworkedDeployable (Task 2.0, FAIL #15) so "a late joiner
    /// computes the same age as everyone else" is provable without PhotonNetwork or a scene.
    ///
    /// WHY THIS EXISTS AT ALL: NetworkedDeployable used to compute Age as
    /// PhotonNetwork.Time - info.SentServerTime, trusting PUN to hand a late joiner replaying a
    /// cached PhotonNetwork.Instantiate event the ORIGINAL SentServerTime. Measured on a genuinely
    /// fresh late joiner (two-client-harness.md ss7, Task 2.0): it does not - info.SentServerTime read
    /// back as though the object had just been placed, even ~23 real seconds after it actually was,
    /// so a mine (say) reported Age 0.00 and a full Lifetime Seconds of life still ahead of it. The
    /// fix carries the placer's own PhotonNetwork.ServerTimestamp explicitly, as the last element of
    /// instantiationData (NetworkedDeployable.Spawn appends it; OnPhotonInstantiate strips it back off
    /// before handing the rest to a subclass's own OnPlaced), and this class is the one place that
    /// turns "placed at" and "now" - both raw PhotonNetwork.ServerTimestamp ints - into seconds.
    ///
    /// UNCHECKED 32-BIT SUBTRACTION IS THE POINT, NOT A BUG. PhotonNetwork.ServerTimestamp is
    /// milliseconds since the game server started, carried as a 32-bit signed int that reinterprets
    /// an ever-increasing unsigned counter - it wraps from its most positive value straight to its
    /// most negative one about every 49.7 days. Plain int subtraction wraps exactly the same way, so
    /// nowMs - placedMs still lands on the true millisecond gap even when "now" and "placed" fall on
    /// opposite sides of that wrap, as long as the real gap is under the ~24.8-day half-range - a
    /// deployable's lifetime is measured in seconds, nowhere close. A checked subtraction would throw
    /// on exactly the placements that most need this to work.
    ///
    /// CLAMPED AT ZERO, NEVER NEGATIVE. A negative result only means "now" reads earlier than
    /// "placed" by less than that half-range - ordinary clock/RTT noise around the moment of
    /// placement, or a redundant OnPhotonInstantiate replay of a client's own just-sent instantiate -
    /// never a real deployable that is somehow younger than zero.
    /// </summary>
    public static class DeployableAge
    {
        public static double SecondsSince(int placedServerTimestampMs, int nowServerTimestampMs)
        {
            int deltaMs = unchecked(nowServerTimestampMs - placedServerTimestampMs);
            return deltaMs > 0 ? deltaMs / 1000.0 : 0.0;
        }
    }
}
