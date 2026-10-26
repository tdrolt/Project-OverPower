using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// The mark where a Stun Gun bolt strikes a player (StunImpactOnHit plays it on every client that sees the spot): electric spikes shooting
    /// out across the screen, a blue light on the floor around it and a crackling shock ring spreading over the floor, every move from
    /// StunImpactLook. One unparented object per hit, left where the bolt struck and destroyed when the last part has faded.
    /// The glow rides on each line's _BaseColor in the stun's own blue (GlowKeepingHue) with white vertex colours carrying only the fade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StunImpactView : MonoBehaviour
    {
        public const string RaysName = "Flash Rays";
        public const string RingName = "Shock Ring";
        private const int RingPoints = 28; // enough for a crackling circle; not a look number
        private const float FloorLift = 0.03f; // keeps the ring off the floor's own surface; not a look number

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private ArenaLook look;
        private LineRenderer[] rays;
        private Vector3[] rayDirections;
        private LineRenderer ring;
        private Light flashLight;
        private int seed;
        private float startedAt;

        /// <summary>Builds and starts the mark at <paramref name="point"/>, or returns null (with a warning) when the theme has no Arena Look with FX Alpha.</summary>
        public static StunImpactView Play(Vector3 point, UiTheme theme)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (look == null || look.fxAlpha == null)
            {
                Debug.LogWarning("[StunImpactView] no UiTheme Arena Look with FX Alpha - a Stun Gun hit shows no flash or ring.");
                return null;
            }

            Color blue = LookColours.For(LookFamily.ElectricBlue, -1, theme);
            var root = new GameObject("Stun Impact");
            root.transform.position = point;
            StunImpactView view = root.AddComponent<StunImpactView>();
            view.look = look;
            view.seed = Mathf.RoundToInt(point.x * 1000f) * 31 + Mathf.RoundToInt(point.z * 1000f);
            view.startedAt = Time.time;

            Camera eye = Camera.main;
            Vector3 right = eye != null ? eye.transform.right : Vector3.right;
            Vector3 up = eye != null ? eye.transform.up : Vector3.up;
            float turn = StunArcsRule.Hash01(view.seed, 0, 0) * 360f;
            var rays = new GameObject(RaysName);
            rays.transform.SetParent(root.transform, false);
            view.rays = new LineRenderer[look.stunFlashRayCount];
            view.rayDirections = new Vector3[view.rays.Length];
            for (int i = 0; i < view.rays.Length; i++)
            {
                LineRenderer ray = AddLine(rays.transform, "Spike " + i, look, look.stunFlashRayWidth, TowerLookRules.GlowKeepingHue(blue, look.stunFlashGlow));
                ray.useWorldSpace = true;
                ray.alignment = LineAlignment.View;
                ray.positionCount = 2;
                ray.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // full width at the hit, a point at its tip
                view.rays[i] = ray;
                view.rayDirections[i] = StunImpactLook.RayDirection(i, view.rays.Length, turn, right, up);
            }

            var ringRoot = new GameObject(RingName);
            ringRoot.transform.SetParent(root.transform, false);
            float floor = GroundSnap.TryFindGroundY(point, out float groundY) ? groundY : point.y;
            ringRoot.transform.SetPositionAndRotation(new Vector3(point.x, floor + FloorLift, point.z), Quaternion.Euler(90f, 0f, 0f));
            view.ring = AddLine(ringRoot.transform, "Band", look, look.stunRingWidth, TowerLookRules.GlowKeepingHue(blue, look.stunRingGlow));
            view.ring.useWorldSpace = false;
            view.ring.loop = true;
            view.ring.alignment = LineAlignment.TransformZ; // with the root turned 90 degrees on X the band lies flat
            view.ring.positionCount = RingPoints;

            if (look.stunFlashLightIntensity > 0f)
            {
                view.flashLight = root.AddComponent<Light>();
                view.flashLight.type = LightType.Point;
                view.flashLight.color = blue;
                view.flashLight.range = look.stunFlashLightRange;
                view.flashLight.shadows = LightShadows.None;
            }

            view.Draw(0f);
            return view;
        }

        /// <summary>The mark <paramref name="ageSeconds"/> after the hit.</summary>
        public void Draw(float ageSeconds)
        {
            float reach = StunImpactLook.RayReach(ageSeconds, look.stunFlashSeconds, look.stunFlashReach);
            Color rayFade = new Color(1f, 1f, 1f, AbilityVisualGeometry.Fade01(ageSeconds, look.stunFlashSeconds));
            Vector3 centre = transform.position;
            for (int i = 0; i < rays.Length; i++)
            {
                rays[i].SetPosition(0, centre);
                rays[i].SetPosition(1, centre + rayDirections[i] * reach);
                VisualTint.SetLineColor(rays[i], rayFade);
            }

            float radius = StunImpactLook.RingRadius(ageSeconds, look.stunRingSeconds, look.stunRingStartRadius, look.stunRingRadius);
            int crackle = unchecked(seed + Mathf.FloorToInt(ageSeconds * look.stunArcRate) * 7919); // re-shapes at the arcs' own rate
            for (int k = 0; k < RingPoints; k++)
            {
                Vector3 p = StunImpactLook.RingPoint(crackle, k, RingPoints, radius, look.stunRingJitter);
                ring.SetPosition(k, new Vector3(p.x, p.z, 0f));
            }
            VisualTint.SetLineColor(ring, new Color(1f, 1f, 1f, AbilityVisualGeometry.Fade01(ageSeconds, look.stunRingSeconds)));

            if (flashLight != null)
                flashLight.intensity = StunImpactLook.LightIntensity(ageSeconds, look.stunFlashSeconds, look.stunFlashLightIntensity);
        }

        private void Update()
        {
            float age = Time.time - startedAt;
            if (age > Mathf.Max(look.stunFlashSeconds, look.stunRingSeconds))
                Destroy(gameObject);
            else
                Draw(age);
        }

        private static LineRenderer AddLine(Transform parent, string lineName, ArenaLook look, float width, Color glow)
        {
            var owner = new GameObject(lineName);
            owner.transform.SetParent(parent, false);
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.sharedMaterial = look.fxAlpha; // blended, not added: added light turns the blue pale cyan over the grey floor
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, glow);
            line.SetPropertyBlock(block);
            return line;
        }
    }
}
