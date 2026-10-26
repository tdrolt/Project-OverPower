using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers the two small facts a mine's every-client FixedUpdate trigger check needs -
    /// see Mine.cs and the Task 1.8 addendum ("arm delay so you cannot instantly detonate it at
    /// your own feet" / "every client runs the first RPC_Detonate it receives").</summary>
    public class MineDetonationStateTests
    {
        [Test]
        public void NotArmedBeforeTheDelayElapses()
        {
            var state = new MineDetonationState(armDelaySeconds: 0.5f);

            Assert.IsFalse(state.IsArmed(0.49f));
        }

        [Test]
        public void ArmedTheInstantTheDelayElapses()
        {
            var state = new MineDetonationState(armDelaySeconds: 0.5f);

            Assert.IsTrue(state.IsArmed(0.5f));
        }

        [Test]
        public void StaysArmedAfterTheDelay()
        {
            var state = new MineDetonationState(armDelaySeconds: 0.5f);

            Assert.IsTrue(state.IsArmed(45f));
        }

        [Test]
        public void ZeroArmDelayIsArmedImmediately()
        {
            var state = new MineDetonationState(armDelaySeconds: 0f);

            Assert.IsTrue(state.IsArmed(0f));
        }

        [Test]
        public void FirstDetonateCallSucceeds()
        {
            var state = new MineDetonationState(armDelaySeconds: 0.5f);

            Assert.IsTrue(state.TryDetonate());
            Assert.IsTrue(state.Detonated);
        }

        [Test]
        public void SecondDetonateCallFails()
        {
            // AllViaServer gives every client the same order, but a client could still see its own
            // trigger fire and then receive the RPC it caused - TryDetonate must not run the blast
            // twice on that client.
            var state = new MineDetonationState(armDelaySeconds: 0.5f);
            state.TryDetonate();

            Assert.IsFalse(state.TryDetonate());
        }

        [Test]
        public void EveryCallAfterTheFirstFails()
        {
            var state = new MineDetonationState(armDelaySeconds: 0.5f);
            state.TryDetonate();
            state.TryDetonate();

            Assert.IsFalse(state.TryDetonate());
        }
    }
}
