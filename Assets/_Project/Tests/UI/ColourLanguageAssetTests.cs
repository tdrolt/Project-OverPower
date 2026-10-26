using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The colour language on the saved assets: a team colour is a colour (white and grey mean neutral), each team is one hue in every
    /// list that draws it, the neutral greys, the whites, the reds and the golds never read as a team, fire is orange-red (on the red side
    /// of the Orange team and of the Shield's gold, and only its heart glows past white), the Conquest scan wave is white and the low
    /// barriers are painted as dark as the rest of the arena with a light stripe down their top. Relations only: the colours themselves
    /// stay free to tune.</summary>
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

        // Fire is orange-red (Tudor, 25 October): it reads apart from every team, or sits clearly on the red side of it (the Orange team's own
        // orange and anything yellower read as the team), and it sits on the red side of the Shield's gold, apart from it. Its heart is
        // the palest part of it, as a flame's hottest part is.
        [Test]
        public void FireIsOrangeRedApartFromTheTeamsAndTheShieldGold()
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
