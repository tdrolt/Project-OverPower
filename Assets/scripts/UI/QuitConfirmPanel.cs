using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Escape asks "Close the game?": a scene-level runtime component (one instance, like TestRangePanel and
    /// ConnectionLostPanel), a clickable overlay canvas with a GraphicRaycaster built in code. A raw Keyboard.current
    /// poll, not an InputAction (a tool key, like every other Escape consumer here).
    /// OWNERSHIP: LoadoutScreen and the chat panel close THEMSELVES on Escape in the same frame and Update() order
    /// is not fixed, so this asks EscapeOwnershipRule (this frame OR the last). While open it claims tool focus on the
    /// local player's PlayerInputRouter (no moving or shooting while deciding; the match keeps running), the
    /// "resolve the local player, claim/release keyed by this" recipe TestRangePanel uses.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuitConfirmPanel : MonoBehaviour
    {
        [Tooltip("Read for quitPromptText/quitYesText/quitNoText.")]
        [SerializeField] private UiTheme theme;

        private GameObject uiRoot;
        private TextMeshProUGUI promptLabel;
        private bool visible;

        // One shared TMP material for both button labels (as ConnectionLostPanel.ApplyOutline/PlayerHud.AddLabel):
        // otherwise TMP clones one per label the moment its outline is touched.
        private Material textMaterial;

        private bool shopOpenLastFrame;
        private bool chatOpenLastFrame;
        private bool pageOpenLastFrame;

        private GameObject cachedLocalPlayer;
        private PlayerInputRouter claimedRouter;

        private void Awake()
        {
            BuildUi();
        }

        private void OnDisable()
        {
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private void OnDestroy()
        {
            if (textMaterial != null)
                Destroy(textMaterial);
        }

        private void Update()
        {
            bool shopOpenNow = LoadoutScreen.IsOpen;
            bool chatOpenNow = PhotonChat.IsOpen;
            bool pageOpenNow = Overpower.UI.LobbyOverlayPanel.AnyPageOpen; // How to play / mode info close themselves on Escape
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!visible)
            {
                if (escapePressed && !EscapeOwnershipRule.BelongsToShopOrChat(shopOpenNow, shopOpenLastFrame, chatOpenNow, chatOpenLastFrame, pageOpenNow, pageOpenLastFrame))
                    Open();
            }
            else if (escapePressed)
            {
                Close();
            }

            shopOpenLastFrame = shopOpenNow;
            chatOpenLastFrame = chatOpenNow;
            pageOpenLastFrame = pageOpenNow;
        }

        private void Open()
        {
            visible = true;
            if (promptLabel != null && theme != null)
                promptLabel.text = theme.quitPromptText;
            uiRoot.SetActive(true);

            PlayerInputRouter router = ResolveLocalPlayer()?.GetComponentInChildren<PlayerInputRouter>(true);
            if (router != claimedRouter)
            {
                claimedRouter?.SetToolFocus(this, false);
                claimedRouter = router;
            }
            claimedRouter?.SetToolFocus(this, true);
        }

        private void Close()
        {
            visible = false;
            uiRoot.SetActive(false);
            claimedRouter?.SetToolFocus(this, false);
            claimedRouter = null;
        }

        private GameObject ResolveLocalPlayer()
        {
            if (cachedLocalPlayer != null)
                return cachedLocalPlayer;

            if (PhotonNetwork.LocalPlayer == null)
                return null;

            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            cachedLocalPlayer = view != null ? view.gameObject : null;
            return cachedLocalPlayer;
        }

        // ---- Yes / No ----

        /// <summary>NEVER call this from an automated verification pass - it stops Play Mode in the
        /// Editor (or quits the built Player). Check this wiring by reading it instead.</summary>
        private void OnYesClicked() => GameQuit.Quit();

        private void OnNoClicked() => Close();

        // ---- UI construction (TestRangePanel.BuildUi/AddButton's own recipe) ----

        private void BuildUi()
        {
            var canvasGo = new GameObject("Quit Confirm Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 700; // Above the F1 test range panel (500) - a decision to close the game must never be hidden behind a debug tool.
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            uiRoot = canvasGo;

            GameObject dim = new GameObject("Dim", typeof(RectTransform));
            dim.transform.SetParent(canvasGo.transform, false);
            Image dimImage = dim.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.55f);
            RectTransform dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasGo.transform, false);
            panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0.5f, 0.5f);
            // Wide enough for two matchStartButtonSize buttons plus the row's own spacing.
            panelRt.sizeDelta = new Vector2(600f, 0f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = true;
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var res = new TMP_DefaultControls.Resources();
            promptLabel = AddLabel(panel.transform, theme != null ? theme.quitPromptText : "Close the game?");
            promptLabel.alignment = TextAlignmentOptions.Center;
            promptLabel.fontSize = 26f;

            GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            // All four off: a freshly-added HorizontalLayoutGroup defaults childForceExpandHeight to true, which
            // stretches the buttons to the ROW's height, and the row's height comes from its children, so it
            // collapsed to 0 (zero-height buttons). Each button keeps its own fixed size via a LayoutElement
            // (see AddButton).
            rowLayout.childControlWidth = rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            // Real buttons (filled, not the bare default sprite): the size/font/outline recipe of WarmupBar's End
            // warm-up button, copied rather than shared because that builder is private to its canvas. Yes takes the
            // Start button's green (an affirmative action); No takes Bar Track Colour, the neutral fill Loadout's
            // buttons reuse (see UiTheme.matchStartButtonColor's tooltip); no new UiTheme token.
            AddButton(buttonRow.transform, res, theme != null ? theme.quitYesText : "Yes",
                theme != null ? theme.matchStartButtonColor : new Color(0.16f, 0.45f, 0.25f, 0.95f), OnYesClicked);
            AddButton(buttonRow.transform, res, theme != null ? theme.quitNoText : "No",
                theme != null ? theme.barTrackColor : new Color(0.18f, 0.18f, 0.2f, 1f), OnNoClicked);

            uiRoot.SetActive(false);
        }

        private static TextMeshProUGUI AddLabel(Transform parent, string text)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        /// <summary>A real button: filled background sized from the Start button's UiTheme tokens; the label's
        /// font/size/outline come from this panel's theme and textMaterial.</summary>
        private void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label, Color fillColor,
                                UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            Vector2 size = theme != null ? theme.matchStartButtonSize : new Vector2(260f, 52f);
            go.GetComponent<RectTransform>().sizeDelta = size;
            // The row's HorizontalLayoutGroup never resizes children (all flags off, see BuildUi), but it still needs
            // a LayoutElement for ITS OWN preferred height: sizeDelta alone measured 0, since Unity reads preferred
            // size from an ILayoutElement, not the RectTransform, once a LayoutGroup is involved.
            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            go.GetComponent<Image>().color = fillColor;

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
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // Same fix as every other code-built control here - see TestRangePanel.AddDropdown's comment.
        }

        /// <summary>Same reasoning as ConnectionLostPanel.ApplyOutline: one shared Material instance for
        /// every button label this panel builds, instead of letting TMP auto-clone one per label.</summary>
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
