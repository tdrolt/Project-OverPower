using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// Electric-blue arcs crackling round one player while their STUNNED label shows, on every client's copy (built beside the label by
    /// PlayerStatusEffects). The arcs live on a child that this view switches on and off; the view's own object stays active so its
    /// LateUpdate keeps running, and the fog gate on it (VisibleWhenSeen, following the player) only ever touches Renderer.enabled.
    /// The gate collects renderers once, so every arc is built before it attaches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StunArcsView : MonoBehaviour
    {
        public const string ArcsChildName = "Arcs";
        public const string CoreName = "Core";
        private const int PointsPerArc = 7; // enough for a zig-zag; not a look number

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private PlayerStatusEffects source;
        private PhotonView caster;
        private ArenaLook look;
        private GameObject arcs;
        private LineRenderer[] lines;
        private LineRenderer[] cores; // null entries when the Arena Look's core width is 0
        private VisibleWhenSeen gate;
        private int shapedSeed;
        private bool shaped;

        /// <summary>Builds the hidden arcs under <paramref name="parent"/>, centred <paramref name="bodyCentreY"/> above its feet, or returns
        /// null (with a warning) when the theme has no Arena Look with FX Alpha.</summary>
        public static StunArcsView TryCreate(Transform parent, PlayerStatusEffects source, PhotonView caster, UiTheme theme, float bodyCentreY)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (look == null || look.fxAlpha == null)
            {
                Debug.LogWarning($"[StunArcsView] {parent.name}: no UiTheme Arena Look with FX Alpha - this player shows no stun arcs.");
                return null;
            }

            var root = new GameObject("Stun Arcs");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, bodyCentreY, 0f);
            var arcs = new GameObject(ArcsChildName);
            arcs.transform.SetParent(root.transform, false);
            arcs.SetActive(false);

            Color colour = LookColours.For(LookFamily.ElectricBlue, -1, theme);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, new Color(look.stunArcGlow, look.stunArcGlow, look.stunArcGlow, 1f));
            var coreBlock = new MaterialPropertyBlock();
            coreBlock.SetColor(BaseColorId, TowerLookRules.GlowKeepingHue(colour, look.stunArcCoreGlow)); // white vertex colours, so this is its whole colour
            var lines = new LineRenderer[look.stunArcCount];
            var cores = new LineRenderer[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var arc = new GameObject("Arc " + i);
                arc.transform.SetParent(arcs.transform, false);
                LineRenderer line = arc.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = look.fxAlpha; // blended, not added: added light turns the blue pale cyan over the grey floor
                line.positionCount = PointsPerArc;
                line.widthMultiplier = look.stunArcWidth;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                VisualTint.SetLineColor(line, colour);
                line.SetPropertyBlock(block); // the glow rides on the shared material's colour multiplier, per renderer
                lines[i] = line;

                if (look.stunArcCoreWidth <= 0f)
                    continue;
                var coreObject = new GameObject(CoreName);
                coreObject.transform.SetParent(arc.transform, false);
                LineRenderer core = coreObject.AddComponent<LineRenderer>();
                core.useWorldSpace = false;
                core.sharedMaterial = look.fxAlpha;
                core.positionCount = PointsPerArc;
                core.widthMultiplier = look.stunArcWidth * look.stunArcCoreWidth;
                core.alignment = LineAlignment.View;
                core.shadowCastingMode = ShadowCastingMode.Off;
                core.receiveShadows = false;
                core.sortingOrder = line.sortingOrder + 1; // both sit at the same depth, so the order alone keeps the core on top
                VisualTint.SetLineColor(core, Color.white);
                core.SetPropertyBlock(coreBlock);
                cores[i] = core;
            }

            StunArcsView view = root.AddComponent<StunArcsView>();
            view.source = source;
            view.caster = caster;
            view.look = look;
            view.arcs = arcs;
            view.lines = lines;
            view.cores = cores;
            view.gate = VisibleWhenSeen.AttachToCaster(root, caster);
            return view;
        }

        private void LateUpdate()
        {
            if (source == null)
                return;

            source.TryGetStatusLabel(out StatusLabel label, out _, out _);
            if (StunArcsRule.Show(label))
            {
                // Null when this player was built before the local sight existed (a late join); asked again so the arcs never show ungated.
                if (gate == null)
                    gate = VisibleWhenSeen.AttachToCaster(gameObject, caster);
                if (!arcs.activeSelf)
                    arcs.SetActive(true);
                Reshape();
            }
            else if (arcs.activeSelf)
            {
                arcs.SetActive(false);
                shaped = false;
            }
        }

        private void Reshape()
        {
            int actor = caster != null ? caster.OwnerActorNr : 0;
            int seed = StunArcsRule.Seed(PhotonNetwork.ServerTimestamp, actor, look.stunArcRate);
            if (shaped && seed == shapedSeed)
                return;
            shaped = true;
            shapedSeed = seed;
            for (int i = 0; i < lines.Length; i++)
                for (int k = 0; k < PointsPerArc; k++)
                {
                    Vector3 p = StunArcsRule.ArcPoint(seed, i, k, PointsPerArc, look.stunArcRadius, look.stunArcHeight, look.stunArcJitter, look.stunArcSweepDegrees.x, look.stunArcSweepDegrees.y);
                    lines[i].SetPosition(k, p);
                    if (cores[i] != null)
                        cores[i].SetPosition(k, p);
                }
        }
    }
}
