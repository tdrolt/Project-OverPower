namespace Overpower.Telemetry
{
    /// <summary>
    /// Buckets continuous damage (DamageSource.Burn - status burn and FireField's DoT both use it)
    /// between flushes, instead of one `hit` line per tick. Status burn and DummyTarget both tick
    /// their burn in Update, once per rendered frame - measured at ~129 lines/second for a single
    /// burning victim at Editor framerate (T3 review), which a 9-player match with several burns
    /// running would turn into on the order of 100k+ lines. Ticks are merged in as they land;
    /// Flush-equivalent is Reset (the caller reads the fields first, then calls Reset) so the same
    /// instance is reused for the next bucket rather than reallocated.
    ///
    /// Plain C#, no Unity types - pure and unit tested without touching the engine, same reasoning
    /// as DamageResolver/StatusEffectState.
    /// </summary>
    public sealed class DotAccumulator
    {
        public int Ticks { get; private set; }
        public float RawSum { get; private set; }
        public float ArmorSum { get; private set; }
        public float HealthSum { get; private set; }
        public double FirstT { get; private set; }
        public double LastT { get; private set; }

        /// <summary>False once Reset() has run (or before the first Merge) - a fresh or just-flushed
        /// bucket has nothing worth writing a `dot` line for.</summary>
        public bool HasData => Ticks > 0;

        /// <summary>Adds one tick's damage. t is the match-clock time the tick landed - the first
        /// Merge after a Reset sets FirstT; every Merge (including the first) updates LastT.</summary>
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

        /// <summary>Back to empty, ready to be reused for the next bucket at this same key - see the
        /// class comment on why this is Reset rather than a fresh allocation.</summary>
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
