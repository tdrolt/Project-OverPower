using System.Reflection;
using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// UiTheme.ShotColorFor/GradientFor decide what colour every shot's trail and tinted core draw
    /// with (ShotTeamVisuals) - Playtest polish review, fix 11. Both fail open to
    /// unknownTeamShotColor for an id Teams.TryGetTeam could not resolve or that falls outside
    /// Team Shot Colors, which matters because a debug/test-range shot with no team assigned reads
    /// this path every time it fires. GradientFor's caching is pinned too, since ShotTeamVisuals'
    /// whole reason for calling it instead of building a Gradient by hand is to never allocate one
    /// per shot (see that class's own comment) - a test-range weapon can put several projectiles in
    /// the air a second.
    /// </summary>
    public class UiThemeTests
    {
        private UiTheme theme;

        private static readonly Color Team0 = new Color(0.93f, 0.97f, 1f, 1f);
        private static readonly Color Team1 = new Color(0.68f, 0.32f, 1f, 1f);
        private static readonly Color Team2 = new Color(0.15f, 0.95f, 1f, 1f);
        private static readonly Color Fallback = new Color(0.55f, 0.55f, 0.58f, 0.5f);

        [SetUp]
        public void CreateTheme()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { Team0, Team1, Team2 };
            theme.unknownTeamShotColor = Fallback;
        }

        [TearDown]
        public void DestroyTheme()
        {
            // CreateInstance'd ScriptableObjects are not garbage collected on their own - same
            // reason HitscanChargedRangeTests tears its WeaponDefinition down.
            if (theme != null)
                Object.DestroyImmediate(theme);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Expected a private method '{methodName}' on {target.GetType().Name}.");
            method.Invoke(target, null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ShotColorForInRangeIdsReturnsTheMatchingArrayEntry(int teamId)
        {
            Color[] expected = { Team0, Team1, Team2 };

            Assert.AreEqual(expected[teamId], theme.ShotColorFor(teamId));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void ShotColorForAnUnresolvableOrOutOfRangeIdFallsBackToUnknownTeamColor(int teamId)
        {
            Assert.AreEqual(Fallback, theme.ShotColorFor(teamId));
        }

        [Test]
        public void GradientForReturnsTheSameInstanceOnRepeatCallsForTheSameTeam()
        {
            Gradient first = theme.GradientFor(1);
            Gradient second = theme.GradientFor(1);

            // Not just equal - the SAME object, which is what lets ShotTeamVisuals hand a shared
            // Gradient to every bullet's TrailRenderer without allocating one per shot.
            Assert.AreSame(first, second);
        }

        [Test]
        public void GradientForBucketsEveryUnresolvedIdTogether()
        {
            // -1 and an out-of-range id both fall back to unknownTeamShotColor (see ShotColorFor
            // above), and GradientFor is documented to bucket every such id into the same cached
            // Gradient rather than building one per distinct out-of-range value.
            Gradient unresolved = theme.GradientFor(-1);
            Gradient outOfRange = theme.GradientFor(99);

            Assert.AreSame(unresolved, outOfRange);
        }

        [Test]
        public void GradientForFadesToZeroAlphaAtItsEnd()
        {
            Gradient gradient = theme.GradientFor(2);

            GradientAlphaKey lastKey = gradient.alphaKeys[gradient.alphaKeys.Length - 1];
            Assert.AreEqual(0f, lastKey.alpha, 1e-4f);
            // The fade STARTS at the team colour's own alpha, not always 1 - Task 11a's
            // unknown-team fallback relies on this to read as a dimmer, washed-out trail (see
            // UiTheme's own tooltip on Unknown Team Shot Colour).
            GradientAlphaKey firstKey = gradient.alphaKeys[0];
            Assert.AreEqual(theme.ShotColorFor(2).a, firstKey.alpha, 1e-4f);
        }

        [Test]
        public void GradientForColorMatchesShotColorFor()
        {
            Gradient gradient = theme.GradientFor(1);

            Color sampled = gradient.colorKeys[0].color;
            Assert.AreEqual(Team1.r, sampled.r, 1e-4f);
            Assert.AreEqual(Team1.g, sampled.g, 1e-4f);
            Assert.AreEqual(Team1.b, sampled.b, 1e-4f);
        }

        [Test]
        public void OnValidateClearsTheGradientCacheSoALiveColourEditIsPickedUp()
        {
            // Fix 3 (Playtest polish review): without OnValidate clearing the cache, editing Team
            // Shot Colors in the Inspector while the Editor is open kept handing out the OLD
            // Gradient object until the next domain reload - a live colour tweak looked like it
            // did nothing. OnValidate is private (a Unity message, never called directly by other
            // code), so it is invoked through reflection here, the same pattern
            // HitscanChargedRangeTests uses to reach a private field.
            Gradient before = theme.GradientFor(0);

            theme.teamShotColors[0] = Color.magenta;
            InvokePrivate(theme, "OnValidate");
            Gradient after = theme.GradientFor(0);

            Assert.AreNotSame(before, after);
            Assert.AreEqual(Color.magenta, after.colorKeys[0].color);
        }
    }
}
