using Overpower.Abilities;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Arena
{
    /// <summary>
    /// Green plus signs rising round one player while their health goes up, from any source, on every client's copy (built by
    /// PlayerHealth as the body starts). Read only from the health and alive state every client already has, so there is no network
    /// message of its own. The signs live on a child this view switches by HealRule; the view's own object stays active so LateUpdate
    /// keeps running, and the fog gate on it (VisibleWhenSeen, following the player) only touches Renderer.enabled, so the signs fade by
    /// colour and size, never by switching a renderer. The gate collects renderers once: every sign is built before it attaches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealSparksView : MonoBehaviour
    {
        public const string RootName = "Heal Sparks";
        public const string SparksChildName = "Sparks";
        public const string MeshName = "Heal Plus";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Transform body;
        private PhotonView player;
        private PlayerHealth health;
        private PlayerNetSync netSync;
        private Rigidbody rigidbodyOfBody;
        private UiTheme theme;
        private ArenaLook look;
        private float feetBelowRoot;
        private GameObject sparks;
        private Transform[] signs;
        private MeshRenderer[] renderers;
        private MaterialPropertyBlock block;
        private VisibleWhenSeen gate;
        private Mesh mesh;
        private Color green;

        private bool sampled;
        private float lastHealth;
        private bool lastAlive;
        private Vector3 lastBody;
        private float sinceRise = float.PositiveInfinity;

        public bool SparksShown => sparks != null && sparks.activeSelf;
        public Mesh PlusMesh => mesh;

        /// <summary>Builds the hidden signs under <paramref name="body"/>, whose feet are <paramref name="feetBelowRoot"/> below its position,
        /// or returns null (with a warning) when the theme has no Arena Look with FX Alpha.</summary>
        public static HealSparksView TryCreate(Transform body, PhotonView player, PlayerHealth health, UiTheme theme, float feetBelowRoot)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (body == null || look == null || look.fxAlpha == null)
            {
                Debug.LogWarning($"[HealSparksView] {(body != null ? body.name : "?")}: no UiTheme Arena Look with FX Alpha - this player shows no heal effect.");
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(body, false);
            var sparks = new GameObject(SparksChildName);
            sparks.transform.SetParent(root.transform, false);
            sparks.SetActive(false);

            Mesh mesh = CreatePlusMesh();
            int count = Mathf.Max(1, look.healSparkCount);
            var signs = new Transform[count];
            var renderers = new MeshRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var sign = new GameObject("Plus " + i);
                sign.transform.SetParent(sparks.transform, false);
                sign.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = sign.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = look.fxAlpha; // blended, not added: added light turns the green pale over a bright floor
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                signs[i] = sign.transform;
                renderers[i] = renderer;
            }

            HealSparksView view = root.AddComponent<HealSparksView>();
            view.body = body;
            view.player = player;
            view.health = health;
            view.netSync = body.GetComponent<PlayerNetSync>();
            view.rigidbodyOfBody = body.GetComponent<Rigidbody>();
            view.theme = theme;
            view.look = look;
            view.feetBelowRoot = feetBelowRoot;
            view.sparks = sparks;
            view.signs = signs;
            view.renderers = renderers;
            view.block = new MaterialPropertyBlock();
            view.mesh = mesh;
            view.gate = VisibleWhenSeen.AttachToCaster(root, player);
            return view;
        }

        private void LateUpdate()
        {
            if (health == null)
                return;
            Step(health.Health, health.IsAlive, BodySample(), Time.deltaTime, Time.time);
        }

        // Another player's update carries its position and its health together, so the jump that marks a respawn is measured on that
        // position, not on the smoothed body that reaches the spot a physics step later.
        private Vector3 BodySample()
        {
            if (netSync != null && player != null && !player.IsMine && netSync.HasReceivedFromOwner)
                return netSync.NetworkPosition;
            return rigidbodyOfBody != null ? rigidbodyOfBody.position : body.position;
        }

        /// <summary>One frame: the player's health and alive state now, where the body is (for the respawn jump), the frame's length and the clock the signs rise on.</summary>
        public void Step(float currentHealth, bool alive, Vector3 bodyPosition, float deltaTime, float time)
        {
            sinceRise += deltaTime;
            if (sampled)
            {
                if (HealRule.IsHealRise(lastHealth, currentHealth, lastAlive, alive, Vector3.Distance(lastBody, bodyPosition), look.healSpawnJumpMetres))
                    sinceRise = 0f;
            }
            sampled = true;
            lastHealth = currentHealth;
            lastAlive = alive;
            lastBody = bodyPosition;

            if (HealRule.IsHealing(alive, sinceRise, look.healSparkTailSeconds))
            {
                // Null when this player was built before the local sight existed (a late join); asked again so the signs never show ungated.
                if (gate == null)
                    gate = VisibleWhenSeen.AttachToCaster(gameObject, player);
                if (!sparks.activeSelf)
                {
                    Dress();
                    sparks.SetActive(true);
                }
                Animate(time);
            }
            else if (sparks.activeSelf)
                sparks.SetActive(false);
        }

        // Read when the signs switch on, so a retuned green or glow shows on the next heal without work every frame.
        private void Dress() => green = LookColours.For(LookFamily.Heal, -1, theme) * look.healSparkGlow;

        private void Animate(float time)
        {
            Vector3 feet = body.position + Vector3.down * feetBelowRoot;
            Camera viewer = Camera.main;
            for (int i = 0; i < signs.Length; i++)
            {
                HealRule.PlaceSpark(i, signs.Length, time, look.healSparkRiseSeconds, look.healSparkRadius, look.healSparkStartHeight,
                                    look.healSparkRiseMetres, out Vector3 local, out float strength);
                Transform sign = signs[i];
                sign.position = feet + local;
                if (viewer != null)
                    sign.rotation = viewer.transform.rotation; // flat to the camera, so it always reads as a plus
                sign.localScale = Vector3.one * (look.healSparkSize * strength);
                block.SetColor(BaseColorId, new Color(green.r, green.g, green.b, strength));
                renderers[i].SetPropertyBlock(block);
            }
        }

        /// <summary>A flat plus one unit tip to tip, arms a third as thick, in its XY plane; both faces, so no camera sees it vanish.</summary>
        private static Mesh CreatePlusMesh()
        {
            const float h = 0.5f, t = 1f / 6f;
            var vertices = new Vector3[12];
            var triangles = new int[36];
            int v = 0, n = 0;
            void Rect(float x0, float y0, float x1, float y1)
            {
                int b = v;
                vertices[v++] = new Vector3(x0, y0, 0f);
                vertices[v++] = new Vector3(x1, y0, 0f);
                vertices[v++] = new Vector3(x0, y1, 0f);
                vertices[v++] = new Vector3(x1, y1, 0f);
                foreach (int k in new[] { b, b + 2, b + 3, b, b + 3, b + 1, b, b + 3, b + 2, b, b + 1, b + 3 })
                    triangles[n++] = k;
            }
            Rect(-h, -t, h, t);
            Rect(-t, t, t, h);
            Rect(-t, -h, t, -t);

            var colours = new Color[vertices.Length];
            for (int i = 0; i < colours.Length; i++)
                colours[i] = Color.white; // the colour rides on the property block; FX Alpha multiplies by the vertex colour
            var mesh = new Mesh { name = MeshName, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.colors = colours;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (mesh == null)
                return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        /// <summary>With domain reload on, a plus mesh alive when Play Mode stopped lost its owner: free it by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Mesh leftover in Resources.FindObjectsOfTypeAll<Mesh>())
                if (leftover != null && leftover.name == MeshName && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
