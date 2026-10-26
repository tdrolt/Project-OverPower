using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The rocket's shockwave ring shows the real blast: it snaps out quickly, spends most of its short life close to the Splash
    /// Radius and never draws past it, so nobody outside the ring was in the blast.</summary>
    public class RocketBlastRingTests
    {
        [Test]
        public void TheRingNeverPassesTheSplashRadiusAndEndsOnIt()
        {
            const float radius = 3.7f, seconds = 0.4f, start = 0.5f, width = 0.3f;
            float last = 0f;
            for (int i = 0; i <= 40; i++)
            {
                float r = SplashShell.RingRadius(radius, i * seconds / 40f, seconds, start, width);
                Assert.LessOrEqual(r + width * 0.5f, radius + 1e-5f, "the drawn line's outer edge is never past the real blast");
                Assert.GreaterOrEqual(r, last - 1e-5f, "it only grows");
                last = r;
            }
            Assert.AreEqual(radius * start - width * 0.5f, SplashShell.RingRadius(radius, 0f, seconds, start, width), 1e-4f,
                "its outer edge starts at Start Scale of the radius");
            Assert.AreEqual(radius - width * 0.5f, SplashShell.RingRadius(radius, seconds, seconds, start, width), 1e-4f,
                "its outer edge ends exactly on the radius");
            Assert.AreEqual(radius - width * 0.5f, SplashShell.RingRadius(radius, 2f * seconds, seconds, start, width), 1e-4f);
            Assert.AreEqual(0f, SplashShell.RingRadius(0.1f, 0f, seconds, start, width), 1e-5f, "a blast smaller than the line: drawn at the centre, never inside out");
        }

        [Test]
        public void TheRingSnapsOutFasterThanItFades()
        {
            const float radius = 2f, seconds = 0.4f, start = 0.5f;
            float halfway = SplashShell.RingRadius(radius, seconds * 0.5f, seconds, start, 0f);
            Assert.Greater(halfway, Mathf.Lerp(start, 1f, 0.5f) * radius, "at half its life the ring is past the halfway size: it reads at the real radius");
        }

        [Test]
        public void TheShellDrawsTheTestedRingInTheImpactWhite()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(SplashShell), "Apply", typeof(SplashShell).GetMethod(nameof(SplashShell.RingRadius))),
                "the drawn ring is the tested size");
            FieldInfo white = typeof(UiTheme).GetField(nameof(UiTheme.flashWhiteColor));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Weapons.RocketBlastView), "HandleDetonated", white), "an impact is white");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Abilities.MineView), "LateUpdate", white), "a mine's blast too");
            Assert.IsFalse(IlWiring.Uses(typeof(Overpower.Weapons.RocketBlastView), typeof(UiTheme).GetMethod(nameof(UiTheme.ShotColorFor))),
                "and never the shooter's team (the design: hits and impacts never carry it)");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Weapons.RocketBlastView), "HandleDetonated",
                typeof(Overpower.Arena.ArenaLook).GetField(nameof(Overpower.Arena.ArenaLook.blastRingWidth))), "as thick as the Arena Look's Blast Ring Width");
        }
    }
}
