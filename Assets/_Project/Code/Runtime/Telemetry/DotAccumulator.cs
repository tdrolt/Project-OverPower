namespace Overpower.Telemetry
{
    /// <summary>
    /// Buckets continuous damage (DamageSource.Burn - status burn and FireField's DoT) between flushes instead of one `hit` line per
    /// tick: burns tick in Update, once per rendered frame (~129 lines/second for one burning victim at Editor framerate), which would
    /// run to 100k+ lines in a 9-player match. The caller reads the fields, then calls Reset, so the instance is reused for the next
    /// bucket. Plain C#, no Unity types, so it is unit tested without the engine.
    /// </summary>
    public sealed class DotAccumulator
    {
        public int Ticks { get; private set; }
        public float RawSum { get; private set; }
        public float ArmorSum { get; private set; }
        public float HealthSum { get; private set; }
        public double FirstT { get; private set; }
        public double LastT { get; private set; }

        /// <summary>False for a fresh or just-flushed bucket: nothing to write a `dot` line for.</summary>
        public bool HasData => Ticks > 0;

        /// <summary>t is the match-clock time the tick landed; the first Merge after a Reset sets FirstT.</summary>
        public void Merge(double t, float raw, float armorAbsorbed, float healthLost)
        {
            if (Ticks == 0)
                FirstT = t;

            Ticks++;
            RawSum += raw;
            ArmorSum += armorAbsorbed;
            HealthSum += healthLost;
            LastT = t;
        }

        public void Reset()
        {
            Ticks = 0;
            RawSum = 0f;
            ArmorSum = 0f;
            HealthSum = 0f;
            FirstT = 0.0;
            LastT = 0.0;
        }
    }
}
