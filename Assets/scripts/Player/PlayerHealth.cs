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
/// One player's health, armor and damage funnel. Replaces two copies that used to live in
/// Multiplayer.cs (TakeDamage for bullets, ApplyAoEDamage for area damage) and had already
/// diverged - only the bullet path applied the dash damage-reduction buff. ApplyDamage below is
/// now the only place damage maths happens.
///
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

    // Mark plan step 1 (Tudor's override, top-of-plan table #6): the yellow "shield immunity" look is
    // a translucent OVERLAY on top of the shared health/shield rect, not a recolour of either fill -
    // "so it doesn't mess with the shield". Built lazily (EnsureImmuneOverlay) the first time it is
    // actually needed, as the LAST child of overheadBarRoot, copying healthFillImage's own rect: the
    // shield fill already draws in that identical rect (Task 6 [T], full armour over full health), so
    // one overlay covers both. No prefab change - this Image exists only at runtime.
    private Image overheadImmuneOverlay;
    private readonly ImmuneLookClock immuneLook = new ImmuneLookClock();
    // Latches ApplyImmuneLook's last value so a remote copy's Update tick (below) and an owner's
    // ShowImmuneLook/ClearImmuneLook calls never redo the SetActive/color work when nothing changed.
    private bool immuneLookApplied;

    /// <summary>True while the yellow immunity overlay is showing - PlayerHud reads this for the
    /// owner's own screen-space bars (the overhead bar above needs no such read: it drives itself).</summary>
    public bool ShowsImmuneLook => immuneLookApplied;

    private PhotonView photonView;

    private float health;
    private ArmorState armor;
    private PlayerStatusEffects statusEffects; // A status is not health - see PlayerStatusEffects.cs.

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
    // A single ArmorState per client now, owner and remote alike - a remote client's copy is
    // written by SetHealthFromNetwork below through ArmorState.SetFromNetwork instead of a
    // separately mirrored field, so Armor means the same thing regardless of whose client reads it.
    public float Armor => armor.Current;
    public float ArmorCapacity => armor.Capacity;
    public int AbsorbLevel => absorbLevel;
    public int RechargeLevel => rechargeLevel;
    public float SecondsSinceCombat => secondsSinceCombat;
    public bool IsOutOfCombat => gameplayConfig != null && secondsSinceCombat >= gameplayConfig.OutOfCombatSeconds;
    public bool IsAlive => !isDead;
    public int TeamId => Teams.TryGetTeam(photonView.Owner, out int teamId) ? teamId : -1;
    public int ActorNumber => photonView.OwnerActorNr;

    /// <summary>True only on the machine this player belongs to - the same guard ApplyDamage
    /// already enforces, exposed so a mine (Task 1.8) can ask before it decides to trigger.</summary>
    public bool HasLocalAuthority => photonView.IsMine;
    public event System.Action<DamageResult, DamageInfo> Damaged;
    public event System.Action<DamageInfo> Died;

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        statusEffects = GetComponent<PlayerStatusEffects>();

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
        // Mirrors PlayerHud's own guard: a theme with no bar sprite is the exact Task 3 bug (a
        // Filled Image with no sprite ignores fillAmount and draws full), and unlike a missing
        // theme entirely this would say nothing in the console while every overhead bar quietly
        // lies about health and armor.
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

    /// <summary>Hides (or shows) the whole overhead bar - called from PlayerLifecycle.ApplyAliveState,
    /// which runs on every client for every player, not just the owner (see that method's own class
    /// comment). Deliberately keyed off the caller's own alive value rather than this class's IsAlive:
    /// PlayerHealth.isDead is only ever written on the owner's machine (ApplyDamage's IsMine guard),
    /// so a remote copy's IsAlive silently reads true for the whole time that player is actually
    /// dead - the exact trap this method exists to route around. Callers must pass
    /// PlayerLifecycle.IsAlive (replicated), never PlayerHealth.IsAlive.</summary>
    public void SetOverheadBarVisible(bool visible)
    {
        if (overheadBarRoot != null)
            overheadBarRoot.SetActive(visible);
        // No separate hide for overheadImmuneOverlay: it is a CHILD of overheadBarRoot, so
        // SetActive(false) above already takes it out of the hierarchy with everything else on the
        // bar - Unity does not run a hidden child's Update either, so ApplyImmuneLook still fires
        // correctly (see Update below) the moment the bar - and the overlay under it - reappear.
    }

    /// <summary>Builds the immunity overlay the first time ApplyImmuneLook actually needs one - never
    /// eagerly in Awake, since most lives never trigger the shield at all. Copies healthFillImage's
    /// own RectTransform exactly (anchors, offsets, pivot) rather than stretching to fill
    /// overheadBarRoot, so it lines up with the fills pixel-for-pixel even if a future prefab edit
    /// insets them. Starts inactive; ApplyImmuneLook is the only thing that ever shows it.</summary>
    private void EnsureImmuneOverlay()
    {
        if (overheadImmuneOverlay != null || overheadBarRoot == null || healthFillImage == null)
            return;

        var overlayGo = new GameObject("Immune Overlay", typeof(RectTransform));
        overlayGo.transform.SetParent(overheadBarRoot.transform, false);

        RectTransform overlayRect = overlayGo.GetComponent<RectTransform>();
        RectTransform sourceRect = healthFillImage.rectTransform;
        overlayRect.anchorMin = sourceRect.anchorMin;
        overlayRect.anchorMax = sourceRect.anchorMax;
        overlayRect.offsetMin = sourceRect.offsetMin;
        overlayRect.offsetMax = sourceRect.offsetMax;
        overlayRect.pivot = sourceRect.pivot;

        Image overlay = overlayGo.AddComponent<Image>();
        overlay.raycastTarget = false;
        overlayGo.SetActive(false);

        overheadImmuneOverlay = overlay;
    }

    /// <summary>The one source for "immune right now" on the overhead bar - driven by
    /// InvulnerabilityAbility's ShowShield/ClearShield, which run on every client (What exists D: a
    /// remote copy's own IsInvulnerable is always false, so the shield's replicated phase message is
    /// the only signal that reaches every screen). Starts (or restarts) the clock from Time.time.</summary>
    public void ShowImmuneLook(float seconds)
    {
        immuneLook.Show(Time.time, seconds);
        ApplyImmuneLook(immuneLook.IsOn(Time.time));
    }

    /// <summary>Ends the look at once - called from InvulnerabilityAbility.ClearShield and from
    /// ResetForRespawn (a fresh spawn, or the 2.7b match-start fresh start, must never carry a stale
    /// yellow bar into the next life - PlayerLifecycle.ResetForMatchStart already calls
    /// ResetForRespawn, so nothing extra was needed there).</summary>
    public void ClearImmuneLook()
    {
        immuneLook.Clear();
        ApplyImmuneLook(false);
    }

    /// <summary>Tudor's override on the Mark plan (top-of-plan table, #6): toggles the OVERLAY only -
    /// healthFillImage/shieldFillImage are never touched here, so they always keep whatever
    /// ApplyTheme set them to. Guarded on the latched value so a remote copy's per-frame Update check
    /// (below) and repeated ShowImmuneLook calls while already on do no redundant work.</summary>
    private void ApplyImmuneLook(bool on)
    {
        if (on == immuneLookApplied)
            return;
        immuneLookApplied = on;

        if (theme == null)
            return;

        EnsureImmuneOverlay();
        if (overheadImmuneOverlay == null)
            return;

        overheadImmuneOverlay.color = theme.immuneBarColor;
        overheadImmuneOverlay.gameObject.SetActive(on);
    }

    /// <summary>Applies the theme's bar sprite and colours to the three overhead-bar Images once,
    /// at spawn - the prefab holds only structure (hierarchy, rect sizes, Filled/Horizontal/Left
    /// set up on the two fill Images), so the theme asset stays the ONE place a retune happens.
    /// Without theme.barSprite a Filled Image ignores fillAmount and draws full - see UiTheme's
    /// own comment on barSprite, the exact bug Task 3 fixed on the screen-space HUD.
    ///
    /// Re-review fix: also forces raycastTarget false on all three. This is a second guard, not
    /// the real fix - HealthBarCanvas (world-space, every player) should carry no
    /// GraphicRaycaster at all, since nothing on an overhead bar is clickable and a world-space
    /// raycaster falls back to Camera.main, making PlayerInputRouter.pointerOverUi true (and so
    /// swallowing a shot) the instant the cursor crossed ANY player's head, including an enemy's,
    /// which is exactly what a crosshair does mid-fight. Belt-and-braces in case a prefab variant
    /// or a future edit re-adds a raycaster here without noticing what it would break.</summary>
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

    /// <summary>The one place both overhead fills are written - option C, Task 6 [T]: shield drawn
    /// OVER health, each fill against its OWN max (health/maxHealth, armor/ArmorCapacity), so a
    /// full shield hides max HP by design. Called from every place that used to set healthBar.value
    /// or call UpdateArmorBar: Awake, ApplyDamage, SetArmorLevels, ResetForRespawn, the armor
    /// recharge tick in Update, and SetHealthFromNetwork (the remote-client path). Guards against
    /// rewriting an unchanged fillAmount - the recharge tick calls this every frame armor is not
    /// already full, and a filled Image write is not free.</summary>
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
        // Mark plan step 1: a REMOTE copy's look must expire on its own clock too, which is why this
        // sits ABOVE the owner-only return below. ShowImmuneLook/ClearImmuneLook already run on every
        // client (InvulnerabilityAbility's RpcTarget.All), so the only thing a remote copy cannot do
        // for itself is notice time passing without ticking anything - this one line is that tick.
        if (immuneLookApplied && !immuneLook.IsOn(Time.time))
            ApplyImmuneLook(false);

        // No other client should simulate your health or tick your armor recharge. isDead is
        // checked too: without it, armor kept climbing on a corpse (Update never used to look at
        // isDead), so how much armor you respawned with silently depended on how long the respawn
        // timer happened to take. ResetForRespawn resets this clock and decides the respawn armor
        // outright (ArmorConfig.RespawnWithFullArmor), so there is nothing useful to tick while dead.
        if (!photonView.IsMine || isDead)
            return;

        secondsSinceCombat += Time.deltaTime;

        // Burn is ticked by PlayerStatusEffects now, which routes it back through ApplyDamage below.
        armor.Tick(Time.deltaTime, secondsSinceCombat);
        TickHealthRegen(Time.deltaTime);
        UpdateOverheadBar(); // So the shield fill visibly refills as the pool recharges, not just on the next hit.
    }

    /// <summary>Task 2.3: health regen by tier. Owner only (guarded by Update's own IsMine check),
    /// after the armor tick above - armor and health recharge on the same out-of-combat clock, armor
    /// first, same order the class always had for the two pools. Finds which zone (if any) this
    /// player is standing in via BuildingManager.TryGetZoneAt, checks it against the replicated
    /// owner, and asks HealthRegenRule for the rate - see that class's own comment for the gate.
    /// The resulting health change reaches other clients through the existing PlayerNetSync
    /// serialize tick (it reads playerHealth.Health live); no new RPC or property.</summary>
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
        if (rate <= 0f)
            return;

        health = Mathf.Min(maxHealth, health + rate * deltaTime);
    }

    /// The one funnel every damage source goes through - see the class comment.
    public DamageResult ApplyDamage(in DamageInfo info)
    {
        // Mark plan step 2 (Tudor's override, answer 2): every client simulates every shot fired at
        // every player, including its own shots against an enemy it does NOT own - this is the one
        // moment the SHOOTER's own client can learn where its shot actually landed, since the real
        // damage math below (and PlayerHealth.Damaged/the credit RPC that follows it) only ever runs
        // on the victim's own machine. Raised BEFORE the IsMine/isDead return on purpose: it must fire
        // on a copy the shooter does not own. A self-hit (IsMine true here) raises nothing - the
        // shooter already knows exactly where it is standing.
        if (!photonView.IsMine && PhotonNetwork.LocalPlayer != null
            && info.SourceActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            CombatEvents.RaiseImpactSeen(transform, info.HitPoint);
        }

        // Bug 1.1: the victim is the sole authority on its own health, or every client would
        // subtract from its own copy and the owner's next serialization would fight it back.
        if (!photonView.IsMine || isDead)
            return default;

        // Shield combat order (2.7b, Tudor 2026-09-18): self, then teammate, then an immunity already running,
        // then the armed trap, then the hit lands - HitVerdictRule.Classify is the one home for this order, so
        // a self or teammate hit can never spring the trap or touch the combat clock below.
        Photon.Realtime.Player sourcePlayer = PhotonNetwork.CurrentRoom?.GetPlayer(info.SourceActorNumber);
        bool fromSelf = sourcePlayer != null && sourcePlayer == photonView.Owner;
        // AreSameTeam deliberately fails OPEN: an unknown team must never silently make someone invulnerable.
        bool fromTeammate = !fromSelf && Teams.AreSameTeam(sourcePlayer, photonView.Owner);

        HitVerdict verdict = HitVerdictRule.Classify(fromSelf, fromTeammate,
            statusEffects != null && statusEffects.IsInvulnerable, statusEffects, info.Amount);

        // Tudor, 2026-09-18: being shot while the shield is up IS combat (no armour recharge, no shop, no
        // regen while being shot). Self and teammate hits are classified above, but CountsAsCombat is false
        // for them, so they never touch the clock.
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

        float vulnerability = statusEffects != null ? statusEffects.Vulnerability : 0f;
        DamageResult result = DamageResolver.Resolve(info.Amount, info.IgnoresArmor, health,
                                                       armor.Current, vulnerability, CurrentDamageReduction());
        armor.Absorb(result.ArmorAbsorbed);
        health -= result.HealthLost;

        UpdateOverheadBar();

        Damaged?.Invoke(result, info);

        if (result.Lethal)
        {
            isDead = true;   // Latched before raising Died so a re-entrant hit cannot double-kill.
            armor.Clear();   // A corpse has no armor; ResetForRespawn decides what comes back.
            sourcePlayer?.AddScore(1);
            Died?.Invoke(info);
        }

        return result;
    }

    /// The one place damage reduction is read, as a 0..1 fraction for DamageResolver. That single
    /// location is the point of the damage funnel: the reduction used to be applied on the bullet
    /// path only and was silently absent from AoE, so the same buff did two different things
    /// depending on what hit you.
    ///
    /// Reads PlayerStatusEffects.CurrentDamageReduction (Task 1.0a), which combines every source -
    /// a dash buff, an armor upgrade - the same way Vulnerability above already does. Do not
    /// inline this away - every future source of damage reduction reports through here.
    private float CurrentDamageReduction() => statusEffects != null ? statusEffects.CurrentDamageReduction : 0f;

    // The OnCollisionEnter that used to sit here read damage off a Rigidbody bullet that had
    // collided with this player. Task 0.13 deleted it along with those bullets: projectiles are now
    // swept spherecasts that build a DamageInfo and call ApplyDamage above directly, so there is no
    // longer a physics collision to react to. Nothing replaced it, which is the point - there is
    // one way in.

    /// <summary>
    /// Applies a new pair of armor upgrade levels - the sink every client calls when
    /// PlayerLoadout replicates a purchase (or a late joiner reads one for the first time). Refills
    /// the pool to the new capacity immediately: this is a purchase, not a recharge, the same
    /// distinction ArmorState.SetTier documents.
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
    /// OverPowerBuff's hook (Task 2.6, GDD p.20): "regenerate their shield instantly" the moment
    /// the comeback buff triggers. Goes through ArmorState.RefillToFull, the same instant-fill path
    /// ResetForRespawn already uses when RespawnWithFullArmor is on - a comeback moment deserves the
    /// same immediacy as a bought upgrade or a fresh spawn, not the ordinary gradual recharge.
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
        secondsSinceCombat = 0f;
        isDead = false;

        // Controller decision [C]: respawn with full armor by default. RespawnWithFullArmor off
        // means a respawning player earns their armor back through the out-of-combat timer like
        // anyone else, the original design before this setting existed. A missing config fails
        // toward Clear() rather than assuming the field's true default, same as every other
        // missing-config fallback in this class.
        if (armorConfig != null && armorConfig.RespawnWithFullArmor)
            armor.RefillToFull();
        else
            armor.Clear();

        UpdateOverheadBar();
        // Mark plan step 1: a fresh spawn must never carry a stale yellow bar into the next life -
        // and PlayerLifecycle.ResetForMatchStart (the 2.7b fresh start) already calls this same method
        // (PlayerLifecycle.cs:446), so the match-start case is covered for free, with no extra call.
        ClearImmuneLook();
    }

    /// Call when this player deals damage, so dealing it keeps you "in combat" the same way
    /// taking it does.
    public void NoteDealtDamage() => secondsSinceCombat = 0f;

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
