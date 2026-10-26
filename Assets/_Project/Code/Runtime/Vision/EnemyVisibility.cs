using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// On every player's prefab, remote copies only: while my team does not see this player, their body (every Renderer
    /// under the character model) and the overhead canvas (the plate with name and bar, STUNNED/SLOWED label, immune
    /// frame) are switched off. Teammates are always shown. Only Renderer.enabled and Canvas.enabled are touched - never
    /// active state, colliders or networking - so PlayerLifecycle's hide-on-death (SetActive) stays independent and a
    /// hidden player still moves, collides and takes damage. Effects parented outside the model (rings, glows) are not covered here.
    /// </summary>
    public sealed class EnemyVisibility : MonoBehaviourPun
    {
        private readonly List<Renderer> bodyRenderers = new List<Renderer>(16);
        private Canvas overheadCanvas;
        private PlayerLifecycle lifecycle;
        private bool applied = true;
        private bool forceRefresh = true;

        public bool Shown => applied;

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
            // Found by its own lookup, not through the health bar's image, so the plate (NameplateView) can ask the same question.
            overheadCanvas = FindOverheadCanvas(this);
        }

        /// <summary>The canvas over the player's head that this class hides in the fog: the world-space one (the other is the owner's
        /// screen-space win/lose panel). Anything drawn over a player's head belongs on it, or the fog will not hide it.</summary>
        public static Canvas FindOverheadCanvas(Component player)
        {
            foreach (Canvas canvas in player.GetComponentsInChildren<Canvas>(true))
                if (canvas.renderMode == RenderMode.WorldSpace)
                    return canvas;
            return null;
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
