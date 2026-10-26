using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>Result of a single TryVent() call - see OverheatState's own "Vent" section.</summary>
    public enum VentResult
    {
        /// <summary>Not silenced right now, so there was nothing to attempt - no attempt spent.</summary>
        NotSilenced,
        /// <summary>Landed inside the window: this silence's remaining time is halved.</summary>
        Hit,
        /// <summary>Pressed before the window opened.</summary>
        Early,
        /// <summary>Pressed after the window closed.</summary>
        Late,
        /// <summary>This silence's one attempt was already spent (Early/Late/Hit) - does nothing.</summary>
        AlreadyUsed
    }

    /// <summary>The Vent attempt's outcome so far, collapsed for the HUD (Early and Late both read as
    /// "missed", see BuildUi's band). None until a press has been made or the window has closed
    /// unused.</summary>
    public enum VentOutcome
    {
        None,
        Hit,
        Missed
    }

    /// <summary>
    /// The firing resource every weapon spends, and Sprint too: that shared pool is the point,
    /// sprinting costs you the ability to shoot with neither system knowing the other. Plain C#, unit
    /// tested without the Unity engine; a MonoBehaviour wrapper calls Tick from Update.
    ///
    /// Hitting max silences the primary weapon and every ability until the bar empties back to zero (a
    /// deliberate choice over a gentler weapon-only lockout, on the condition that IsWarning exists so
    /// the silence reads as the player's own mistake; see 00-master-plan.md). How long that lasts comes
    /// from GameplayConfig's max and decay values.
    ///
    /// "Vent": a short window opens a while into a silence; pressing R (TryVent) inside it cuts the
    /// rest of the silence in half, implemented as HALVING HEAT rather than a second timer, because
    /// heat already IS the clock once decay has started (see TryVent for the condition that makes that
    /// exact).
    /// </summary>
    public sealed class OverheatState
    {
        private readonly float max;
        private readonly float decayDelay;
        private readonly float decayPerSecond;
        private readonly float warningThreshold;
        private readonly float ventDelay;
        private readonly float ventWindow;
        private readonly bool ventRandomTiming;
        private readonly float ventRandomDelayMin;
        private readonly float ventRandomDelayMax;
        private readonly System.Func<float> randomSource;

        // THIS silence's own vent delay: ventDelay in fixed mode, or a value picked from
        // [ventRandomDelayMin, ventRandomDelayMax] when the silence started (see PickVentDelay).
        // Everything that times the window reads this, so a silence keeps one window for its whole
        // duration even though the next may pick another.
        private float currentVentDelay;

        // Counts down from decayDelay every time heat is added, and only once it reaches zero
        // does Tick start removing heat. This is what makes rapid, repeated firing feel like it
        // "holds" the bar up rather than bleeding off between shots.
        private float timeSinceLastAdd;

        // How long the CURRENT silence has run: 0 the instant IsSilenced flips false -> true, advanced
        // in Tick, reset in Clear and when the silence ends. Its own field so the vent timing reads as
        // its own concept, though it moves in lockstep with timeSinceLastAdd (nothing may add heat
        // while silenced: WeaponFiring/AbilityRunner gate on CastGate before either ever runs).
        private float silenceClock;

        // This silence's single Vent attempt: set when the first TryVent() lands, whatever it returns;
        // cleared in Clear and when the silence ends.
        private bool ventAttempted;
        private bool ventHit;

        // Slack on the window's closing edge only, so silenceClock reaching the closing time through
        // several small Tick calls reads as inside the window exactly like reaching it in one Tick,
        // though the two paths can land a float ULP apart. The OPEN edge needs none (a clock a ULP
        // short just opens the window one frame later, no hit lost); at the close edge the same ULP
        // would turn a genuine on-time hit into a Late miss.
        private const float WindowCloseSlack = 0.0001f;

        public float Heat { get; private set; }

        /// <summary>0..1, for the HUD bar.</summary>
        public float Normalised => Heat / max;

        /// <summary>
        /// True from the instant Heat reaches max until it decays all the way back to 0 - not
        /// merely below max. Checking Heat alone would clear the silence the moment decay ticks
        /// heat down by any amount, which is not the punishment the designer signed off on.
        /// </summary>
        public bool IsSilenced { get; private set; }

        /// <summary>
        /// Warns that overheat is close, but only before it happens. Deliberately false while
        /// IsSilenced: the warning's job is to precede the silence, not to accompany it.
        /// </summary>
        public bool IsWarning => Heat >= warningThreshold && !IsSilenced;

        public bool CanAct => !IsSilenced;

        /// <summary>False when ventWindow &lt;= 0: Vent is off entirely (GameplayConfig tooltip).
        /// PlayerHud reads this (through PlayerOverheat) so the band never appears, rather than
        /// showing a stale Missed for the rest of every silence; see Outcome and VentBandLookRule.</summary>
        public bool VentEnabled => ventWindow > 0f;

        /// <summary>True exactly while the vent window is open: the silence clock is inside
        /// [ventDelay, ventDelay + ventWindow], both ends inclusive. A 0 window never opens.</summary>
        public bool IsVentWindowOpen => IsSilenced && WindowOpenAt(silenceClock);

        /// <summary>currentVentDelay + ventWindow + WindowCloseSlack: the window's closing instant. One
        /// shared property so WindowOpenAt and Outcome cannot drift apart on where the window ends.</summary>
        private float WindowCloseEdge => currentVentDelay + ventWindow + WindowCloseSlack;

        /// <summary>The shared inclusive-both-ends check TryVent, IsVentWindowOpen and Outcome all use,
        /// so they cannot disagree about where the window sits (see WindowCloseSlack for why only the
        /// close edge carries tolerance).</summary>
        private bool WindowOpenAt(float clock) =>
            ventWindow > 0f && clock >= currentVentDelay && clock <= WindowCloseEdge;

        /// <summary>The current attempt's outcome, for the HUD band: None for the whole silence when
        /// VentEnabled is false (a disabled Vent must never paint a band, not even a Missed one once
        /// its window edge has passed); otherwise Hit once TryVent has landed one, Missed once an
        /// Early/Late press has spent the attempt OR the window closed with nothing pressed (the HUD
        /// shows the miss immediately either way, see PlayerHud), None otherwise.</summary>
        public VentOutcome Outcome
        {
            get
            {
                if (!IsSilenced || !VentEnabled)
                    return VentOutcome.None;
                if (ventHit)
                    return VentOutcome.Hit;
                if (ventAttempted)
                    return VentOutcome.Missed;
                if (silenceClock > WindowCloseEdge)
                    return VentOutcome.Missed;
                return VentOutcome.None;
            }
        }

        /// <summary>Heat fraction (0..1 of max) the fill sits at when the vent window opens: the band's
        /// upper edge, since heat only falls while silenced. Projected from bandOriginHeat, the decay
        /// delay left, the decay rate and this silence's run time, not fixed numbers, so the band lines
        /// up with the fill even when a laser's refund lowers heat before the window opens (see
        /// Refund). Uses THIS silence's currentVentDelay, so in random mode it tracks where the window
        /// actually opened.</summary>
        public float VentBandHighFraction => max > 0f ? HeatAtSilenceTime(currentVentDelay) / max : 0f;

        /// <summary>Heat fraction (0..1 of max) the fill sits at the instant the vent window closes -
        /// the band's lower edge. See VentBandHighFraction.</summary>
        public float VentBandLowFraction => max > 0f ? HeatAtSilenceTime(currentVentDelay + ventWindow) / max : 0f;

        /// <summary>THIS silence's own vent delay: ventDelay in fixed mode, or what random mode picked
        /// when the silence started (see PickVentDelay). Read-only, for the HUD and tests.</summary>
        public float CurrentVentDelay => currentVentDelay;

        /// <summary>What the band is projected FROM (see HeatAtSilenceTime): set to Heat (max, since
        /// IsSilenced only goes false-&gt;true inside Add() when Heat reaches max) when a silence
        /// starts, and lowered by Refund while silenced and before a hit. Trap: a laser's Refund can
        /// land in the very trigger pull that caused the overheat (WeaponFiring.RefundHeatIfBeamConnects
        /// runs right after the Add that silenced the player), so a band always projected from max
        /// would be drawn as if nothing was refunded. A hit deliberately does not touch it: the band
        /// (and its hit flash) stays where the window was, not where TryVent's Heat *= 0.5f left Heat.</summary>
        private float bandOriginHeat;

        /// <summary>Heat at a point in time within THIS silence (t in seconds since IsSilenced went
        /// true): the decay math Tick uses (nothing decays until decayDelay has elapsed since the last
        /// Add), solved for an arbitrary instant. Starts from bandOriginHeat, not the current Heat,
        /// because a laser's Refund CAN lower heat mid-silence before the window opens (heat cannot
        /// rise while silenced: WeaponFiring/AbilityRunner gate on CastGate before Add runs).</summary>
        private float HeatAtSilenceTime(float t)
        {
            float decayingTime = Mathf.Max(0f, t - decayDelay);
            return Mathf.Max(0f, bandOriginHeat - decayPerSecond * decayingTime);
        }

        /// <summary>
        /// ventRandomTiming/ventRandomDelayMin/ventRandomDelayMax/randomSource are optional, so fixed
        /// mode needs none of them. randomSource returns 0..1 (e.g. () =&gt; UnityEngine.Random.value);
        /// injected rather than read directly so this stays deterministically testable plain C#.
        /// </summary>
        public OverheatState(float max, float decayDelay, float decayPerSecond, float warningThreshold,
            float ventDelay, float ventWindow, bool ventRandomTiming = false, float ventRandomDelayMin = 0f,
            float ventRandomDelayMax = 0f, System.Func<float> randomSource = null)
        {
            this.max = max;
            this.decayDelay = decayDelay;
            this.decayPerSecond = decayPerSecond;
            this.warningThreshold = warningThreshold;
            this.ventDelay = ventDelay;
            this.ventWindow = ventWindow;
            this.ventRandomTiming = ventRandomTiming;
            this.ventRandomDelayMin = ventRandomDelayMin;
            this.ventRandomDelayMax = ventRandomDelayMax;
            this.randomSource = randomSource;
            currentVentDelay = ventDelay;
        }

        /// <summary>THIS silence's vent delay: ventDelay in fixed mode (or if random mode has no source:
        /// a safe fallback, never a null reference), otherwise randomSource's next 0..1 value mapped onto
        /// [min, max] of the two random bounds; the smaller is always the minimum, so a designer
        /// swapping them in the Inspector still gets a sane range.</summary>
        private float PickVentDelay()
        {
            if (!ventRandomTiming || randomSource == null)
                return ventDelay;

            float lo = Mathf.Min(ventRandomDelayMin, ventRandomDelayMax);
            float hi = Mathf.Max(ventRandomDelayMin, ventRandomDelayMax);
            float t = Mathf.Clamp01(randomSource());
            return Mathf.Lerp(lo, hi, t);
        }

        /// <summary>Spend heat: a shot, or a second of sprinting.</summary>
        public void Add(float amount)
        {
            bool wasSilenced = IsSilenced;
            Heat = Mathf.Min(max, Heat + amount);
            timeSinceLastAdd = 0f;

            if (Heat >= max)
                IsSilenced = true;

            if (IsSilenced && !wasSilenced)
            {
                // A fresh silence just started: the vent clock and this silence's one attempt begin
                // fresh, in lockstep with timeSinceLastAdd's reset above. bandOriginHeat starts at
                // Heat, which IS max here (Heat was just clamped up to it).
                silenceClock = 0f;
                ventAttempted = false;
                ventHit = false;
                bandOriginHeat = Heat;
                currentVentDelay = PickVentDelay();
            }
        }

        /// <summary>The laser's half-cost refund when a shot connects; never below zero. While silenced
        /// and before a hit it also lowers bandOriginHeat by the amount actually applied to Heat, so the
        /// projected Vent band moves down with the fill instead of staying pinned to max. After a hit it
        /// stops touching the band: the hit flash must stay where the window was.</summary>
        public void Refund(float amount)
        {
            float before = Heat;
            Heat = Mathf.Max(0f, Heat - amount);

            if (IsSilenced && !ventHit)
            {
                float actuallyRefunded = before - Heat;
                bandOriginHeat = Mathf.Max(0f, bandOriginHeat - actuallyRefunded);
            }
        }

        public void Tick(float deltaTime)
        {
            if (IsSilenced)
                silenceClock += deltaTime;

            float previousElapsed = timeSinceLastAdd;
            timeSinceLastAdd += deltaTime;

            if (timeSinceLastAdd <= decayDelay)
                return;

            // Only the slice of this step that falls after the delay has elapsed actually
            // decays heat. Without this split, a single large Tick that straddles the delay
            // boundary would wrongly decay for its *entire* deltaTime instead of just the part
            // of it that occurs once the delay is over.
            float decayTime = timeSinceLastAdd - Mathf.Max(previousElapsed, decayDelay);
            Heat = Mathf.Max(0f, Heat - decayPerSecond * decayTime);

            if (Heat <= 0f)
            {
                IsSilenced = false;
                silenceClock = 0f;
                ventAttempted = false;
                ventHit = false;
                bandOriginHeat = 0f;
            }
        }

        /// <summary>Vent's own button: the FIRST call during a silence is the attempt, whichever of
        /// Hit/Early/Late it lands on - every later call in the same silence is AlreadyUsed and
        /// does nothing, even a later one that would otherwise have hit. Not silenced at all costs
        /// no attempt, so the next real silence still gets its own fresh one.</summary>
        public VentResult TryVent()
        {
            if (!IsSilenced)
                return VentResult.NotSilenced;

            if (ventAttempted)
                return VentResult.AlreadyUsed;

            ventAttempted = true;

            // ventWindow <= 0 turns Vent off entirely (see the field's own comment and
            // WindowOpenAt's own ventWindow > 0f guard) - so a zero-width window at exactly
            // ventDelay never reads as a (degenerate) Hit.
            if (WindowOpenAt(silenceClock))
            {
                ventHit = true;

                // The halving IS the "cut the remaining silence in half", not a parallel timer. Heat
                // decays linearly once past decayDelay (Tick), so time-to-zero is Heat / decayPerSecond
                // and halving Heat exactly halves it. Exact only once decay has started (silenceClock
                // past decayDelay): true by the time the window can open whenever ventDelay >=
                // decayDelay (GameplayConfig's tooltip says so).
                Heat *= 0.5f;
                return VentResult.Hit;
            }

            return silenceClock < currentVentDelay ? VentResult.Early : VentResult.Late;
        }

        /// <summary>On death: zero the bar and lift any silence with it.</summary>
        public void Clear()
        {
            Heat = 0f;
            IsSilenced = false;
            timeSinceLastAdd = 0f;
            silenceClock = 0f;
            ventAttempted = false;
            ventHit = false;
            bandOriginHeat = 0f;
            currentVentDelay = ventDelay; // the next silence's Add() picks its own fresh one anyway
        }
    }
}
