using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Sizes the Flamethrower's upright flames and embers to the cone it really burns. Every particle system under the tongues is spawned on
    /// a flat circle sector (laid on the floor, opening along +Z) of the cone's range and angle, and never moves sideways: no start speed,
    /// so only the prefab's upward drift moves it. That keeps the fire inside ConeFilter's shape however the prefab's particles are tuned.
    /// </summary>
    public static class FlameLook
    {
        /// <summary>x: the emission arc in degrees (the cone's full angle). y: the circle's turn about its own axis, so the arc, which a
        /// Circle shape starts at its +X and sweeps toward +Y, is centred on +Z once the shape is laid flat by a 90 degree turn on X.</summary>
        public static Vector2 Sector(float coneAngle)
        {
            float arc = Mathf.Clamp(coneAngle, 0f, 360f);
            return new Vector2(arc, 90f - arc * 0.5f);
        }

        public static Color Core(UiTheme theme) => LookColours.FireCore(theme);

        public static Color Edge(UiTheme theme, int abilityId) => LookColours.ForAbility(theme, abilityId, -1, LookFamily.Fire);

        /// <summary><paramref name="tongues"/> is the flames; every particle system below it (the embers) gets the same sector. The flames
        /// draw with the Arena Look's Fx Alpha, the embers with its Fx Additive (a slot left empty keeps the prefab's material).</summary>
        public static void Apply(ParticleSystem tongues, float range, float coneAngle, Color core, Color edge, ArenaLook look)
        {
            Vector2 sector = Sector(coneAngle);
            foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.startSpeed = 0f;
                main.startColor = new ParticleSystem.MinMaxGradient(core, edge);
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = range;
                shape.radiusThickness = 1f - look.flameTongueStart;
                shape.arc = sector.x;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
                shape.position = Vector3.zero;
                shape.rotation = new Vector3(90f, 0f, sector.y);
                shape.scale = Vector3.one;
                shape.randomDirectionAmount = 0f;
                shape.randomPositionAmount = 0f;
                Material material = system == tongues ? look.fxAlpha : look.fxAdditive;
                if (material != null && system.TryGetComponent(out ParticleSystemRenderer renderer))
                    renderer.sharedMaterial = material;
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = system == tongues ? look.flameTonguesPerMetre * range : look.flameEmbersPerSecond;
            }
        }
    }
}
