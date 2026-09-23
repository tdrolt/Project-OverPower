using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The one place gameplay input enters the game. Reads the OverpowerControls actions asset and
/// exposes each of the nine design-doc bindings as a property/event pair - nothing else in the
/// project should call Input or an InputAction directly (Task 0.12, replacing the old per-script
/// Input.GetKey/GetAxisRaw polling).
///
/// Deliberately NOT IPunObservable: see PlayerNetSync's class comment for why a second observable
/// on this GameObject would silently corrupt the network wire format.
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
    private InputAction moveAction, primaryAction, equipmentAction, ultimateAction;
    private InputAction mobilityAction, mapAction, shopAction, scoreboardAction, ventAction;

    // Mirrors PlayerLifecycle.IsAlive rather than polling it on every property read - updated once
    // per AliveChanged event instead.
    private bool isAlive = true;

    // Every tool that currently wants gameplay input suppressed, keyed by the caller itself - see
    // SetToolFocus below. A HashSet rather than a single bool because Task 9 added a second tool
    // (the loadout screen) alongside the first (the F1 test range panel): with one shared bool,
    // opening the loadout screen while F1 was already open and then closing the loadout again
    // would call SetToolFocus(false) and unlock input right out from under the still-open F1
    // panel. Keying by owner means each tool can only ever release its OWN claim.
    private readonly HashSet<object> toolFocusOwners = new HashSet<object>();

    // Whether the cursor sits over a raycast-target Graphic on a canvas that has a
    // GraphicRaycaster - cached once a frame in Update rather than read inside the Input System
    // callbacks below, because EventSystem.IsPointerOverGameObject() logs an Input System warning
    // when called from outside a MonoBehaviour message (Playtest polish review, fix 1). The HUD's
    // own canvas has no GraphicRaycaster, so hovering its bars never sets this true - only a real
    // clickable panel (the F1 test range, the loadout screen) does.
    private bool pointerOverUi;

    /// <summary>Raw WASD, not camera-relative - PlayerMotor does that conversion, since it depends
    /// on the camera rig and not on input.</summary>
    public Vector2 MoveAxis => gameplayMap != null && !InputSuppressed ? moveAction.ReadValue<Vector2>() : Vector2.zero;

    // A polled Held property AND an edge event exist for each of the four ability slots on
    // purpose: a charge weapon reads Held every frame it is still holding the trigger, while a
    // dash only cares about the instant the button went down. One cannot substitute for the other.
    //
    // Primary and Equipment additionally gate on !pointerOverUi (fix 1) - both are mouse buttons
    // (see OverpowerControls.inputactions), and the other two Held properties are keyboard keys
    // that can never land on a UI click in the first place, so they do not need the same gate.
    public bool PrimaryHeld => gameplayMap != null && !InputSuppressed && !pointerOverUi && primaryAction.IsPressed();
    public bool EquipmentHeld => gameplayMap != null && !InputSuppressed && !pointerOverUi && equipmentAction.IsPressed();
    public bool UltimateHeld => gameplayMap != null && !InputSuppressed && ultimateAction.IsPressed();
    public bool MobilityHeld => gameplayMap != null && !InputSuppressed && mobilityAction.IsPressed();

    public event System.Action PrimaryPressed;
    public event System.Action PrimaryReleased;
    public event System.Action EquipmentPressed;
    public event System.Action UltimatePressed;
    public event System.Action MobilityPressed;
    public event System.Action MobilityReleased;

    /// <summary>The overheat Vent button (R) - gated exactly like MobilityPressed (see Emit/
    /// InputSuppressed): dead, typing in chat, or a tool holding focus all swallow it. PlayerOverheat
    /// is the only subscriber (owner only), and forwards it to OverheatState.TryVent().</summary>
    public event System.Action VentPressed;

    // Scoreboard still has nothing subscribed. Shop (LoadoutScreen) and Map (MinimapView) each have their own,
    // narrower emit gate - see ShopSuppressed and MapSuppressed.
    public event System.Action MapToggled;
    public event System.Action ShopToggled;
    public event System.Action ScoreboardPressed;
    public event System.Action ScoreboardReleased;

    /// <summary>True while dead, while the player is typing into a text field (e.g. chat), or while
    /// any tool has claimed focus (see SetToolFocus). Every property and event above except
    /// ShopToggled and MapToggled is gated on this being false - those two have their own, narrower
    /// gates (ShopSuppressed, MapSuppressed) so the P key can still close the loadout screen while
    /// that screen's own focus claim would otherwise block it, and so M still works while dead or
    /// while the loadout screen holds focus.
    ///
    /// Deliberately NOT gated on PlayerOverheat.CanAct - full overheat silencing the weapon and
    /// abilities is an ability-level rule the ability system enforces itself, not an input-level
    /// one, and the design wants an overheated player to still be able to walk away.</summary>
    public bool InputSuppressed => !isAlive || IsTypingInChat() || toolFocusOwners.Count > 0;

    /// <summary>Dead or typing in chat block Shop too, but a tool holding general focus must not -
    /// P is how the loadout screen (itself a tool focus owner) closes again, and F1 being open must
    /// not swallow a P press either (Verification 1: F1 open AND loadout open, closing the loadout
    /// must leave InputSuppressed true, which only works if opening/closing the loadout while F1
    /// holds focus works at all).</summary>
    private bool ShopSuppressed => !isAlive || IsTypingInChat();

    /// <summary>MapToggled's own, narrowest gate (controller amendment 5, capture ring + minimap spec,
    /// 2026-09-16/17): M must work while dead, and while the loadout screen holds tool focus, because opening the
    /// large map closes the P screen. Only typing blocks it - a dead player still has a map to look at; without
    /// this, a large map opened before death stayed open (nothing could close it) until respawn.</summary>
    private bool MapSuppressed => IsTypingInChat();

    /// <summary>
    /// General-purpose input lock for anything that is a tool rather than gameplay - the F1 test
    /// range panel and the loadout screen today, either of which must stop a click on itself from
    /// also firing the weapon underneath it. owner is whichever MonoBehaviour is claiming or
    /// releasing focus (typically "this" from the caller) - see toolFocusOwners' comment for why a
    /// single shared bool was not enough once a second tool existed. Safe to call with hasFocus:
    /// false for an owner that never claimed focus; safe to call twice with the same value.
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

        // Every player prefab instance in the scene (a 3v3v3 match renders 9 - yourself plus 8
        // others) shares the same serialized asset reference unless it is cloned here. Without
        // this, Enable()/Disable() below would race across all 9 routers on the one shared map:
        // whichever OnEnable ran last would decide whether the LOCAL player's input worked at all.
        controls = Instantiate(controls);
        gameplayMap = controls.FindActionMap("Gameplay", throwIfNotFound: false);
        if (gameplayMap == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Controls asset has no 'Gameplay' action map.");
            return;
        }

        moveAction = gameplayMap.FindAction("Move"); primaryAction = gameplayMap.FindAction("Primary");
        equipmentAction = gameplayMap.FindAction("Equipment"); ultimateAction = gameplayMap.FindAction("Ultimate");
        mobilityAction = gameplayMap.FindAction("Mobility"); mapAction = gameplayMap.FindAction("ExpandMap");
        shopAction = gameplayMap.FindAction("Shop"); scoreboardAction = gameplayMap.FindAction("Scoreboard");
        ventAction = gameplayMap.FindAction("Vent");

        if (moveAction == null || primaryAction == null || equipmentAction == null || ultimateAction == null ||
            mobilityAction == null || mapAction == null || shopAction == null || scoreboardAction == null ||
            ventAction == null)
        {
            Debug.LogError($"[PlayerInputRouter] {name}: Gameplay map is missing one or more of the " +
                            "nine expected actions - check OverpowerControls.inputactions.");
            gameplayMap = null; // Marks setup as failed; every property above already checks this.
            return;
        }

        // Button actions with no interaction assigned go Waiting -> Started -> Performed on press
        // (same frame) and Performed -> Canceled on release, so started/canceled are the clean
        // press/release edges below.
        // Primary/Equipment PRESSED go through EmitPointerGated, not Emit - see fix 1's comment on
        // that method. Release is deliberately left on plain Emit: a click that opened a tool
        // fires no press (blocked above), so there is nothing still "held" for a release to
        // wrongly end, and gating release too would risk a stuck-held weapon if the pointer were
        // over UI at the exact frame the button came up.
        primaryAction.started += _ => EmitPointerGated(PrimaryPressed); primaryAction.canceled += _ => Emit(PrimaryReleased);
        equipmentAction.started += _ => EmitPointerGated(EquipmentPressed); ultimateAction.started += _ => Emit(UltimatePressed);
        mobilityAction.started += _ => Emit(MobilityPressed); mobilityAction.canceled += _ => Emit(MobilityReleased);
        mapAction.started += _ => EmitMap(); shopAction.started += _ => EmitShop();
        scoreboardAction.started += _ => Emit(ScoreboardPressed); scoreboardAction.canceled += _ => Emit(ScoreboardReleased);
        ventAction.started += _ => Emit(VentPressed);
    }

    private void OnEnable()
    {
        if (gameplayMap == null)
            return; // Awake already logged why.

        // Only the local player should ever read input - a remote copy's map is disabled outright
        // rather than just ignored, so the Input System stops polling devices for it entirely.
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
        gameplayMap?.Disable(); // Entering/leaving play mode must not leak an enabled map behind.
        if (playerLifecycle != null)
            playerLifecycle.AliveChanged -= HandleAliveChanged;
    }

    /// <summary>Fix 1 (Playtest polish review): a UI click and a mouse-button gameplay action are
    /// the SAME physical click, so a "Loadout (P)" HUD button press reached PlayerInputRouter as a
    /// Primary press too, fired the equipped weapon (or Equipment, for a right-click control),
    /// then only afterwards did the button's own onClick claim tool focus and shut input off - by
    /// then the shot had already gone. Read here, once a frame, rather than inside the Input
    /// System callback that raises PrimaryPressed/EquipmentPressed: EventSystem.
    /// IsPointerOverGameObject() logs an Input System warning if it is called from outside a
    /// MonoBehaviour message. Only ever meaningful for the local player - gameplayMap is null (or
    /// disabled) for every other copy, so nothing reads this field for them.</summary>
    private void Update()
    {
        // Only the local player's clicks can ever be a UI click on THIS machine - see OnEnable's
        // own "only the local player should ever read input" reasoning. A remote copy's gameplayMap
        // is disabled anyway (nothing reads pointerOverUi for it), so this skips 8 redundant
        // EventSystem queries a frame in a full room rather than just one.
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

    /// <summary>Primary/Equipment's own emit path (fix 1) - both are mouse buttons (see
    /// OverpowerControls.inputactions), so both can land on a UI click before that click's own
    /// onClick has claimed tool focus (see Update's comment above for the concrete "Loadout (P)"
    /// case). IsPointerOverGameObject() only ever returns true for a raycast-target Graphic on a
    /// canvas that carries a GraphicRaycaster - the HUD's own canvas has none, so hovering its bars
    /// never blocks firing, only an actual clickable panel (F1, the loadout screen) does.</summary>
    private void EmitPointerGated(System.Action evt)
    {
        if (!InputSuppressed && !pointerOverUi)
            evt?.Invoke();
    }

    /// <summary>ShopToggled's own emit path - see ShopSuppressed's comment for why this cannot
    /// reuse Emit/InputSuppressed above.</summary>
    private void EmitShop()
    {
        if (!ShopSuppressed)
            ShopToggled?.Invoke();
    }

    /// <summary>MapToggled's own emit path - see MapSuppressed.</summary>
    private void EmitMap()
    {
        if (!MapSuppressed)
            MapToggled?.Invoke();
    }

    /// <summary>Suppresses input the instant a text field takes focus - e.g. the chat box under
    /// "chat manager" (a TMP_InputField); without this you can fire your weapon while typing a
    /// message. Also covers the legacy UI InputField in case a future menu uses one.</summary>
    private static bool IsTypingInChat()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        return selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null;
    }
}
