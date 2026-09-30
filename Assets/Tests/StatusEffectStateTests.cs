using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class StatusEffectStateTests
    {
        [Test]
        public void SingleSlowReturnsItsMagnitude()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });

            Assert.AreEqual(0.3f, s.Magnitude(StatusKind.Slow), 0.001f);
        }

        [Test]
        public void TwoSlowsSumTheirMagnitude()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });

            Assert.AreEqual(0.6f, s.Magnitude(StatusKind.Slow), 0.001f);
        }

        [Test]
        public void ThreeSlowsClampAtTheCapNotTheirSum()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });

            Assert.AreEqual(0.6f, s.Magnitude(StatusKind.Slow), 0.001f);
        }

        [Test]
        public void BurnRefreshesInsteadOfStacking()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Burn, duration = 5f, magnitude = 5f });
            s.Tick(2f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Burn, duration = 5f, magnitude = 5f });

            Assert.AreEqual(1, s.StackCount(StatusKind.Burn));
            Assert.AreEqual(5.0f, s.Remaining(StatusKind.Burn), 0.001f);
        }

        [Test]
        public void LongerStunWinsOverShorterStun()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 1.0f, magnitude = 0f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 2.5f, magnitude = 0f });

            Assert.AreEqual(2.5f, s.Remaining(StatusKind.Stun), 0.001f);
        }

        [Test]
        public void VulnerabilityStacksAndClampsAtItsCap()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 3f, magnitude = 0.3f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 3f, magnitude = 0.3f });

            Assert.AreEqual(0.6f, s.Magnitude(StatusKind.Vulnerability), 0.001f);

            s.Apply(new StatusEffectSpec { kind = StatusKind.Vulnerability, duration = 3f, magnitude = 0.3f });

            Assert.AreEqual(0.6f, s.Magnitude(StatusKind.Vulnerability), 0.001f);
        }

        [Test]
        public void TickingPastDurationDeactivatesTheEffect()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 2f, magnitude = 0.3f });
            s.Tick(2.5f);

            Assert.IsFalse(s.IsActive(StatusKind.Slow));
            Assert.AreEqual(0f, s.Magnitude(StatusKind.Slow), 0.001f);
        }

        [Test]
        public void ClearAllDeactivatesEveryKind()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Slow, duration = 3f, magnitude = 0.3f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Burn, duration = 3f, magnitude = 5f });
            s.Apply(new StatusEffectSpec { kind = StatusKind.Stun, duration = 1f, magnitude = 0f });

            s.ClearAll();

            Assert.IsFalse(s.IsActive(StatusKind.Slow));
            Assert.IsFalse(s.IsActive(StatusKind.Burn));
            Assert.IsFalse(s.IsActive(StatusKind.Stun));
        }

        [Test]
        public void TickWithNoEffectsActiveDoesNotThrow()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);

            Assert.DoesNotThrow(() => s.Tick(0.5f));
        }

        [Test]
        public void ConsumeBurnDamageSumsToTheTotalThenStops()
        {
            var s = new StatusEffectState(slowCap: 0.6f, vulnerabilityCap: 0.6f);
            s.Apply(new StatusEffectSpec { kind = StatusKind.Burn, duration = 5f, magnitude = 5f });

            float total = 0f;
            for (int i = 0; i < 10; i++)
            {
                total += s.ConsumeBurnDamage(0.5f);
                s.Tick(0.5f);
            }

            Assert.AreEqual(25f, total, 0.01f);
            Assert.AreEqual(0f, s.ConsumeBurnDamage(0.5f), 0.01f);
        }
    }
}
