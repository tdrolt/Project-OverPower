using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Lays fire out over the ground it really burns, as soft round puffs from the Soft Fire sheet. The Flamethrower's flames jet level from
    /// the gun along a flat sector of its cone (opening along +Z, local space so it turns with the aim), growing and slowing as they fly, and
    /// each stays inside the cone: a puff draws only inside the circle that fits its quad, and JetWidest keeps that circle inside. The
    /// Rocket's burning ground is the same puffs rising from a flat circle of its real radius. Particles never move sideways but by their
    /// start speed (guarded by the shipped-prefab tests).
    /// </summary>
    public static class FlameLook
    {
        // A point emitter: the flames all leave the muzzle. Not a look number.
        public const float JetRadius = 0.01f;
        private const float MinJetForward = 0.05f;

        /// <summary>The Soft Fire texture is a square sheet of this many cells a side, one puff shape in each (SoftFireTexture draws it).</summary>
        public const int FireSheetTiles = 2;

        /// <summary>The share of a puff's width that is dense enough to read as fire (the rest is its feathered edge): the no-gap rules count
        /// only this much of each flame (TheShippedFireShapeNeverDrawsOutsideItsCircle holds the sheet to it).</summary>
        public const float FireDrawnShare = 0.55f;

        /// <summary>x: the emission arc in degrees (the cone's full angle). y: the circle's turn about its own axis, so the arc, which a
        /// Circle shape starts at its +X and sweeps toward +Y, is centred on +Z once the shape is laid flat by a 90 degree turn on X.</summary>
        public static Vector2 Sector(float coneAngle)
        {
            float arc = Mathf.Clamp(coneAngle, 0f, 360f);
            return new Vector2(arc, 90f - arc * 0.5f);
        }

        public static Color Core(UiTheme theme) => LookColours.FireCore(theme);

        public static Color Edge(UiTheme theme, int abilityId) => LookColours.ForAbility(theme, abilityId, -1, LookFamily.Fire);

        public static Color Tip(UiTheme theme) => LookColours.FireTip(theme);

        /// <summary>Where the jet leaves from, in the cone's local space (feet at the origin, aim along +Z): the gun's muzzle, pulled inside
        /// the cone when it pokes out of it, and never past half the range so the flames always have room to fly.</summary>
        public static Vector3 JetFrom(Vector3 muzzleLocal, float range, float coneAngle)
        {
            float half = Mathf.Clamp(coneAngle * 0.5f, 0f, 89f) * Mathf.Deg2Rad;
            float forward = Mathf.Max(muzzleLocal.z, MinJetForward);
            float side = forward * Mathf.Tan(half) * 0.98f;
            var flat = new Vector2(Mathf.Clamp(muzzleLocal.x, -side, side), forward);
            float most = Mathf.Max(0f, range) * 0.5f;
            if (flat.magnitude > most)
                flat *= most / flat.magnitude;
            return new Vector3(flat.x, muzzleLocal.y, flat.y);
        }

        /// <summary>How far a flame's middle leaving <paramref name="jetFrom"/> flies: <paramref name="reach"/> of the flat room left to the
        /// end of the range; its soft front reaches on past that, by as much as JetWidest leaves room for.</summary>
        public static float JetTravel(Vector3 jetFrom, float range, float reach) =>
            Mathf.Clamp01(reach) * Mathf.Max(0f, range - new Vector2(jetFrom.x, jetFrom.z).magnitude);

        /// <summary>The flames' emission arc in degrees: Flame Jet Spread of the cone's angle. Their paths fan out round the aim and their
        /// growing width, not their paths, fills the cone's sides.</summary>
        public static float JetSpread(float coneAngle, float spreadShare) => Mathf.Clamp(coneAngle, 0f, 360f) * Mathf.Clamp01(spreadShare);

        /// <summary>How far from the caster's feet, measured flat, the farthest flame's middle lands.</summary>
        public static float JetTipReach(Vector3 jetFrom, float range, float reach) =>
            new Vector2(jetFrom.x, jetFrom.z).magnitude + JetTravel(jetFrom, range, reach);

        /// <summary>The most a round flame with its middle at <paramref name="flat"/> (cone space, x across, y along the aim) may reach out
        /// before it leaves the ground the cone burns: to the nearer side edge, or to the end of the range. None outside the cone.</summary>
        public static float JetRoom(Vector2 flat, float range, float coneAngle)
        {
            float half = Mathf.Clamp(coneAngle * 0.5f, 0f, 89f) * Mathf.Deg2Rad;
            float side = flat.y * Mathf.Sin(half) - Mathf.Abs(flat.x) * Mathf.Cos(half);
            return Mathf.Max(0f, Mathf.Min(side, range - flat.magnitude));
        }

        /// <summary>The width the flames grow to where they land: Flame Jet Width of the widest that keeps every flame inside the cone all its
        /// flight. A flame's width grows evenly with its age and its middle flies a straight path while slowing, so it is always past the even
        /// share of its way and its room only grows along it: it is enough that it fits as it leaves the gun (at Flame Jet Start Size of
        /// this) and where it lands, on the spread's outer paths and on the path that flies farthest out (that one up to
        /// <paramref name="overshoot"/> of its way past where an even flight would land it, JetOvershoot).</summary>
        public static float JetWidest(Vector3 jetFrom, float range, float coneAngle, ArenaLook look, float overshoot = 0f)
        {
            float spread = JetSpread(coneAngle, look.flameJetSpread) * 0.5f * Mathf.Deg2Rad;
            float travel = JetTravel(jetFrom, range, look.flameJetReach);
            var gun = new Vector2(jetFrom.x, jetFrom.z);
            var outer = new Vector2(Mathf.Sin(spread), Mathf.Cos(spread)) * travel;
            float room = JetRoom(gun, range, coneAngle) / Mathf.Max(0.01f, look.flameJetStartSize);
            room = Mathf.Min(room, JetRoom(gun + outer, range, coneAngle));
            room = Mathf.Min(room, JetRoom(gun + new Vector2(-outer.x, outer.y), range, coneAngle));
            // Overshooting only carries a flame away from the sides, but toward the end of the range.
            room = Mathf.Min(room, range - gun.magnitude - travel * (1f + Mathf.Max(0f, overshoot)));
            return Mathf.Max(0f, look.flameJetWidth * 2f * room);
        }

        // The longest frame step the flames are moved by (Time.maximumParticleDeltaTime is 0.03 s).
        private const float LongestStep = 1f / 30f;

        /// <summary>How far past its even landing, as a share of its way, a slowing flame lands when it is moved in frame steps: each step
        /// moves it at the speed it had when the step began, so it overshoots by about half a step of the speed it loses.</summary>
        public static float JetOvershoot(float life, float slowdown) =>
            life > 0f ? 0.5f * LongestStep * (1f - Mathf.Clamp(slowdown, 0.05f, 1f)) / (life * JetPace(slowdown)) : 0f;

        /// <summary>A flame's mean speed over its flight as a share of its launch speed, when its speed falls evenly to
        /// <paramref name="slowdown"/> of it by the end.</summary>
        public static float JetPace(float slowdown) => 0.5f * (1f + Mathf.Clamp(slowdown, 0.05f, 1f));

        /// <summary>How fast a flame leaves the gun so that, slowing to <paramref name="slowdown"/> of it, its middle lands
        /// <paramref name="travel"/> away at the end of its <paramref name="life"/>.</summary>
        public static float JetLaunchSpeed(float travel, float life, float slowdown) =>
            life > 0f ? Mathf.Max(0f, travel) / (life * JetPace(slowdown)) : 0f;

        /// <summary>The upward speed (slowed with the rest of a flame's speed, at <paramref name="pace"/>) that lifts it
        /// <paramref name="rise"/> metres over its flight.</summary>
        public static float JetLift(float rise, float life, float pace) =>
            life > 0f && pace > 0f ? Mathf.Max(0f, rise) / (life * pace) : 0f;

        /// <summary>Flames a second for an unbroken jet: the heads leave Speed / Rate apart, so the shortest flame (the smallest, just out
        /// of the gun) must be at least that long, <paramref name="overlap"/> times over; but never more than <paramref name="mostAlive"/>
        /// alive at once over their <paramref name="life"/>, which only binds when a wall pulls the gun back to the cone's tip.</summary>
        public static float JetRate(float speed, float shortestLength, float overlap, float life, float mostAlive)
        {
            if (shortestLength <= 0f)
                return 0f;
            float rate = Mathf.Max(0f, overlap) * Mathf.Max(0f, speed) / shortestLength;
            return life > 0f ? Mathf.Min(rate, Mathf.Max(0f, mostAlive) / life) : rate;
        }

        /// <summary>How many times more flames than one path needs (JetRate's overlap times this), so the jet is unbroken across its
        /// spread too: at flight f the paths span 2 f tan(spread / 2) and the smallest flame is w(f) = w0 + k f wide (k its growth per
        /// metre over <paramref name="travel"/>), so a point sees w / span of them; that share times the one-path need peaks at
        /// tan(spread / 2) / (2 k). Never below one.</summary>
        public static float JetAcross(float spreadDegrees, float travel, float smallestLanding, float startSize)
        {
            float k = Mathf.Max(0f, smallestLanding) * (1f - Mathf.Clamp01(startSize)) / Mathf.Max(0.01f, travel);
            float wide = Mathf.Tan(Mathf.Clamp(spreadDegrees * 0.5f, 0f, 89f) * Mathf.Deg2Rad);
            return k > 0f ? Mathf.Max(1f, wide / (2f * k)) : 1f;
        }

        /// <summary>The emitter's arc speed (whole turns a second, wrapped into the arc) that starts each flame a golden share of the
        /// spread round from the last: the paths cover the spread evenly with no clump or hole, where chance leaves both.</summary>
        public static float JetSweep(float rate, float spreadDegrees) =>
            spreadDegrees > 0f ? Mathf.Max(0f, rate) * GoldenShare * spreadDegrees / 360f : 0f;

        private const float GoldenShare = 0.618034f;

        // Where in a flame's life it turns yellow and then takes on the tip colour: the white-hot heart lasts only its first fifth.
        private const float FireYellow = 0.2f;
        private const float FireTip = 0.6f;

        /// <summary>A flame's colour over its life: the white-hot <paramref name="core"/> at full glow as it is born, then, at
        /// <paramref name="bodyGlow"/> of the glow, a yellow half-way from the pale hot <paramref name="edge"/> to the <paramref name="tip"/>
        /// and the tip's warm yellow-amber over its second half, dimming to <paramref name="tipGlow"/> as it burns out. A particle's colour
        /// only scales the glow down: kept at full glow the yellows bloom and wash out to cream, so only the heart does. It swells in from
        /// nothing over <paramref name="catchShare"/> of its life (0: born whole) and fades out to nothing at the end.</summary>
        public static Gradient FireOverLife(Color core, Color edge, Color tip, float bodyGlow, float tipGlow, float catchShare)
        {
            float born = Mathf.Clamp(catchShare, 0f, 0.6f);
            float body = Mathf.Clamp01(bodyGlow);
            var life = new Gradient();
            life.SetKeys(
                new[]
                {
                    new GradientColorKey(core, 0f), new GradientColorKey(Color.Lerp(edge, tip, 0.5f) * body, FireYellow),
                    new GradientColorKey(tip * body, FireTip), new GradientColorKey(tip * Mathf.Min(body, Mathf.Clamp01(tipGlow)), 1f),
                },
                born > 0f
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, born), new GradientAlphaKey(0.85f, Mathf.Max(born, 0.6f)), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            return life;
        }

        /// <summary>Each spray: the flames on the jet's spread and the embers on the cone's whole sector, the flames as soft fire puffs coloured
        /// by their age, the embers in the fire colours; with the Arena Look's materials. The flames draw with Flame Jet Material (else Fx
        /// Alpha); the embers (every system below) with Fx Additive; a slot left empty keeps the prefab's material. How many flames and how
        /// big is AimJet's, every step.</summary>
        public static void Apply(ParticleSystem flames, float range, float coneAngle, Color core, Color edge, Color tip, ArenaLook look)
        {
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool jet = system == flames;
                Vector2 sector = Sector(jet ? JetSpread(coneAngle, look.flameJetSpread) : coneAngle);
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = JetRadius;
                shape.radiusThickness = 1f;
                shape.arc = sector.x;
                shape.arcMode = jet ? ParticleSystemShapeMultiModeValue.Loop : ParticleSystemShapeMultiModeValue.Random;
                shape.arcSpread = 0f;
                shape.rotation = new Vector3(90f, 0f, sector.y);
                shape.scale = Vector3.one;
                shape.randomDirectionAmount = 0f;
                shape.sphericalDirectionAmount = 0f;
                shape.randomPositionAmount = 0f;
                if (jet)
                {
                    ShapeJet(system, look);
                    SoftFlames(system, FireOverLife(core, edge, tip, look.flameBodyGlow, look.flameTipGlow, 0f), 1f, look);
                }
                else
                {
                    main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
                    SetMaterial(system, look.fxAdditive);
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.rateOverTime = look.flameEmbersPerSecond;
                }
            }
        }

        // JetWidest's promise holds only while a flame's size says how old it is and its middle flies a straight, slowing path: one life for
        // all, size growing evenly with age, nothing but the code's own lift and slowdown changing its speed, and no fall of its own.
        private static void ShapeJet(ParticleSystem flames, ArenaLook look)
        {
            ParticleSystem.MainModule main = flames.main;
            main.startLifetime = main.startLifetime.constantMax;
            main.gravityModifier = 0f;
            ParticleSystem.SizeOverLifetimeModule growth = flames.sizeOverLifetime;
            growth.enabled = true;
            growth.separateAxes = false;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, look.flameJetStartSize, 1f, 1f));
            ParticleSystem.VelocityOverLifetimeModule lift = flames.velocityOverLifetime;
            lift.enabled = true;
            lift.space = ParticleSystemSimulationSpace.Local;
            lift.x = 0f;
            lift.y = 0f;
            lift.z = 0f;
            lift.orbitalX = 0f;
            lift.orbitalY = 0f;
            lift.orbitalZ = 0f;
            lift.radial = 0f;
            // Scales the start speed and the lift alike (the whole velocity), so the path stays straight while it slows.
            lift.speedModifier = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, Mathf.Clamp(look.flameJetSlowdown, 0.05f, 1f)));
        }

        // Soft round puffs: the cone and floor rules count on a puff drawing only inside the circle that fits its quad, whichever way it is
        // turned, so they face the camera unstretched.
        private static void SoftFlames(ParticleSystem system, Gradient overLife, float opacity, ArenaLook look)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(overLife);
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = FireSheetTiles;
            sheet.numTilesY = FireSheetTiles;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.cycleCount = 1;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f); // a share of the sheet, not a cell number
            if (system.TryGetComponent(out ParticleSystemRenderer renderer))
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }
            SetMaterial(system, look.flameJetMaterial != null ? look.flameJetMaterial : look.fxAlpha);
            SetGlow(system, look.flameGlow);
        }

        /// <summary>Every physics step: the jet leaves from the gun (<paramref name="muzzleLocal"/>, in the space the flames and every system
        /// below them share) and each system's flames fly as far as Jet Travel over their life, no farther. The flames
        /// (<paramref name="systems"/>[0], as GetComponentsInChildren gathers them) leave at JetLaunchSpeed, slow as they fly, drift up by
        /// Flame Jet Rise, grow to JetWidest and are thrown as often as JetRate asks. Takes the systems already gathered, so a spray allocates
        /// nothing per step.</summary>
        public static void AimJet(ParticleSystem[] systems, Vector3 muzzleLocal, float range, float coneAngle, ArenaLook look)
        {
            Vector3 from = JetFrom(muzzleLocal, range, coneAngle);
            float travel = Mathf.Max(0f, JetTravel(from, range, look.flameJetReach) - JetRadius);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.ShapeModule shape = systems[i].shape;
                shape.position = from;
                ParticleSystem.MainModule main = systems[i].main;
                float life = Mathf.Max(0.01f, main.startLifetime.constantMax);
                if (i > 0)
                {
                    main.startSpeed = travel / life;
                    continue;
                }
                float speed = JetLaunchSpeed(travel, life, look.flameJetSlowdown);
                main.startSpeed = speed;
                ParticleSystem.VelocityOverLifetimeModule lift = systems[i].velocityOverLifetime;
                lift.y = JetLift(look.flameJetRise, life, JetPace(look.flameJetSlowdown));
                SizeJet(systems[i], from, travel, speed, life, range, coneAngle, look);
            }
        }

        private static void SizeJet(ParticleSystem flames, Vector3 from, float travel, float speed, float life, float range, float coneAngle, ArenaLook look)
        {
            float widest = JetWidest(from, range, coneAngle, look, JetOvershoot(life, look.flameJetSlowdown));
            ParticleSystem.EmissionModule emission = flames.emission;
            ParticleSystem.MainModule main = flames.main;
            ParticleSystem.MinMaxCurve size = main.startSize;
            if (widest <= 0f || size.constantMax <= 0f)
            {
                emission.rateOverTime = 0f;
                return;
            }
            // The prefab's smallest-to-biggest share survives the rescale; a zero size is never written, so it is never lost.
            float share = size.mode == ParticleSystemCurveMode.TwoConstants ? size.constantMin / size.constantMax : 1f;
            main.startSize = new ParticleSystem.MinMaxCurve(widest * share, widest);
            ParticleSystem.ShapeModule paths = flames.shape;
            // Leaving the gun at the launch speed, a flame near it is younger, so smaller, than an even flight would make it there.
            float smallest = widest * share * FireDrawnShare;
            float across = JetAcross(paths.arc, travel / JetPace(look.flameJetSlowdown), smallest, look.flameJetStartSize);
            // The gun is where the flames are smallest and fastest, so the heads are farthest apart for their size there.
            float rate = JetRate(speed, smallest * look.flameJetStartSize, look.flameJetOverlap * across, life, look.flameJetMostFlames);
            emission.rateOverTime = rate;
            paths.arcSpeed = JetSweep(rate, paths.arc);
            int most = Mathf.Max(1, look.flameJetMostFlames) + 1;
            // Grows to what is alive but never past the limit, and is never shrunk below that: resizing the buffer every step would
            // cost more than the room.
            int room = Mathf.Clamp(main.maxParticles, Mathf.Min(Mathf.CeilToInt(rate * life) + 1, most), most);
            if (room != main.maxParticles)
                main.maxParticles = room;
        }

        /// <summary>How wide the biggest of these flames grows: its biggest start size times the peak of its size over life.</summary>
        public static float BiggestFlame(ParticleSystem flames)
        {
            float biggest = flames.main.startSize.constantMax;
            ParticleSystem.SizeOverLifetimeModule growth = flames.sizeOverLifetime;
            if (!growth.enabled)
                return biggest;
            float peak = 0f;
            for (int i = 0; i <= 20; i++)
                peak = Mathf.Max(peak, growth.size.Evaluate(i / 20f, 1f));
            return biggest * peak;
        }

        /// <summary>The Rocket's burning ground: soft fire puffs and embers rising from the whole flat circle of <paramref name="radius"/>, the
        /// puffs' middles kept half the biggest one's width in from its edge so none draws outside it and as high above the floor so none
        /// sinks into it, as many as its area calls for. Each puff catches over Fire Field Flame Catch of its life, is never more solid than
        /// Fire Field Flame Opacity, and burns from the hot heart to the tip colour; nothing is lit at once.</summary>
        public static void ApplyFloor(ParticleSystem flames, float radius, Color core, Color edge, Color tip, ArenaLook look)
        {
            float area = Mathf.PI * radius * radius;
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool puffs = system == flames;
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startSpeed = 0f;
                main.prewarm = false;
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                float half = puffs ? 0.5f * BiggestFlame(system) : 0f;
                shape.radius = Mathf.Max(0f, radius - half);
                shape.radiusThickness = 1f;
                shape.arc = 360f;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
                // A puff faces the camera, so up to half its width hangs below its middle: lifted that much, the floor never cuts it flat.
                shape.position = new Vector3(0f, half, 0f);
                shape.rotation = new Vector3(90f, 0f, 0f);
                shape.scale = Vector3.one;
                shape.randomDirectionAmount = 0f;
                shape.sphericalDirectionAmount = 0f;
                shape.randomPositionAmount = 0f;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                emission.rateOverTime = area * (puffs ? look.fireFieldFlamesPerSquareMetre : look.fireFieldEmbersPerSquareMetre);
                if (puffs)
                {
                    SoftFlames(system, FireOverLife(core, edge, tip, look.flameBodyGlow, look.flameTipGlow, look.fireFieldFlameCatch),
                        look.fireFieldFlameOpacity, look);
                }
                else
                {
                    main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
                    SetMaterial(system, look.fxAdditive);
                }
            }
        }

        /// <summary>When, after the burning ground lights, its flames stop catching so the longest-lived of them and their embers has burnt
        /// out by the end of its <paramref name="duration"/>: the field is destroyed then, and every flame still in the air with it.</summary>
        public static float FloorBurnsOutAt(float duration, ParticleSystem flames)
        {
            float longest = 0f;
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
                longest = Mathf.Max(longest, system.main.startLifetime.constantMax);
            return Mathf.Max(0f, duration - longest);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock glowBlock;

        // A particle's own colour is 8-bit, so only the material side can pass 1 and reach the bloom: Flame Glow rides in the renderer's base colour.
        private static void SetGlow(ParticleSystem system, float glow)
        {
            if (!system.TryGetComponent(out ParticleSystemRenderer renderer))
                return;
            glowBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(glowBlock);
            glowBlock.SetColor(BaseColorId, new Color(glow, glow, glow, 1f));
            renderer.SetPropertyBlock(glowBlock);
        }

        private static void SetMaterial(ParticleSystem system, Material material)
        {
            if (material != null && system.TryGetComponent(out ParticleSystemRenderer renderer))
                renderer.sharedMaterial = material;
        }
    }
}
