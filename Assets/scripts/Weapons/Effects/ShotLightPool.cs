using System.Collections.Generic;
using Overpower.Abilities;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// A few short, shadowless point lights hanging low over the floor under the shots this client can see, nearest to the camera first
    /// (ShotLightRule). The lights are made once and moved, never one per shot, so a nine-player SMG fight costs the same as one shot.
    /// Runs after every VisibleWhenSeen.LateUpdate: a Light is not a Renderer, so it is re-chosen every frame from the gates' fresh answer,
    /// or a shot that just went into the fog would light the floor where its shooter hides. Lives on MatchSystems.prefab.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class ShotLightPool : MonoBehaviour
    {
        public const string LightName = "Shot Light";

        [SerializeField, Tooltip("Where the number of lights, their reach and brightness come from (its Arena Look) and the team colours: " +
                 "Assets/Gameplay/Config/UiTheme.asset.")]
        private UiTheme theme;

        private static readonly List<ShotTeamVisuals> Live = new List<ShotTeamVisuals>(64);

        private readonly List<ShotCandidate> candidates = new List<ShotCandidate>(64);
        private readonly List<int> chosen = new List<int>(8);
        private Light[] lights;

        public static void Register(ShotTeamVisuals shot)
        {
            if (shot != null && !Live.Contains(shot))
                Live.Add(shot);
        }

        public static void Unregister(ShotTeamVisuals shot) => Live.Remove(shot);

        private void Awake() => Build(theme);

        /// <summary>Makes the Shot Light Cap lights once, switched off; a second call keeps the ones already made.</summary>
        public void Build(UiTheme source)
        {
            theme = source;
            if (lights != null || theme == null || theme.arenaLook == null)
                return;
            int cap = Mathf.Max(0, theme.arenaLook.shotLightCap);
            lights = new Light[cap];
            for (int i = 0; i < cap; i++)
            {
                var go = new GameObject(LightName);
                go.transform.SetParent(transform, false);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.range = theme.arenaLook.shotLightRange;
                light.intensity = theme.arenaLook.shotLightIntensity;
                light.enabled = false;
                lights[i] = light;
            }
        }

        private void LateUpdate()
        {
            if (lights == null)
                return;
            Camera view = Camera.main;
            Vector3 eye = view != null ? view.transform.position : Vector3.zero;
            bool hasSight = TeamSight.Local != null;

            candidates.Clear();
            for (int i = Live.Count - 1; i >= 0; i--)
                if (Live[i] == null)
                    Live.RemoveAt(i); // a shot destroyed without its OnExpired (scene change, a failed spawn)
            for (int i = 0; i < Live.Count; i++)
            {
                VisibleWhenSeen gate = Live[i].Gate;
                bool shown = ShotLightRule.Shown(gate != null, gate != null && gate.Shown, hasSight);
                candidates.Add(new ShotCandidate(shown, (Live[i].transform.position - eye).sqrMagnitude));
            }
            ShotLightRule.Pick(candidates, lights.Length, chosen);

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (i >= chosen.Count)
                {
                    light.enabled = false;
                    continue;
                }
                ShotTeamVisuals shot = Live[chosen[i]];
                Vector3 at = shot.transform.position;
                float height = theme.arenaLook.shotLightHeight;
                float groundY = GroundSnap.TryFindGroundY(at, out float floor) ? floor : at.y - height;
                light.transform.position = ShotLightRule.LightAt(at, groundY, height);
                light.color = shot.LightColour;
                light.enabled = true;
            }
        }
    }
}
