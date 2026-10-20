using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The "Connection lost - Rejoin" panel (D21): a dimmed overlay with a title, a line saying what Rejoin does and
    /// Rejoin / Leave buttons; it also shows "Reconnecting..." while the rejoin runs, and a message with one OK button
    /// when the match cannot be returned to. Built in code in the card look (PopupCard) like QuitConfirmPanel (every text
    /// is a UiTheme field). Only draws and reports clicks; what Rejoin and Leave DO belongs to RejoinController.
    /// </summary>
    public sealed class ConnectionLostPanel : MonoBehaviour
    {
        private UiTheme theme;
        private GameObject canvasRoot;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI bodyLabel;
        private GameObject lostButtons;
        private GameObject okButtons;
        private Button rejoinButton;
        private LobbyUiKit kit;

        public event Action RejoinClicked;
        public event Action LeaveClicked;
        public event Action OkClicked;

        public bool IsShowing => canvasRoot != null && canvasRoot.activeSelf;
        public string TitleText => titleLabel != null ? titleLabel.text : "";
        public string BodyText => bodyLabel != null ? bodyLabel.text : "";

        /// <summary>Builds the (hidden) panel. Call once, right after adding the component.</summary>
        public void Build(UiTheme uiTheme)
        {
            theme = uiTheme;
            BuildUi();
            canvasRoot.SetActive(false);
        }

        private void OnDestroy() => kit?.Dispose();

        /// <summary>The connection dropped: title, what Rejoin does, and the Rejoin / Leave buttons.</summary>
        public void ShowLost()
        {
            Show(Text(t => t.connectionLostTitle, "Connection lost"), Text(t => t.connectionLostBody, ""), lost: true);
            rejoinButton.interactable = true;
        }

        /// <summary>A rejoin is running (or the name screen's button was pressed): no buttons to press.</summary>
        public void ShowWorking()
        {
            Show(Text(t => t.connectionLostTitle, "Connection lost"), Text(t => t.rejoinWorkingText, "Reconnecting..."), lost: false, ok: false);
        }

        /// <summary>The rejoin cannot happen: the message and one OK button back to the name screen.</summary>
        public void ShowFailed(string message = null)
        {
            Show(Text(t => t.connectionLostTitle, "Connection lost"),
                 string.IsNullOrEmpty(message) ? Text(t => t.rejoinFailedText, "The match is gone.") : message, lost: false, ok: true);
        }

        public void Hide()
        {
            if (canvasRoot != null)
                canvasRoot.SetActive(false);
        }

        private string Text(Func<UiTheme, string> pick, string fallback) => theme != null ? pick(theme) : fallback;

        private void Show(string title, string body, bool lost, bool ok = false)
        {
            titleLabel.text = title;
            bodyLabel.text = body;
            lostButtons.SetActive(lost);
            okButtons.SetActive(ok);
            canvasRoot.SetActive(true);
        }

        // ---- UI construction (PopupCard: the card look of the round bar and result card) ----

        private void BuildUi()
        {
            theme = PopupCard.ThemeOrDefault(theme);
            kit = new LobbyUiKit(theme);
            Canvas canvas = PopupCard.CreateCanvas(transform, "Connection Lost Canvas", UiLayers.ConnectionLost, theme, true);
            canvasRoot = canvas.gameObject;
            kit.Backdrop(canvas.transform, "Dim", theme.popupDimColor);

            RectTransform card = PopupCard.Card(kit, canvas.transform, "Panel", theme.connectionLostPanelWidth, theme.spectatorResultCardPadding, theme.spectatorResultCardGap);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            titleLabel = PopupCard.Line(kit, card, "Title", "", kit.Display, theme.connectionLostTitleSize, theme.connectionLostTitleColor, false);
            bodyLabel = PopupCard.Line(kit, card, "Body", "", kit.Body, theme.connectionLostBodySize, theme.lobbyOffWhiteColor, true);

            lostButtons = PopupCard.ButtonRow(card, theme.lobbyListColumnGap).gameObject;
            lostButtons.name = "Lost buttons";
            rejoinButton = PopupCard.Primary(kit, lostButtons.transform, Text(t => t.connectionLostRejoinButton, "Rejoin"), () => RejoinClicked?.Invoke()).Button;
            PopupCard.Secondary(kit, lostButtons.transform, Text(t => t.connectionLostLeaveButton, "Leave"), () => LeaveClicked?.Invoke());

            okButtons = PopupCard.ButtonRow(card, theme.lobbyListColumnGap).gameObject;
            okButtons.name = "OK buttons";
            PopupCard.Primary(kit, okButtons.transform, Text(t => t.rejoinFailedOkButton, "OK"), () => OkClicked?.Invoke());
        }
    }
}
