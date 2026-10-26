using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Fire burns in the colour of the team that fired it, keeping each flame's own brightness, with its heart paler than
    /// its flame; fire of no known team stays the theme's orange-red. Every client colours the Flamethrower's jet and the Rocket's burning
    /// ground from the team the cast or the field already carries.</summary>
    public class TeamFireTests
    {
        private static readonly Color Orange = new Color(1f, 0.571f, 0.242f, 1f);
        private static readonly Color Purple = new Color(0.68f, 0.32f, 1f, 1f);
        private static readonly Color Cyan = new Color(0.15f, 0.95f, 1f, 1f);

        private static Vector3 Hsv(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return new Vector3(h * 360f, s, v);
        }

        private static float HueApart(Color a, Color b) => Mathf.Abs(Mathf.DeltaAngle(Hsv(a).x, Hsv(b).x));

        [Test]
        public void FireTakesTheTeamsColourAtItsOwnBrightness()
        {
            Color fire = new Color(Orange.r, Orange.g, Orange.b, 0.6f);
            foreach (Color team in new[] { Purple, Cyan })
            {
                Color tinted = TeamFire.Tint(fire, team, 1f, 0f);
                Assert.Less(HueApart(tinted, team), 1f, $"{tinted} burns in the team's hue ({team})");
                Assert.AreEqual(Hsv(team).y, Hsv(tinted).y, 1e-3f, "at the team's own strength of colour: on the grey team sheet nothing else colours it");
                Assert.AreEqual(Hsv(fire).z, Hsv(tinted).z, 1e-3f, "and the fire's own brightness");
                Assert.AreEqual(fire.linear.maxColorComponent, tinted.linear.maxColorComponent, 1e-3f, "so no flame glows brighter for its team");
                Assert.AreEqual(0.6f, tinted.a, 1e-4f, "its see-through share kept");
            }
        }

        [Test]
        public void HueShareTurnsTheFirePartOfTheWayAlongTheShortWayRound()
        {
            Assert.Less(HueApart(TeamFire.Tint(Orange, Purple, 0f, 0f), Orange), 1e-3f, "no share keeps the fire's own hue");
            Color half = TeamFire.Tint(Orange, Purple, 0.5f, 0f);
            float fromFire = HueApart(half, Orange), toTeam = HueApart(half, Purple), whole = HueApart(Orange, Purple);
            Assert.AreEqual(whole * 0.5f, fromFire, 1f, "half way from the fire's hue");
            Assert.AreEqual(whole * 0.5f, toTeam, 1f, "to the team's, the short way round (through red, not through green)");
            Assert.AreEqual(Mathf.Lerp(Hsv(Orange).y, Hsv(Purple).y, 0.5f), Hsv(half).y, 1e-3f, "and half way to the team's strength of colour");
        }

        [Test]
        public void TheHeartIsPaledTowardWhiteAtItsOwnBrightness()
        {
            Color heart = TeamFire.Tint(Orange, Cyan, 1f, 0.5f);
            Assert.Less(HueApart(heart, Cyan), 1f, "the heart is the team's too");
            Assert.AreEqual(Hsv(Cyan).y * 0.5f, Hsv(heart).y, 1e-3f, "with half the team's strength of colour");
            Assert.AreEqual(Hsv(Orange).z, Hsv(heart).z, 1e-3f, "and its brightness");
            Assert.AreEqual(0f, Hsv(TeamFire.Tint(Orange, Cyan, 1f, 1f)).y, 1e-3f, "full whiteness is plain white");
        }

        [Test]
        public void ARampIsTintedKeyByKeyAndKeepsItsFade()
        {
            var ramp = new Gradient { mode = GradientMode.Blend };
            ramp.SetKeys(new[] { new GradientColorKey(Orange, 0f), new GradientColorKey(new Color(0.37f, 0.15f, 0.1f), 0.8f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            Gradient tinted = TeamFire.Ramp(ramp, Purple, 1f, 0.3f);
            Assert.AreNotSame(ramp, tinted, "a new ramp: the theme's own is never changed");
            Assert.AreEqual(Orange, ramp.colorKeys[0].color, "the theme's ramp is untouched");
            Assert.AreEqual(ramp.colorKeys.Length, tinted.colorKeys.Length);
            for (int i = 0; i < ramp.colorKeys.Length; i++)
            {
                Assert.AreEqual(ramp.colorKeys[i].time, tinted.colorKeys[i].time, 1e-4f, "each key at its own time");
                Color want = TeamFire.Tint(ramp.colorKeys[i].color, Purple, 1f, 0.3f);
                Assert.AreEqual(want.r, tinted.colorKeys[i].color.r, 1e-3f);
                Assert.AreEqual(want.g, tinted.colorKeys[i].color.g, 1e-3f);
                Assert.AreEqual(want.b, tinted.colorKeys[i].color.b, 1e-3f);
            }
            Assert.AreEqual(ramp.alphaKeys, tinted.alphaKeys, "its fade kept");
            Assert.AreEqual(ramp.mode, tinted.mode);
        }

        private static (UiTheme, ArenaLook) Theme(float hue, float whiteness)
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            theme.arenaLook = look;
            theme.teamShotColors = new[] { new Color(1f, 0.45f, 0.05f), Purple, Cyan };
            theme.fireBodyOverLife = new Gradient();
            theme.fireBodyOverLife.SetKeys(new[] { new GradientColorKey(Orange, 0f), new GradientColorKey(new Color(0.78f, 0.34f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            theme.fireCoreOverLife = new Gradient();
            theme.fireCoreOverLife.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.33f), 0f), new GradientColorKey(new Color(0.45f, 0.2f, 0.06f), 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            theme.fireCoreColor = new Color(1f, 0.7f, 0.33f);
            look.fireTeamHue = hue;
            look.fireTeamHeartWhiteness = whiteness;
            return (theme, look);
        }

        [Test]
        public void FireOfAKnownTeamBurnsInItsHueWithAPalerHeart()
        {
            (UiTheme theme, ArenaLook look) = Theme(1f, 0.4f);
            try
            {
                Color edge = new Color(1f, 0.38f, 0.1f);
                FlameLook.FireColours fire = FlameLook.Colours(theme, edge, 1);
                Assert.IsTrue(fire.TeamSheets, "drawn on the plain team sheets, so its colour is the colour seen");
                foreach ((string part, Color c) in new[] { ("flame", fire.Body.Evaluate(0f)), ("heart", fire.Core.Evaluate(0f)), ("ember's hot end", fire.Hot), ("ember's cool end", fire.Edge) })
                    Assert.Less(HueApart(c, Purple), 1f, $"the {part} ({c}) is the shooter's team's");
                AssertTinted(theme.fireBodyOverLife, fire.Body, 0f);
                AssertTinted(theme.fireCoreOverLife, fire.Core, 0.4f);
                Assert.Less(Hsv(fire.Core.Evaluate(0f)).y, Hsv(fire.Body.Evaluate(0f)).y, "its heart is paler than its flame");
                Assert.AreEqual(Hsv(fire.Body.Evaluate(0f)).z, Hsv(theme.fireBodyOverLife.Evaluate(0f)).z, 1e-3f, "and the flame as bright as orange-red fire");
            }
            finally
            {
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        private static void AssertTinted(Gradient theirs, Gradient tinted, float whiteness)
        {
            Gradient want = TeamFire.Ramp(theirs, Purple, 1f, whiteness);
            for (int i = 0; i <= 10; i++)
            {
                Color a = want.Evaluate(i / 10f), b = tinted.Evaluate(i / 10f);
                Assert.AreEqual(a.r, b.r, 2e-3f, $"at {i / 10f:F1}");
                Assert.AreEqual(a.g, b.g, 2e-3f, $"at {i / 10f:F1}");
                Assert.AreEqual(a.b, b.b, 2e-3f, $"at {i / 10f:F1}");
                Assert.AreEqual(a.a, b.a, 2e-3f, $"at {i / 10f:F1}");
            }
        }

        [Test]
        public void FireOfNoKnownTeamOrWithNoHueShareIsTheThemesOrangeRed()
        {
            (UiTheme theme, ArenaLook look) = Theme(1f, 0.4f);
            try
            {
                Color edge = new Color(1f, 0.38f, 0.1f);
                foreach (int team in new[] { -1, 3 })
                {
                    FlameLook.FireColours fire = FlameLook.Colours(theme, edge, team);
                    Assert.AreSame(theme.fireBodyOverLife, fire.Body, $"team {team}: the theme's own flames");
                    Assert.AreSame(theme.fireCoreOverLife, fire.Core, $"team {team}: and heart");
                    Assert.AreEqual(theme.fireCoreColor, fire.Hot);
                    Assert.AreEqual(edge, fire.Edge);
                    Assert.IsFalse(fire.TeamSheets, $"team {team}: on the orange sheets");
                }
                look.fireTeamHue = 0f;
                Assert.IsFalse(FlameLook.Colours(theme, edge, 1).TeamSheets, "Fire Team Hue 0 leaves every fire orange-red");
                Assert.AreSame(theme.fireBodyOverLife, FlameLook.Colours(theme, edge, 1).Body);
                theme.arenaLook = null;
                Assert.IsFalse(FlameLook.Colours(theme, edge, 1).TeamSheets, "and so does a theme without its Arena Look");
            }
            finally
            {
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TeamFireIsDrawnOnThePlainSheets()
        {
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var jet = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "jet" };
            var core = new Material(jet) { name = "core" };
            var teamJet = new Material(jet) { name = "team jet" };
            var teamCore = new Material(jet) { name = "team core" };
            try
            {
                look.flameJetMaterial = jet;
                look.flameCoreMaterial = core;
                look.flameJetTeamMaterial = teamJet;
                look.flameCoreTeamMaterial = teamCore;
                Assert.AreSame(teamJet, FlameLook.SheetMaterial(look, false, true));
                Assert.AreSame(teamCore, FlameLook.SheetMaterial(look, true, true));
                Assert.AreSame(jet, FlameLook.SheetMaterial(look, false, false), "orange-red fire keeps the orange sheet");
                Assert.AreSame(core, FlameLook.SheetMaterial(look, true, false));
                look.flameJetTeamMaterial = null;
                look.flameCoreTeamMaterial = null;
                Assert.AreSame(jet, FlameLook.SheetMaterial(look, false, true), "an empty team slot falls back to the orange sheet");
                Assert.AreSame(core, FlameLook.SheetMaterial(look, true, true));

                // The flames really are laid on it.
                var root = new GameObject("Tongues", typeof(ParticleSystem));
                try
                {
                    new GameObject(FlameLook.CoreChildName, typeof(ParticleSystem)).transform.SetParent(root.transform, false);
                    look.flameJetTeamMaterial = teamJet;
                    look.flameCoreTeamMaterial = teamCore;
                    var ramp = new Gradient();
                    FlameLook.ApplyFloor(root.GetComponent<ParticleSystem>(), 2f, new FlameLook.FireColours(ramp, ramp, Color.white, Color.white, true), look);
                    Assert.AreSame(teamJet, root.GetComponent<ParticleSystemRenderer>().sharedMaterial, "the flames");
                    Assert.AreSame(teamCore, root.transform.Find(FlameLook.CoreChildName).GetComponent<ParticleSystemRenderer>().sharedMaterial, "their hearts");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                Object.DestroyImmediate(look);
                foreach (Material m in new[] { jet, core, teamJet, teamCore })
                    Object.DestroyImmediate(m);
            }
        }

        // The team is the one each client already has: the cast's caster team for the jet, the sender's team for the burning ground.
        [Test]
        public void EveryClientColoursTheFireFromTheTeamThatFiredIt()
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo colours = typeof(FlameLook).GetMethod(nameof(FlameLook.Colours));
            Assert.IsTrue(IlWiring.CallsEndingWithOwnParameter(typeof(FlamethrowerAbility), "ShowVfx",
                typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.ConfigureFire)), 0), "the spray hands its caster's team to the flames");
            Assert.IsTrue(IlWiring.CallsEndingWithOwnParameter(typeof(FlameConeVisual), nameof(FlameConeVisual.ConfigureFire), colours, 4),
                "which colour the jet in that team's fire");

            MethodInfo show = typeof(FireField).GetMethod("ShowLook", any);
            int at = System.Linq.Enumerable.Single(IlWiring.CallOffsets(show, colours));
            Assert.Contains(at - 5, IlWiring.FieldLoadOffsets(show, typeof(FireField).GetField("sourceTeamId", any)),
                "the burning ground burns in the team of the player who fired the rocket");
        }
    }
}
