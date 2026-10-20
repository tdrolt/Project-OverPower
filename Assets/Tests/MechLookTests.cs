using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The mech's colours as pure rules: the body is never black whatever the team colour (a dark team colour still reads as
    /// a colour), the White team stays light, and the glow lines are the team's own hue at a brightness that does not depend on it.</summary>
    public class MechLookTests
    {
        private static readonly Color White = new Color(0.93f, 0.97f, 1f, 1f);
        private static readonly Color Purple = new Color(0.68f, 0.32f, 1f, 1f);
        private static readonly Color Cyan = new Color(0.15f, 0.95f, 1f, 1f);
        private static readonly Color DarkPurple = new Color(0.1f, 0.03f, 0.2f, 1f);
        private static readonly Color Black = new Color(0f, 0f, 0f, 1f);

        private static readonly Color[] EveryColour = { White, Purple, Cyan, DarkPurple, Black };
        private static readonly float[] EveryMix = { 0f, 0.25f, 0.5f, 0.9f, 1f };

        private static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        private static float HueGap(float a, float b)
        {
            float gap = Mathf.Abs(a - b);
            return Mathf.Min(gap, 1f - gap);
        }

        [Test]
        public void BaseNeverBlackForAnyTeamColour()
        {
            foreach (Color team in EveryColour)
                foreach (float mix in EveryMix)
                {
                    Color body = MechLook.Base(team, mix);
                    float brightest = Mathf.Max(body.r, Mathf.Max(body.g, body.b));
                    Assert.GreaterOrEqual(brightest, 0.2f, $"team {team} at mix {mix} made a black mech: {body}");
                    Assert.AreEqual(1f, body.a, 1e-5f, "the body is opaque");
                }
        }

        [Test]
        public void TheWhiteTeamStaysLight()
        {
            Color body = MechLook.Base(White, 0.5f);
            Assert.GreaterOrEqual(body.grayscale, 0.6f, $"the White team's mech must stay light: {body}");
        }

        [Test]
        public void TheBodyKeepsTheTeamHue()
        {
            foreach (float mix in new[] { 0.3f, 0.5f, 0.9f, 1f })
                Assert.Less(HueGap(Hue(MechLook.Base(Purple, mix)), Hue(Purple)), 0.02f, $"purple at mix {mix} must stay purple");
        }

        [Test]
        public void MixZeroIsANeutralGreyAndMoreMixMovesTowardTheTeam()
        {
            Color grey = MechLook.Base(Purple, 0f);
            Assert.AreEqual(grey.r, grey.g, 1e-5f);
            Assert.AreEqual(grey.g, grey.b, 1e-5f);
            Assert.Greater(MechLook.Base(Purple, 0.8f).b - MechLook.Base(Purple, 0.8f).g, MechLook.Base(Purple, 0.3f).b - MechLook.Base(Purple, 0.3f).g,
                "a higher mix is more saturated");
        }

        /// <summary>What the screen shows of a linear-light glow: the hue of its sRGB form, brightness divided out.</summary>
        private static float ShownHue(Color linearGlow)
        {
            float peak = Mathf.Max(linearGlow.r, Mathf.Max(linearGlow.g, linearGlow.b));
            return Hue(new Color(linearGlow.r / peak, linearGlow.g / peak, linearGlow.b / peak, 1f).gamma);
        }

        [Test]
        public void GlowIsTheTeamHue()
        {
            foreach (Color team in new[] { White, Purple, Cyan, DarkPurple })
                foreach (float strength in new[] { 0.5f, 1f, 1.5f, 3f })
                {
                    Color glow = MechLook.Glow(team.linear, strength);
                    Assert.Less(HueGap(ShownHue(glow), Hue(team)), 0.01f, $"glow of {team} at {strength} left the team hue: {glow}");
                }
        }

        [Test]
        public void TheBrightestChannelOfTheGlowIsTheStrengthWhateverTheTeamColour()
        {
            foreach (Color team in new[] { White, Purple, Cyan, DarkPurple })
            {
                Color glow = MechLook.Glow(team.linear, 1.5f);
                Assert.AreEqual(1.5f, Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b)), 1e-4f, $"{team}: a dim team colour glows as bright as a bright one");
                Assert.AreEqual(1f, glow.a, 1e-5f);
            }
            Assert.AreEqual(3f, MechLook.Glow(Purple.linear, 3f).b, 1e-4f, "the glow is not clamped, it is bright (HDR) light");
        }

        [Test]
        public void NoStrengthOrNoColourIsNoGlow()
        {
            Color none = MechLook.Glow(Purple.linear, -2f);
            Assert.AreEqual(0f, Mathf.Max(none.r, Mathf.Max(none.g, none.b)), 1e-5f, "a negative strength is no glow, never negative light");
            Color black = MechLook.Glow(Black, 1.5f);
            Assert.AreEqual(0f, Mathf.Max(black.r, Mathf.Max(black.g, black.b)), 1e-5f, "a black team colour has no hue to glow in");
        }
    }
}
