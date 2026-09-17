using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The picture under the minimap and the patch of the world it shows.
    ///
    /// HOW IT STAYS UP TO DATE: pressing Rebuild thirds on Enviorment/Arena re-bakes it automatically. After any
    /// other change you can see from above (terrain paint, lighting, props outside Source), use
    /// OverPower > Arena > Bake minimap image. The bake writes the image and the three "written by the bake" values;
    /// don't type those by hand, or the map's bubbles won't line up with the picture.
    ///
    /// Fields are [SerializeField] private with read-only properties, like the rest of the Data folder.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Minimap Config", fileName = "MinimapConfig")]
    public sealed class MinimapConfig : ScriptableObject
    {
        [Header("Written by the bake - don't edit")]
        [Tooltip("Top-down picture of the arena drawn under the minimap. Rebuild thirds re-bakes it; for any other change " +
                 "you can see from above, use OverPower > Arena > Bake minimap image.")]
        [SerializeField] private Texture2D arenaImage;

        [Tooltip("World X and Z at the centre of the picture: the arena's symmetry centre. Written by the bake.")]
        [SerializeField] private Vector2 worldCentre = new Vector2(65.05f, 53.34f);

        [Tooltip("How many metres the picture's side covers. Written by the bake.")]
        [SerializeField, Min(1f)] private float worldSizeMetres = 154f;

        [Header("Bake settings")]
        [Tooltip("Pixels along each side of the baked picture. 1024 stays sharp on the large map; more costs memory " +
                 "for no visible gain at minimap sizes. Press Bake minimap image after changing it.")]
        [SerializeField, Range(256, 2048)] private int imagePixels = 1024;

        [Tooltip("Extra metres of ground shown beyond the arena's farthest wall, on every side. Press Bake minimap image " +
                 "after changing it.")]
        [SerializeField, Min(0f)] private float marginMetres = 4f;

        public Texture2D ArenaImage => arenaImage;
        public Vector2 WorldCentre => worldCentre;
        public float WorldSizeMetres => worldSizeMetres;
        public int ImagePixels => imagePixels;
        public float MarginMetres => marginMetres;
    }
}
