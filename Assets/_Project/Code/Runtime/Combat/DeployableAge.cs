namespace Overpower.Combat
{
    /// <summary>
    /// How old a networked deployable really is, from a placement timestamp that travels in
    /// instantiationData, so "a late joiner computes the same age as everyone else" is provable
    /// without PhotonNetwork or a scene.
    ///
    /// WHY NOT PhotonNetwork.Time - info.SentServerTime: a late joiner replaying a cached
    /// PhotonNetwork.Instantiate event does NOT get the original SentServerTime (measured, see
    /// two-client-harness.md ss7): it read as just-placed ~23 s after the fact, so a mine reported
    /// Age 0.00 with its full Lifetime Seconds ahead. Instead the placer's
    /// PhotonNetwork.ServerTimestamp is the LAST element of instantiationData
    /// (NetworkedDeployable.Spawn appends it; OnPhotonInstantiate strips it before a subclass's
    /// OnPlaced), and this class turns "placed at" and "now" into seconds.
    ///
    /// UNCHECKED 32-BIT SUBTRACTION IS THE POINT: ServerTimestamp is a 32-bit signed int that wraps
    /// about every 49.7 days, and plain int subtraction wraps the same way, so nowMs - placedMs is
    /// still the true gap across the wrap while the real gap is under ~24.8 days (a deployable lives
    /// seconds). A checked subtraction would throw on exactly the placements that most need this.
    ///
    /// CLAMPED AT ZERO: a negative result is only clock/RTT noise around placement, or a redundant
    /// OnPhotonInstantiate replay of a client's own just-sent instantiate, never a real negative age.
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
