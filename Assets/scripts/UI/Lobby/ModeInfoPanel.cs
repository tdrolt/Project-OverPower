using Overpower.Data;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The game mode info page (board 9), opened by the mode button of the lobby room. Until lobby Task 12 fills it with the mode's info cards it is
    /// the page frame with the mode's name and a close button.
    /// </summary>
    public sealed class ModeInfoPanel : LobbyOverlayPanel
    {
        public static ModeInfoPanel Create(Transform canvas, LobbyUiKit kit)
        {
            var go = new GameObject("Mode Info", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            ModeInfoPanel panel = go.AddComponent<ModeInfoPanel>();
            panel.BuildCard(kit, "Mode Info Page");
            return panel;
        }

        public void Show(GameModeDefinition mode) => Show(mode != null ? mode.DisplayName : "");
    }
}
