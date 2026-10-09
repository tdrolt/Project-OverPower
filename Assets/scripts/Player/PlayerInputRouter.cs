using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Overpower.Match;
using TMPro;

/// <summary>
/// The one place gameplay input enters the game. Reads the OverpowerControls actions asset and
/// exposes each of the nine design-doc bindings as a property/event pair; nothing else should call
/// Input or an InputAction directly.
///
/// Deliberately NOT IPunObservable: see PlayerNetSync's class comment.
/// </summary>
public class PlayerInputRouter : MonoBehaviour
{
    [SerializeField, Tooltip("The Input System actions asset holding the Gameplay map. Rebinding " +
             "a key is an edit to this asset (Window > Analysis > Input Actions), never an edit to " +
             "this script.")]
    private InputActionAsset controls;

    private PhotonView photonView;
    private PlayerLifecycle playerLifecycle;
    private InputActionMap gameplayMap;
    private InputAction moveAction, primaryAction, attachmentAction, ultimateAction;
    private InputAction mobilityAction, mapAction, shopAction, scoreboardAction, ventAction;

    // Mirrors PlayerLifecycle.IsAlive, updated once per AliveChanged instead of polled per read.
    private bool isAlive = true;

    // Every tool that currently wants gameplay input suppressed, keyed by the caller (SetToolFocus).
    // A HashSet, not one bool: with a shared bool, opening the loadout screen while F1 was open and
    // then closing it would unlock input under the still-open F1 panel. Keyed by owner, each tool
    // can only release its OWN claim.
    private readonly HashSet<object> toolFocusOwners = new HashSet<object>();

    // Whether the cursor sits over a raycast-target Graphic on a canvas with a GraphicRaycaster.
    // Cached once a frame in Update, not read inside the Input System callbacks, because
    // EventSystem.IsPointerOverGameObject() logs a warning outside a MonoBehaviour message. The HUD
    // canvas has no GraphicRaycaster, so hovering its bars never sets this; only a real clickable
    // panel (F1, the loadout screen) does.
    private bool pointerOverUi;

    /// <summary>Raw WASD, not camera-relative: PlayerMotor converts, since that depends on the camera rig.</summary>
    public Vector2 MoveAxis => gameplayMap != null && !InputSuppressed ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    // Each of the four ability slots has both a polled Held property and an edge event on purpose: a
    // charge weapon reads Held every frame, a dash only cares about the instant of press.
    //
    // Primary and Attachment also gate on !pointerOverUi: both are mouse buttons, so they can land on
    // a UI click; the other two Held properties are keys and cannot.
    public bool PrimaryHeld => gameplayMap != null && !InputSuppressed && !pointerOverUi && primaryAction.IsPressed();
    public bool AttachmentHeld => gameplayMap != null && !InputSuppressed && !pointerOverUi && attachmentAction.IsPressed();
    public bool UltimateHeld => gameplayMap != null && !InputSuppressed && ultimateAction.IsPressed();
    public bool MobilityHeld => gameplayMap != null && !InputSuppressed && mobilityAction.IsPressed();

    public event System.Action PrimaryPressed;
    public event System.Action PrimaryReleased;
    public event System.Action AttachmentPressed;
    public event System.Action UltimatePressed;
    public event System.Action MobilityPressed;
    public event System.Action MobilityReleased;

    /// <summary>The overheat Vent button (R), gated like MobilityPressed (Emit/InputSuppressed).
    /// PlayerOverheat is the only subscriber (owner only).</summary>
    public event System.Action VentPressed;

    // Scoreboard, Shop and Map each have their own, narrower emit gate - see EmitScoreboardPressed,
    // ShopSuppressed and MapSuppressed.
    public event System.Action MapToggled;
    public event System.Action ShopToggled;
    public event System.Action ScoreboardPressed;
    public event System.Action ScoreboardReleased;

    /// <summary>True while dead, typing into a text field (chat), or while any tool has claimed focus
    /// (SetToolFocus). Everything above except ShopToggled and MapToggled is gated on this being false;
    /// those two have narrower gates (ShopSuppressed, MapSuppressed).
    ///
    /// Deliberately NOT gated on PlayerOverheat.CanAct: overheat silencing is an ability-level rule the
    /// ability system enforces, and an overheated player must still be able to walk away.</summary>
    public bool InputSuppressed => !isAlive || IsTypingInChat() || toolFocusOwners.Count > 0;

    /// <summary>Only typing in chat blocks Shop (a player waiting to respawn can shop, D4). A tool holding
    /// general focus must not: P is how the loadout screen (itself a focus owner) closes, and F1 being open
    /// must not swallow it, or closing the loadout while F1 holds focus would fail.</summary>
    private bool ShopSuppressed => IsTypingInChat();

    /// <summary>MapToggled's narrowest gate: M must work while dead and while the loadout screen holds
    /// focus, because opening the large map closes the P screen. Only typing blocks it; otherwise a large
    /// map opened before death could not be closed until respawn.</summary>
    private bool MapSuppressed => IsTypingInChat();

    /// <summary>
    /// Input lock for anything that is a tool rather than gameplay (F1 panel, loadout screen), so a click
    /// on it cannot also fire the weapon underneath. owner is the claiming MonoBehaviour (see
    /// toolFocusOwners). Safe with hasFocus: false for an owner that never claimed, and safe to call
    /// twice with the same value.
    /// </summary>
    public void SetToolFocus(object owner, bool hasFocus)
    {
        if (owner == null)
            return;

        if (hasFocus)
            toolFocusOwners.Add(owner);
        else
            toolFocusOwners.Remove(owner);
    }

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        playerLifecycle = GetComponent<PlayerLifecycle>();

        if (controls == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset is not assigned - this player has no input.");
            return;
        }

        // Every player prefab instance (9 in a 3v3v3 match) shares the same serialized asset unless it
        // is cloned here; otherwise Enable()/Disable() would race across all routers on one shared map
        // and whichever OnEnable ran last would decide whether the LOCAL player's input worked.
        controls = Instantiate(controls);
        gameplayMap = controls.FindActionMap("Gameplay", throwIfNotFound: false);
        if (gameplayMap == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset has no 'Gameplay' action map.");
            return;
        }

        moveAction = gameplayMap.FindAction("Move"); primaryAction = gameplayMap.FindAction("Primary");
        attachmentAction = gameplayMap.FindAction("Attachment"); ultimateAction = gameplayMap.FindAction("Ultimate");
        mobilityAction = gameplayMap.FindAction("Mobility"); mapAction = gameplayMap.FindAction("ExpandMap");
        shopAction = gameplayMap.FindAction("Shop"); scoreboardAction = gameplayMap.FindAction("Scoreboard");
        ventAction = gameplayMap.FindAction("Vent");

        if (moveAction == null || primaryAction == null || attachmentAction == null || ultimateAction == null ||
            mobilityAction == null || mapAction == null || shopAction == null || scoreboardAction == null ||
            ventAction == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Gameplay map is missing one or more of the " +
                            "nine expected actions - check OverpowerControls.inputactions.");
            gameplayMap = null; // marks setup as failed; every property above checks it
            return;
        }

        // Button actions with no interaction go Started -> Performed on press (same frame) and
        // Canceled on release, so started/canceled are the clean press/release edges.
        // Primary/Attachment PRESSED go through EmitPointerGated (see that method). Release stays on
        // plain Emit: a click that opened a tool fired no press, so there is nothing for a release to
        // wrongly end, and gating it would risk a stuck-held weapon if the pointer were over UI on
        // the frame the button came up.
        primaryAction.started += _ => EmitPointerGated(PrimaryPressed); primaryAction.canceled += _ => Emit(PrimaryReleased);
        attachmentAction.started += _ => EmitPointerGated(AttachmentPressed); ultimateAction.started += _ => Emit(UltimatePressed);
        mobilityAction.started += _ => Emit(MobilityPressed); mobilityAction.canceled += _ => Emit(MobilityReleased);
        mapAction.started += _ => EmitMap(); shopAction.started += _ => EmitShop();
        scoreboardAction.started += _ => EmitScoreboardPressed(); scoreboardAction.canceled += _ => ScoreboardReleased?.Invoke();
        ventAction.started += _ => Emit(VentPressed);
    }

    private void OnEnable()
    {
        if (gameplayMap == null)
            return; // Awake already logged why

        // Only the local player reads input: a remote copy's map is disabled outright so the Input
        // System stops polling devices for it.
        if (photonView.IsMine)
        {
            gameplayMap.Enable();
            if (playerLifecycle != null)
            {
                isAlive = playerLifecycle.IsAlive;
                playerLifecycle.AliveChanged += HandleAliveChanged;
            }
        }
        else
        {
            gameplayMap.Disable();
        }
    }

    private void OnDisable()
    {
        gameplayMap?.Disable(); // entering/leaving play mode must not leak an enabled map
        if (playerLifecycle != null)
            playerLifecycle.AliveChanged -= HandleAliveChanged;
    }

    /// <summary>A UI click and a mouse-button gameplay action are the SAME physical click: a "Loadout (P)"
    /// HUD button press reached the router as a Primary press too and fired the weapon before the button's
    /// onClick claimed tool focus. Hence pointerOverUi, read here once a frame rather than inside the Input
    /// System callback (IsPointerOverGameObject() warns outside a MonoBehaviour message). Only meaningful
    /// for the local player.</summary>
    private void Update()
    {
        // Only the local player's clicks can be a UI click on THIS machine; skips the redundant
        // EventSystem queries for the other copies in a full room.
        if (photonView != null && !photonView.IsMine)
            return;

        pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void HandleAliveChanged(bool alive) => isAlive = alive;

    private void Emit(System.Action evt)
    {
        if (!InputSuppressed)
            evt?.Invoke();
    }

    /// <summary>Primary/Attachment's emit path: both are mouse buttons, so both can land on a UI click
    /// before its onClick has claimed tool focus (see Update). Only a clickable panel (F1, the loadout
    /// screen) blocks firing; the HUD canvas has no GraphicRaycaster.</summary>
    private void EmitPointerGated(System.Action evt)
    {
        if (!InputSuppressed && !pointerOverUi)
            evt?.Invoke();
    }

    /// <summary>ShopToggled's own emit path (see ShopSuppressed).</summary>
    private void EmitShop()
    {
        if (!ShopSuppressed)
            ShopToggled?.Invoke();
    }

    /// <summary>ScoreboardPressed's own emit path (D12): only typing in chat blocks it (ScoreboardRules.MayOpen).
    /// The release is never gated, so a board opened just before typing or dying can always close.</summary>
    private void EmitScoreboardPressed()
    {
        if (ScoreboardRules.MayOpen(IsTypingInChat()))
            ScoreboardPressed?.Invoke();
    }

    private void EmitMap()
    {
        if (!MapSuppressed)
            MapToggled?.Invoke();
    }

    /// <summary>True the instant a text field takes focus (the chat box is a TMP_InputField), so you
    /// cannot fire while typing. Covers the legacy InputField too. Public because BugMarkerKey shares
    /// this check to refuse Ctrl+B while typing.</summary>
    public static bool IsTypingInChat()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
    }
}
