using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The colour language on the saved assets: a team colour is a colour (white and grey mean neutral), each team is one hue in every
    /// list that draws it, the neutral greys, the whites, the reds and the golds never read as a team, fire burns in the hue of the team
    /// that fired it with a paler heart (orange-red when no team is known, on the red side of the Shield's gold; only its heart glows past
    /// white), the Conquest scan wave is white and the low barriers are painted as dark as the rest of the arena with a light stripe down
    /// their top. Relations only: the colours themselves stay free to tune.</summary>
    public class ColourLanguageAssetTests
    {
        private const float MinTeamSaturation = 0.4f;
        private const float SameHueDegrees = 20f;

        private static UiTheme Theme() => AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Shared/Config/UiTheme.asset");
        private static ArenaLook Look() => AssetDatabase.LoadAssetAtPath<ArenaLook>("Assets/_Project/Shared/Config/ArenaLook.asset");
        private static Color ScanWave() => AssetDatabase.LoadAssetAtPath<VisionConfig>("Assets/_Project/Shared/Config/VisionConfig.asset").ScanWaveColour;

        // The Flamethrower fan's own hot tip under the caster, a second fire colour kept on the prefab.
        private static Color FanCore()
        {
            var cone = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Abilities/Data/Flamethrower Cone.prefab");
            Assert.NotNull(cone, "the Flamethrower cone prefab");
            var view = cone.GetComponentInChildren<Overpower.Abilities.FlameConeVisual>(true);
            Assert.NotNull(view, "its FlameConeVisual");
            SerializedProperty core = new SerializedObject(view).FindProperty("coreColor");
            Assert.NotNull(core, "the fan's Core Color field");
            return core.colorValue;
        }

        private static float Hue(Color c) { Color.RGBToHSV(c, out float h, out _, out _); return h * 360f; }
        private static float Sat(Color c) { Color.RGBToHSV(c, out _, out float s, out _); return s; }
        private static float Val(Color c) { Color.RGBToHSV(c, out _, out _, out float v); return v; }

        private static float HueDegreesApart(Color a, Color b)
        {
            float d = Mathf.Abs(Hue(a) - Hue(b));
            return Mathf.Min(d, 360f - d);
        }

        // Two colours a player tells apart at a glance: clearly different hues (when both are colours at all), or clearly paler, or
        // clearly darker.
        private static bool ReadApart(Color a, Color b) =>
            (Sat(a) >= 0.25f && Sat(b) >= 0.25f && HueDegreesApart(a, b) >= 15f)
            || Mathf.Abs(Sat(a) - Sat(b)) >= 0.25f || Mathf.Abs(Val(a) - Val(b)) >= 0.35f;

        [Test]
        public void TheReadApartCheckCanFail()
        {
            var orange = new Color(1f, 0.45f, 0.05f, 1f);
            Assert.IsFalse(ReadApart(orange, new Color(1f, 0.5f, 0.08f, 1f)), "two oranges");
            Assert.IsTrue(ReadApart(orange, new Color(1f, 0.2f, 0.15f, 1f)), "orange and red");
            Assert.IsTrue(ReadApart(orange, new Color(1f, 0.92f, 0.5f, 1f)), "orange and a pale yellow");
            Assert.IsTrue(ReadApart(orange, Color.white), "orange and white");
            Assert.IsTrue(RedSideOf(new Color(1f, 0.35f, 0.08f, 1f), orange), "an orange-red is on the red side of the orange");
            Assert.IsFalse(RedSideOf(new Color(1f, 0.47f, 0.06f, 1f), orange), "the same orange is not");
            Assert.IsFalse(RedSideOf(new Color(1f, 0.7f, 0.1f, 1f), orange), "nor a yellower one");
            Assert.IsFalse(RedSideOf(new Color(0.4f, 0.3f, 1f, 1f), orange), "nor a blue");
        }

        // A clearly redder hue than the other colour's, both being colours at all: the hue running toward red (down, wrapping at 0).
        private static bool RedSideOf(Color a, Color b)
        {
            if (Sat(a) < 0.25f || Sat(b) < 0.25f)
                return false;
            float toward = Hue(b) - Hue(a);
            if (toward > 180f) toward -= 360f;
            if (toward < -180f) toward += 360f;
            return toward >= RedderDegrees && toward <= 90f;
        }

        private const float RedderDegrees = 5f;

        private static (string name, Color[] colours)[] TeamLists(UiTheme t) => new[]
        {
            ("Team Shot Colors", t.teamShotColors), ("Dominion Team Colors", t.dominionTeamColors),
            ("Dominion Team Text Colors", t.dominionTeamTextColors), ("Lobby Room Team Colors", t.lobbyRoomTeamColors),
        };

        [Test]
        public void EveryTeamColourIsAColourNeverWhiteOrGrey()
        {
            foreach ((string name, Color[] colours) in TeamLists(Theme()))
                for (int team = 0; team < colours.Length; team++)
                    Assert.GreaterOrEqual(Sat(colours[team]), MinTeamSaturation, $"{name} team {team} ({colours[team]}) reads as white or grey, which means neutral");
        }

        [Test]
        public void EachTeamIsOneHueInEveryList()
        {
            UiTheme t = Theme();
            foreach ((string name, Color[] colours) in TeamLists(t))
            {
                Assert.AreEqual(t.teamShotColors.Length, colours.Length, name + " has one colour per team");
                for (int team = 0; team < colours.Length; team++)
                    Assert.LessOrEqual(HueDegreesApart(colours[team], t.teamShotColors[team]), SameHueDegrees,
                        $"{name} team {team} ({colours[team]}) is another colour than its shots ({t.teamShotColors[team]})");
            }
        }

        [Test]
        public void NeutralColoursNeverReadAsATeam()
        {
            UiTheme t = Theme();
            var neutrals = new (string, Color)[]
            {
                ("Unknown Team Shot Color", t.unknownTeamShotColor), ("Tower Neutral Color", t.towerNeutralColor),
                ("Arena Neutral Line Color", t.arenaNeutralLineColor), ("Minimap Neutral Color", t.minimapNeutralColor),
                ("Capture Ring Neutral Color", t.captureRingNeutralColor), ("Dominion Dim Color", t.dominionDimColor),
                ("Flash White Color", t.flashWhiteColor), ("Barrier Top Stripe Color", Look().barrierTopStripeColor),
                ("Scan Wave Colour", ScanWave()),
            };
            foreach ((string name, Color neutral) in neutrals)
                for (int team = 0; team < t.teamShotColors.Length; team++)
                    Assert.IsTrue(ReadApart(neutral, t.teamShotColors[team]), $"{name} ({neutral}) reads as team {team} ({t.teamShotColors[team]})");
        }

        // These are the colours of fire with no known team and of the floor fan (a team's fire takes that team's colour): orange-red, reading
        // apart from every team or clearly on the red side of it (the Orange team's own orange and anything yellower read as the team), and on
        // the red side of the Shield's gold, apart from it. The heart is the palest part, as a flame's hottest part is.
        [Test]
        public void TeamlessFireColoursAreOrangeRedApartFromTheTeamsAndTheShieldGold()
        {
            UiTheme t = Theme();
            var fires = new[] { ("Fire Core Color", t.fireCoreColor), ("Fire Edge Color", t.fireEdgeColor), ("the Flamethrower fan's Core Color", FanCore()) };
            foreach ((string name, Color fire) in fires)
            {
                for (int team = 0; team < t.teamShotColors.Length; team++)
                    Assert.IsTrue(ReadApart(fire, t.teamShotColors[team]) || RedSideOf(fire, t.teamShotColors[team]),
                        $"{name} ({fire}) reads as team {team} ({t.teamShotColors[team]})");
                Assert.IsTrue(ReadApart(fire, t.immuneBarColor) && RedSideOf(fire, t.immuneBarColor), $"{name} ({fire}) is not clearly on the red side of the Shield's gold ({t.immuneBarColor})");
            }
            Assert.Less(Sat(t.fireCoreColor), Sat(t.fireEdgeColor), "the fire's heart is paler than its orange-red");
        }

        // Fire burns in the hue of the team that fired it (Tudor, 26 October), so whose fire it is reads at a glance. The colour a player
        // sees is the ramp times its sheet's own colour, so each team's flames are checked as drawn: every solid, coloured part of them and
        // of their hearts is nearer its own team's hue than any other team's, and the heart is paler than the flame around it.
        [Test]
        public void TheFlamesAsDrawnBurnInTheShootersTeamHueWithAPalerHeart()
        {
            UiTheme t = Theme();
            ArenaLook look = Look();
            Assert.AreSame(look, t.arenaLook, "the theme draws with the shipped Arena Look");
            for (int team = 0; team < t.teamShotColors.Length; team++)
            {
                Overpower.Abilities.FlameLook.FireColours fire = Overpower.Abilities.FlameLook.Colours(t, t.fireEdgeColor, team);
                Assert.IsTrue(fire.TeamSheets, $"team {team} fires its own fire");
                var paleness = new float[2];
                foreach ((bool heart, Gradient ramp) in new[] { (false, fire.Body), (true, fire.Core) })
                {
                    string name = $"team {team}'s {(heart ? "hearts" : "flames")}";
                    Color sheet = MeanOpaqueColour(Overpower.Abilities.FlameLook.SheetMaterial(look, heart, true));
                    int coloured = 0;
                    float saturation = 0f;
                    for (int i = 0; i <= 50; i++)
                    {
                        Color at = ramp.Evaluate(i / 50f);
                        if (at.a < 0.5f)
                            continue;
                        Color drawn = Drawn(at, sheet);
                        saturation += Sat(drawn);
                        coloured++;
                        if (Sat(drawn) < 0.25f)
                            continue;
                        for (int other = 0; other < t.teamShotColors.Length; other++)
                            if (other != team)
                                Assert.Less(HueDegreesApart(drawn, t.teamShotColors[team]), HueDegreesApart(drawn, t.teamShotColors[other]),
                                    $"{name} at {i / 50f:F2} of their life are drawn {drawn} (hue {Hue(drawn):F1}): nearer team {other}'s hue than their own");
                    }
                    Assert.Greater(coloured, 0, name + " are solid for part of their life");
                    paleness[heart ? 1 : 0] = saturation / coloured;
                }
                Assert.Less(paleness[1], paleness[0], $"team {team}'s hearts are paler than their flames");
            }
        }

        // Fire of no known team stays the theme's orange-red, as drawn on the orange sheets: clearly on the red side of the Shield's gold.
        [Test]
        public void TeamlessFlamesAsDrawnSitOnTheRedSideOfTheShieldGold()
        {
            UiTheme t = Theme();
            ArenaLook look = Look();
            Overpower.Abilities.FlameLook.FireColours fire = Overpower.Abilities.FlameLook.Colours(t, t.fireEdgeColor, -1);
            Assert.IsFalse(fire.TeamSheets);
            foreach ((bool heart, Gradient ramp) in new[] { (false, fire.Body), (true, fire.Core) })
            {
                Color sheet = MeanOpaqueColour(Overpower.Abilities.FlameLook.SheetMaterial(look, heart, false));
                int samples = 0;
                for (int i = 0; i <= 50; i++)
                {
                    Color at = ramp.Evaluate(i / 50f);
                    if (at.a < 0.5f)
                        continue;
                    samples++;
                    Color drawn = Drawn(at, sheet);
                    Assert.IsTrue(ReadApart(drawn, t.immuneBarColor) && RedSideOf(drawn, t.immuneBarColor),
                        $"{(heart ? "the hearts" : "the flames")} at {i / 50f:F2} of their life are drawn {drawn}: not clearly on the red side of the Shield's gold");
                }
                Assert.Greater(samples, 0);
            }
        }

        // The team whose colour is nearest today's fire burns like today's fire: its flames and hearts, as drawn on the team sheets, are as
        // strong in colour and as bright on average as fire of no team on the orange sheets, so team fire never reads paler than fire did.
        [Test]
        public void TheFireHuedTeamsFlamesAsDrawnAreAsStrongAndBrightAsTeamlessFire()
        {
            const float Tolerance = 0.08f;
            UiTheme t = Theme();
            ArenaLook look = Look();
            Overpower.Abilities.FlameLook.FireColours teamless = Overpower.Abilities.FlameLook.Colours(t, t.fireEdgeColor, -1);
            Color fireHue = Drawn(teamless.Body.Evaluate(0f), MeanOpaqueColour(Overpower.Abilities.FlameLook.SheetMaterial(look, false, false)));
            int nearest = 0;
            for (int team = 1; team < t.teamShotColors.Length; team++)
                if (HueDegreesApart(t.teamShotColors[team], fireHue) < HueDegreesApart(t.teamShotColors[nearest], fireHue))
                    nearest = team;
            Overpower.Abilities.FlameLook.FireColours teamFire = Overpower.Abilities.FlameLook.Colours(t, t.fireEdgeColor, nearest);
            Assert.IsTrue(teamFire.TeamSheets);
            foreach ((bool heart, Gradient teamRamp, Gradient teamlessRamp) in new[] { (false, teamFire.Body, teamless.Body), (true, teamFire.Core, teamless.Core) })
            {
                string name = heart ? "hearts" : "flames";
                (float s, float v) = MeanDrawn(teamRamp, MeanOpaqueColour(Overpower.Abilities.FlameLook.SheetMaterial(look, heart, true)));
                (float teamlessS, float teamlessV) = MeanDrawn(teamlessRamp, MeanOpaqueColour(Overpower.Abilities.FlameLook.SheetMaterial(look, heart, false)));
                Assert.AreEqual(teamlessS, s, Tolerance, $"team {nearest}'s {name} are drawn {s:F2} strong in colour, fire of no team {teamlessS:F2}");
                Assert.AreEqual(teamlessV, v, Tolerance, $"team {nearest}'s {name} are drawn {v:F2} bright, fire of no team {teamlessV:F2}");
            }
        }

        // Mean strength of colour and brightness of a ramp's solid samples drawn on a sheet.
        private static (float s, float v) MeanDrawn(Gradient ramp, Color sheet)
        {
            float s = 0f, v = 0f;
            int n = 0;
            for (int i = 0; i <= 50; i++)
            {
                Color at = ramp.Evaluate(i / 50f);
                if (at.a < 0.5f)
                    continue;
                Color drawn = Drawn(at, sheet);
                s += Sat(drawn);
                v += Val(drawn);
                n++;
            }
            Assert.Greater(n, 0, "the ramp is solid for part of its life");
            return (s / n, v / n);
        }

        private static Color Drawn(Color ramp, Color sheet)
        {
            Color linear = ramp.linear, under = sheet.linear;
            return new Color(linear.r * under.r, linear.g * under.g, linear.b * under.b, 1f).gamma;
        }

        // The sheet's mean colour over its solid pixels, read from the file the way TheShippedFireShapeNeverDrawsOutsideItsCircle reads it.
        private static Color MeanOpaqueColour(Material material)
        {
            Assert.IsNotNull(material, "the fire material");
            Texture sheet = material.GetTexture("_BaseMap");
            var pixels = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(pixels.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(sheet))));
                double r = 0, g = 0, b = 0;
                int n = 0;
                foreach (Color32 p in pixels.GetPixels32())
                {
                    if (p.a < 128)
                        continue;
                    r += p.r; g += p.g; b += p.b; n++;
                }
                Assert.Greater(n, 0, sheet.name + " draws something solid");
                return new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255), 1f);
            }
            finally
            {
                Object.DestroyImmediate(pixels);
            }
        }

        // Fire read as bright smoke while all of it was pushed past white (bake-off, 26 October): the flames stay at or under plain colour in
        // light and only their heart, drawn with light added, glows past white to bloom.
        [Test]
        public void OnlyTheFiresHeartGlowsPastWhite()
        {
            UiTheme t = Theme();
            ArenaLook look = Look();
            float body = 0f, heart = 0f;
            for (int i = 0; i <= 50; i++)
            {
                body = Mathf.Max(body, t.fireBodyOverLife.Evaluate(i / 50f).linear.maxColorComponent * look.flameBodyGlow);
                heart = Mathf.Max(heart, t.fireCoreOverLife.Evaluate(i / 50f).linear.maxColorComponent * look.flameGlow);
            }
            Assert.LessOrEqual(body, 1.1f, "the flames themselves never glow past white, so they never wash out to cream");
            Assert.Greater(heart, 1.2f, "their heart does, so the fire blooms hot");
        }

        // The Dominion HUD gold sits right next to a team's name on the cards, so it is checked against the text colours too.
        [Test]
        public void TeamsReadApartFromTheRedsAndTheGolds()
        {
            UiTheme t = Theme();
            var others = new (string, Color)[]
            {
                ("Capture Ring Warning Color (a contested tower)", t.captureRingWarningColor), ("Danger Color", t.dangerColor),
                ("Immune Bar Color (the Shield's gold)", t.immuneBarColor), ("Dominion Gold Color (the HUD's gold)", t.dominionGoldColor),
                ("Mark Color (a marked enemy and its damage numbers)", t.markColor),
            };
            foreach ((string name, Color other) in others)
                foreach ((string list, Color[] colours) in new[] { ("Team Shot Colors", t.teamShotColors), ("Dominion Team Text Colors", t.dominionTeamTextColors) })
                    for (int team = 0; team < colours.Length; team++)
                        Assert.IsTrue(ReadApart(other, colours[team]), $"{name} ({other}) reads as {list} team {team} ({colours[team]})");
        }

        [Test]
        public void TheConquestScanWaveIsWhite()
        {
            Color wave = ScanWave();
            Assert.LessOrEqual(Sat(wave), 0.15f, $"the scan wave ({wave}) is white, never a colour a team or an ability owns");
            Assert.GreaterOrEqual(Val(wave), 0.85f, $"and bright ({wave})");
        }

        [Test]
        public void TheLowBarriersArePaintedAsDarkAsTheArena()
        {
            ArenaLook look = Look();
            Assert.LessOrEqual(look.barrierSurface.baseTint.grayscale, look.blockSurface.baseTint.grayscale,
                "the low barriers are no lighter than the blocks: the grey on their top says low cover, not the paint");
            Assert.LessOrEqual(Sat(look.barrierSurface.baseTint), 0.3f, "and not a colour of their own");
            Assert.GreaterOrEqual(Val(look.barrierTopStripeColor) - Val(look.barrierSurface.baseTint), 0.3f,
                "their top stripe is clearly lighter than their sides, so from above a barrier reads as a raised bar");
            // The stripe against the real barriers' width: BarrierSurfaceSceneTests.
            float stripeLight = look.barrierTopStripeColor.linear.maxColorComponent * look.barrierTopStripeSelfLight;
            float wallLine = look.outsideLineColor.linear.maxColorComponent * look.outsideStripeGlow;
            Assert.GreaterOrEqual(stripeLight, 1.3f * wallLine, "the stripe's own light alone is clearly lighter than a wall's edge line, so it reads in any light");
            Assert.Less(stripeLight, 0.6f, "and stays plain paint: in linear light below the bloom threshold, so it never glows like an owned line");
        }
    }
}
