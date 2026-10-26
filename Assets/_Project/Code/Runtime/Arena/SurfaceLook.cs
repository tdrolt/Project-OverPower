using System;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The paint and shine of one kind of arena surface (floor, walls, blocks or barriers).</summary>
    [Serializable]
    public struct SurfaceLook
    {
        [Tooltip("The colour of the surface itself, under its lines and stripes. Keep the floor and walls dark so players, shots and team glows stand out against them.")]
        public Color baseTint;
        [Tooltip("How glossy the surface is: 0 is matte, 1 is a mirror-like shine that shows the light and the sky as bright streaks.")]
        [Range(0f, 1f)] public float gloss;
        [Tooltip("How bright the sun's shine spot is on this surface: 1 is the full mirror-bright spot, 0 shows none. On the floor the spot is a soft bright blob that, for the team whose camera faces the sun, sits right where the player stands, so keep it at 0 there; on small hard shapes like the barriers it is what shows they are glossy.")]
        [Range(0f, 1f)] public float sunShine;

        public SurfaceLook(Color baseTint, float gloss, float sunShine)
        {
            this.baseTint = baseTint;
            this.gloss = gloss;
            this.sunShine = sunShine;
        }
    }
}
