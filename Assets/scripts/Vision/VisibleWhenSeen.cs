using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Net;

namespace Overpower.Vision
{
    /// <summary>
    /// On an enemy's shot (a bullet, rocket, Stun Gun or Zip Gun bolt, a rope anchor): every Renderer under this object
    /// (mesh, trail, line, particles) is switched on only while my team sees the object's position, so a shot from the
    /// fog appears as it crosses the fog's edge (vision D2). Only Renderer.enabled is touched - never the object's active
    /// state, colliders or scripts - so the shot still flies, hits and damages exactly as before. ForceVisible keeps my
    /// own team's shots always shown. No TeamSight yet, or the fog off, means shown.
    /// </summary>
    public sealed class VisibleWhenSeen : MonoBehaviour
    {
        private readonly List<Renderer> renderers = new List<Renderer>(8);
        private readonly List<bool> authoredEnabled = new List<bool>(8); // what each renderer was set to before we touched it
        private bool shown = true;
        // For a placed thing with a PhotonView (AoE Zone, Fire Field): shown always when its owner is on my team. Set on the
        // prefabs; not a designer field (unticking it would hide your own team's zones).
        [SerializeField, HideInInspector]
        private bool ownersTeamAlwaysSees;
        private float seenRadius; // > 0: a placed disc, shown when any part of it is seen or it reaches my team
        private float coneRange, coneAngle; // > 0 range: a flame cone, shown while its caster is shown or it reaches my team

        private PhotonView ownView;
        private PhotonView followCaster; // set: shown exactly while that player is shown (their abilities' visuals)

        /// <summary>True for my team's shots: always drawn.</summary>
        public bool ForceVisible { get; set; }

        /// <summary>Whether the renderers are currently on (recorders and tests read it).</summary>
        public bool Shown => shown;

        /// <summary>Adds the component to a freshly spawned shot of <paramref name="shooterTeam"/>: friendly shots are
        /// force-visible, an enemy's follow the sight. Does nothing without a TeamSight (nothing is hidden then).</summary>
        public static VisibleWhenSeen Attach(GameObject shot, int shooterTeam)
        {
            TeamSight sight = TeamSight.Local;
            if (shot == null || sight == null)
                return null;
            VisibleWhenSeen gate = shot.AddComponent<VisibleWhenSeen>();
            gate.ForceVisible = sight.IsFriendlyTeam(shooterTeam);
            gate.Apply();
            return gate;
        }

        /// <summary>Adds (or reuses) the component on an ability visual that belongs to a player and follows them: it is
        /// shown exactly while that player is shown (my team's always, an enemy's while seen), checked every frame. Does
        /// nothing without a TeamSight.</summary>
        public static VisibleWhenSeen AttachToCaster(GameObject visual, PhotonView casterView)
        {
            if (visual == null || TeamSight.Local == null)
                return null;
            VisibleWhenSeen gate = visual.GetComponent<VisibleWhenSeen>();
            if (gate == null)
                gate = visual.AddComponent<VisibleWhenSeen>();
            gate.followCaster = casterView;
            gate.RefreshRenderers(); // the visual may have switched renderers back on (FlameConeVisual.Configure) while we hid it
            gate.Apply();
            return gate;
        }

        /// <summary>For a placed disc (AoE Zone, Fire Field): with a radius above zero it is shown when its centre or any
        /// part of its rim is seen, or when it reaches my team (not only its centre).</summary>
        public void SetSeenRadius(float radius) => seenRadius = Mathf.Max(0f, radius);

        /// <summary>For the Flamethrower cone: also shown while the cone reaches any of my team (its length and full angle).</summary>
        public void SetCone(float range, float fullAngleDegrees)
        {
            coneRange = Mathf.Max(0f, range);
            coneAngle = fullAngleDegrees;
        }

        /// <summary>The whole choice, plain values: forced or no sight = shown; an object bound to a caster follows the
        /// caster; any other follows its own position.</summary>
        public static bool Decide(bool forceVisible, bool hasSight, bool followsCaster, bool casterShown, bool positionSeen)
        {
            if (forceVisible || !hasSight)
                return true;
            return followsCaster ? casterShown : positionSeen;
        }

        private void Awake()
        {
            ownView = GetComponent<PhotonView>();
            RefreshRenderers();
        }

        /// <summary>Collects the renderers again; call after adding children later (a trail, a muzzle effect). Renderers
        /// we switched off are put back to their authored state first, so "off" is never recorded as authored.</summary>
        public void RefreshRenderers()
        {
            if (!shown)
                SetRenderers(true);
            renderers.Clear();
            authoredEnabled.Clear();
            GetComponentsInChildren(true, renderers);
            for (int i = 0; i < renderers.Count; i++)
                authoredEnabled.Add(renderers[i] != null && renderers[i].enabled);
            shown = true;
        }

        private void LateUpdate() => Apply();

        /// <summary>Works out whether the object is shown right now (my team's: always; else its position seen) and applies it.</summary>
        public void Apply()
        {
            TeamSight sight = TeamSight.Local;
            bool bound = followCaster != null; // a caster who left (destroyed view) no longer binds: the spot rules
            bool force = ForceVisible || (ownersTeamAlwaysSees && sight != null && ownView != null
                && Teams.TryGetTeam(ownView.Owner, out int ownerTeam) && sight.IsFriendlyTeam(ownerTeam));
            bool casterShown = bound && sight != null && (sight.CanSeePlayer(followCaster)
                || (coneRange > 0f && TeamSight.ConeReachesMyTeam(transform.position, transform.forward, coneRange, coneAngle)));
            bool spotSeen = sight != null && !bound && !force
                && (seenRadius > 0f ? TeamSight.DiscShownAt(-1, transform.position, seenRadius) : sight.CanSeeShot(transform.position));
            SetVisible(Decide(force, sight != null, bound, casterShown, spotSeen));
        }

        /// <summary>Switches the renderers on (to their authored state) or off. A trail is cleared when it comes back, so
        /// a bullet that left the fog and returned draws no line through it.</summary>
        public void SetVisible(bool visible)
        {
            if (visible == shown)
            {
                // Hidden means every renderer off: outside code may have switched one back on since (a repeat spray).
                if (!visible)
                    SetRenderers(false);
                return;
            }
            shown = visible;
            SetRenderers(visible);
            if (visible)
                for (int i = 0; i < renderers.Count; i++)
                    if (renderers[i] is TrailRenderer trail)
                        trail.Clear();
        }

        private void SetRenderers(bool visible)
        {
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null)
                    renderers[i].enabled = visible && authoredEnabled[i];
        }
    }
}
