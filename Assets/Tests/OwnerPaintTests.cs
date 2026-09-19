using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Arena rebuild step 1: what OwnerPaint.From shows for a zone's owner-coloured things (the ring's edge,
    /// a tower's crown and caps), built from the same CaptureRingState the ring already reads every frame. Mirrors
    /// CaptureRingView's own branch order (drain before "under attack") so the two can never disagree.</summary>
    public class OwnerPaintTests
    {
        [Test]
        public void AnOwnedZoneShowsItsOwnerAndDoesNotPulse()
        {
            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 1, TerritoryMap.Neutral, false);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Team, paint.Base);
            Assert.AreEqual(1, paint.Team);
            Assert.IsFalse(paint.PulsesToWarning);
            Assert.AreEqual(TerritoryMap.Neutral, paint.PulseTeam);
        }

        [Test]
        public void ANeutralZoneShowsNeutral()
        {
            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, TerritoryMap.Neutral, TerritoryMap.Neutral, false);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Neutral, paint.Base);
            Assert.AreEqual(TerritoryMap.Neutral, paint.Team);
        }

        [Test]
        public void OutOfPlayWinsOverOwnerAttackAndDrain()
        {
            var state = new CaptureRingState(CaptureRingPhase.Draining, 0.4f, 1, 1, 0, true, outOfPlay: true);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.OutOfPlay, paint.Base);
            Assert.IsFalse(paint.PulsesToWarning);
            Assert.AreEqual(TerritoryMap.Neutral, paint.PulseTeam);
        }

        [Test]
        public void AnAttackedOwnedZonePulsesToTheWarning()
        {
            var state = new CaptureRingState(CaptureRingPhase.Idle, 0f, TerritoryMap.Neutral, 2, TerritoryMap.Neutral, true);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Team, paint.Base);
            Assert.AreEqual(2, paint.Team);
            Assert.IsTrue(paint.PulsesToWarning);
            Assert.AreEqual(TerritoryMap.Neutral, paint.PulseTeam);
        }

        [Test]
        public void ADrainPulsesToTheDrainerNotTheWarning()
        {
            // Mirrors CaptureRingView (before arena step 2): the drain check comes first, so a draining, under-attack
            // zone pulses to the drainer's colour, never the warning colour.
            var state = new CaptureRingState(CaptureRingPhase.Draining, 0.3f, 0, 0, 1, true);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Team, paint.Base);
            Assert.AreEqual(0, paint.Team);
            Assert.IsFalse(paint.PulsesToWarning);
            Assert.AreEqual(1, paint.PulseTeam);
        }

        [Test]
        public void ACaptureInProgressLeavesANeutralZoneNeutral()
        {
            var state = new CaptureRingState(CaptureRingPhase.Capturing, 0.5f, 2, TerritoryMap.Neutral, TerritoryMap.Neutral, false);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Neutral, paint.Base);
            Assert.IsFalse(paint.PulsesToWarning);
            Assert.AreEqual(TerritoryMap.Neutral, paint.PulseTeam);
        }

        [Test]
        public void APausedCaptureChangesNothing()
        {
            var state = new CaptureRingState(CaptureRingPhase.Paused, 0.6f, 1, 0, TerritoryMap.Neutral, false);
            OwnerPaint paint = OwnerPaint.From(state);

            Assert.AreEqual(OwnerPaintBase.Team, paint.Base);
            Assert.AreEqual(0, paint.Team);
            Assert.IsFalse(paint.PulsesToWarning);
            Assert.AreEqual(TerritoryMap.Neutral, paint.PulseTeam);
        }
    }
}
