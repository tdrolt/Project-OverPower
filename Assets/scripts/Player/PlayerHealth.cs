using Photon.Pun;
using Photon.Pun.UtilityScripts;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;

/// <summary>
/// One player's health, armor and damage funnel: ApplyDamage is the only place damage maths happens.
/// Deliberately NOT IPunObservable: the PhotonView auto-finds observables on children too, so a
/// second one here would silently add a second serialization block. PlayerNetSync is the sole
/// observable and reads/writes through the members below.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private GameplayConfig gameplayConfig;
    [SerializeField] private ArmorConfig armorConfig;

    [SerializeField, Tooltip("Shared per-tier numbers, used here for health regen (Task 2.3): the " +
             "rate you heal while standing in a zone your own team owns. The same asset every tower " +
             "and GoldWallet point at - one home for territory numbers.")]
    private TerritoryConfig territoryConfig;

    [SerializeField, Tooltip("The health fill on the overhead HealthBarCanvas - drawn first, so " +
             "the shield fill can sit on top of it. A plain filled Image, not a Slider: Task 6 " +
             "dropped the Slider (it cannot cleanly draw a second fill over its own) in favour of " +
             "two Images PlayerHealth drives directly. Fraction is health / max health.")]
    private Image healthFillImage;

    [SerializeField, Tooltip("The shield (armor) fill on the overhead HealthBarCanvas, same rect " +
             "as the health fill but drawn AFTER it in the hierarchy so it renders on top - " +
             "Tudor's call [T]: one bar, shield drawn over health, each against its own max. A " +
             "full shield hides max HP on purpose. Fraction is armor / armor capacity.")]
    private Image shieldFillImage;

    [SerializeField, Tooltip("The static backing behind both fills on the overhead HealthBarCanvas. " +
             "Themed from code too (colour + sprite) so a retune never needs reopening the prefab.")]
    private Image overheadTrackImage;

    [SerializeField, Tooltip("Shared colours and bar sprite.")]
    private UiTheme theme;

    // The overhead bar's own fraction of the last value it was WRITTEN with, not the health/armor
    // value itself - so ApplyDamage, SetArmorLevels, SetHealthFromNetwork and the per-frame armor
    // recharge tick can all funnel through UpdateOverheadBar without that tick rewriting
    // Image.fillAmount 60 times a second while armor sits at full or empty. -1 so the very first
    // call (Awake) always writes, even though a fresh spawn's fraction is often 1.
    private float lastHealthFraction = -1f;
    private float lastShieldFraction = -1f;

    // The whole overhead bar's root GameObject ("Bar", the shared parent of the three Images above
    // on HealthBarCanvas) - cached in Awake from healthFillImage's own parent rather than a new
    // serialized field, so wiring this needs no prefab edit. Deliberately NOT HealthBarCanvas
    // itself: that canvas also carries the floating name text (PlayerNameTag), which must stay
    // visible over a corpse. See SetOverheadBarVisible.
    private GameObject overheadBarRoot;

    // The yellow "shield immunity" look is a FRAME (four thin edge Images) round the shared health/shield rect: not a
    // recolour of either fill and not a translucent whole-bar overlay, which read grey over the blue shield fill (see
    // UiTheme.immuneBarColor's tooltip). Built lazily (EnsureImmuneFrame) as the LAST child of overheadBarRoot, copying
    // healthFillImage's rect; the shield fill draws in that identical rect, so one frame covers both. Runtime-only: no
    // prefab change.
    private ImmuneFrame overheadImmuneFrame;

    /// <summary>The overhead bar's immune-look frame: four thin edge Images round the shared health/shield rect plus
    /// an optional faint wash. Like PlayerHud.ImmuneFrame but separate, since the two build in different coordinate
    /// spaces (HUD canvas units there, THIS bar's local RectTransform units here, UiTheme.immuneOverheadFrameThickness).
    /// One root so ApplyImmuneLook shows/hides the whole look with a single SetActive.</summary>
    private sealed class ImmuneFrame
    {
        public readonly GameObject root;
        private readonly Image top, bottom, left, right, wash;

        public ImmuneFrame(GameObject root, Image top, Image bottom, Image left, Image right, Image wash)
        {
            this.root = root;
            this.top = top;
            this.bottom = bottom;
            this.left = left;
            this.right = right;
            this.wash = wash;
        }

        /// <summary>frameColor is applied at its own alpha; washAlpha overrides the wash's alpha independently.</summary>
        public void Apply(Color frameColor, float washAlpha)
        {
            top.color = bottom.color = left.color = right.color = frameColor;
            Color washColor = frameColor;
            washColor.a = washAlpha;
            wash.color = washColor;
        }

        public void SetShown(bool shown) => root.SetActive(shown);
    }
    private readonly ImmuneLookClock immuneLook = new ImmuneLookClock();
    // Latches ApplyImmuneLook's last value so a remote copy's Update tick (below) and an owner's
    // ShowImmuneLook/ClearImmuneLook calls never redo the SetActive/color work when nothing changed.
    private bool immuneLookApplied;

    /// <summary>True while the yellow immunity overlay is showing - PlayerHud reads this for the
    /// owner's own screen-space bars (the overhead bar above needs no such read: it drives itself).</summary>
    public bool ShowsImmuneLook => immuneLookApplied;

    /// <summary>The shared theme, so the STUNNED / SLOWED label built over this player's head (D18) reads its
    /// colours and sizes from the same asset as the bar under it.</summary>
    public UiTheme Theme => theme;

    /// <summary>The world-space canvas the overhead bar and the name sit on - null when the prefab has no health
    /// fill assigned. The status label is built onto it.</summary>
    public Canvas OverheadCanvas => healthFillImage != null ? healthFillImage.GetComponentInParent<Canvas>(true) : null; // includes a canvas EnemyVisibility switched off (Image.canvas reads null then)

    private PhotonView photonView;
    private PlayerLifecycle lifecycle; // the replicated alive state, read by IsAlive

    private float health;
    private ArmorState armor;
    private PlayerStatusEffects statusEffects; // A status is not health - see PlayerStatusEffects.cs.
    private Overpower.Dominion.RespawnShield respawnShield; // Dominion: stops every hit while the respawn shield is up

    // This VICTIM's own marks, keyed by attacker. Damage is victim-side, so this is the one client that can decide
    // the +50% with no message (MarkLedger's class comment).
    private readonly MarkLedger marks = new MarkLedger();

    // The two independent armor upgrade paths (Combat/ArmorUpgradePath), each an index into
    // ArmorConfig's own array. Everyone starts at level 0 on both, not on no armor. These survive
    // death - only the current fill of the pool (armor.Clear(), below) resets - because an upgrade
    // is bought, not lent.
    private int absorbLevel = 0;
    private int rechargeLevel = 0;
    private float secondsSinceCombat;
    private bool isDead;

    // Each blocked-damage reason logs once per match, not per hit - a teamfight would otherwise
    // fill a log that writes synchronously to disk in a build.
    private bool loggedSelfHitBlocked;
    private bool loggedFriendlyFireBlocked;

    public float Health => health;
    /// <summary>The most health this player can have, from GameplayConfig (100 if that is missing, as elsewhere here).</summary>
    public float MaxHealth => gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
    // One ArmorState per client, owner and remote alike: a remote copy's is written by SetHealthFromNetwork through
    // ArmorState.SetFromNetwork, so Armor means the same thing whoever reads it.
    public float Armor => armor.Current;
    public float ArmorCapacity => armor.Capacity;
    public int AbsorbLevel => absorbLevel;
    public int RechargeLevel => rechargeLevel;
    public float SecondsSinceCombat => secondsSinceCombat;
    public bool IsOutOfCombat => gameplayConfig != null && secondsSinceCombat >= gameplayConfig.OutOfCombatSeconds;
    /// <summary>Alive on EVERY client: the owner's own death latch and the replicated alive state, see PlayerAliveRule.</summary>
    public bool IsAlive => PlayerAliveRule.IsAlive(isDead, lifecycle != null, lifecycle != null && lifecycle.IsAlive);
    public int TeamId => Teams.TryGetTeam(photonView.Owner, out int teamId) ? teamId : -1;
    public int ActorNumber => photonView.OwnerActorNr;

    /// <summary>True only on the machine this player belongs to - the same guard ApplyDamage
    /// already enforces, exposed so a mine can ask before it decides to trigger.</summary>
    public bool HasLocalAuthority => photonView.IsMine;
    public event System.Action<DamageResult, DamageInfo> Damaged;
    public event System.Action<DamageInfo> Died;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        lifecycle = GetComponent<PlayerLifecycle>();
        statusEffects = GetComponent<PlayerStatusEffects>();
        respawnShield = GetComponent<Overpower.Dominion.RespawnShield>(); // null in a scene without Dominion

        // A silent null here would make this player un-damageable - the worst failure mode.
        if (gameplayConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: GameplayConfig is not assigned - cannot take damage.");
        if (armorConfig == null)
            Debug.LogError($"[PlayerHealth] {name}: ArmorConfig is not assigned - cannot take damage.");
        // Not fatal like the two above - health regen simply never ticks without it (TickHealthRegen
        // guards on the same null), so a warning rather than an error.
        if (territoryConfig == null)
            Debug.LogWarning($"[PlayerHealth] {name}: Territory Config is not assigned - health will never regenerate from standing in owned territory.");
        // A silent null here would leave the overhead bar wearing whatever colours/sprite the
        // prefab happened to ship with, which is exactly the "two homes for one value" bug the
        // theme asset exists to prevent.
        if (theme == null)
            Debug.LogError($"[PlayerHealth] {name}: UiTheme is not assigned - overhead bar will not be themed.");
        // Mirrors PlayerHud's guard: a Filled Image with no sprite ignores fillAmount and draws full, and unlike a
        // missing theme this would say nothing in the console while every overhead bar quietly lies about health and armor.
        else if (theme.barSprite == null)
            Debug.LogError($"[PlayerHealth] {name}: UiTheme has no Bar Sprite - overhead bar will draw full width regardless of health/armor.");

        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        armor = new ArmorState(armorConfig != null ? armorConfig.AbsorbFor(absorbLevel) : 0f,
                                armorConfig != null ? armorConfig.RechargeSecondsFor(rechargeLevel) : 6f,
                                armorConfig != null ? armorConfig.RefillSeconds : 2.5f);

        overheadBarRoot = healthFillImage != null ? healthFillImage.transform.parent?.gameObject : null;

        ApplyTheme();
        UpdateOverheadBar();
    }

    /// <summary>Hides (or shows) the whole overhead bar; called from PlayerLifecycle.ApplyAliveState, which runs on
    /// every client for every player. Keyed off the caller's own alive value rather than IsAlive: isDead is only
    /// written on the owner's machine (ApplyDamage's IsMine guard), and this caller is what updates
    /// PlayerLifecycle.IsAlive, so it passes PlayerLifecycle's own value.</summary>
    public void SetOverheadBarVisible(bool visible)
    {
        if (overheadBarRoot != null)
            overheadBarRoot.SetActive(visible);

        // The bar over a dead player hides with the body, so the immune frame must not survive either. On death, on
        // EVERY client, PlayerLifecycle.ApplyAliveState(false) raises AliveChanged, and AbilityRunner.HandleAliveChanged
        // -> Interrupt(Died) -> InvulnerabilityAbility.ClearShield() -> ClearImmuneLook() already clears the frame
        // before this is reached; only that module switches the look on, and the shortest respawn outlasts the longest
        // immune look, so no stale "on" frame can reappear. This is a harmless SECOND GUARD that keeps PlayerHealth
        // independent of the ultimate module: don't remove it, but it is not the load-bearing path.
        if (!visible)
            ClearImmuneLook();
    }

    /// <summary>Builds the immunity frame the first time ApplyImmuneLook needs one, never eagerly in Awake (most
    /// lives never trigger the shield). Copies healthFillImage's RectTransform exactly (anchors, offsets, pivot)
    /// rather than stretching to fill overheadBarRoot, so it lines up with the fills pixel-for-pixel even if a prefab
    /// edit insets them. Starts inactive; ApplyImmuneLook is the only thing that shows it.</summary>
    private void EnsureImmuneFrame()
    {
        if (overheadImmuneFrame != null || overheadBarRoot == null || healthFillImage == null)
            return;

        var frameGo = new GameObject("Immune Frame", typeof(RectTransform));
        frameGo.transform.SetParent(overheadBarRoot.transform, false);

        RectTransform frameRect = frameGo.GetComponent<RectTransform>();
        RectTransform sourceRect = healthFillImage.rectTransform;
        frameRect.anchorMin = sourceRect.anchorMin;
        frameRect.anchorMax = sourceRect.anchorMax;
        frameRect.offsetMin = sourceRect.offsetMin;
        frameRect.offsetMax = sourceRect.offsetMax;
        frameRect.pivot = sourceRect.pivot;

        // Built FIRST (so it sits UNDER the frame edges below in draw order) - a non-zero Immune Bar
        // Wash Alpha must never paint over the frame's own crisp edge.
        Image wash = BuildFullRectImage(frameGo.transform, "Wash");

        float t = theme != null ? theme.immuneOverheadFrameThickness : 0.6f;
        Image top = BuildFrameEdge(frameGo.transform, "Frame Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t));
        Image bottom = BuildFrameEdge(frameGo.transform, "Frame Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t));
        // Left/Right inset vertically by the frame's own thickness top and bottom, same reasoning as
        // PlayerHud.BuildSlot's own Frame Left/Right: without the inset every corner would carry two
        // strips stacked on top of each other.
        Image left = BuildFrameEdge(frameGo.transform, "Frame Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, -2f * t));
        Image right = BuildFrameEdge(frameGo.transform, "Frame Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, -2f * t));

        frameGo.SetActive(false);
        overheadImmuneFrame = new ImmuneFrame(frameGo, top, bottom, left, right, wash);
    }

    /// <summary>A plain Image stretched to fill its parent's whole rect - the wash's own shape.</summary>
    private static Image BuildFullRectImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    /// <summary>One thin edge strip of the overhead frame (the shape PlayerHud.BuildFrameStrip builds, kept separate
    /// since this class has no PlayerHud instance). Equal min/max anchors on one axis pin the strip to that edge with
    /// zero size, which sizeDelta on that axis supplies; the other axis's anchors span the full rect, so its
    /// sizeDelta stays 0.</summary>
    private static Image BuildFrameEdge(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
        Image img = go.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    /// <summary>The one source for "immune right now" on the overhead bar, driven by InvulnerabilityAbility's
    /// ShowShield/ClearShield, which run on every client: a remote copy's own IsInvulnerable is always false, so the
    /// shield's replicated phase message is the only signal that reaches every screen.</summary>
    public void ShowImmuneLook(float seconds)
    {
        immuneLook.Show(Time.time, seconds);
        ApplyImmuneLook(immuneLook.IsOn(Time.time));
    }

    /// <summary>Ends the look at once. Called from InvulnerabilityAbility.ClearShield (the real path off a death),
    /// from ResetForRespawn (a fresh life, including ResetForMatchStart, must never carry a stale yellow bar), and
    /// from SetOverheadBarVisible(false) (a second guard, see that method).</summary>
    public void ClearImmuneLook()
    {
        immuneLook.Clear();
        ApplyImmuneLook(false);
    }

    /// <summary>Toggles the FRAME only: healthFillImage/shieldFillImage are never touched here, so they keep
    /// whatever ApplyTheme set. Guarded on the latched value so a remote copy's per-frame Update check and repeated
    /// ShowImmuneLook calls while already on do no redundant work.</summary>
    private void ApplyImmuneLook(bool on)
    {
        if (on == immuneLookApplied)
            return;
        immuneLookApplied = on;

        if (theme == null)
            return;

        EnsureImmuneFrame();
        if (overheadImmuneFrame == null)
            return;

        overheadImmuneFrame.Apply(theme.immuneBarColor, theme.immuneBarWashAlpha);
        overheadImmuneFrame.SetShown(on);
    }

    /// <summary>Applies the theme's bar sprite and colours to the three overhead-bar Images once, at spawn: the
    /// prefab holds only structure, so the theme asset is the ONE place a retune happens. Without theme.barSprite a
    /// Filled Image ignores fillAmount and draws full (UiTheme.barSprite).
    ///
    /// Also forces raycastTarget false on all three, as a second guard: HealthBarCanvas (world-space, every player)
    /// should carry no GraphicRaycaster at all, since a world-space raycaster falls back to Camera.main and makes
    /// PlayerInputRouter.pointerOverUi true (swallowing a shot) whenever the cursor crosses ANY player's head. This
    /// covers a prefab variant or edit re-adding one.</summary>
    private void ApplyTheme()
    {
        if (theme == null)
            return;

        if (healthFillImage != null)
        {
            healthFillImage.sprite = theme.barSprite;
            healthFillImage.color = theme.healthColor;
            healthFillImage.raycastTarget = false;
        }
        if (shieldFillImage != null)
        {
            shieldFillImage.sprite = theme.barSprite;
            shieldFillImage.color = theme.shieldColor;
            shieldFillImage.raycastTarget = false;
        }
        if (overheadTrackImage != null)
        {
            overheadTrackImage.sprite = theme.barSprite;
            overheadTrackImage.color = theme.barTrackColor;
            overheadTrackImage.raycastTarget = false;
        }
    }

    /// <summary>The one place both overhead fills are written: shield drawn OVER health, each fill against its OWN max
    /// (health/maxHealth, armor/ArmorCapacity), so a full shield hides max HP by design. Skips an unchanged
    /// fillAmount: the recharge tick calls this every frame armor is not full, and a filled Image write is not
    /// free.</summary>
    private void UpdateOverheadBar()
    {
        float maxHealth = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        float healthFraction = maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;

        float capacity = armor.Capacity;
        // Guarded against a capacity of 0 (no armor upgrade bought yet) - dividing by it would be
        // a NaN fillAmount, which Unity's UI renders as an empty-looking bar anyway but for the
        // wrong reason.
        float shieldFraction = capacity > 0f ? Mathf.Clamp01(armor.Current / capacity) : 0f;

        if (healthFillImage != null && !Mathf.Approximately(healthFraction, lastHealthFraction))
        {
            healthFillImage.fillAmount = healthFraction;
            lastHealthFraction = healthFraction;
        }
        if (shieldFillImage != null && !Mathf.Approximately(shieldFraction, lastShieldFraction))
        {
            shieldFillImage.fillAmount = shieldFraction;
            lastShieldFraction = shieldFraction;
        }
    }

    private void Update()
    {
        // A REMOTE copy's look must expire on its own clock too, which is why this sits ABOVE the owner-only return.
        // ShowImmuneLook/ClearImmuneLook already run on every client (InvulnerabilityAbility's RpcTarget.All); the only
        // thing a remote copy cannot do for itself is notice time passing, and this is that tick.
        if (immuneLookApplied && !immuneLook.IsOn(Time.time))
            ApplyImmuneLook(false);

        // No other client should simulate your health or tick your armor recharge. isDead is checked too: without
        // it, armor kept climbing on a corpse, so the armor you respawned with silently depended on how long the
        // respawn timer took. ResetForRespawn resets this clock and decides the respawn armor outright
        // (ArmorConfig.RespawnWithFullArmor), so there is nothing useful to tick while dead.
        if (!photonView.IsMine || isDead)
            return;

        secondsSinceCombat += Time.deltaTime;

        // Burn ticks in PlayerStatusEffects, which routes it back through ApplyDamage below.
        armor.Tick(Time.deltaTime, secondsSinceCombat);
        TickHealthRegen(Time.deltaTime);
        UpdateOverheadBar(); // So the shield fill visibly refills as the pool recharges, not just on the next hit.
    }

    /// <summary>Health regen by tier. Owner only (Update's IsMine check), after the armor tick: both recharge on the
    /// same out-of-combat clock, armor first. Finds the zone this player stands in via BuildingManager.TryGetZoneAt,
    /// checks it against the replicated owner, and asks HealthRegenRule for the rate. Other clients see the change
    /// through PlayerNetSync's serialize tick reading Health live; no new RPC or property.</summary>
    private void TickHealthRegen(float deltaTime)
    {
        if (gameplayConfig == null || territoryConfig == null)
            return;

        float maxHealth = gameplayConfig.MaxHealth;
        if (health >= maxHealth)
            return;

        bool standingInOwnZone = false;
        float tierRegenPerSecond = 0f;

        BuildingManager manager = BuildingManager.Instance;
        if (manager != null && manager.Current != null && manager.TryGetZoneAt(transform.position, out int zone))
        {
            int team = TeamId;
            int tier = manager.TierOf(zone); // 0 = tower hasn't registered itself yet; never a real tier.
            if (team >= 0 && tier > 0 && manager.Current.OwnerOf(zone) == team)
            {
                standingInOwnZone = true;
                tierRegenPerSecond = territoryConfig.ForTier(tier).healthRegenPerSecond;
            }
        }

        float rate = HealthRegenRule.RegenPerSecond(standingInOwnZone, tierRegenPerSecond,
                                                      secondsSinceCombat, gameplayConfig.OutOfCombatSeconds);

        // The player's own spawn heals at its own rates, also mid-fight (A14: owned Tier 2/3 zones elsewhere keep the
        // Conquest regen above). Only once the match is live (A22); the warm-up is a sandbox. The rate is
        // DominionHealRules.HealRate's answer, not a copy of its branching here.
        if (Overpower.Dominion.DominionMode.IsLive())
        {
            Overpower.Data.DominionConfig dominion = Overpower.Dominion.DominionMode.Config();
            if (dominion != null)
                rate = Overpower.Dominion.DominionHealRules.HealRate(Overpower.Dominion.SpawnHealArea.InOwnSpawn(TeamId, transform.position),
                    secondsSinceCombat, dominion.SpawnHealOutOfCombatPerSecond, dominion.SpawnHealInCombatPerSecond,
                    dominion.SpawnHealOutOfCombatDelaySeconds, rate);
        }
        // Dominion sudden death (A29): no healing at all - neither the spawn's nor an owned zone's. Health packs (Heal below) still work.
        Overpower.Dominion.DominionDirector dominionDirector = Overpower.Dominion.DominionDirector.Instance;
        rate = Overpower.Dominion.DominionHealRules.RateInStage(dominionDirector != null ? dominionDirector.Stage : Overpower.Dominion.DominionStage.None, rate);
        if (rate <= 0f)
            return;

        health = Mathf.Min(maxHealth, health + rate * deltaTime);
    }

    /// <summary>A health pack heals here. Owner only (health is this client's own), alive only, never above max health;
    /// returns how much actually landed. Reaches the other clients the same way regen does, through PlayerNetSync's
    /// serialize tick reading Health live, and updates the overhead bar at once.</summary>
    public float Heal(float amount)
    {
        if (!photonView.IsMine || isDead || gameplayConfig == null)
            return 0f;

        float applied = HealthPackRules.HealAmount(amount, health, gameplayConfig.MaxHealth);
        if (applied <= 0f)
            return 0f;

        health += applied;
        UpdateOverheadBar();
        return applied;
    }

    /// The one funnel every damage source goes through - see the class comment.
    public DamageResult ApplyDamage(in DamageInfo info)
    {
        // Every client simulates every shot fired at every player, including its own shots against an enemy it does
        // NOT own: this is the one moment the SHOOTER's client can learn where its shot landed, since the real damage
        // math below (and Damaged / the credit RPC) only runs on the victim's machine. Raised BEFORE the IsMine/isDead
        // return on purpose, so it fires on a copy the shooter does not own. A self-hit raises nothing.
        //
        // Only for a source whose HitPoint is a genuine point of impact (Projectile, Splash, Contact). Burn and Zone
        // carry the field's or zone's own centre (FireField, AoeZone, ElectricFence), which would anchor the next
        // number at that stationary point instead of falling back to the victim's centre.
        if (!photonView.IsMine && PhotonNetwork.LocalPlayer != null
            && info.SourceActorNumber == PhotonNetwork.LocalPlayer.ActorNumber
            && info.Source != DamageSource.Burn && info.Source != DamageSource.Zone)
        {
            // Raised unconditionally, before the Blocked decision: otherwise a Blocked hit never refreshes this
            // victim's latest-impact record, and once the last real impact ages out
            // (DamageNumberView.ImpactFreshnessSeconds) every later "Blocked" pop falls back to the victim's centre
            // instead of the impact point (DamageNumberView.HandleBlockedSeen).
            CombatEvents.RaiseImpactSeen(transform, info.HitPoint);

            // "Blocked" is a shooter-side guess with no network, not the victim's truth (CombatEvents.LocalBlockedSeen
            // has the accuracy trade-off). ShowsImmuneLook already replicates to every client, so the shooter's copy of
            // the victim knows it without a message. Guarded off a teammate hit like the real funnel below
            // (AreSameTeam fails the same direction on an unknown team): friendly fire shows nothing even on a
            // shielded "victim", so it must never read as "Blocked".
            Photon.Realtime.Player shooterSidePlayer = PhotonNetwork.CurrentRoom?.GetPlayer(info.SourceActorNumber);
            if (ShowsImmuneLook && !Teams.AreSameTeam(shooterSidePlayer, photonView.Owner))
                CombatEvents.RaiseBlockedSeen(transform);
        }

        // The victim is the sole authority on its own health, or every client would subtract from its own copy and
        // the owner's next serialization would fight it back.
        if (!photonView.IsMine || isDead)
            return default;

        // Combat order: self, then teammate, then an immunity already running, then the armed trap, then the hit
        // lands. HitVerdictRule.Classify is the one home for it, so a self or teammate hit can never spring the trap
        // or touch the combat clock below.
        Photon.Realtime.Player sourcePlayer = PhotonNetwork.CurrentRoom?.GetPlayer(info.SourceActorNumber);
        bool fromSelf = sourcePlayer != null && sourcePlayer == photonView.Owner;
        // AreSameTeam deliberately fails OPEN: an unknown team must never silently make someone invulnerable.
        bool fromTeammate = !fromSelf && Teams.AreSameTeam(sourcePlayer, photonView.Owner);

        // Dominion respawn shield: stopped before anything else, so no health, armour, mark or combat-clock change and no damage credit follows
        // (the spawn keeps healing at its fast rate; the attacker earns no ultimate charge and keeps its own shield). A teammate's hit is left
        // to the friendly-fire rule below. Self-damage is stopped too (default: a shielded player cannot hurt themselves), silently - no
        // BLOCKED for a self-hit.
        if (respawnShield != null && respawnShield.BlocksHit(Overpower.Dominion.RespawnShieldRules.OriginOf(fromSelf, fromTeammate)))
            return default;

        HitVerdict verdict = HitVerdictRule.Classify(fromSelf, fromTeammate,
            statusEffects != null && statusEffects.IsInvulnerable, statusEffects, info.Amount,
            HitVerdictRule.IgnoresInvulnerability(info.Source)); // the sudden-death circle goes through Invulnerability (A30)

        // Being shot while the shield is up IS combat (no armour recharge, shop or regen). Self and teammate hits are
        // classified above and CountsAsCombat is false for them, so they never touch the clock.
        if (HitVerdictRule.CountsAsCombat(verdict))
            secondsSinceCombat = 0f;

        if (verdict == HitVerdict.IgnoredSelf)
        {
            if (!loggedSelfHitBlocked)
            {
                loggedSelfHitBlocked = true;
                Debug.Log("[DMG] blocked self-damage (reported once per match)");
            }
            return default;
        }

        if (verdict == HitVerdict.IgnoredTeammate)
        {
            if (!loggedFriendlyFireBlocked)
            {
                loggedFriendlyFireBlocked = true;
                Debug.Log($"[DMG] blocked friendly fire from {sourcePlayer?.NickName} (reported once per match)");
            }
            return default;
        }

        if (verdict == HitVerdict.Shielded)
            return default;

        // The mark is decided ONLY for a hit that reaches this point (HitVerdict.Lands), on the VICTIM's own client,
        // the one machine that can decide the +50% with no extra message. Self, teammate, shield-blocked and
        // dead-player hits returned above, so none of them marks or cashes in; a live mark survives a blocked hit and
        // expires on its own. info.MarkWindowSeconds is 0 for every non-marking source, which OnLandedHit treats as
        // "touch nothing".
        MarkOutcome mark = marks.OnLandedHit(info.SourceActorNumber, Time.time, info.MarkWindowSeconds);
        DamageInfo landed = mark == MarkOutcome.Cashed
            ? info.WithAmount(MarkLedger.ScaledAmount(info.Amount, mark, info.MarkedDamageMultiplier))
            : info;

        float vulnerability = statusEffects != null ? statusEffects.Vulnerability : 0f;
        DamageResult result = DamageResolver.Resolve(landed.Amount, landed.IgnoresArmor, health,
                                                       armor.Current, vulnerability, CurrentDamageReduction())
                                             .WithMark(mark);
        armor.Absorb(result.ArmorAbsorbed);
        health -= result.HealthLost;

        UpdateOverheadBar();

        Damaged?.Invoke(result, landed);

        if (result.Lethal)
        {
            isDead = true;   // Latched before raising Died so a re-entrant hit cannot double-kill.
            armor.Clear();   // A corpse has no armor; ResetForRespawn decides what comes back.
            secondsSinceCombat = CombatClockRule.AfterDeath(RegenGate, ShopGate, LongestArmourDelay); // dying takes you out of combat
            sourcePlayer?.AddScore(1);
            // Death clears the victim's marks BEFORE Died fires, so the death credit flush
            // (PlayerCombatCredit.HandleDied reads MarkSecondsLeftFor per attacker while building each message)
            // reports 0 for everyone and every diamond hides on the kill.
            marks.Clear();
            Died?.Invoke(landed);
        }

        return result;
    }

    /// <summary>Seconds left on THIS attacker's own mark on me, 0 if none live (never marked, expired, or already
    /// cashed); read by PlayerCombatCredit.SendCredit so the attacker's diamond rides on the same credit message as
    /// the damage or takedown it goes with.</summary>
    public float MarkSecondsLeftFor(int attackerActor) => marks.SecondsLeft(attackerActor, Time.time);

    /// <summary>The marked player also sees it over their own head: at most one diamond however many attackers have
    /// me marked, so the LONGEST live mark (whoever placed it) is enough. Read by the victim-side view.</summary>
    public float LongestMarkSecondsLeft => marks.LongestSecondsLeft(Time.time);

    /// The one place damage reduction is read, as a 0..1 fraction for DamageResolver, so the same buff cannot do
    /// two different things depending on what hit you. Reads PlayerStatusEffects.CurrentDamageReduction, which
    /// combines every source (a dash buff, an armor upgrade) the way Vulnerability does. Do not inline this away:
    /// every future source of damage reduction reports through here.
    private float CurrentDamageReduction() => statusEffects != null ? statusEffects.CurrentDamageReduction : 0f;

    // Deliberately no OnCollisionEnter: projectiles are swept spherecasts that call ApplyDamage directly, so there
    // is one way in.

    /// <summary>
    /// Applies a new pair of armor upgrade levels: the sink every client calls when PlayerLoadout replicates a
    /// purchase (or a late joiner reads one). Refills the pool to the new capacity immediately: a purchase, not a
    /// recharge (ArmorState.SetTier).
    /// </summary>
    public void SetArmorLevels(int newAbsorbLevel, int newRechargeLevel)
    {
        // Clamped against the config's own array lengths, not just >= 0: a stale or malformed
        // replicated level (this arrives over the network via PlayerLoadout) must never let
        // absorbLevel/rechargeLevel sit out of range, which would otherwise misreport
        // ArmorUpgradePath.TotalUpgrades and could block every future upgrade for this player.
        if (armorConfig != null)
        {
            newAbsorbLevel = Mathf.Clamp(newAbsorbLevel, 0, Mathf.Max(0, armorConfig.AbsorbLevelCount - 1));
            newRechargeLevel = Mathf.Clamp(newRechargeLevel, 0, Mathf.Max(0, armorConfig.RechargeLevelCount - 1));
        }

        absorbLevel = newAbsorbLevel;
        rechargeLevel = newRechargeLevel;
        if (armorConfig != null)
            armor.SetTier(armorConfig.AbsorbFor(absorbLevel), armorConfig.RechargeSecondsFor(rechargeLevel));
        UpdateOverheadBar(); // Capacity just changed - the shield fraction must recompute against it immediately, not wait for the next hit.
    }

    /// <summary>
    /// OverPowerBuff's hook (GDD p.20): regenerate the shield instantly the moment the comeback buff triggers, through
    /// ArmorState.RefillToFull (the instant path ResetForRespawn uses), not the gradual recharge.
    /// </summary>
    public void RefillArmor()
    {
        armor.RefillToFull();
        UpdateOverheadBar();
    }

    public void ResetForRespawn()
    {
        health = gameplayConfig != null ? gameplayConfig.MaxHealth : 100f;
        statusEffects?.ClearAll();
        secondsSinceCombat = CombatClockRule.AfterRespawn(RegenGate, ShopGate, LongestArmourDelay); // you respawn out of combat
        isDead = false;

        // Respawn with full armor by default. RespawnWithFullArmor off means a respawning player earns their armor
        // back through the out-of-combat timer like anyone else. A missing config fails toward Clear() rather than
        // assuming the field's default, like every other missing-config fallback in this class.
        if (armorConfig != null && armorConfig.RespawnWithFullArmor)
            armor.RefillToFull();
        else
            armor.Clear();

        UpdateOverheadBar();
        // A fresh spawn must never carry a stale yellow bar into the next life; PlayerLifecycle.ResetForMatchStart
        // calls this same method, so the match-start case is covered.
        ClearImmuneLook();
        // A fresh life must not carry marks from the last one either.
        marks.Clear();
    }

    /// Call when this player deals damage, so dealing it keeps you "in combat" the same way
    /// taking it does.
    public void NoteDealtDamage() => secondsSinceCombat = CombatClockRule.AfterDealtDamage(isDead, secondsSinceCombat);

    // The three thresholds every reader of the combat clock waits for (CombatClockRule decides what the clock becomes).
    // The armour delay is the longest over ALL recharge levels, so selling armour after respawn cannot lengthen it past the clock.
    private float RegenGate => gameplayConfig != null ? gameplayConfig.OutOfCombatSeconds : 0f;
    private float ShopGate => gameplayConfig != null ? gameplayConfig.ShopOutOfCombatSeconds : 0f;
    private float LongestArmourDelay => armorConfig != null ? armorConfig.MaxRechargeSeconds : 0f;

    /// Called by PlayerNetSync's receive side on a non-owner. Health is written directly since
    /// only the owner ever simulates it; armor goes through ArmorState.SetFromNetwork rather than
    /// a plain assignment, since ArmorState has no public setter for Current by design (see its
    /// class comment) and clamping here keeps a stale or out-of-order packet from handing a
    /// remote client more armor than the pool can hold.
    public void SetHealthFromNetwork(float health, float armor)
    {
        this.health = health;
        this.armor.SetFromNetwork(armor);

        UpdateOverheadBar();
    }
}
