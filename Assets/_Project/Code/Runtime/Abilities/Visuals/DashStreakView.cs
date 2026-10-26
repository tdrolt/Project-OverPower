using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// What another player's screen shows of a Dash: a straight grey streak from where it began to just behind the dasher, and flat
    /// grey chevrons on the floor at the start pointing the way it went, then a short fade (DashLook decides every place and fade). One
    /// unparented object, left where the dash happened, reused by the next dash; the fog gate follows the dasher, so a dash in the fog draws
    /// nothing, and a dash out of the fog starts where the dasher was first seen. Only line points and colours change per frame
    /// (VisibleWhenSeen owns Renderer.enabled).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DashStreakView : MonoBehaviour
    {
        public const string RootName = "Dash Streak";
        public const string StreakName = "Streak";
        public const string ChevronName = "Chevron ";
        private const float FloorLift = 0.03f; // keeps the chevrons off the floor's own surface; not a look number
        private const float ChevronDepthOfWidth = 0.45f; // how far the wings trail the tip, as a share of the chevron's width; keeps the V's shape

        [SerializeField, Tooltip("Where the streak's colour (the Dash row of the look table), material and sizes come from: " +
                 "Assets/_Project/Shared/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        private AbilityModule module;
        private ArenaLook look;
        private LineRenderer streak;
        private LineRenderer[] chevrons;
        private Transform body;
        private Vector3 start;
        private Vector3 forward;
        private float floorY;
        private float distance;
        private float travelSeconds;
        private float bodyReach;
        private float seenFrom = DashLook.NotSeenYet;
        private VisibleWhenSeen gate;
        private Color colour;
        private float playedAt = -1f;
        private static bool warnedMissingLook;

        public GameObject Root { get; private set; }

        public void Play(Vector3 start, Vector3 forward, float distance, float travelSeconds, Transform body, int casterTeam, PhotonView caster)
        {
            look = theme != null ? theme.arenaLook : null;
            if (look == null || look.fxAlpha == null)
            {
                if (!warnedMissingLook)
                {
                    warnedMissingLook = true;
                    Debug.LogWarning($"[DashStreakView] {name}: no UiTheme Arena Look with FX Alpha - the Dash draws nothing on other screens.");
                }
                return;
            }
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            if (Root == null)
                Build();
            this.start = start;
            this.forward = forward.normalized;
            this.distance = distance;
            this.travelSeconds = travelSeconds;
            this.body = body;
            bodyReach = 0f;
            floorY = GroundSnap.TryFindGroundY(start, out float groundY) ? groundY : start.y;
            if (module == null)
                module = GetComponent<AbilityModule>();
            int abilityId = module != null && module.Definition != null ? module.Definition.Id : -1;
            colour = LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.Movement);
            playedAt = Time.time;
            VisibleWhenSeen.AttachToCaster(Root, caster);
            gate = Root.GetComponent<VisibleWhenSeen>(); // none without fog: everything shows
            seenFrom = DashLook.SeenFrom(DashLook.NotSeenYet, gate == null || gate.Shown, 0f); // seen at the cast = seen at the launch
            Draw(0f);
            Root.SetActive(true);
        }

        // Built active with empty lines (nothing drawn): the gate added in Play then wakes as it attaches, not later when the streak is
        // switched on, which would switch every line back on from a fogged dasher.
        private void Build()
        {
            Root = new GameObject(RootName);
            streak = AddLine(StreakName, look.dashStreakWidth, LineAlignment.View);
            streak.widthCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f); // a point where the dash began, full width at the dasher
            chevrons = new LineRenderer[look.dashChevronCount];
            for (int i = 0; i < chevrons.Length; i++)
            {
                chevrons[i] = AddLine(ChevronName + i, look.dashChevronWidth, LineAlignment.TransformZ);
                chevrons[i].transform.rotation = Quaternion.Euler(90f, 0f, 0f); // TransformZ then lays the band flat on the floor
                chevrons[i].numCornerVertices = 0;
            }
        }

        private LineRenderer AddLine(string lineName, float width, LineAlignment alignment)
        {
            var owner = new GameObject(lineName);
            owner.transform.SetParent(Root.transform, false);
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = look.fxAlpha;
            line.alignment = alignment;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private void Update()
        {
            if (playedAt < 0f || Root == null)
                return;
            float age = Time.time - playedAt;
            if (age > travelSeconds + look.dashFadeSeconds)
            {
                Root.SetActive(false);
                playedAt = -1f;
                return;
            }
            Draw(age);
        }

        /// <summary>The streak and chevrons as they stand <paramref name="secondsSinceCast"/> after the dash began: the streak's head follows
        /// just behind the dasher along the dash line, each chevron appears as the dasher passes it, all solid while the dash travels, then
        /// everything fades.</summary>
        public void Draw(float secondsSinceCast)
        {
            if (streak == null)
                return;
            Color shown = VisualTint.WithAlpha(colour, look.dashStreakAlpha * DashLook.Alpha(secondsSinceCast, travelSeconds, look.dashFadeSeconds));
            Vector3 up = Vector3.up * (floorY + look.dashStreakHeight);
            Vector3 flatStart = new Vector3(start.x, 0f, start.z);
            // Grows only, for as long as the streak shows: another screen's copy of the dasher lags behind the real one, so it reaches the
            // end after the travel time; a frame hitch must not freeze the head short.
            if (body != null)
                bodyReach = Mathf.Max(bodyReach, DashLook.StreakReach(start, forward, body.position, distance));
            // The gate's answer is last frame's (it decides in LateUpdate), so the first frame it opens draws nothing yet: no frame of fog.
            seenFrom = DashLook.SeenFrom(seenFrom, gate == null || gate.Shown, bodyReach);
            float head = DashLook.HeadReach(bodyReach, look.dashStreakGap);
            if (seenFrom < 0f || head <= seenFrom)
                streak.positionCount = 0;
            else
            {
                streak.positionCount = 2;
                streak.SetPosition(0, flatStart + forward * seenFrom + up);
                streak.SetPosition(1, flatStart + forward * head + up);
            }
            VisualTint.SetLineColor(streak, shown);

            Vector3 floor = flatStart + Vector3.up * (floorY + FloorLift);
            float depth = look.dashChevronSize * ChevronDepthOfWidth;
            for (int i = 0; i < chevrons.Length; i++)
            {
                float along = DashLook.ChevronDistance(i, chevrons.Length, look.dashChevronSpacing, distance);
                LineRenderer chevron = chevrons[i];
                bool seen = seenFrom >= 0f && along >= seenFrom;
                if (!DashLook.ChevronShown(along, bodyReach) || !seen)
                {
                    chevron.positionCount = 0;
                    continue;
                }
                Vector3 tip = floor + forward * along;
                chevron.positionCount = 3;
                for (int k = 0; k < 3; k++)
                    chevron.SetPosition(k, DashLook.ChevronPoint(tip, forward, look.dashChevronSize, depth, k));
                VisualTint.SetLineColor(chevron, shown);
            }
        }

        private void OnDestroy()
        {
            if (Root == null)
                return;
            if (Application.isPlaying) Destroy(Root);
            else DestroyImmediate(Root);
            Root = null;
        }
    }
}
