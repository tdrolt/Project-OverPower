using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Playtest extras P6 (2026-09-26): Escape asks "Close the game?" - a runtime, scene-level
    /// component (one instance, like TestRangePanel/MatchStartPanel, not per-player), built the same
    /// way: a clickable overlay canvas with a GraphicRaycaster, controls built in code so every
    /// listener sits on the same line as the button it belongs to.
    ///
    /// A raw Keyboard.current poll, not an InputAction - a tool key, matching every other Escape
    /// consumer in this project (LoadoutScreen.cs:390-394, chatmanager.cs's own Escape block, F1).
    ///
    /// OWNERSHIP: LoadoutScreen and the chat panel both close THEMSELVES on Escape, in the same
    /// frame - Update() order between components is not fixed, so this checks EscapeOwnershipRule
    /// (this frame OR the frame before) rather than just "is the shop/chat open right now". See that
    /// rule's own class comment.
    ///
    /// While open this claims general tool focus on the local player's own PlayerInputRouter (no
    /// moving or shooting while deciding - the match keeps running, it's multiplayer), the same
    /// "resolve the local player, claim/release keyed by this" recipe TestRangePanel already uses for
    /// exactly this reason.
    /// </summary>
    [DisallowMultipleComponent]
    public class QuitConfirmPanel : MonoBehaviour
    {
        [Tooltip("Read for quitPromptText/quitYesText/quitNoText.")]
        [SerializeField] private UiTheme theme;

        private GameObject uiRoot;
        private TextMeshProUGUI promptLabel;
        private bool visible;

        private bool shopOpenLastFrame;
        private bool chatOpenLastFrame;

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

        private void Update()
        {
            bool shopOpenNow = LoadoutScreen.IsOpen;
            bool chatOpenNow = PhotonChat.IsOpen;
            bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (!visible)
            {
                if (escapePressed && !EscapeOwnershipRule.BelongsToShopOrChat(shopOpenNow, shopOpenLastFrame, chatOpenNow, chatOpenLastFrame))
                    Open();
            }
            else if (escapePressed)
            {
                Close();
            }

            shopOpenLastFrame = shopOpenNow;
            chatOpenLastFrame = chatOpenNow;
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
            panelRt.sizeDelta = new Vector2(480f, 0f);

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
            rowLayout.childControlWidth = rowLayout.childForceExpandWidth = true;

            AddButton(buttonRow.transform, res, theme != null ? theme.quitYesText : "Yes", OnYesClicked);
            AddButton(buttonRow.transform, res, theme != null ? theme.quitNoText : "No", OnNoClicked);

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

        private static void AddButton(Transform parent, TMP_DefaultControls.Resources res, string label,
                                       UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = TMP_DefaultControls.CreateButton(res);
            go.transform.SetParent(parent, false);
            go.GetComponentInChildren<TextMeshProUGUI>().text = label;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // Same fix as every other code-built control here - see TestRangePanel.AddDropdown's comment.
        }
    }
}
