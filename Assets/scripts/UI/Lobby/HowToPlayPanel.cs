using TMPro;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The How to play page (board 5A), opened from the lobby list and the lobby room only. Until lobby Task 12 fills it with the pages and pictures
    /// it is the page frame with a title, a line saying more is coming, and a close button.
    /// </summary>
    public sealed class HowToPlayPanel : LobbyOverlayPanel
    {
        public static HowToPlayPanel Create(Transform canvas, LobbyUiKit kit)
        {
            var go = new GameObject("How To Play", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            LobbyUiKit.Stretch((RectTransform)go.transform);
            HowToPlayPanel panel = go.AddComponent<HowToPlayPanel>();
            panel.BuildCard(kit, "How To Play Page");
            TextMeshProUGUI stub = kit.Text(panel.Body, "Stub", kit.Theme.lobbyRoomHowToStubText, kit.Body, kit.Theme.lobbyListRowTextSize,
                kit.Theme.lobbyDimColor, TextAlignmentOptions.TopLeft);
            LobbyUiKit.Stretch((RectTransform)stub.transform);
            return panel;
        }

        public void Show() => Show(Theme.lobbyListHowToText);
    }
}
