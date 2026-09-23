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

    /// <summary>The Vent attempt's outcome so far, collapsed for the HUD (Early and Late both just
    /// read as "missed" - see BuildUi's band). None until either a press has been made or the
    /// window has closed unused.</summary>
    public enum VentOutcome
    {
        None,
        Hit,
        Missed
    }

    /// <summary>
    /// The firing resource every weapon spends, and the one the Sprint ability spends too - that
    /// shared pool is the point: sprinting costs you the ability to shoot, without either system
    /// needing to know about the other. Plain C# for the same reason as DamageResolver and
    /// StatusEffectState: it is unit tested without touching the Unity engine, and a
    /// MonoBehaviour wrapper calls Tick from Update in a later task.
    ///
    /// The rule that makes this more than a health bar in reverse: hitting max does not just
    /// block further Add calls, it silences the primary weapon and every ability until the bar
    /// empties back to zero. With the real numbers (max 100, 25/s decay) that is up to 4 seconds
    /// of being unable to act. That was accepted deliberately over a gentler weapon-only lockout,
    /// on the condition that IsWarning exists at 80 heat so the silence reads as the player's own
    /// mistake rather than an arbitrary wall - see 00-master-plan.md.
    ///
    /// "Vent": a couple of seconds into a silence, a window opens for a short while. Press R
    /// (TryVent) inside it and the rest of the silence is cut in half - implemented as HALVING
    /// HEAT, not a second timer, because heat already IS the clock once decay has started (see
    /// TryVent's own comment for the exact condition that makes that exact).
    /// </summary>
    public sealed class OverheatState
    {
        private readonly float max;
        private readonly float decayDelay;
        private readonly float decayPerSecond;
        private readonly float warningThreshold;
        private readonly float ventDelay;
        private readonly float ventWindow;

        // Counts down from decayDelay every time heat is added, and only once it reaches zero
        // does Tick start removing heat. This is what makes rapid, repeated firing feel like it
        // "holds" the bar up rather than bleeding off between shots.
        private float timeSinceLastAdd;

        // How long the CURRENT silence has been running - starts at 0 the instant IsSilenced
        // flips false -> true, advances alongside timeSinceLastAdd in Tick, and resets to 0 both
        // in Clear and the moment the silence ends. Kept as its own field (rather than reusing
        // timeSinceLastAdd directly) so the vent window's timing reads as its own concept, even
        // though the two happen to move in lockstep for the whole silence in practice - nothing
        // may add heat while silenced (WeaponFiring/AbilityRunner gate on CastGate before either
        // ever runs), so timeSinceLastAdd is never reset mid-silence.
        private float silenceClock;

        // This silence's single Vent attempt - set the moment the first TryVent() call lands,
        // whatever it returns, and cleared with everything else in Clear/when the silence ends.
        private bool ventAttempted;
        private bool ventHit;

        // Tiny slack on the window's closing edge only, so silenceClock reaching "2.8" through
        // several small Tick calls (e.g. 2.0 + 0.4 + 0.4) reads as inside the window exactly like
        // reaching it through one Tick(2.8) call does, even though the two paths can land a float
        // ULP or two apart. Nowhere near large enough to matter at real, sub-millisecond frame
        // deltas - it exists only to make "inclusive" actually mean inclusive regardless of how
        // the caller chose to split their Tick calls. The OPEN edge needs no matching slack: a
        // clock that lands a ULP short of ventDelay just opens the window one frame later than it
        // ideally would - no hit is lost, only delayed by a frame nobody can perceive. The close
        // edge is the one where the same ULP would silently turn a genuine, on-time hit into a
        // Late miss, which is what this slack actually guards against.
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

        /// <summary>False when ventWindow &lt;= 0 - Vent off entirely (GameplayConfig's own tooltip:
        /// "0 turns Vent off entirely"). PlayerHud reads this (through PlayerOverheat) so the band
        /// never appears at all rather than reading a stale Missed for the rest of every silence -
        /// see Outcome and VentBandLookRule.</summary>
        public bool VentEnabled => ventWindow > 0f;

        /// <summary>True exactly while the vent window is open right now: the silence clock is
        /// inside [ventDelay, ventDelay + ventWindow], both ends inclusive. A 0 window can never
        /// open (see the class comment on ventWindow).</summary>
        public bool IsVentWindowOpen => IsSilenced && WindowOpenAt(silenceClock);

        /// <summary>ventDelay + ventWindow + WindowCloseSlack - the window's closing instant, with its
        /// hair of float tolerance. WindowOpenAt and Outcome both used to spell this out separately;
        /// one shared property means they can never drift apart on where the window actually ends.</summary>
        private float WindowCloseEdge => ventDelay + ventWindow + WindowCloseSlack;

        /// <summary>The shared inclusive-both-ends range check TryVent, IsVentWindowOpen and Outcome
        /// all use, so they can never disagree about where the window sits (see WindowCloseSlack's
        /// own comment for why the close edge alone carries a hair of tolerance).</summary>
        private bool WindowOpenAt(float clock) =>
            ventWindow > 0f && clock >= ventDelay && clock <= WindowCloseEdge;

        /// <summary>The current attempt's outcome, for the HUD band: None for the whole silence when
        /// VentEnabled is false (review fix - Vent turned off, GameplayConfig's "0 = off" tooltip,
        /// must never paint a band at all, not even a Missed one, once the disabled window's close
        /// edge has passed); otherwise Hit once TryVent has landed one, Missed once either an
        /// Early/Late press has spent the attempt OR the window has closed with nothing pressed at
        /// all (the older groundwork's "light up and pass unused" - the HUD shows the miss
        /// immediately either way, see PlayerHud), None otherwise.</summary>
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
                    return VentOutcome.Missed; // the window passed with nothing pressed at all
                return VentOutcome.None;
            }
        }

        /// <summary>Heat fraction (0..1 of max) the fill sits at the instant the vent window opens -
        /// the band's brighter/upper edge, since heat only ever falls while silenced. Projected from
        /// bandOriginHeat (see its own comment), how much of the decay delay is left, the decay rate
        /// and how long this silence has run - not a fixed pair of numbers - so the band still lines
        /// up with the fill even if a future change ever silenced a player below max heat, or a
        /// laser's refund lowers heat before the window opens (review fix, see Refund).</summary>
        public float VentBandHighFraction => max > 0f ? HeatAtSilenceTime(ventDelay) / max : 0f;

        /// <summary>Heat fraction (0..1 of max) the fill sits at the instant the vent window closes -
        /// the band's lower edge. See VentBandHighFraction.</summary>
        public float VentBandLowFraction => max > 0f ? HeatAtSilenceTime(ventDelay + ventWindow) / max : 0f;

        /// <summary>What the band is projected FROM (see HeatAtSilenceTime) - set to Heat (i.e. max,
        /// since IsSilenced only ever goes false-&gt;true inside Add() the moment Heat reaches max) the
        /// instant a fresh silence starts, and lowered by Refund while silenced and before a hit (see
        /// that method's own comment). Review fix: a laser's Refund(OverheatRefundOnHit) can land in
        /// the very trigger pull that caused the overheat (WeaponFiring.RefundHeatIfBeamConnects runs
        /// right after the Add that silenced the player) - the band used to always project from max
        /// regardless, so a refunded fill was already partway through a band drawn as if nothing had
        /// been refunded at all. A hit deliberately does not touch this field: the band (and its hit
        /// flash) stays exactly where the window was, not wherever TryVent's own Heat *= 0.5f left
        /// Heat afterwards.</summary>
        private float bandOriginHeat;

        /// <summary>Heat at a given point in time within THIS silence (t measured in seconds since
        /// IsSilenced went true) - the same decay math Tick uses (nothing decays until decayDelay
        /// has elapsed since the last Add), just solved for an arbitrary instant instead of stepped
        /// by a frame. Starts from bandOriginHeat, not the current Heat: heat cannot rise again while
        /// silenced (WeaponFiring/AbilityRunner gate on CastGate before Add ever runs), but a laser's
        /// Refund CAN lower it mid-silence, before the window opens - which is exactly what
        /// bandOriginHeat tracks (see its own comment) so this formula keeps matching the fill it
        /// projects, whether asked before, during or after "now" (silenceClock), refund or no
        /// refund.</summary>
        private float HeatAtSilenceTime(float t)
        {
            float decayingTime = Mathf.Max(0f, t - decayDelay);
            return Mathf.Max(0f, bandOriginHeat - decayPerSecond * decayingTime);
        }

        public OverheatState(float max, float decayDelay, float decayPerSecond, float warningThreshold,
            float ventDelay, float ventWindow)
        {
            this.max = max;
            this.decayDelay = decayDelay;
            this.decayPerSecond = decayPerSecond;
            this.warningThreshold = warningThreshold;
            this.ventDelay = ventDelay;
            this.ventWindow = ventWindow;
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
                // A fresh silence just started - the vent clock and this silence's one attempt
                // begin fresh, in lockstep with timeSinceLastAdd's own reset above (see the class
                // comment on why Heat can stand in for a separate silence timer). bandOriginHeat
                // starts at Heat, which IS max here (Heat was just clamped up to it above).
                silenceClock = 0f;
                ventAttempted = false;
                ventHit = false;
                bandOriginHeat = Heat;
            }
        }

        /// <summary>The laser's half-cost refund when a shot connects. Never goes below zero. While
        /// silenced and before a hit, also lowers bandOriginHeat by the amount actually applied to
        /// Heat (the same floor-at-zero clamp), so the projected Vent band moves down with the fill
        /// instead of staying pinned to max (review fix - see bandOriginHeat's own comment). Once a
        /// hit has landed, ventHit is true and this stops touching the band: the hit flash must stay
        /// exactly where the window was, not follow whatever a later refund does to Heat.</summary>
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
                return; // still inside the delay window - no decay yet

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

                // The halving IS the "cut the remaining silence in half" - not a parallel timer.
                // Heat decays linearly once past decayDelay (Tick), so remaining time-to-zero is
                // Heat / decayPerSecond; halving Heat exactly halves that quotient. This is only
                // exact once decay has actually started, i.e. once silenceClock has passed
                // decayDelay - true by the time the window can even open whenever ventDelay >=
                // decayDelay (GameplayConfig's own tooltip says so; the defaults are 2.0 >= 1.5).
                Heat *= 0.5f;
                return VentResult.Hit;
            }

            return silenceClock < ventDelay ? VentResult.Early : VentResult.Late;
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
        }
    }
}
