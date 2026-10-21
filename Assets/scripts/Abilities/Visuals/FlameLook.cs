using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Lays fire out over the ground it really burns. The Flamethrower's flames jet from the gun along a flat sector of its cone (opening
    /// along +Z, local space so it turns with the aim) and fly no farther than the room left to the end of its reach, so every flame stays
    /// inside ConeFilter's shape: the cone is convex and both the gun and every flame's end point lie inside it. The Rocket's burning ground
    /// is the same flames rising from a flat circle of its real radius. Particles never move sideways but by their start speed; only the
    /// prefabs' vertical drift or fall adds to it (guarded by the shipped-prefab tests).
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

        /// <summary>Each spray: the flames and embers on the cone's sector, in the fire colours, with the Arena Look's materials and rates. The
        /// flames draw with Fx Alpha, the embers (every system below) with Fx Additive; a slot left empty keeps the prefab's material.</summary>
        public static void Apply(ParticleSystem flames, float range, float coneAngle, Color core, Color edge, ArenaLook look)
        {
            Vector2 sector = Sector(coneAngle);
            foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = JetRadius;
                shape.radiusThickness = 1f;
                shape.arc = sector.x;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
                shape.rotation = new Vector3(90f, 0f, sector.y);
                shape.scale = Vector3.one;
                shape.randomDirectionAmount = 0f;
                shape.sphericalDirectionAmount = 0f;
                shape.randomPositionAmount = 0f;
                SetMaterial(system, system == flames ? look.fxAlpha : look.fxAdditive);
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = system == flames ? look.flameTonguesPerMetre * range : look.flameEmbersPerSecond;
            }
        }

        /// <summary>Every physics step: the jet leaves from the gun (<paramref name="muzzleLocal"/>, in the space the flames and every system
        /// below them share) and each system's flames are as fast as its longest-lived flame needs to fly Jet Travel, no faster. Takes the
        /// systems already gathered, so a spray allocates nothing per step.</summary>
        public static void AimJet(ParticleSystem[] systems, Vector3 muzzleLocal, float range, float coneAngle, float reach)
        {
            Vector3 from = JetFrom(muzzleLocal, range, coneAngle);
            float travel = Mathf.Max(0f, JetTravel(from, range, reach) - JetRadius);
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.ShapeModule shape = system.shape;
                shape.position = from;
                ParticleSystem.MainModule main = system.main;
                main.startSpeed = travel / Mathf.Max(0.01f, main.startLifetime.constantMax);
            }
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
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = area * (system == flames ? look.fireFieldFlamesPerSquareMetre : look.fireFieldEmbersPerSquareMetre);
            }
        }

        private static void SetMaterial(ParticleSystem system, Material material)
        {
            if (material != null && system.TryGetComponent(out ParticleSystemRenderer renderer))
                renderer.sharedMaterial = material;
        }
    }
}
