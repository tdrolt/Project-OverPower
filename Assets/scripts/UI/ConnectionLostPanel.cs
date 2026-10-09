using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The "Connection lost - Rejoin" panel (D21): a dimmed overlay with a title, a line saying what Rejoin does and
    /// Rejoin / Leave buttons; it also shows "Reconnecting..." while the rejoin runs, and a message with one OK button
    /// when the match cannot be returned to. Built in code from UiTheme like QuitConfirmPanel (every text is a UiTheme
    /// field). Only draws and reports clicks; what Rejoin and Leave DO belongs to RejoinController.
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
        private Material textMaterial;

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

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

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

        // ---- UI construction (QuitConfirmPanel's own recipe) ----

        private void BuildUi()
        {
            var canvasGo = new GameObject("Connection Lost Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // Above the Escape pop-up (700): with no connection nothing else on screen can be acted on.
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasRoot = canvasGo;

            var dim = new GameObject("Dim", typeof(RectTransform));
            dim.transform.SetParent(canvasGo.transform, false);
            dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var dimRt = (RectTransform)dim.transform;
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panel.AddComponent<Image>().color = theme != null ? theme.connectionLostPanelColor : new Color(0.08f, 0.08f, 0.08f, 0.95f);
            var panelRt = (RectTransform)panel.transform;
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(theme != null ? theme.connectionLostPanelWidth : 640f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 26, 26);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            titleLabel = AddLabel(panel.transform, "", theme != null ? theme.connectionLostTitleSize : 34f,
                                  theme != null ? theme.connectionLostTitleColor : new Color(1f, 0.55f, 0.35f, 1f));
            titleLabel.fontStyle = FontStyles.Bold;
            bodyLabel = AddLabel(panel.transform, "", theme != null ? theme.connectionLostBodySize : 22f,
                                 theme != null ? theme.textColor : Color.white);

            lostButtons = AddButtonRow(panel.transform, "Lost buttons");
            rejoinButton = AddButton(lostButtons.transform, Text(t => t.connectionLostRejoinButton, "Rejoin"),
                theme != null ? theme.rejoinButtonColor : new Color(0.16f, 0.45f, 0.25f, 0.95f), () => RejoinClicked?.Invoke());
            AddButton(lostButtons.transform, Text(t => t.connectionLostLeaveButton, "Leave"),
                theme != null ? theme.barTrackColor : new Color(0.18f, 0.18f, 0.2f, 1f), () => LeaveClicked?.Invoke());

            okButtons = AddButtonRow(panel.transform, "OK buttons");
            AddButton(okButtons.transform, Text(t => t.rejoinFailedOkButton, "OK"),
                theme != null ? theme.rejoinButtonColor : new Color(0.16f, 0.45f, 0.25f, 0.95f), () => OkClicked?.Invoke());
        }

        private TextMeshProUGUI AddLabel(Transform parent, string text, float size, Color color)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = "Label";
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (theme != null && theme.font != null)
                tmp.font = theme.font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static GameObject AddButtonRow(Transform parent, string name)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            // Every flag off, each button carries its own LayoutElement size - the recipe QuitConfirmPanel found the hard way
            // (a row that force-expands its height collapses to 0).
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        private Button AddButton(Transform parent, string label, Color fill, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            Vector2 size = theme != null ? theme.rejoinButtonSize : new Vector2(260f, 52f);
            go.GetComponent<RectTransform>().sizeDelta = size;
            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            go.GetComponent<Image>().color = fill;

            TextMeshProUGUI buttonLabel = go.GetComponentInChildren<TextMeshProUGUI>();
            buttonLabel.text = label;
            if (theme != null)
            {
                if (theme.font != null)
                    buttonLabel.font = theme.font;
                buttonLabel.fontSize = theme.bodyTextSize;
                buttonLabel.color = theme.textColor;
                ApplyOutline(buttonLabel);
            }

            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        private void ApplyOutline(TextMeshProUGUI tmp)
        {
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
        }
    }
}
