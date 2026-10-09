namespace Overpower.Combat
{
    /// <summary>
    /// The two small facts a mine's every-client FixedUpdate trigger check needs, as plain C# so they
    /// are provable without a scene or PhotonNetwork.
    ///
    /// ARMED: nothing may trigger a mine before armDelaySeconds have passed since placement, so it
    /// cannot go off at the caster's feet. The caller (Mine.cs) supplies the seconds: a late joiner
    /// replays an already-old placement, so it must come from NetworkedDeployable.Age plus real time
    /// elapsed on this client, not a stopwatch started at zero.
    ///
    /// FIRST DETONATION WINS: RPC_Detonate is AllViaServer, but a client can see its OWN trigger fire
    /// locally and then receive the very RPC that caused it; TryDetonate lets only the first call of
    /// either kind run the blast.
    /// </summary>
    public sealed class MineDetonationState
    {
        private readonly float armDelaySeconds;

        public bool Detonated { get; private set; }

        public MineDetonationState(float armDelaySeconds)
        {
            this.armDelaySeconds = armDelaySeconds;
        }

        public bool IsArmed(float secondsSincePlaced) => secondsSincePlaced >= armDelaySeconds;

        /// <summary>True only the first time this is called for this mine.</summary>
        public bool TryDetonate()
        {
            if (Detonated)
                return false;

            Detonated = true;
            return true;
        }
    }
}
