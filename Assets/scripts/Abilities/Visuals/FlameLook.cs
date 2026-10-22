using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Lays fire out over the ground it really burns. The Flamethrower's flames jet from the gun along a flat sector of its cone (opening
    /// along +Z, local space so it turns with the aim), each a wedge stretched back along its flight from its head, and their heads fly
    /// no farther than the room left to the end of its reach: the cone is convex and both the gun and every head lie inside it, and
    /// JetWidth keeps each head's corners inside too. The Rocket's burning ground is upright flames rising from a flat circle of its real
    /// radius. Particles never move sideways but by their start speed; only the prefabs' vertical drift or fall adds to it (guarded by
    /// the shipped-prefab tests).
    /// </summary>
    public static class FlameLook
    {
        // A point emitter: the flames all leave the muzzle. Not a look number.
        public const float JetRadius = 0.01f;
        private const float MinJetForward = 0.05f;

        /// <summary>x: the emission arc in degrees (the cone's full angle). y: the circle's turn about its own axis, so the arc, which a
        /// Circle shape starts at its +X and sweeps toward +Y, is centred on +Z once the shape is laid flat by a 90 degree turn on X.</summary>
        public static Vector2 Sector(float coneAngle)
        {
            float arc = Mathf.Clamp(coneAngle, 0f, 360f);
            return new Vector2(arc, 90f - arc * 0.5f);
        }

        public static Color Core(UiTheme theme) => LookColours.FireCore(theme);

        public static Color Edge(UiTheme theme, int abilityId) => LookColours.ForAbility(theme, abilityId, -1, LookFamily.Fire);

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

        /// <summary>How far a flame leaving <paramref name="jetFrom"/> may fly: <paramref name="reach"/> of the flat room left to the end of the
        /// range, so it never burns past the ground the cone covers.</summary>
        public static float JetTravel(Vector3 jetFrom, float range, float reach) =>
            Mathf.Clamp01(reach) * Mathf.Max(0f, range - new Vector2(jetFrom.x, jetFrom.z).magnitude);

        /// <summary>The flames' emission arc in degrees: Flame Jet Spread of the cone's angle. Their paths fan out round the aim and their
        /// growing width, not their paths, fills the cone's sides.</summary>
        public static float JetSpread(float coneAngle, float spreadShare) => Mathf.Clamp(coneAngle, 0f, 360f) * Mathf.Clamp01(spreadShare);

        /// <summary>How far from the caster's feet, measured flat, the flames' heads land.</summary>
        public static float JetTipReach(Vector3 jetFrom, float range, float reach) =>
            new Vector2(jetFrom.x, jetFrom.z).magnitude + JetTravel(jetFrom, range, reach);

        /// <summary>The widest a flame may be with its head <paramref name="headDistance"/> from the caster. A flame is a wedge, full width at
        /// its head (its particle) and narrowing to a point on its own path back toward the gun (the Flame Jet Material's shape never draws
        /// outside it), so on the jet's outermost path (half the spread off the aim) its head corners come nearest the cone's edge:
        /// W / 2 = d tan((angle - spread) / 2), however long it is stretched. In step with the distance, so a flame whose size grows evenly
        /// with its flight stays inside all the way.</summary>
        public static float JetWidth(float headDistance, float coneAngle, float spread)
        {
            float room = Mathf.Tan(Mathf.Clamp((coneAngle - spread) * 0.5f, 0f, 89f) * Mathf.Deg2Rad);
            return 2f * Mathf.Max(0f, headDistance) * room;
        }

        /// <summary>How far behind the gun a flame on the jet's outermost path may trail before it leaves the cone: a flame just out of the
        /// gun reaches back past it, and the gun stands close to the cone's narrow tip.</summary>
        public static float JetTailRoom(Vector3 jetFrom, float coneAngle, float spread)
        {
            float half = Mathf.Clamp(coneAngle * 0.5f, 0f, 89f) * Mathf.Deg2Rad;
            float path = Mathf.Clamp(spread * 0.5f, 0f, 89f) * Mathf.Deg2Rad;
            float room = (jetFrom.z * Mathf.Sin(half) - Mathf.Abs(jetFrom.x) * Mathf.Cos(half)) / Mathf.Sin(half + path);
            return Mathf.Max(0f, room);
        }

        /// <summary>The width the flames grow to where they land: Flame Jet Width of the widest that keeps every flame inside the cone all
        /// its flight. A flame's width and its head's distance both grow evenly, so it is enough that the flame is inside as it leaves the
        /// gun (Flame Jet Start Size of this, trailing Flame Stretch times that behind the gun) and where it lands (JetWidth there, its
        /// tail no farther behind the gun than JetTailRoom).</summary>
        public static float JetWidest(Vector3 jetFrom, float range, float coneAngle, ArenaLook look)
        {
            float spread = JetSpread(coneAngle, look.flameJetSpread);
            float gun = new Vector2(jetFrom.x, jetFrom.z).magnitude;
            float travel = JetTravel(jetFrom, range, look.flameJetReach);
            float start = Mathf.Max(0.01f, look.flameJetStartSize);
            float stretch = Mathf.Max(0.01f, look.flameStretch);
            float tail = JetTailRoom(jetFrom, coneAngle, spread);
            float widest = Mathf.Min(JetWidth(JetTipReach(jetFrom, range, look.flameJetReach), coneAngle, spread), JetWidth(gun, coneAngle, spread) / start);
            widest = Mathf.Min(widest, Mathf.Min(tail / (start * stretch), (travel + tail) / stretch));
            return Mathf.Max(0f, look.flameJetWidth * widest);
        }

        /// <summary>How many degrees below level the flames' flight sinks so their heads land at <paramref name="landHeight"/> after flying
        /// <paramref name="travel"/> over the floor in <paramref name="life"/> seconds, counting their own <paramref name="fall"/> (metres a
        /// second squared; below zero they rise as they fly, so a jet sent steeply down levels out along the floor). A result below zero
        /// sends them up. From the camera a flame high in the air sits up-screen of the ground it burns, so the jet is brought down to the
        /// floor where it lands.</summary>
        public static float JetDip(float muzzleHeight, float landHeight, float travel, float life, float fall)
        {
            if (travel <= 0f)
                return 0f;
            float drop = muzzleHeight - landHeight - 0.5f * fall * life * life;
            return Mathf.Clamp(Mathf.Atan2(drop, travel) * Mathf.Rad2Deg, -60f, 60f);
        }

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

        /// <summary>Each spray: the flames on the jet's spread and the embers on the cone's whole sector, in the fire colours, with the Arena
        /// Look's materials. The flames draw with Flame Jet Material (else Fx Alpha), stretched; the embers (every system below) with Fx
        /// Additive; a slot left empty keeps the prefab's material. How many flames and how big is AimJet's, every step.</summary>
        public static void Apply(ParticleSystem flames, float range, float coneAngle, Color core, Color edge, ArenaLook look)
        {
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool jet = system == flames;
                Vector2 sector = Sector(jet ? JetSpread(coneAngle, look.flameJetSpread) : coneAngle);
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
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
                    StretchJet(system, look);
                    SetMaterial(system, look.flameJetMaterial != null ? look.flameJetMaterial : look.fxAlpha);
                    SetGlow(system, look.flameGlow);
                }
                else
                {
                    SetMaterial(system, look.fxAdditive);
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.rateOverTime = look.flameEmbersPerSecond;
                }
            }
        }

        // JetWidest's promise holds only while a flame's size says how far it has flown: one life for all, size growing evenly with age
        // (the flat speed is constant), a length set by the width alone, never by the speed, lined up with its flight (Stretch adds any
        // start rotation on top), and nothing but the sink below and the prefab's fall moving it off its flat path.
        private static void StretchJet(ParticleSystem flames, ArenaLook look)
        {
            ParticleSystem.MainModule main = flames.main;
            main.startLifetime = main.startLifetime.constantMax;
            main.startRotation = 0f;
            ParticleSystem.SizeOverLifetimeModule growth = flames.sizeOverLifetime;
            growth.enabled = true;
            growth.separateAxes = false;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, look.flameJetStartSize, 1f, 1f));
            ParticleSystem.VelocityOverLifetimeModule sink = flames.velocityOverLifetime;
            sink.enabled = true;
            sink.space = ParticleSystemSimulationSpace.Local;
            sink.x = 0f;
            sink.y = 0f;
            sink.z = 0f;
            sink.orbitalX = 0f;
            sink.orbitalY = 0f;
            sink.orbitalZ = 0f;
            sink.radial = 0f;
            sink.speedModifier = 1f;
            if (!flames.TryGetComponent(out ParticleSystemRenderer renderer))
                return;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = look.flameStretch;
            renderer.velocityScale = 0f;
            renderer.cameraVelocityScale = 0f;
        }

        /// <summary>Every physics step: the jet leaves from the gun (<paramref name="muzzleLocal"/>, in the space the flames and every system
        /// below them share) and each system's flames are as fast as its longest-lived flame needs to fly Jet Travel, no faster. The flames
        /// (<paramref name="systems"/>[0], as GetComponentsInChildren gathers them) sink by JetDip as they fly, grow to JetWidest and are
        /// thrown as often as JetRate asks. Takes the systems already gathered, so a spray allocates nothing per step.</summary>
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
                float speed = travel / life;
                main.startSpeed = speed;
                if (i > 0)
                    continue;
                // A sink, not a tilted emitter: tilting the circle would turn the side paths outward over the floor.
                float dip = JetDip(from.y, look.flameJetLandHeight, travel, life, -Physics.gravity.y * main.gravityModifier.constant);
                float sinking = speed * Mathf.Tan(dip * Mathf.Deg2Rad);
                ParticleSystem.VelocityOverLifetimeModule sink = systems[i].velocityOverLifetime;
                sink.y = -sinking;
                SizeJet(systems[i], from, travel, Mathf.Sqrt(speed * speed + sinking * sinking), life, range, coneAngle, look);
            }
        }

        private static void SizeJet(ParticleSystem flames, Vector3 from, float travel, float speed, float life, float range, float coneAngle, ArenaLook look)
        {
            float widest = JetWidest(from, range, coneAngle, look);
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
            float across = JetAcross(paths.arc, travel, widest * share, look.flameJetStartSize);
            float rate = JetRate(speed, widest * share * look.flameJetStartSize * look.flameStretch, look.flameJetOverlap * across, life, look.flameJetMostFlames);
            emission.rateOverTime = rate;
            paths.arcSpeed = JetSweep(rate, paths.arc);
            int most = Mathf.Max(1, look.flameJetMostFlames) + 1;
            // Grows to what is alive but never past the limit, and is never shrunk below that: resizing the buffer every step would
            // cost more than the room.
            int room = Mathf.Clamp(main.maxParticles, Mathf.Min(Mathf.CeilToInt(rate * life) + 1, most), most);
            if (room != main.maxParticles)
                main.maxParticles = room;
        }

        /// <summary>The Rocket's burning ground: the flames and embers rise from the whole flat circle of <paramref name="radius"/>, as many as
        /// its area calls for, in the fire colours.</summary>
        public static void ApplyFloor(ParticleSystem flames, float radius, Color core, Color edge, ArenaLook look)
        {
            float area = Mathf.PI * radius * radius;
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startSpeed = 0f;
                main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = Mathf.Max(0f, radius);
                shape.radiusThickness = 1f;
                shape.arc = 360f;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
                shape.position = Vector3.zero;
                shape.rotation = new Vector3(90f, 0f, 0f);
                shape.scale = Vector3.one;
                shape.randomDirectionAmount = 0f;
                shape.sphericalDirectionAmount = 0f;
                shape.randomPositionAmount = 0f;
                SetMaterial(system, system == flames ? look.fxAlpha : look.fxAdditive);
                if (system == flames)
                    SetGlow(system, look.flameGlow);
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = area * (system == flames ? look.fireFieldFlamesPerSquareMetre : look.fireFieldEmbersPerSquareMetre);
            }
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
