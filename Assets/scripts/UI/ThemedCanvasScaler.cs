using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Applies UiTheme's reference resolution and match value to this GameObject's CanvasScaler at
    /// Awake - the one home for those two numbers, so a canvas built by hand in the scene (the chat
    /// window's canvases, which PlayerHud does not build or touch) scales identically to PlayerHud's
    /// own code-built canvas instead of drifting apart as the window size changes.
    ///
    /// Task 5's own fix pass first tried fixing this by copying 1920x1080/0.5 straight into each
    /// chat CanvasScaler by hand - which worked, but left the same two numbers living in two places
    /// (UiTheme's fields, and every CanvasScaler that needs them), with nothing keeping them in sync
    /// if either changed. This component is that sync: the CanvasScaler's own serialized values in
    /// the scene are now only a fallback (what you see before this runs, or if the theme reference
    /// is ever lost) - the theme is the one home.
    ///
    /// PlayerHud keeps applying the theme to its own CanvasScaler directly in BuildUi rather than
    /// also going through this component - it builds that canvas in code already, one line each for
    /// referenceResolution/matchWidthOrHeight costs nothing extra, and routing through a component
    /// would mean AddComponent-ing this onto a freshly built GameObject for no real gain.
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class ThemedCanvasScaler : MonoBehaviour
    {
        [SerializeField, Tooltip("Reference resolution and match value come from here. Leave unassigned " +
                 "and the CanvasScaler keeps whatever it was last saved with in the scene.")]
        private UiTheme theme;

        private void Awake()
        {
            if (theme == null)
            {
                Debug.LogError($"[ThemedCanvasScaler] {name}: UiTheme is not assigned - the CanvasScaler keeps its " +
                                "own serialized reference resolution/match value instead of the theme's.");
                return;
            }

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
        }
    }
}
