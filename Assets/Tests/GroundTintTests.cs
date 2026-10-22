using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A faint team colour on the floor moves its middle channel toward its brightest, so the floor's blue does not drag it toward
    /// another hue; the brightest and darkest channels never move, so a primary or a grey stays exactly itself. The Electric Fence's floor
    /// band takes the same lift as an owned hexagon's fill.</summary>
    public class GroundTintTests
    {
        private static void AreClose(Color expected, Color actual, string label)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, label + " r");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, label + " g");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, label + " b");
            Assert.AreEqual(expected.a, actual.a, 1e-4f, label + " a");
        }

        [Test]
        public void TheMiddleChannelMovesTowardTheBrightest()
        {
            AreClose(new Color(1f, 0.6f, 0f, 1f), GroundTint.Lift(new Color(1f, 0.2f, 0f, 1f), 0.5f), "an orange's green");
            AreClose(new Color(0.7f, 0.1f, 1f, 0.4f), GroundTint.Lift(new Color(0.4f, 0.1f, 1f, 0.4f), 0.5f), "a purple's red, alpha kept");
            AreClose(new Color(0.1f, 0.95f, 1f, 1f), GroundTint.Lift(new Color(0.1f, 0.9f, 1f, 1f), 0.5f), "a cyan barely moves");
        }

        [Test]
        public void NoLiftPrimariesAndGreysStayExactlyThemselves()
        {
            var orange = new Color(1f, 0.2f, 0.01f, 1f);
            AreClose(orange, GroundTint.Lift(orange, 0f), "lift 0");
            AreClose(Color.red, GroundTint.Lift(Color.red, 1f), "a primary");
            AreClose(new Color(0.5f, 0.5f, 0.5f, 1f), GroundTint.Lift(new Color(0.5f, 0.5f, 0.5f, 1f), 1f), "a grey");
            AreClose(new Color(1f, 1f, 0f, 1f), GroundTint.Lift(new Color(1f, 1f, 0f, 1f), 1f), "two equal brightest channels");
            AreClose(new Color(0.2f, 0.9f, 0.2f, 1f), GroundTint.Lift(new Color(0.2f, 0.9f, 0.2f, 1f), 1f), "two equal weakest channels");
        }

        [Test]
        public void ATeamColourIsLiftedInLinearSpaceLikeTheShaderDoes()
        {
            var orange = new Color(1f, 0.45f, 0.05f, 0.2f);
            Color expected = GroundTint.Lift(orange.linear, 0.35f).gamma;
            AreClose(expected, GroundTint.Faint(orange, 0.35f), "Faint is Lift on the linear colour");
            Assert.Greater(GroundTint.Faint(orange, 0.35f).g, orange.g + 0.1f, "and it really lifts");
        }

        [Test]
        public void TheFenceBandIsAFaintTeamColourOnTheFloor()
        {
            var faint = typeof(GroundTint).GetMethod(nameof(GroundTint.Faint));
            var lift = typeof(ArenaLook).GetField(nameof(ArenaLook.ownedFillHueLift));
            Assert.IsTrue(IlWiring.Uses(typeof(FenceCageView), nameof(FenceCageView.OnDeployablePlaced), faint), "the band colour goes through GroundTint.Faint");
            Assert.IsTrue(IlWiring.Uses(typeof(FenceCageView), nameof(FenceCageView.OnDeployablePlaced), lift), "with the floor's own lift");
        }
    }
}
