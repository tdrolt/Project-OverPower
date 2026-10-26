using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Applies UiTheme's reference resolution and match value to this GameObject's CanvasScaler at Awake: the one
    /// home for those two numbers, so scene-built canvases (the chat window's, which PlayerHud does not touch)
    /// scale identically to PlayerHud's code-built one. The CanvasScaler's own serialized values are only a
    /// fallback (before this runs, or if the theme reference is lost). PlayerHud applies the theme to its own
    /// CanvasScaler directly in BuildUi instead of adding this component to a freshly built GameObject.
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
