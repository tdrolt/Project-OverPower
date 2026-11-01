using Overpower.Abilities;
using Overpower.Data;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Match
{
    /// <summary>
    /// Rings rising round a player who just took an ultimate charge pack: each starts at the feet a little after the one before, widens as it
    /// climbs and fades out at the top (HealthPackLook's ring rules, HealthPackConfig's numbers). Made on every client from the pack's room
    /// value, so no message is added; it follows the player and is hidden while the fog hides them. Removes itself when the last ring is done.
    /// </summary>
    public sealed class PickupRingsView : MonoBehaviour
    {
        private const int Segments = 48;
        private static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f); // FillFlatCircle's local XY becomes the floor

        private HealthPackConfig config;
        private Color colour;
        private float startedAt;
        private LineRenderer[] rings;

        public static PickupRingsView Play(PhotonView player, HealthPackConfig config, Color colour, Material material)
        {
            if (player == null || config == null || material == null)
                return null;
            var root = new GameObject("Ultimate Pack Rings");
            root.transform.SetParent(player.transform, false);
            PickupRingsView view = root.AddComponent<PickupRingsView>();
            view.config = config;
            view.colour = colour;
            view.startedAt = Time.time;
            view.rings = new LineRenderer[config.PickupRingCount];
            for (int i = 0; i < view.rings.Length; i++)
            {
                var ring = new GameObject("Ring " + i);
                ring.transform.SetParent(root.transform, false);
                ring.transform.localRotation = Flat;
                LineRenderer line = ring.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.alignment = LineAlignment.TransformZ;
                line.widthMultiplier = config.PickupRingWidthMetres;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                VisualTint.FillFlatCircle(line, 1f, Segments);
                view.rings[i] = line;
            }
            VisibleWhenSeen.AttachToCaster(root, player);
            view.LateUpdate();
            return view;
        }

        private void LateUpdate()
        {
            float elapsed = Time.time - startedAt;
            bool done = true;
            for (int i = 0; i < rings.Length; i++)
            {
                float progress = HealthPackLook.PickupRingProgress(elapsed, i, config.PickupRingDelaySeconds, config.PickupRingSeconds);
                if (progress < 1f)
                    done = false;
                float p = Mathf.Clamp01(progress);
                float radius = Mathf.Lerp(config.PickupRingStartRadius, config.PickupRingEndRadius, p);
                Transform ring = rings[i].transform;
                ring.localPosition = new Vector3(0f, HealthPackLook.PickupRingHeight(p, config.PickupRingRiseMetres), 0f);
                ring.localScale = new Vector3(radius, radius, 1f);
                // A ring not yet started is see-through rather than switched off: the fog gate owns the renderers' on/off.
                VisualTint.SetLineColor(rings[i], VisualTint.WithAlpha(colour, progress < 0f ? 0f : HealthPackLook.PickupRingAlpha(p)));
            }
            if (done)
                Destroy(gameObject);
        }
    }
}
