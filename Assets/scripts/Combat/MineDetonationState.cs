namespace Overpower.Combat
{
    /// <summary>
    /// The two small facts a mine's every-client FixedUpdate trigger check needs, pulled out as
    /// plain C# so they are provable without a scene or PhotonNetwork - the same reasoning as
    /// PortalChannelState and DeployablePruning.
    ///
    /// ARMED. A mine must not be able to detonate at the caster's own feet the instant it lands, so
    /// nothing may trigger it until at least armDelaySeconds have passed since it was placed. The
    /// caller (Mine.cs) is the one that knows how many seconds that actually is - late joiners
    /// replay an already-old placement, so "seconds since placed" has to come from
    /// NetworkedDeployable.Age plus real time elapsed on this client, not from a local stopwatch
    /// started at zero - this class only answers the comparison once it is told the number.
    ///
    /// FIRST DETONATION WINS. RPC_Detonate is sent AllViaServer, which gives every client the RPCs
    /// in the same order, but a client can still see its OWN trigger fire locally and then receive
    /// the very RPC that caused it - TryDetonate makes sure only the first call of either kind ever
    /// runs the blast, on every client, matching the addendum's "every client runs the first
    /// RPC_Detonate it receives" rule.
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

        /// <summary>True only the first time this is ever called for this mine; every later call -
        /// even for what would otherwise be a perfectly real detonation - returns false.</summary>
        public bool TryDetonate()
        {
            if (Detonated)
                return false;

            Detonated = true;
            return true;
        }
    }
}
