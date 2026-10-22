using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The team accent: an ability's own colour says what it is, a thin part of it in the caster's team colour (the shots' colour)
    /// says whose; movement is grey with a little of that team colour. One rule each (LookColours.Accent and the Movement family), asked by
    /// every view; an unknown team draws no accent. The colours here are made-up sentinels, never the tuned ones.</summary>
    public class TeamAccentTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int DashId = 14, BlinkId = 15, SprintId = 16, ZipId = 18, StunGunId = 22, RaybeamId = 24;
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string LookPath = "Assets/Gameplay/Config/ArenaLook.asset";
        private const string FxAlphaPath = "Assets/Gameplay/FX/Materials/FX Alpha.mat";
        private const string StunBulletPath = "Assets/Gameplay/Projectiles/Stun Gun Bullet.prefab";
        private const string StunBurstPath = "Assets/Gameplay/FX/Impacts/FX Stun Burst.prefab";
        private const string BeamPath = "Assets/Gameplay/FX/Raybeam Beam.prefab";
        private const string FlameConePath = "Assets/Gameplay/Abilities/Flamethrower Cone.prefab";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Color Own = new Color(0.41f, 0.42f, 0.43f, 1f);
        private static readonly Color Grey = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color Unknown = new Color(0.3f, 0.31f, 0.32f, 1f);
        private static readonly Color[] TeamColours =
        {
            new Color(0.9f, 0.3f, 0.1f, 1f),
            new Color(0.6f, 0.2f, 0.9f, 1f),
            new Color(0.1f, 0.8f, 0.9f, 1f),
        };

        private UiTheme theme;
        private ArenaLook look;

        [SetUp]
        public void CreateTheme()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            look = ScriptableObject.CreateInstance<ArenaLook>();
            theme.teamShotColors = (Color[])TeamColours.Clone();
            theme.unknownTeamShotColor = Unknown;
            theme.movementGreyColor = Grey;
            theme.arenaLook = look;
            look.movementTeamTint = 0.25f;
        }

        [TearDown]
        public void DestroyTheme()
        {
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private static MethodInfo AccentRule => typeof(LookColours).GetMethod(nameof(LookColours.Accent));

        // ---- the two rules ----

        [Test]
        public void TheAccentIsTheCastersTeamColourFromTheShotsSource()
        {
            for (int team = 0; team < TeamColours.Length; team++)
            {
                Assert.AreEqual(theme.ShotColorFor(team), LookColours.Accent(team, theme, Own), "team " + team + ": the colour its shots wear");
                Assert.AreEqual(TeamColours[team], LookColours.Accent(team, theme, Own));
            }
        }

        [Test]
        public void AnUnknownTeamHasNoAccentSoThePartKeepsTheAbilitysOwnColour()
        {
            foreach (int team in new[] { -1, 3, 99 })
                Assert.AreEqual(Own, LookColours.Accent(team, theme, Own), "team " + team + ": not the grey of an unknown shot, the ability's own colour");
            theme.teamShotColors = null;
            Assert.AreEqual(Own, LookColours.Accent(0, theme, Own), "a theme without team colours");
        }

        [Test]
        public void MovementIsGreyWithMovementTeamTintOfTheCastersTeam()
        {
            for (int team = 0; team < TeamColours.Length; team++)
                MovementLookTestKit.AssertRgb(Color.Lerp(Grey, TeamColours[team], 0.25f), LookColours.For(LookFamily.Movement, team, theme),
                    "team " + team + ": a quarter of the way from the grey to the team colour");
            Assert.AreNotEqual(LookColours.For(LookFamily.Movement, 0, theme), LookColours.For(LookFamily.Movement, 1, theme), "two teams' moves differ");

            look.movementTeamTint = 0f;
            Assert.AreEqual(Grey, LookColours.For(LookFamily.Movement, 1, theme), "no tint: plain grey");
            look.movementTeamTint = 1f;
            MovementLookTestKit.AssertRgb(TeamColours[1], LookColours.For(LookFamily.Movement, 1, theme), "full tint: the team colour");
            Assert.IsTrue(IlWiring.Uses(typeof(LookColours), nameof(LookColours.Movement), AccentRule), "the tint's colour is the accent's colour");
        }

        [Test]
        public void MovementOfAnUnknownTeamOrWithoutAnArenaLookIsPlainGrey()
        {
            Assert.AreEqual(Grey, LookColours.For(LookFamily.Movement, -1, theme), "no team known: no tint");
            theme.arenaLook = null;
            Assert.AreEqual(Grey, LookColours.For(LookFamily.Movement, 1, theme), "no Arena Look: no tint");
        }

        [Test]
        public void TheShippedMovementRowsReadTheMovementFamilyAndItShowsTheTeam()
        {
            var shippedTheme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            ArenaLook shipped = shippedTheme.arenaLook;
            Assert.AreEqual(LookPath, AssetDatabase.GetAssetPath(shipped));
            foreach (int id in new[] { DashId, BlinkId, SprintId, ZipId })
            {
                Assert.IsTrue(shipped.TryGetLook(id, out AbilityLook row), "ability " + id);
                Assert.AreEqual(LookFamily.Movement, row.Family, "ability " + id + " is movement");
                Assert.AreNotEqual(LookColours.ForAbility(shippedTheme, id, 1, LookFamily.Gold), LookColours.ForAbility(shippedTheme, id, 2, LookFamily.Gold),
                    "ability " + id + ": two teams' moves can be told apart in the shipped look");
            }
        }

        [Test]
        public void TheTeleportRingsAreGreyTintedByTheTeamTheCastHandsIn()
        {
            for (int team = 0; team < TeamColours.Length; team++)
                MovementLookTestKit.AssertRgb(Color.Lerp(Grey, TeamColours[team], 0.25f), TeleportAbility.RingColour(theme, team), "team " + team);
            Assert.AreEqual(Grey, TeleportAbility.RingColour(theme, -1), "no team known: plain grey");

            MethodInfo spawn = typeof(TeleportAbility).GetMethod("SpawnMarker", Any);
            AssertAll(
                (LastArgumentIs(spawn, typeof(TeleportAbility).GetMethod(nameof(TeleportAbility.RingColour)), Ldarg(3)), "the mark's colour takes SpawnMarker's caster team"),
                (LastArgumentIs(typeof(TeleportAbility).GetMethod("PlayArrivalVfx", Any), spawn, Ldarg(2)), "the arrival marks hand theirs in"),
                (LastArgumentIs(typeof(TeleportAbility).GetMethod(nameof(TeleportAbility.ExecuteCast)), spawn, CastersTeam), "the channel mark: the cast's caster team"));
        }

        [Test]
        public void TheSprintLinesTakeTheSprintersTeamAgainEveryTimeTheyStart()
        {
            using (var kit = new MovementLookTestKit(SprintId, LookShape.SpeedLines, LookFamily.Movement))
            {
                kit.Theme.teamShotColors = (Color[])TeamColours.Clone();
                kit.Look.movementTeamTint = 0.25f;
                kit.Look.sprintLineCount = 1;
                kit.Look.sprintLineMinSpeed = 1f;
                kit.Look.sprintLineAlpha = 1f;
                GameObject player = kit.NewObject("tinted sprinter");
                player.transform.position = new Vector3(0f, 500.5f, 0f);
                SprintAbility sprint = player.AddComponent<SprintAbility>();
                kit.GiveDefinition(sprint, SprintId);
                Photon.Pun.PhotonView caster = player.AddComponent<Photon.Pun.PhotonView>();
                var owner = (Photon.Realtime.Player)System.Activator.CreateInstance(typeof(Photon.Realtime.Player), Any, null,
                    new object[] { "sprinter", 7, false }, null);
                typeof(Photon.Pun.PhotonView).GetProperty(nameof(Photon.Pun.PhotonView.Owner)).SetValue(caster, owner);

                SprintLinesView view = SprintLinesView.TryCreate(player.transform, sprint, caster, kit.Theme, 0.5f);
                LineRenderer line = view.GetComponentInChildren<LineRenderer>(true);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Grey, line.startColor, "equipped before the team arrived: plain grey");

                float time = 0f;
                foreach (int team in new[] { 2, 0 })
                {
                    owner.CustomProperties[Overpower.Net.Teams.TeamKey] = team; // arrives (or changes) after the lines were built
                    sprint.ExecuteCast(new CastEvent(default, SprintLinesRule.StartedPhase, 2, 1, false, 0f));
                    view.Step(Vector3.right * 0.75f, 0.1f, time += 0.1f);
                    Assert.IsTrue(line.gameObject.activeInHierarchy, "sprinting");
                    MovementLookTestKit.AssertRgb(Color.Lerp(MovementLookTestKit.Grey, TeamColours[team], 0.25f), line.startColor,
                        "team " + team + ", known when this run starts");
                    sprint.ExecuteCast(new CastEvent(default, SprintLinesRule.StoppedPhase, 2, 1, false, 0f));
                    view.Step(Vector3.right * 0.75f, 0.1f, time += 0.1f);
                    Assert.IsFalse(line.gameObject.activeInHierarchy, "stopped");
                }
            }
        }

        [Test]
        public void EveryCastHandsItsCastersTeamToTheMovementAndAccentViews()
        {
            System.Type spray = typeof(FlamethrowerAbility).GetNestedTypes(Any).Single(t => t.Name.StartsWith("<SprayRoutine>"));
            MethodInfo showVfx = typeof(FlamethrowerAbility).GetMethod("ShowVfx", Any);
            AssertAll(
                (LastArgumentIs(typeof(BlinkAbility).GetMethod(nameof(BlinkAbility.ExecuteCast)), typeof(BlinkMarkView).GetMethod(nameof(BlinkMarkView.PlayDeparture)), CastersTeam), "Blink shards"),
                (LastArgumentIs(typeof(BlinkAbility).GetMethod(nameof(BlinkAbility.ExecuteCast)), typeof(BlinkMarkView).GetMethod(nameof(BlinkMarkView.PlayArrival)), CastersTeam), "Blink ring"),
                (IlWiring.Uses(typeof(DashAbility), "PlayRemoteStreak", typeof(CastEvent).GetField(nameof(CastEvent.CasterTeam))), "Dash streak and chevrons"),
                (LastArgumentIs(typeof(ZipGunAbility).GetMethod(nameof(ZipGunAbility.ExecuteCast)), typeof(ZipGunAbility).GetMethod("PlayTether", Any), CastersTeam), "Zip cable"),
                (LastArgumentIs(typeof(FlamethrowerAbility).GetMethod(nameof(FlamethrowerAbility.ExecuteCast)), typeof(FlamethrowerAbility).GetMethod("SprayRoutine", Any), CastersTeam), "Flamethrower spray"),
                (LastArgumentIs(spray.GetMethod("MoveNext", Any), showVfx, Ldfld(spray.GetField("casterTeam", Any))), "Flamethrower fan outline"),
                (LastArgumentIs(typeof(RaybeamAbility).GetMethod(nameof(RaybeamAbility.ShowBeam), Any), typeof(RaybeamBeamView).GetMethod(nameof(RaybeamBeamView.Draw)), Ldarg(6)), "Raybeam edges"),
                (LastArgumentIs(typeof(RaybeamAbility).GetMethod("ShowCharge", Any), typeof(RaybeamChargeView).GetMethod(nameof(RaybeamChargeView.Begin)), CastersTeam), "Raybeam charge edge"));
        }

        [Test]
        public void ThePulseTheShieldAndTheStunBoltTakeTheCastersTeam()
        {
            MethodInfo pulseCast = typeof(SonicPulseAbility).GetMethod(nameof(SonicPulseAbility.ExecuteCast));
            byte[] pulseIl = pulseCast.GetMethodBody().GetILAsByteArray();
            var angleAt = IlWiring.CallOffsets(pulseCast, typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeAngle)).GetMethod);
            var playAt = IlWiring.CallOffsets(pulseCast, typeof(SonicPulseView).GetMethod(nameof(SonicPulseView.Play)));
            byte[] castsTeam = new byte[] { 0x03 }.Concat(CastersTeam).ToArray(); // ldarg.1 (the cast) + ldfld CasterTeam: Play's team argument, right after the angle
            bool pulse = angleAt.Count == 1 && playAt.Count == 1 && pulseIl.Skip(angleAt[0] + 5).Take(castsTeam.Length).SequenceEqual(castsTeam);

            MethodInfo showShield = typeof(InvulnerabilityAbility).GetMethod("ShowShield", Any);
            MethodInfo teamGetter = typeof(PlayerHealth).GetProperty(nameof(PlayerHealth.TeamId)).GetMethod;

            MethodInfo onSpawned = typeof(LookTint).GetMethod(nameof(LookTint.OnSpawned));
            var shooterAt = IlWiring.CallOffsets(onSpawned, typeof(ProjectileContext).GetProperty(nameof(ProjectileContext.ShooterTeamId)).GetMethod);
            var applyAt = IlWiring.CallOffsets(onSpawned, typeof(LookTint).GetMethod(nameof(LookTint.Apply)));

            Assert.NotNull(showShield, "the Shield has its ShowShield");
            AssertAll(
                (pulse, "Sonic Pulse: the cast's team is Play's team argument"),
                (LastArgumentIs(typeof(InvulnerabilityAbility).GetMethod(nameof(InvulnerabilityAbility.ExecuteCast)), showShield, CastersTeam), "Shield: the cast's team reaches the shell"),
                (LastArgumentIs(showShield, typeof(ShieldLook).GetMethod(nameof(ShieldLook.Colour)), Ldarg(1)), "Shield: the shell's gold takes it"),
                (!IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", teamGetter), "Shield: no second team source beside the armed ring's"),
                (applyAt.Count == 1 && shooterAt.Count == 1 && shooterAt[0] + 5 == applyAt[0], "Stun bolt: the shooter's team is what Apply is handed"));
        }

        [Test]
        public void EveryAccentedViewAsksTheOneAccentRule()
        {
            MethodInfo accent = AccentRule;
            AssertAll(
                (IlWiring.Uses(typeof(SonicPulseView), nameof(SonicPulseView.Play), accent), "Sonic Pulse arch edge"),
                (IlWiring.Uses(typeof(LookTint), nameof(LookTint.Apply), accent), "Stun Gun bolt crackle"),
                (IlWiring.Uses(typeof(FlamethrowerAbility), "ShowVfx", accent), "Flamethrower fan outline"),
                (IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", accent), "Shield base ring"),
                (IlWiring.Uses(typeof(RaybeamBeamView), nameof(RaybeamBeamView.Draw), accent), "Raybeam edges"),
                (IlWiring.Uses(typeof(FireField), "ShowLook", accent), "Rocket Cursor Fire rim"),
                (IlWiring.Uses(typeof(LookColours), nameof(LookColours.For), typeof(LookColours).GetMethod(nameof(LookColours.Movement))), "the movement family"));
        }

        // ---- Sonic Pulse ----

        [Test]
        public void ThePulseArchsEdgeIsTheCastersTeamAndTheArchKeepsItsBlue()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var go = new GameObject("pulse accent test");
            try
            {
                theme.statusStunnedColor = Own;
                look.fxAlpha = material;
                look.pulseArchCount = 1;
                look.pulseSweepSeconds = 0.2f;
                look.pulseFadeSeconds = 0.25f;
                look.pulseArchWidth = 0.3f;
                look.pulseArchHeight = 0.8f;
                look.pulseArchMaxHeight = 1.1f;
                look.pulseArchAlpha = 0.8f;
                look.pulseEdgeWidth = 0.05f;
                look.pulseEdgeStrength = 0.5f;
                SonicPulseView view = go.AddComponent<SonicPulseView>();
                MovementLookTestKit.SetTheme(view, theme);

                view.Play(Vector3.zero, Vector3.forward, 5f, 60f, 2, null);
                view.Draw(0.1f);
                Transform arch = view.Arch.transform.Find("Arch 0");
                LineRenderer edge = arch.Find(SonicPulseView.EdgeName).GetComponent<LineRenderer>();
                MovementLookTestKit.AssertRgb(TeamColours[2], edge.startColor, "the edge says whose pulse it is");
                MovementLookTestKit.AssertRgb(Own, arch.GetComponent<LineRenderer>().startColor, "the arch itself stays the pulse's own colour");

                view.Play(Vector3.zero, Vector3.forward, 5f, 60f, -1, null);
                view.Draw(0.1f);
                MovementLookTestKit.AssertRgb(Own, edge.startColor, "no team known: the edge is the arch's own colour");
            }
            finally
            {
                SonicPulseView view = go.GetComponent<SonicPulseView>();
                if (view != null && view.Arch != null)
                    Object.DestroyImmediate(view.Arch);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TheTubeOutlineFramesTheTubeSidewaysAsTheViewerSeesIt()
        {
            var points = new[] { new Vector3(-2f, 0f, 3f), new Vector3(0f, 1f, 3f), new Vector3(2f, 0f, 3f) };
            var outline = new Vector3[6];
            var viewer = new Vector3(0f, 20f, -10f);
            Overpower.Combat.AbilityVisualGeometry.TubeOutline(points, 3, viewer, 0.2f, outline);
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = outline[k], b = outline[5 - k];
                Assert.AreEqual(0f, Vector3.Distance((a + b) * 0.5f, points[k]), 1e-5f, "point " + k + ": one side out, the other back");
                Assert.AreEqual(0.2f, Vector3.Distance(a, points[k]), 1e-5f, "the offset from the tube's middle");
                Vector3 tangent = points[Mathf.Min(k + 1, 2)] - points[Mathf.Max(k - 1, 0)];
                Assert.AreEqual(0f, Vector3.Dot(a - points[k], tangent), 1e-4f, "across the tube, not along it");
                Assert.AreEqual(0f, Vector3.Dot(a - points[k], viewer - points[k]), 1e-4f, "sideways as the viewer sees it, the way the camera-facing tube widens");
            }
        }

        [Test]
        public void TheTubeOutlineSeenEndOnStillStandsBesideTheTube()
        {
            foreach (Vector3 dir in new[] { Vector3.right, Vector3.up })
            {
                var points = new[] { Vector3.zero, dir, dir * 2f };
                var outline = new Vector3[6];
                Overpower.Combat.AbilityVisualGeometry.TubeOutline(points, 3, dir * 10f, 0.2f, outline); // the viewer in line with the tube
                for (int k = 0; k < 3; k++)
                {
                    Assert.AreEqual(0.2f, Vector3.Distance(outline[k], points[k]), 1e-5f, dir + " point " + k + ": never collapsed onto the tube");
                    Assert.AreEqual(0f, Vector3.Dot(outline[k] - points[k], dir), 1e-4f, "across the tube");
                    Assert.AreEqual(0f, Vector3.Distance((outline[k] + outline[5 - k]) * 0.5f, points[k]), 1e-5f, "one side out, the other back");
                }
            }
        }

        // ---- Stun Gun ----

        [Test]
        public void TheStunBoltsCrackleWearsTheCastersTeamAndTheBurstWhereItLandsDoesNot()
        {
            theme.statusStunnedColor = Own;
            look.abilitySparkGlow = 1f;
            look.stunBoltGlow = 1f;
            SetRow(StunGunId, LookShape.Bolt, LookFamily.ElectricBlue);
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            GameObject bolt = TintedObject("bolt", lit, true, out MeshRenderer core, out LineRenderer spark);
            GameObject burst = TintedObject("burst", lit, false, out _, out LineRenderer burstSpark);
            var tail = new GameObject("Team Trail");
            tail.transform.SetParent(bolt.transform, false);
            TrailRenderer trail = tail.AddComponent<TrailRenderer>();
            look.stunTrailSeconds = 0.17f;
            look.stunTrailWidth = 0.09f;
            try
            {
                var block = new MaterialPropertyBlock();
                Assert.IsTrue(bolt.GetComponent<LookTint>().Apply(1));
                spark.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(TeamColours[1], block.GetColor(BaseColorId), "the crackle says whose bolt it is");
                trail.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(TeamColours[1], block.GetColor(BaseColorId), "and so does the thin trail behind it");
                Assert.AreEqual(0.17f, trail.time, 1e-5f, "Stun Trail Seconds");
                Assert.AreEqual(0.09f, trail.widthMultiplier, 1e-5f, "Stun Trail Width");
                core.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(Own, block.GetColor(BaseColorId), "the bolt itself stays electric blue");

                Assert.IsTrue(bolt.GetComponent<LookTint>().Apply(-1));
                spark.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(Own, block.GetColor(BaseColorId), "no team known: the crackle stays blue");
                trail.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(Own, block.GetColor(BaseColorId), "and the trail too");

                Assert.IsTrue(burst.GetComponent<LookTint>().Apply(1));
                burstSpark.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(Own, block.GetColor(BaseColorId), "a hit never carries a team");
            }
            finally
            {
                Object.DestroyImmediate(bolt);
                Object.DestroyImmediate(burst);
                Object.DestroyImmediate(lit);
            }
        }

        [Test]
        public void TheShippedBoltCarriesTheAccentAndItsBurstDoesNot()
        {
            Assert.IsTrue(SparksInCastersTeam(StunBulletPath), "the bolt's crackle says whose bolt it is");
            Assert.IsFalse(SparksInCastersTeam(StunBurstPath), "the burst is a hit: no team");

            GameObject bolt = AssetDatabase.LoadAssetAtPath<GameObject>(StunBulletPath);
            TrailRenderer[] trails = bolt.GetComponentsInChildren<TrailRenderer>(true);
            Assert.AreEqual(1, trails.Length, "one thin team trail behind the bolt, a line that reads where the crackle's sparks are specks");
            TrailRenderer trail = trails[0];
            Assert.IsTrue(trail.transform.IsChildOf(bolt.GetComponentInChildren<LookTint>(true).transform), "tinted by the bolt's LookTint");
            Assert.AreEqual(FxAlphaPath, AssetDatabase.GetAssetPath(trail.sharedMaterial), "a colour, faded by its gradient");
            Assert.IsTrue(trail.emitting);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, trail.shadowCastingMode);
            Color head = trail.colorGradient.Evaluate(0f);
            MovementLookTestKit.AssertRgb(Color.white, head, "white, so the tint alone gives its colour");
            Assert.Greater(head.a, 0.5f, "solid at the bolt");
            Assert.AreEqual(0f, trail.colorGradient.Evaluate(1f).a, 1e-3f, "fading out behind the bolt");
            Assert.IsEmpty(AssetDatabase.LoadAssetAtPath<GameObject>(StunBurstPath).GetComponentsInChildren<TrailRenderer>(true), "the burst has no trail");
            Assert.IsEmpty(bolt.GetComponentsInChildren<Collider>(true).Where(c => c.transform.IsChildOf(trail.transform)), "a look never adds a collider");
        }

        // ---- Flamethrower ----

        [Test]
        public void TheFlameFansOutlineTakesTheOutlineColourAndTheShowHandsItTheAccent()
        {
            GameObject cone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FlameConePath));
            try
            {
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(5f, 60f, Own, TeamColours[0], true);
                var edge = (LineRenderer)Ref(visual, "edge");
                Assert.IsNotNull(edge);
                MovementLookTestKit.AssertRgb(TeamColours[0], edge.startColor, "the fan's outline says whose fire it is");
                Assert.AreEqual(new SerializedObject(visual).FindProperty("edgeOpacity").floatValue, edge.startColor.a, 1f / 255f, "at the outline's own opacity");
            }
            finally { Object.DestroyImmediate(cone); }

            MethodInfo show = typeof(FlamethrowerAbility).GetMethod("ShowVfx", Any);
            int accentAt = IlWiring.CallOffsets(show, AccentRule).Single();
            int configureAt = IlWiring.CallOffsets(show, typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.Configure))).Single();
            Assert.Less(accentAt, configureAt, "the accent is asked for before the fan is coloured");
            Assert.AreEqual(1, show.GetParameters().Length, "the cast's caster team is handed in (EveryCastHandsItsCastersTeam... checks from where)");
        }

        // ---- Shield ----

        [Test]
        public void TheShieldHasAThinTeamRingHuggingTheBaseOfItsShell()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var shell = new GameObject("shield accent test");
            var bare = new GameObject("shield accent test, no look");
            try
            {
                look.fxAlpha = material;
                look.shieldTeamRingWidth = 0.07f;
                look.shieldTeamRingHeight = 0.05f;
                look.shieldTeamRingAlpha = 0.6f;
                look.shieldTeamRingGlow = 1.7f;
                const float diameter = 2.6f, lift = 1f;
                shell.transform.position = new Vector3(3f, 10f + lift, 4f); // the caster's feet at y 10
                shell.transform.localScale = Vector3.one * diameter;

                LineRenderer ring = ShieldLook.AddTeamRing(shell, diameter, lift, TeamColours[1], look);
                Assert.IsNotNull(ring);
                Assert.IsTrue(ring.transform.IsChildOf(shell.transform), "it comes and goes with the shell, behind the same fog gate");
                Assert.AreEqual(1f, ring.transform.lossyScale.x, 1e-4f, "measured in metres, whatever size the shell is drawn");
                Assert.AreSame(material, ring.sharedMaterial, "FX Alpha");
                Assert.AreEqual(0.07f, ring.widthMultiplier, 1e-5f, "Shield Team Ring Width");
                Assert.AreEqual(LineAlignment.TransformZ, ring.alignment, "lying flat");
                Assert.IsTrue(ring.loop);
                Assert.AreEqual(6, ring.positionCount, "a hexagon, like the armed ring");
                MovementLookTestKit.AssertRgb(TeamColours[1], ring.startColor, "the caster's team colour");
                Assert.AreEqual(0.6f, ring.startColor.a, 1f / 255f, "Shield Team Ring Alpha");
                Assert.AreEqual(new Color(1.7f, 1.7f, 1.7f, 1f), GlowOf(ring), "Shield Team Ring Glow: brighter than the owned floor's lines, so a hexagon on a hex floor still reads");

                const float across = 2.6f * 0.5f; // the shell's widest circle: from the camera above, the ring shows round the shell, not under it
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = ring.transform.TransformPoint(ring.GetPosition(k));
                    Assert.AreEqual(10.05f, p.y, 1e-3f, "Shield Team Ring Height over the feet");
                    Assert.AreEqual(across / Mathf.Cos(30f * Mathf.Deg2Rad), new Vector2(p.x - 3f, p.z - 4f).magnitude, 1e-3f,
                        "its sides touch the shell's outline as seen from above, so it rims the shell's base from outside");
                }

                Assert.IsNull(ShieldLook.AddTeamRing(bare, diameter, lift, TeamColours[1], null), "no look: no ring");
                Assert.AreEqual(0, bare.transform.childCount);
            }
            finally
            {
                Object.DestroyImmediate(shell);
                Object.DestroyImmediate(bare);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ShowShieldAddsTheTeamRingBeforeTheFogGate()
        {
            MethodInfo show = typeof(InvulnerabilityAbility).GetMethod("ShowShield", Any);
            var ringAt = IlWiring.CallOffsets(show, typeof(ShieldLook).GetMethod(nameof(ShieldLook.AddTeamRing)));
            var attachAt = IlWiring.CallOffsets(show, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            Assert.AreEqual(1, ringAt.Count, "the shell gets its team ring");
            Assert.AreEqual(1, attachAt.Count);
            Assert.Less(ringAt[0], attachAt[0], "the gate collects the renderers once: the ring must exist before it attaches");
        }

        // ---- Raybeam ----

        [Test]
        public void TheRaybeamHasAThinTeamOutlineJustOutsideItsSheath()
        {
            using (var kit = new MovementLookTestKit(RaybeamId, LookShape.Beam))
            {
                kit.Theme.teamShotColors = (Color[])TeamColours.Clone();
                kit.Look.raybeamSheathAlpha = 0.85f;
                kit.Look.raybeamEdgeWidth = 0.04f;
                kit.Look.raybeamEdgeAlpha = 0.7f;
                kit.Look.raybeamEdgeGap = 0.03f;
                kit.Look.raybeamEdgeGlow = 1.3f;
                GameObject go = kit.NewObject("beam accent test");
                LineRenderer sheath = Line(kit, go, "Sheath"), core = Line(kit, go, "Core"), edge = Line(kit, go, "Edge");
                RaybeamBeamView view = go.AddComponent<RaybeamBeamView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = kit.Theme;
                so.FindProperty("abilityId").intValue = RaybeamId;
                so.FindProperty("sheath").objectReferenceValue = sheath;
                so.FindProperty("core").objectReferenceValue = core;
                so.FindProperty("edge").objectReferenceValue = edge;
                so.ApplyModifiedPropertiesWithoutUndo();
                var from = new Vector3(1f, 1.2f, 2f);
                var to = new Vector3(9f, 1.2f, 14f);

                // Three lines on one track: the merged beam is one plain beam, its outline a rectangle.
                Vector3[] froms = { from, from, from }, tos = { to, to, to };
                view.Draw(from, to - from, froms, tos, 0.5f, 2);
                Assert.IsTrue(edge.useWorldSpace);
                Assert.IsTrue(edge.loop, "one closed outline round the beam");
                Assert.AreEqual(LineAlignment.TransformZ, edge.alignment, "laid flat like the sheath, so the camera sees both the same way");
                Assert.Greater(Vector3.Dot(edge.transform.forward, Vector3.down), 0.999f);
                Assert.AreEqual(4, edge.positionCount);
                Assert.AreEqual(0.04f, edge.startWidth, 1e-5f, "Raybeam Edge Width thick");
                Assert.AreEqual(0.04f, edge.endWidth, 1e-5f);
                Vector3 along = (to - from).normalized;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = edge.GetPosition(k);
                    Vector3 end = k == 1 || k == 2 ? to : from;
                    Vector3 off = p - end;
                    Assert.AreEqual(0f, Vector3.Dot(off, along), 1e-4f, "corner " + k + " sits beside an end of the beam");
                    Assert.AreEqual(0f, off.y, 1e-4f, "sideways in the floor's plane, so the camera above sees both sides");
                    Assert.AreEqual((0.5f + 0.04f) * 0.5f + 0.03f, off.magnitude, 1e-4f,
                        "its line runs outside the sheath with Raybeam Edge Gap of floor between, never over the beam's body");
                }
                Assert.Less(Vector3.Dot(edge.GetPosition(0) - from, edge.GetPosition(3) - from), 0f, "one long side each side of the beam");
                MovementLookTestKit.AssertRgb(TeamColours[2], edge.startColor, "the outline says whose beam it is");
                Assert.AreEqual(0.7f, edge.startColor.a, 1f / 255f, "Raybeam Edge Alpha");
                Assert.AreEqual(new Color(1.3f, 1.3f, 1.3f, 1f), GlowOf(edge), "Raybeam Edge Glow");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, sheath.startColor, "the sheath keeps the Raybeam's own row");
                Assert.AreEqual(0.5f, sheath.widthCurve.Evaluate(0f) * sheath.widthMultiplier, 1e-5f, "and its width, what the beam hits");

                view.Draw(from, to - from, froms, tos, 0.5f, -1);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, edge.startColor, "no team known: the outline is the beam's own colour");
                Assert.IsTrue(IlWiring.Uses(typeof(RaybeamBeamView), "PaintEdge", typeof(RaybeamLook).GetMethod(nameof(RaybeamLook.MergedOutline))), "the outline's shape is the rule's");
            }
        }

        [Test]
        public void TheShippedRaybeamBeamHasItsTeamEdge()
        {
            GameObject beam = AssetDatabase.LoadAssetAtPath<GameObject>(BeamPath);
            var view = beam.GetComponent<RaybeamBeamView>();
            var edge = (LineRenderer)Ref(view, "edge");
            Assert.IsNotNull(edge, "the beam prefab has its edge line");
            Assert.IsTrue(edge.transform.IsChildOf(beam.transform));
            Assert.AreNotSame(Ref(view, "sheath"), edge);
            Assert.AreEqual(FxAlphaPath, AssetDatabase.GetAssetPath(edge.sharedMaterial), "a colour that reads over any floor");
            Assert.AreEqual(0, edge.numCapVertices, "hard ends");
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, edge.shadowCastingMode);
            Assert.IsEmpty(beam.GetComponentsInChildren<Collider>(true), "a look never adds a collider");
        }

        // ---- movement: the views read their row, and the row's family tints with the team they are handed ----

        [Test]
        public void TheDashBlinkAndZipPaintTheirGreyTintedByTheCastersTeam()
        {
            AssertTinted(DashId, LookShape.Streak, (kit, team) =>
            {
                kit.Look.dashStreakWidth = 0.4f;
                kit.Look.dashStreakAlpha = 0.8f;
                kit.Look.dashChevronCount = 1;
                GameObject go = kit.NewObject("dash tint test");
                kit.GiveDefinition(go.AddComponent<DashAbility>(), DashId);
                DashStreakView view = go.AddComponent<DashStreakView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                Transform body = kit.NewObject("dasher").transform;
                body.position = new Vector3(3f, 500f, 1f);
                view.Play(new Vector3(2f, 500f, 1f), Vector3.right, 3f, 0.2f, body, team, null);
                kit.Make(view.Root);
                view.Draw(0.1f);
                return view.Root.transform.Find(DashStreakView.StreakName).GetComponent<LineRenderer>().startColor;
            });
            AssertTinted(BlinkId, LookShape.Shards, (kit, team) =>
            {
                kit.Look.blinkShardCount = 3;
                kit.Look.blinkShardSeconds = 0.35f;
                kit.Look.blinkAlpha = 0.9f;
                GameObject go = kit.NewObject("blink tint test");
                kit.GiveDefinition(go.AddComponent<BlinkAbility>(), BlinkId);
                BlinkMarkView view = go.AddComponent<BlinkMarkView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                view.PlayDeparture(new Vector3(2f, 500f, 1f), team);
                kit.Make(view.Shards);
                view.DrawShards(0.1f);
                return view.Shards.GetComponentInChildren<LineRenderer>(true).startColor;
            });
            AssertTinted(ZipId, LookShape.HarpoonCable, (kit, team) =>
            {
                kit.Look.zipCable = kit.Make(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                kit.Look.abilityShotGlow = 1f;
                GameObject bolt = kit.NewObject("zip tint test");
                var head = new GameObject("Head");
                head.transform.SetParent(bolt.transform, false);
                MeshRenderer headRenderer = head.AddComponent<MeshRenderer>();
                head.AddComponent<MeshFilter>();
                var ropeObject = new GameObject("Rope");
                ropeObject.transform.SetParent(bolt.transform, false);
                LineRenderer rope = ropeObject.AddComponent<LineRenderer>();
                rope.positionCount = 2;
                ZipBoltView view = bolt.AddComponent<ZipBoltView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = kit.Theme;
                so.FindProperty("head").objectReferenceValue = headRenderer;
                so.FindProperty("rope").objectReferenceValue = rope;
                so.ApplyModifiedPropertiesWithoutUndo();
                view.OnSpawned(null, new ProjectileContext(ZipId, 40f, 0.225f, 15f, 0f, -1, team, Vector3.forward, bolt.transform.position));
                var block = new MaterialPropertyBlock();
                headRenderer.GetPropertyBlock(block);
                return block.GetColor(BaseColorId);
            });
        }

        // The view's colour for each team is the grey a quarter of the way to that team, so each view hands its caster's team over.
        private static void AssertTinted(int abilityId, LookShape shape, System.Func<MovementLookTestKit, int, Color> paint)
        {
            foreach (int team in new[] { 0, 1, 2 })
                using (var kit = new MovementLookTestKit(abilityId, shape, LookFamily.Movement))
                {
                    kit.Theme.teamShotColors = (Color[])TeamColours.Clone();
                    kit.Look.movementTeamTint = 0.25f;
                    MovementLookTestKit.AssertRgb(Color.Lerp(MovementLookTestKit.Grey, TeamColours[team], 0.25f), paint(kit, team),
                        "ability " + abilityId + " for team " + team);
                }
        }

        // ---- helpers ----

        private void SetRow(int id, LookShape shape, LookFamily family)
        {
            var so = new SerializedObject(look);
            SerializedProperty list = so.FindProperty("abilityLooks");
            list.arraySize = 1;
            SerializedProperty row = list.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("abilityId").intValue = id;
            row.FindPropertyRelative("shape").enumValueIndex = (int)shape;
            row.FindPropertyRelative("family").enumValueIndex = (int)family;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private GameObject TintedObject(string objectName, Material lit, bool accent, out MeshRenderer core, out LineRenderer spark)
        {
            var root = new GameObject(objectName);
            root.AddComponent<MeshFilter>();
            core = root.AddComponent<MeshRenderer>();
            core.sharedMaterial = lit;
            var sparks = new GameObject("Crackle");
            sparks.transform.SetParent(root.transform, false);
            spark = sparks.AddComponent<LineRenderer>();
            LookTint tint = root.AddComponent<LookTint>();
            var so = new SerializedObject(tint);
            so.FindProperty("theme").objectReferenceValue = theme;
            so.FindProperty("abilityId").intValue = StunGunId;
            so.FindProperty("sparksInCastersTeam").boolValue = accent;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static bool SparksInCastersTeam(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            LookTint tint = prefab.GetComponentInChildren<LookTint>(true);
            Assert.IsNotNull(tint, prefabPath + " is tinted from the look table");
            Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(Ref(tint, "theme")));
            return new SerializedObject(tint).FindProperty("sparksInCastersTeam").boolValue;
        }

        private static LineRenderer Line(MovementLookTestKit kit, GameObject parent, string lineName)
        {
            GameObject part = kit.NewObject(lineName);
            part.transform.SetParent(parent.transform, false);
            LineRenderer line = part.AddComponent<LineRenderer>();
            line.sharedMaterial = kit.Fx;
            return line;
        }

        private static Object Ref(Object owner, string field) => new SerializedObject(owner).FindProperty(field).objectReferenceValue;

        private static Color GlowOf(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(BaseColorId);
        }

        private static readonly byte[] CastersTeam = Ldfld(typeof(CastEvent).GetField(nameof(CastEvent.CasterTeam)));

        private static byte[] Ldarg(int index) => index <= 3 ? new[] { (byte)(0x02 + index) } : new byte[] { 0x0E, (byte)index };

        private static byte[] Ldfld(FieldInfo field) => new byte[] { 0x7B }.Concat(System.BitConverter.GetBytes(field.MetadataToken)).ToArray();

        // Every call of the target in the method takes, as its last argument, what the instruction right before the call loads: the hand-over
        // of a team (an argument or a field), not a -1 or a team read from somewhere else.
        private static bool LastArgumentIs(MethodBase method, MethodBase target, byte[] load)
        {
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            var calls = IlWiring.CallOffsets(method, target);
            return calls.Count > 0 && calls.All(at => at >= load.Length && il.Skip(at - load.Length).Take(load.Length).SequenceEqual(load));
        }

        // Every failing check named in one run.
        private static void AssertAll(params (bool ok, string what)[] checks)
        {
            string[] failed = checks.Where(c => !c.ok).Select(c => c.what).ToArray();
            Assert.IsEmpty(failed, "failed: " + string.Join(" | ", failed));
        }
    }
}
