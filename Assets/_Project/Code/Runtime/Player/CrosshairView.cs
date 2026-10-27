using Overpower.Data;
using Overpower.UI;
using Overpower.Weapons;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crosshair that replaces the arrow during play: a small centre hexagon inside a spread hexagon that opens with the gun's
/// current spread, spins faster the faster you fire and runs white to red with the heat meter, plus a hexagon that fills while a
/// charging gun charges. Wherever a click would not fire (the shop, a menu, the chat, dead) it hides and the arrow comes back, by
/// the router's own gate (PlayerInputRouter.PointerFireBlocked).
///
/// OWNER ONLY, no networking. Its canvas has no GraphicRaycaster and nothing on it is a raycast target: pointerOverUi would
/// otherwise always be true under the crosshair and block every shot. Every size and colour is on UiTheme, Crosshair.
/// </summary>
public class CrosshairView : MonoBehaviourPun
{
    [SerializeField, Tooltip("Colours and sizes the crosshair draws with - the same UiTheme asset the HUD and the aim cone read.")]
    private UiTheme theme;

    internal sealed class Parts
    {
        public GameObject canvasObject;
        public Canvas canvas;
        public RectTransform reticle;
        public GameObject chargeRoot;
        public Image chargeFill;
        public RectTransform spreadHex;
        public HexRingGraphic spreadLine;
        public HexRingGraphic spreadEdge;
    }

    private PlayerAim aim;
    private WeaponFiring weaponFiring;
    private PlayerInputRouter input;
    private PlayerOverheat overheat;
    private Parts parts;
    private float radius;
    private float shotRate;
    private float spinAngle;
    private float seenShotTime = float.NegativeInfinity;
    private bool hidingCursor;

    private void Awake()
    {
        aim = GetComponent<PlayerAim>();
        weaponFiring = GetComponent<WeaponFiring>();
        input = GetComponent<PlayerInputRouter>();
        overheat = GetComponent<PlayerOverheat>();

        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        // Loud: a silent null leaves the arrow with no crosshair and no clue why.
        if (theme == null || aim == null || weaponFiring == null || input == null)
        {
            Debug.LogError($"[CrosshairView] {name}: missing " +
                           (theme == null ? "UI Theme" : aim == null ? "PlayerAim" : weaponFiring == null ? "WeaponFiring" : "PlayerInputRouter") +
                           " - the crosshair will not be drawn and the arrow stays.");
            enabled = false;
            return;
        }

        parts = BuildCanvas(theme, transform);
        radius = theme.crosshairRestRadius;
        parts.canvasObject.SetActive(false);
    }

    private void LateUpdate()
    {
        WeaponDefinition weapon = weaponFiring.Weapon;
        if (!CrosshairRules.ShowCrosshair(input.PointerFireBlocked, weapon != null))
        {
            Conceal();
            return;
        }
        Reveal();

        // Under SetAimOverride the OS cursor is not where the aim is, so the crosshair follows the aim point instead.
        Camera cam = Camera.main;
        Vector2 screen = aim.HasAimOverride && cam != null
            ? (Vector2)cam.WorldToScreenPoint(aim.GroundPointUnderCursor)
            : (Vector2)Input.mousePosition;
        float scale = parts.canvas.scaleFactor > 0f ? parts.canvas.scaleFactor : 1f;
        parts.reticle.anchoredPosition = screen / scale;

        float outerHalf = CrosshairRules.OuterHalfAngle(weapon.Simultaneous, weapon.SpreadDegrees, aim.EffectiveConeAngle);
        float target = CrosshairRules.ReticleRadius(outerHalf, theme.crosshairRestRadius, theme.crosshairUnitsPerDegree, theme.crosshairMaxRadius);
        radius = CrosshairRules.Approach(radius, target, Time.deltaTime, theme.crosshairRadiusSmoothSeconds);

        bool shot = weaponFiring.LastShotTime != seenShotTime;
        seenShotTime = weaponFiring.LastShotTime;
        shotRate = CrosshairRules.ShotRate(shotRate, shot, Time.deltaTime, theme.crosshairShotRateWindowSeconds);
        float spin = CrosshairRules.SpinDegreesPerSecond(shotRate, theme.crosshairSpinIdleDegreesPerSecond, theme.crosshairSpinDegreesPerShot,
                                                         theme.crosshairSpinMaxDegreesPerSecond);
        spinAngle = CrosshairRules.SpinAngle(spinAngle, spin, Time.deltaTime);
        // The one heat meter every gun and ability fills (OverheatState): red warns that the gun is about to lock.
        float heat = overheat != null ? CrosshairRules.HeatShown(overheat.Normalised, overheat.IsSilenced) : 0f;
        Color ringColour = CrosshairRules.HeatColor(heat, theme.crosshairHeatColdColor, theme.crosshairHeatWarmColor,
                                                    theme.crosshairHeatHotColor, theme.crosshairHeatFullColor, theme.crosshairHeatFullAt);
        DrawSpreadRing(parts, radius, spinAngle, ringColour, theme.crosshairRingLineWidth, Mathf.Max(0f, theme.crosshairOutlineWidth));

        bool charging = CrosshairRules.IsCharging(weaponFiring.ChargeHeld, weapon.MaxChargeSeconds);
        if (charging)
        {
            float fill = CrosshairRules.ChargeFill(charging, weaponFiring.CurrentChargeFraction);
            parts.chargeFill.fillAmount = fill;
            parts.chargeFill.color = fill >= 1f ? theme.crosshairChargeFullColor : theme.crosshairChargeFillColor;
        }
        parts.chargeRoot.SetActive(charging);
    }

    private void Reveal()
    {
        if (!parts.canvasObject.activeSelf)
            parts.canvasObject.SetActive(true);
        Cursor.visible = false;
        hidingCursor = true;
    }

    // Only the instance that hid the arrow gives it back: a remote player's copy being destroyed must not show the arrow
    // under a live crosshair.
    private void Conceal()
    {
        if (parts != null && parts.canvasObject.activeSelf)
            parts.canvasObject.SetActive(false);
        if (!hidingCursor)
            return;
        Cursor.visible = true;
        hidingCursor = false;
    }

    private void OnDisable() => Conceal();

    /// <summary>The crosshair's canvas, built in code under <paramref name="parent"/>. Each part is a dark copy (UiTheme Crosshair
    /// Outline) behind a bright one, so it reads on a bright floor as well as a dark one.</summary>
    internal static Parts BuildCanvas(UiTheme theme, Transform parent)
    {
        var p = new Parts();
        p.canvasObject = new GameObject("Crosshair Canvas", typeof(RectTransform));
        p.canvasObject.transform.SetParent(parent, false);
        p.canvas = p.canvasObject.AddComponent<Canvas>();
        p.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        p.canvas.overrideSorting = true;
        p.canvas.sortingOrder = UiLayers.Crosshair;
        var scaler = p.canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = theme.referenceResolution;
        scaler.matchWidthOrHeight = theme.matchWidthOrHeight;

        p.reticle = NewRect("Reticle", p.canvasObject.transform, Vector2.zero);
        p.reticle.anchorMin = Vector2.zero;
        p.reticle.anchorMax = Vector2.zero;

        float outline = Mathf.Max(0f, theme.crosshairOutlineWidth);

        p.chargeRoot = NewRect("Charge Hex", p.reticle, Vector2.zero).gameObject;
        HexOutlined(p.chargeRoot.transform, "Charge", theme.crosshairChargeHexSize, theme.crosshairChargeLineWidth, outline,
                    theme.crosshairOutlineColor, theme.crosshairChargeTrackColor);
        p.chargeFill = Hex(p.chargeRoot.transform, "Charge Fill", theme.crosshairChargeHexSize, theme.crosshairChargeLineWidth, theme.crosshairChargeFillColor);
        p.chargeFill.type = Image.Type.Filled; // after the sprite: a Filled Image without one ignores fillAmount
        p.chargeFill.fillMethod = Image.FillMethod.Radial360;
        p.chargeFill.fillOrigin = (int)Image.Origin360.Top;
        p.chargeFill.fillClockwise = true;
        p.chargeFill.fillAmount = 0f;
        p.chargeRoot.SetActive(false);

        HexOutlined(p.reticle, "Centre", theme.crosshairCentreSize, theme.crosshairCentreLineWidth, outline,
                    theme.crosshairOutlineColor, theme.crosshairColor);

        p.spreadHex = NewRect("Spread Hex", p.reticle, Vector2.zero);
        if (outline > 0f)
            p.spreadEdge = Ring(p.spreadHex, "Spread Edge", theme.crosshairOutlineColor);
        p.spreadLine = Ring(p.spreadHex, "Spread Line", theme.crosshairHeatColdColor);
        DrawSpreadRing(p, theme.crosshairRestRadius, 0f, theme.crosshairHeatColdColor, theme.crosshairRingLineWidth, outline);
        return p;
    }

    /// <summary>Every frame: no allocation (CrosshairWiringTests). The dark edge reaches <paramref name="outline"/> past the line on both sides.</summary>
    internal static void DrawSpreadRing(Parts p, float apothem, float angleDegrees, Color colour, float lineWidth, float outline)
    {
        p.spreadHex.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        p.spreadLine.SetShape(apothem, lineWidth);
        p.spreadLine.color = colour;
        if (p.spreadEdge != null)
            p.spreadEdge.SetShape(apothem - outline, lineWidth + 2f * outline);
    }

    private static RectTransform NewRect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        return rt;
    }

    private static Image Bar(Transform parent, string name, Vector2 size, Color color)
    {
        Image image = NewRect(name, parent, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static HexRingGraphic Ring(Transform parent, string name, Color color)
    {
        HexRingGraphic ring = NewRect(name, parent, Vector2.zero).gameObject.AddComponent<HexRingGraphic>();
        ring.color = color;
        ring.raycastTarget = false;
        return ring;
    }

    private static Image Hex(Transform parent, string name, float size, float lineWidth, Color color)
    {
        float safeSize = Mathf.Max(1f, size);
        Image image = Bar(parent, name, new Vector2(safeSize, safeSize), color);
        image.sprite = GeneratedSprites.BuildHexRing(lineWidth / safeSize);
        return image;
    }

    // The dark copy's sides sit `outline` further out and its line is `outline` thicker on each side; a hexagon's corner-to-corner
    // width grows by 2 x outline / cos 30 when every side moves out by outline.
    private static void HexOutlined(Transform parent, string name, float size, float lineWidth, float outline, Color edge, Color line)
    {
        if (outline > 0f)
            Hex(parent, name + " Edge", size + 2f * outline / 0.8660254f, lineWidth + 2f * outline, edge);
        Hex(parent, name + " Line", size, lineWidth, line);
    }
}
