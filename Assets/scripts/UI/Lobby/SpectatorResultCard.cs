using System;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The result card a spectator sees when the match ends (spec section 11): who won, and the same "Back to the lobby list"
    /// button the players' result screen has. A spectator has no body, so none of the MatchUI panels (which live on the player prefab) can
    /// show; this is built in code on its own overlay canvas like SpectatorBar, above the bar. The zip that runs when the card goes up
    /// raises the saved-log box only for a spectator who is the room's host (an ordinary spectator writes no log, by design). The look is
    /// LobbyUiKit.ResultCard; texts, colours and sizes are UiTheme fields (Spectator bar section).
    /// </summary>
    public sealed class SpectatorResultCard : MonoBehaviour
    {
        private UiTheme theme;
        private Action onBack;
        private LobbyUiKit kit;

        /// <summary>The title as shown ("Purple wins the match"); checks read it.</summary>
        public string Title { get; private set; } = "";

        /// <summary>The button's label ("Back to the lobby list").</summary>
        public string ButtonText { get; private set; } = "";

        /// <summary>The card's button, for checks that press it through its onClick.</summary>
        public Button BackButton { get; private set; }

        public static SpectatorResultCard Create(Transform parent, UiTheme theme, string title, Color teamColor, Action onBack)
        {
            var go = new GameObject("Spectator Result Card", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SpectatorResultCard card = go.AddComponent<SpectatorResultCard>();
            card.theme = theme;
            card.onBack = onBack;
            card.Title = title;
            card.ButtonText = theme.resultButtonLobbyList;
            card.Build(teamColor);
            return card;
        }

        private void OnDestroy()
        {
            kit?.Dispose();
        }

        private void Build(Color teamColor)
        {
            kit = new LobbyUiKit(theme);
            Canvas canvas = kit.CreateCanvas(transform, "Spectator Result Canvas");
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5; // over the spectator bar (-10), under the saved-log box (10)

            // The card itself (title and button) is the lobby look from LobbyUiKit.ResultCard, shared with Dominion's result screen.
            LobbyButton button = kit.ResultCard(canvas.transform, Title, teamColor, ButtonText, out RectTransform card);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -theme.spectatorResultCardTop);
            BackButton = button.Button;
            BackButton.onClick.AddListener(() => onBack?.Invoke());
        }
    }
}
