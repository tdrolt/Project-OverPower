using Overpower.Abilities;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The red wave on the ground (Tudor 2026-10-01: "everyone sees the red wave roll out from the centre"): a flat ring
    /// that grows from the centre tower at the wave speed, the same size on every screen at the same moment because
    /// CentreScan works it out from the server clock. A LineRenderer lying flat just above the floor, in a material from the
    /// transparent queue: the fog pass runs before the transparents, so the ring is drawn over the fog and stays bright
    /// in it. Hidden when no scan is travelling. Everyone gets it, not only the holding team.
    /// </summary>
    public sealed class CentreScanWaveView : MonoBehaviour
    {
        private const int Segments = 96;
        /// <summary>Lifts the ring off the floor so it does not flicker against it; a look, not a gameplay value.</summary>
        private const float LiftMetres = 0.12f;

        private LineRenderer line;
        private Material material;

        private void Awake()
        {
            var go = new GameObject("Centre Scan Wave (cosmetic only)");
            // At the scene root, not under this object: the circle is laid out in the line's local space, which a scaled parent would distort.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            line = go.AddComponent<LineRenderer>();
            // Sprites/Default is a transparent, unlit shader that reads the line's own colour: it draws after the fog pass.
            material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            line.sharedMaterial = material;
            line.alignment = LineAlignment.TransformZ;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 0;
            line.enabled = false;
        }

        private void OnDestroy()
        {
            if (line != null)
                Destroy(line.gameObject);
            if (material != null)
                Destroy(material);
        }

        private void LateUpdate()
        {
            CentreScan scan = CentreScan.Instance;
            if (scan == null || scan.Config == null || !scan.WaveVisible)
            {
                if (line.enabled)
                    line.enabled = false;
                return;
            }

            Vector3 centre = scan.CentrePosition;
            line.transform.position = new Vector3(centre.x, centre.y + LiftMetres, centre.z);
            VisualTint.FillFlatCircle(line, Mathf.Max(0.01f, scan.Frame.Radius), Segments);
            line.widthMultiplier = scan.Config.ScanWaveWidth;
            VisualTint.SetLineColor(line, scan.Config.ScanWaveColour);
            if (!line.enabled)
                line.enabled = true;
        }
    }
}
