using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// On every player's prefab, working on remote copies only: while my team does not see this player, their body
    /// (every Renderer under the character model) and the overhead canvas (name, health/armour bar, STUNNED/SLOWED label,
    /// immune frame) are switched off, and they come back the moment they are seen. Teammates are always shown.
    /// Only Renderer.enabled and Canvas.enabled are touched - never a GameObject's active state, the colliders or the
    /// networking - so PlayerLifecycle's own hide-on-death (SetActive) stays independent, and a hidden player still
    /// moves, collides and takes damage. Effects parented to the player outside the model (rings, glows) are not covered
    /// here; Task 5 owns them.
    /// </summary>
    public sealed class EnemyVisibility : MonoBehaviourPun
    {
        private readonly List<Renderer> bodyRenderers = new List<Renderer>(16);
        private Canvas overheadCanvas;
        private PlayerLifecycle lifecycle;
        private bool applied = true;
        private bool forceRefresh = true;

        /// <summary>Whether this player's body is currently shown on this client (tests and recorders read it).</summary>
        public bool Shown => applied;

        /// <summary>The overhead (world-space) canvas that carries the name, bar and labels; recorders read its enabled flag.</summary>
        public Canvas OverheadCanvas => overheadCanvas;

        private void Awake()
        {
            if (photonView.IsMine)
            {
                enabled = false;
                return;
            }

            lifecycle = GetComponent<PlayerLifecycle>();
            if (lifecycle != null && lifecycle.PlayerMesh != null)
                lifecycle.PlayerMesh.GetComponentsInChildren(true, bodyRenderers);
            // The world-space canvas under the player root is the overhead one (the other canvas is the owner's own
            // screen-space win/lose panel). PlayerHealth.OverheadCanvas now finds it too (it includes a switched-off
            // canvas); this search stays so the canvas is found without needing the health bar's image.
            foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    overheadCanvas = canvas;
                    break;
                }
            }
        }

        private void OnEnable()
        {
            forceRefresh = true;
            if (lifecycle != null)
                lifecycle.AliveChanged += OnAliveChanged;
        }

        private void OnDisable()
        {
            if (lifecycle != null)
                lifecycle.AliveChanged -= OnAliveChanged;
        }

        // A respawn re-activates the model (SetActive), which leaves Renderer.enabled as it was - but re-apply anyway.
        private void OnAliveChanged(bool alive) => forceRefresh = true;

        private void LateUpdate()
        {
            TeamSight sight = TeamSight.Local;
            bool visible = sight == null || sight.CanSeePlayer(photonView);
            if (visible == applied && !forceRefresh)
                return;
            forceRefresh = false;
            applied = visible;
            for (int i = 0; i < bodyRenderers.Count; i++)
                if (bodyRenderers[i] != null)
                    bodyRenderers[i].enabled = visible;
            if (overheadCanvas != null)
                overheadCanvas.enabled = visible;
        }
    }
}
