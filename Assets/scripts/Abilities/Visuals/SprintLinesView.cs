using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// Short grey speed-lines streaming behind a sprinting player's feet, on every client's copy of the player (built by the Sprint
    /// module as it is equipped). The lines live on a child this view switches on and off by SprintLinesRule; the view's own object stays
    /// active so its LateUpdate keeps running, and the fog gate on it (VisibleWhenSeen, following the player) only ever touches
    /// Renderer.enabled. The gate collects renderers once, so every line is built before it attaches. The run's speed and direction come
    /// from how far the body moved, which every client sees.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SprintLinesView : MonoBehaviour
    {
        public const string RootName = "Sprint Lines";
        public const string LinesChildName = "Lines";
        private const float SpeedResponseSeconds = 0.1f; // smooths over frames without a physics step; not a look number

        private SprintAbility source;
        private PhotonView caster;
        private UiTheme theme;
        private int abilityId;
        private ArenaLook look;
        private Transform body;
        private float feetBelowRoot;
        private GameObject lines;
        private LineRenderer[] all;
        private VisibleWhenSeen gate;
        private Color colour;
        private Vector3 lastPosition;
        private bool hasLast;
        private float speed;
        private Vector3 heading = Vector3.forward;

        /// <summary>Builds the hidden lines under <paramref name="body"/>, whose feet are <paramref name="feetBelowRoot"/> below its
        /// position, or returns null (with a warning) when the theme has no Arena Look with FX Alpha.</summary>
        public static SprintLinesView TryCreate(Transform body, SprintAbility source, PhotonView caster, UiTheme theme, float feetBelowRoot)
        {
            ArenaLook look = theme != null ? theme.arenaLook : null;
            if (body == null || look == null || look.fxAlpha == null)
            {
                Debug.LogWarning($"[SprintLinesView] {(body != null ? body.name : "?")}: no UiTheme Arena Look with FX Alpha - this player shows no sprint lines.");
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(body, false);
            var lines = new GameObject(LinesChildName);
            lines.transform.SetParent(root.transform, false);
            lines.SetActive(false);

            int abilityId = source != null && source.Definition != null ? source.Definition.Id : -1;
            Color colour = ColourOf(theme, abilityId, caster);
            var all = new LineRenderer[look.sprintLineCount];
            for (int i = 0; i < all.Length; i++)
            {
                var owner = new GameObject("Line " + i);
                owner.transform.SetParent(lines.transform, false);
                LineRenderer line = owner.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = look.fxAlpha;
                line.positionCount = 2;
                line.widthMultiplier = look.sprintLineWidth;
                line.alignment = LineAlignment.View;
                line.numCapVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                VisualTint.SetLineColor(line, colour);
                all[i] = line;
            }

            SprintLinesView view = root.AddComponent<SprintLinesView>();
            view.source = source;
            view.caster = caster;
            view.theme = theme;
            view.abilityId = abilityId;
            view.look = look;
            view.body = body;
            view.feetBelowRoot = feetBelowRoot;
            view.lines = lines;
            view.all = all;
            view.colour = colour;
            view.gate = VisibleWhenSeen.AttachToCaster(root, caster);
            return view;
        }

        // Grey tinted by the sprinter's team as it stands now; plain grey while the team property has not arrived.
        private static Color ColourOf(UiTheme theme, int abilityId, PhotonView caster)
        {
            int team = caster != null && Overpower.Net.Teams.TryGetTeam(caster.Owner, out int t) ? t : -1;
            return LookColours.ForAbility(theme, abilityId, team, LookFamily.Movement);
        }

        private void LateUpdate()
        {
            if (body == null)
                return;
            Vector3 now = body.position;
            Vector3 moved = hasLast ? now - lastPosition : Vector3.zero;
            lastPosition = now;
            hasLast = true;
            Step(moved, Time.deltaTime, Time.time);
        }

        /// <summary>One frame: <paramref name="moved"/> is how far the body went since the last one.</summary>
        public void Step(Vector3 moved, float deltaTime, float time)
        {
            if (source == null)
                return;
            bool wasRunning = speed >= look.sprintLineMinSpeed;
            speed = SprintLinesRule.Smooth(speed, SprintLinesRule.Speed(Vector3.zero, moved, deltaTime), deltaTime, SpeedResponseSeconds);
            moved.y = 0f;
            if (moved.sqrMagnitude > 1e-8f) // a run that just began points its lines straight back; a turning run eases round
                heading = wasRunning ? Vector3.Slerp(heading, moved.normalized, 1f - Mathf.Exp(-deltaTime / SpeedResponseSeconds)) : moved.normalized;

            if (SprintLinesRule.Show(source.LooksSprinting, speed, look.sprintLineMinSpeed))
            {
                // Null when this player was built before the local sight existed (a late join); asked again so the lines never show ungated.
                if (gate == null)
                    gate = VisibleWhenSeen.AttachToCaster(gameObject, caster);
                if (!lines.activeSelf)
                {
                    // The lines are built on equip, which can come before the player's team property (a late join): ask again per run.
                    colour = ColourOf(theme, abilityId, caster);
                    lines.SetActive(true);
                }
                Reshape(time);
            }
            else if (lines.activeSelf)
                lines.SetActive(false);
        }

        private void Reshape(float time)
        {
            Vector3 forward = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
            Quaternion run = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 feet = body.position + Vector3.up * (look.sprintLineLift - feetBelowRoot);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].SetPosition(0, feet + run * SprintLinesRule.LinePoint(i, all.Length, time, look.sprintLineRate, look.sprintLineSpread,
                    look.sprintLineBehind, look.sprintLineLength, look.sprintLineTravel, false));
                all[i].SetPosition(1, feet + run * SprintLinesRule.LinePoint(i, all.Length, time, look.sprintLineRate, look.sprintLineSpread,
                    look.sprintLineBehind, look.sprintLineLength, look.sprintLineTravel, true));
                VisualTint.SetLineColor(all[i], VisualTint.WithAlpha(colour, look.sprintLineAlpha * SprintLinesRule.LineAlpha(i, all.Length, time, look.sprintLineRate)));
            }
        }
    }
}
