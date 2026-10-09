using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using Overpower.Weapons;
using Overpower.TestRange;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Player prefab, owner only: logs everything this player's own client is the sole authority on (design doc Principle 2: "a hit"
    /// is logged by the victim, "shots"/"casts" by the shooter/caster, always THIS player). Every event goes through
    /// MatchTelemetry.Instance.Log, which no-ops when telemetry is off or the file never opened - this class never checks that itself.
    ///
    /// REMOTE COPIES DO NOTHING: Awake disables the component when photonView.IsMine is false, before any GetComponent call or
    /// subscription runs. Deleting Assets/scripts/Telemetry leaves the game running (design doc, Principle 1).
    ///
    /// ALLOCATION: one TelemetryLine is reused for every event; the shots/dots accumulators are reset in place on every flush; event-name
    /// strings come from static arrays instead of Enum.ToString(). The only per-frame work is two cheap bool reads in Update's polling
    /// (IsSilenced, IsFull).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerTelemetry : MonoBehaviourPun
    {
        [Tooltip("Turns this player's own telemetry on/off and holds the sample interval and " +
                 "whether positions are recorded - the same asset MatchTelemetry reads, assigned " +
                 "separately here since MatchTelemetry keeps its own copy private.")]
        [SerializeField] private TelemetryConfig config;

        [Tooltip("Task T4: the same Territory Config asset GoldWallet reads on this same prefab - " +
                 "needed here too because per-zone income attribution (IncomeAttribution.Accumulate) " +
                 "needs each tier's raw team gold/second and Players Per Team, which GoldWallet keeps " +
                 "as a private field rather than exposing.")]
        [SerializeField] private TerritoryConfig territoryConfig;

        private PlayerHealth playerHealth;
        private PlayerStatusEffects statusEffects;
        private PlayerLifecycle lifecycle;
        private WeaponFiring weaponFiring;
        private AbilityRunner abilityRunner;
        private OverPowerBuff overPowerBuff;
        private PlayerOverheat overheat;
        private UltimateCharge ultimateCharge;
        private PlayerCombatCredit combatCredit;
        private GoldWallet goldWallet;
        private LoadoutScreen loadoutScreen;

        /// <summary>PlayerDisplacement.TeleportTo writes rb.position directly and transform.position lags until the next physics step, so
        /// a same-frame read (respawn's AliveChanged(true), fired right after TeleportToSpawnPoint) saw the corpse's x/z through the
        /// transform. MyPosition is used for every "this player's own position" field.</summary>
        private Rigidbody body;
        private Vector3 MyPosition => body != null ? body.position : transform.position;

        private readonly TelemetryLine line = new TelemetryLine();

        private float sampleTimer;

        // Polled state transitions (Update), never events of their own (design doc, "Changes from the spec").
        private bool wasSilenced;
        private bool wasUltimateFull;

        // Time.time this ultimate last became ready (IsFull false -> true), or -1 while it either
        // has not filled yet or was already spent since - ultimateUsed's own "seconds since ready".
        private float ultimateReadyTime = -1f;

        // Time.time this life started - Start() for the very first life, AliveChanged(true) for
        // every respawn after. death's own "time alive".
        private float aliveSinceTime;

        // Time.time the most recent death happened - respawn's "time dead", and the freshness stamp AssistArray checks against
        // PlayerCombatCredit.LastDeathTime.
        private float deathTime;

        // Index-matched to their enums (Combat/DamageInfo.cs, Combat/StatusEffectState.cs): avoids a ToString()/ToLowerInvariant()
        // allocation on every hit/dot/status line. NameFor falls back to ToString() only if an enum grows past these arrays.
        private static readonly string[] DamageSourceNames = { "Projectile", "Splash", "Burn", "Zone", "Contact", "SuddenDeath" };
        private static readonly string[] StatusKindNames = { "burn", "slow", "stun", "vulnerability", "invulnerability" };

        private static string NameFor(DamageSource source)
        {
            int i = (int)source;
            return i >= 0 && i < DamageSourceNames.Length ? DamageSourceNames[i] : source.ToString();
        }

        private static string NameFor(StatusKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < StatusKindNames.Length ? StatusKindNames[i] : kind.ToString();
        }

        private sealed class ShotAccumulator
        {
            public int Pulls;
            public int Projectiles;
            public void Reset() { Pulls = 0; Projectiles = 0; }
        }

        // Entries persist for the whole match and are Reset() in place on every flush (sample interval, death/leave, BeforeClose)
        // rather than removed, so no ShotAccumulator is reallocated per flush.
        private readonly Dictionary<int, ShotAccumulator> shotsByWeapon = new Dictionary<int, ShotAccumulator>();

        /// <summary>Continuous damage (DamageSource.Burn) is bucketed here instead of one `hit` line per tick (see DotAccumulator);
        /// keyed by victim, attacker actor/team, weapon, ability, source. Reset in place like shotsByWeapon.</summary>
        // Source is stored as int, not DamageSource: a DamageSource-valued tuple field boxes the enum on every Equals/GetHashCode a
        // Dictionary lookup makes, the per-frame allocation this class avoids. Cast back only where a name is needed (NameFor, WriteDotLine).
        private readonly Dictionary<(int Victim, int AttackerActor, int AttackerTeam, int WeaponId, int AbilityId, int Source), DotAccumulator> dotsByKey =
            new Dictionary<(int, int, int, int, int, int), DotAccumulator>();

        // ---------------------------------------------------------------- goldEarned
        //
        // Territory/bounty/refund/debug/other totals, summed from GoldWallet.Credited every time it fires and flushed to a `goldEarned`
        // line every sample interval (FlushGoldEarned). Per-zone attribution accumulates every Update, one frame's delta at a time,
        // against the LIVE owners/tiers (AccumulateZoneIncome), so a zone that changes hands mid-interval is split correctly instead of
        // attributed wholesale to whoever owns it at the end.

        private int territoryCreditedThisInterval;
        private int bountyCreditedThisInterval;
        private int refundCreditedThisInterval;
        private int debugCreditedThisInterval;
        private int otherCreditedThisInterval;

        // Reused every frame/flush, never reallocated once ZoneCount is known (mirrors GoldWallet's ownersScratch/teamGoldByTierScratch
        // so the two cannot disagree about a frame's income).
        private int[] zoneOwnersScratch;
        private int[] teamGoldByTierScratch;
        private double[] perZoneGoldScratch;
        private int[] perZoneGoldWholeScratch;

        // ---------------------------------------------------------------- heal
        //
        // Health regenerated while alive and not hit this frame, bucketed by the tier of the zone stood in (index 0 = not in any zone,
        // so a heal outside any zone is still counted) and flushed per sample interval (PollHeal/FlushHeal).

        private double[] healByTierScratch;
        private int[] healByTierWholeScratch;
        private float lastHealthForHealPoll = -1f;
        private bool tookDamageThisFrame;
        private bool wasAliveLastFrameForHealPoll = true;

        private void Awake()
        {
            // Nobody but the owner logs their own combat/economy facts (class comment); every field below stays null on a remote copy,
            // which Start's subscriptions and Update's polling rely on never running.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            if (config == null)
                Debug.LogError($"[PlayerTelemetry] {name}: TelemetryConfig is not assigned - the sample interval and Record Positions fall back to hardcoded defaults.");
            if (territoryConfig == null)
                Debug.LogError($"[PlayerTelemetry] {name}: Territory Config is not assigned - goldEarned's per-zone attribution will always read as empty.");

            playerHealth = GetComponent<PlayerHealth>();
            statusEffects = GetComponent<PlayerStatusEffects>();
            lifecycle = GetComponent<PlayerLifecycle>();
            abilityRunner = GetComponent<AbilityRunner>();
            overPowerBuff = GetComponent<OverPowerBuff>();
            overheat = GetComponent<PlayerOverheat>();
            ultimateCharge = GetComponent<UltimateCharge>();
            combatCredit = GetComponent<PlayerCombatCredit>();
            goldWallet = GetComponent<GoldWallet>();
            loadoutScreen = GetComponent<LoadoutScreen>();
            body = GetComponent<Rigidbody>();
            // Not on the root: a child lookup, as in OverPowerBuff and PlayerLifecycle.
            weaponFiring = GetComponentInChildren<WeaponFiring>(true);

            if (playerHealth == null || statusEffects == null || lifecycle == null || weaponFiring == null)
                Debug.LogError($"[PlayerTelemetry] {name}: missing PlayerHealth/PlayerStatusEffects/PlayerLifecycle/WeaponFiring - most events cannot be logged for this player.");
        }

        private void Start()
        {
            aliveSinceTime = Time.time;

            if (playerHealth != null)
            {
                playerHealth.Damaged += HandleDamaged;
                playerHealth.Died += HandleDied;
            }
            if (statusEffects != null)
                statusEffects.StatusApplied += HandleStatusApplied;
            if (lifecycle != null)
                lifecycle.AliveChanged += HandleAliveChanged;
            if (weaponFiring != null)
                weaponFiring.Fired += HandleFired;
            if (abilityRunner != null)
            {
                abilityRunner.Cast += HandleCast;
                abilityRunner.FollowUpCast += HandleFollowUpCast;
            }
            if (overPowerBuff != null)
            {
                overPowerBuff.Triggered += HandleOverpowerTriggered;
                overPowerBuff.Ended += HandleOverpowerEnded;
            }
            if (goldWallet != null)
                goldWallet.Credited += HandleCredited;
            if (loadoutScreen != null)
            {
                loadoutScreen.Purchased += HandlePurchased;
                loadoutScreen.Refunded += HandleRefunded;
                loadoutScreen.PurchaseRefused += HandlePurchaseRefused;
            }
            // Flush shots/dots before the writer closes, whether this object's OnDestroy runs before or after MatchTelemetry's (see BeforeClose).
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose += HandleBeforeClose;

            // Static event: a dummy is not networked and lives in exactly one client's scene, so the only right listener is that client's
            // own local player (see DummyTarget.AnyDamaged).
            DummyTarget.AnyDamaged += HandleDummyDamaged;
        }

        private void OnDestroy()
        {
            // Unity calls OnDestroy on every component being torn down, even one Awake left disabled. A remote copy's fields are all still
            // null, so the unsubscribes are harmless no-ops, but the unconditional HandleBeforeClose() at the end would reach
            // MatchTelemetry.Instance (shared by every player) and write an all-zero `goldEarned` line into THIS client's file whenever any
            // OTHER player's object was destroyed.
            if (!photonView.IsMine)
                return;

            if (playerHealth != null)
            {
                playerHealth.Damaged -= HandleDamaged;
                playerHealth.Died -= HandleDied;
            }
            if (statusEffects != null)
                statusEffects.StatusApplied -= HandleStatusApplied;
            if (lifecycle != null)
                lifecycle.AliveChanged -= HandleAliveChanged;
            if (weaponFiring != null)
                weaponFiring.Fired -= HandleFired;
            if (abilityRunner != null)
            {
                abilityRunner.Cast -= HandleCast;
                abilityRunner.FollowUpCast -= HandleFollowUpCast;
            }
            if (overPowerBuff != null)
            {
                overPowerBuff.Triggered -= HandleOverpowerTriggered;
                overPowerBuff.Ended -= HandleOverpowerEnded;
            }
            if (goldWallet != null)
                goldWallet.Credited -= HandleCredited;
            if (loadoutScreen != null)
            {
                loadoutScreen.Purchased -= HandlePurchased;
                loadoutScreen.Refunded -= HandleRefunded;
                loadoutScreen.PurchaseRefused -= HandlePurchaseRefused;
            }
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose -= HandleBeforeClose;
            DummyTarget.AnyDamaged -= HandleDummyDamaged;

            // On death/leave: this player object is about to stop existing, so anything accumulated since the last sample must not be
            // lost. BeforeClose additionally covers MatchTelemetry closing the writer before this OnDestroy would run.
            HandleBeforeClose();
        }

        private void HandleBeforeClose()
        {
            FlushShots();
            FlushDots();
            FlushGoldEarned();
            FlushHeal();
        }

        private void Update()
        {
            if (MatchTelemetry.Instance == null)
                return;

            PollOverheat();
            PollUltimateCharge();
            // Every frame, not gated by the sample interval: health can be hit and healed several times inside one interval, and a zone
            // can change hands, so only a per-frame poll sees each (AccumulateZoneIncome, PollHeal).
            AccumulateZoneIncome();
            PollHeal();

            float interval = config != null ? config.SampleIntervalSeconds : 5f;
            sampleTimer += Time.unscaledDeltaTime;
            if (sampleTimer < interval)
                return;

            sampleTimer = 0f;
            WriteSample();
            FlushShots();
            FlushDots();
            FlushGoldEarned();
            FlushHeal();
        }

        // ---------------------------------------------------------------- overheat / ultimate polling

        private void PollOverheat()
        {
            if (overheat == null)
                return;

            bool silenced = overheat.IsSilenced;
            if (silenced == wasSilenced)
                return;
            wasSilenced = silenced;

            line.Begin(TelemetryKeys.Overheat, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, silenced ? "silenced" : "recovered");
            line.Int(TelemetryKeys.Weapon, weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : -1);
            MatchTelemetry.Instance.Log(line);
        }

        private void PollUltimateCharge()
        {
            if (ultimateCharge == null)
                return;

            bool full = ultimateCharge.IsFull;
            if (full == wasUltimateFull)
                return;
            wasUltimateFull = full;

            if (!full)
                return; // Only the false -> true edge is `ultimateReady` (going down just means it was spent).

            ultimateReadyTime = Time.time;

            line.Begin(TelemetryKeys.UltimateReady, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.AbilityId, abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : -1);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- economy

        private void HandleCredited(int amount, GoldSource source)
        {
            switch (source)
            {
                case GoldSource.Territory: territoryCreditedThisInterval += amount; break;
                case GoldSource.Bounty: bountyCreditedThisInterval += amount; break;
                case GoldSource.Refund: refundCreditedThisInterval += amount; break;
                case GoldSource.Debug: debugCreditedThisInterval += amount; break;
                default: otherCreditedThisInterval += amount; break;
            }
        }

        private void HandlePurchased(PurchaseCategory category, int itemId, int price, int balanceAfter, bool free)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Purchase, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.Category, CategoryName(category));
            line.Int(TelemetryKeys.ItemId, itemId);
            line.Int(TelemetryKeys.Price, price);
            line.Int(TelemetryKeys.BalanceAfter, balanceAfter);
            line.Int(TelemetryKeys.Zone, CurrentZone());
            line.Bool(TelemetryKeys.Free, free);
            MatchTelemetry.Instance.Log(line);
        }

        private void HandleRefunded(PurchaseCategory category, int amount, int balanceAfter)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Refund, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.Category, CategoryName(category));
            line.Int(TelemetryKeys.Amount, amount);
            line.Int(TelemetryKeys.BalanceAfter, balanceAfter);
            line.Int(TelemetryKeys.Zone, CurrentZone());
            MatchTelemetry.Instance.Log(line);
        }

        private void HandlePurchaseRefused(int itemId, int price, PurchaseBlock reason, int shortfall)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.ShopBlocked, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.ItemId, itemId);
            line.Int(TelemetryKeys.Price, price);
            line.String(TelemetryKeys.Reason, ReasonName(reason));
            line.Int(TelemetryKeys.Shortfall, shortfall);
            line.Int(TelemetryKeys.Zone, CurrentZone());
            MatchTelemetry.Instance.Log(line);
        }

        private static string CategoryName(PurchaseCategory category)
        {
            switch (category)
            {
                case PurchaseCategory.Weapon: return "weapon";
                case PurchaseCategory.Armor: return "armor";
                case PurchaseCategory.Attachment: return "attachment";
                case PurchaseCategory.Mobility: return "mobility";
                case PurchaseCategory.Ultimate: return "ultimate";
                default: return "other";
            }
        }

        private static string ReasonName(PurchaseBlock block)
        {
            switch (block)
            {
                case PurchaseBlock.NotInOwnTerritory: return "territory";
                case PurchaseBlock.InCombat: return "combat";
                case PurchaseBlock.CannotAfford: return "gold";
                case PurchaseBlock.NotInBreak: return "not in break";
                default: return "unknown";
            }
        }

        private int CurrentZone()
        {
            int zone = -1;
            BuildingManager.Instance?.TryGetZoneAt(MyPosition, out zone);
            return zone;
        }

        /// <summary>Called every Update (not once per sample interval) with THIS FRAME's delta against the LIVE owners/tiers, as
        /// GoldWallet.Update accrues its balance from the same two arrays, so a zone that changes hands mid-interval is split between
        /// the teams that held it before and after.</summary>
        private void AccumulateZoneIncome()
        {
            if (territoryConfig == null || playerHealth == null)
                return;

            BuildingManager manager = BuildingManager.Instance;
            if (manager == null || manager.Current == null)
                return;

            int team = playerHealth.TeamId;
            if (team < 0)
                return;

            TerritorySnapshot current = manager.Current;
            int zoneCount = current.ZoneCount;
            if (zoneOwnersScratch == null || zoneOwnersScratch.Length != zoneCount)
                zoneOwnersScratch = new int[zoneCount];
            if (perZoneGoldScratch == null || perZoneGoldScratch.Length != zoneCount)
                perZoneGoldScratch = new double[zoneCount];

            for (int zone = 0; zone < zoneCount; zone++)
                zoneOwnersScratch[zone] = current.OwnerOf(zone);

            int[] tiers = manager.TierByZone();
            int[] teamGoldByTier = TeamGoldByTierFromConfig();

            IncomeAttribution.Accumulate(team, zoneOwnersScratch, tiers, teamGoldByTier,
                                         territoryConfig.PlayersPerTeam, Time.unscaledDeltaTime, perZoneGoldScratch);
        }

        private int[] TeamGoldByTierFromConfig()
        {
            int count = territoryConfig.TierCount;
            if (teamGoldByTierScratch == null || teamGoldByTierScratch.Length != count)
                teamGoldByTierScratch = new int[count];
            for (int i = 0; i < count; i++)
                teamGoldByTierScratch[i] = territoryConfig.ForTier(i + 1).teamGoldPerSecond;
            return teamGoldByTierScratch;
        }

        /// <summary>Every sample interval (and once more at BeforeClose for a trailing partial interval): the totals summed since the
        /// last flush plus the per-zone split accumulated frame by frame. `zones` is rounded to whole gold per zone; the `terr` total
        /// (GoldWallet.Credited(Territory), the same whole-gold crossings the wallet publishes) stays the authoritative total. Writes
        /// nothing when every total is zero, so no redundant all-zero line follows an interval that already flushed.</summary>
        private void FlushGoldEarned()
        {
            if (MatchTelemetry.Instance == null)
                return;

            bool anyZoneGold = false;
            if (perZoneGoldScratch != null)
            {
                for (int i = 0; i < perZoneGoldScratch.Length; i++)
                {
                    if (perZoneGoldScratch[i] > 0.0001)
                    {
                        anyZoneGold = true;
                        break;
                    }
                }
            }

            if (territoryCreditedThisInterval == 0 && bountyCreditedThisInterval == 0 && refundCreditedThisInterval == 0
                && debugCreditedThisInterval == 0 && otherCreditedThisInterval == 0 && !anyZoneGold)
                return;

            line.Begin(TelemetryKeys.GoldEarned, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Territory, territoryCreditedThisInterval);
            line.Ints(TelemetryKeys.Zones, RoundedZoneArray());
            line.Int(TelemetryKeys.Bounty, bountyCreditedThisInterval);
            line.Int(TelemetryKeys.Refund, refundCreditedThisInterval);
            line.Int(TelemetryKeys.Debug, debugCreditedThisInterval);
            line.Int(TelemetryKeys.Other, otherCreditedThisInterval);
            MatchTelemetry.Instance.Log(line);

            territoryCreditedThisInterval = 0;
            bountyCreditedThisInterval = 0;
            refundCreditedThisInterval = 0;
            debugCreditedThisInterval = 0;
            otherCreditedThisInterval = 0;
            if (perZoneGoldScratch != null)
                for (int i = 0; i < perZoneGoldScratch.Length; i++)
                    perZoneGoldScratch[i] = 0;
        }

        private int[] RoundedZoneArray()
        {
            int len = perZoneGoldScratch != null ? perZoneGoldScratch.Length : 0;
            if (perZoneGoldWholeScratch == null || perZoneGoldWholeScratch.Length != len)
                perZoneGoldWholeScratch = new int[len];
            for (int i = 0; i < len; i++)
                perZoneGoldWholeScratch[i] = (int)System.Math.Round(perZoneGoldScratch[i]);
            return perZoneGoldWholeScratch;
        }

        // ---------------------------------------------------------------- heal

        /// <summary>Polled every Update: a health increase while alive and not hit this frame (tookDamageThisFrame, set in
        /// HandleDamaged), not a respawn's full heal (wasAliveLastFrameForHealPoll: the `respawn` event already records coming back to
        /// life, and counting that jump as healing would hugely overstate zone regen), and not a jump bigger than any real zone regen
        /// could produce in one frame (MaxPlausibleHealThisFrame: the F1 "Heal" button's instant full heal was otherwise counted as
        /// regen).</summary>
        private void PollHeal()
        {
            if (playerHealth == null || lifecycle == null)
                return;

            bool aliveNow = lifecycle.IsAlive;
            float currentHealth = playerHealth.Health;

            if (!aliveNow)
            {
                wasAliveLastFrameForHealPoll = false;
                lastHealthForHealPoll = currentHealth;
                tookDamageThisFrame = false;
                return;
            }

            bool justRespawned = !wasAliveLastFrameForHealPoll;
            wasAliveLastFrameForHealPoll = true;

            if (lastHealthForHealPoll < 0f)
            {
                lastHealthForHealPoll = currentHealth; // First frame ever - no prior value to diff against.
            }
            else if (!justRespawned && !tookDamageThisFrame)
            {
                float delta = currentHealth - lastHealthForHealPoll;
                if (delta > 0.0001f && delta <= MaxPlausibleHealThisFrame())
                    AccumulateHeal(delta);
            }

            lastHealthForHealPoll = currentHealth;
            tookDamageThisFrame = false;
        }

        // -1 = not yet computed. TerritoryConfig's tier rates are fixed for the match (a retune in Play Mode is the one exception this
        // poll does not follow), so this is worked out once instead of scanning every tier each frame.
        private float cachedMaxTierRegenPerSecond = -1f;

        /// <summary>The most health any REAL zone regen could add in one frame: the fastest tier's healthRegenPerSecond times this
        /// frame's delta time, doubled for slack against a slow frame or two coalescing. A bigger jump (the F1 "Heal" button's instant
        /// full heal, or ResetForRespawn's full-health snap outside the justRespawned window) is not regen and must not be counted as
        /// any zone's.</summary>
        private float MaxPlausibleHealThisFrame()
        {
            if (cachedMaxTierRegenPerSecond < 0f)
            {
                float max = 0f;
                if (territoryConfig != null)
                {
                    for (int tier = 1; tier <= territoryConfig.TierCount; tier++)
                        max = Mathf.Max(max, territoryConfig.ForTier(tier).healthRegenPerSecond);
                }
                cachedMaxTierRegenPerSecond = max;
            }
            return cachedMaxTierRegenPerSecond * Time.unscaledDeltaTime * 2f;
        }

        private void AccumulateHeal(float amount)
        {
            int tierCount = territoryConfig != null ? territoryConfig.TierCount : 4;
            if (healByTierScratch == null || healByTierScratch.Length != tierCount + 1)
                healByTierScratch = new double[tierCount + 1];

            int zone = CurrentZone();
            int tier = zone >= 0 && BuildingManager.Instance != null ? BuildingManager.Instance.TierOf(zone) : 0;
            int index = tier >= 1 && tier < healByTierScratch.Length ? tier : 0; // Index 0 = no zone / tier not registered yet.
            healByTierScratch[index] += amount;
        }

        /// <summary>Every sample interval (and at BeforeClose): tier -> whole health points regenerated; skipped when nothing healed
        /// (the "don't write an empty line" convention FlushShots uses).</summary>
        private void FlushHeal()
        {
            if (MatchTelemetry.Instance == null || healByTierScratch == null)
                return;

            bool any = false;
            for (int i = 0; i < healByTierScratch.Length; i++)
            {
                if (healByTierScratch[i] > 0.0001)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
                return;

            if (healByTierWholeScratch == null || healByTierWholeScratch.Length != healByTierScratch.Length)
                healByTierWholeScratch = new int[healByTierScratch.Length];
            for (int i = 0; i < healByTierScratch.Length; i++)
            {
                healByTierWholeScratch[i] = Mathf.RoundToInt((float)healByTierScratch[i]);
                healByTierScratch[i] = 0;
            }

            line.Begin(TelemetryKeys.Heal, MatchTelemetry.Instance.Now);
            line.Ints(TelemetryKeys.HealTiers, healByTierWholeScratch);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- shots

        /// <summary>newPull is true once per trigger pull (always true for a Simultaneous/shotgun weapon's single call, true only for
        /// round 0 of a burst/Sequential weapon's several calls): only that edge increments Pulls, so a 3-round burst reports pulls=1
        /// per press.</summary>
        private void HandleFired(int weaponId, int projectileCount, bool newPull)
        {
            if (!shotsByWeapon.TryGetValue(weaponId, out ShotAccumulator acc))
            {
                acc = new ShotAccumulator();
                shotsByWeapon[weaponId] = acc;
            }

            if (newPull)
                acc.Pulls++;
            acc.Projectiles += projectileCount;
        }

        private void FlushShots()
        {
            if (MatchTelemetry.Instance == null)
                return;

            foreach (KeyValuePair<int, ShotAccumulator> pair in shotsByWeapon)
            {
                if (pair.Value.Pulls == 0 && pair.Value.Projectiles == 0)
                    continue;

                line.Begin(TelemetryKeys.Shots, MatchTelemetry.Instance.Now);
                line.Int(TelemetryKeys.Weapon, pair.Key);
                line.Int(TelemetryKeys.Pulls, pair.Value.Pulls);
                line.Int(TelemetryKeys.Projectiles, pair.Value.Projectiles);
                MatchTelemetry.Instance.Log(line);
                pair.Value.Reset();
            }
        }

        // ---------------------------------------------------------------- cast / ultimate used

        // A follow-up to an earlier cast (the AoE Zone's throw): a `cast` line tagged followUp, and NO
        // `ultimateUsed` - the ultimate was used once, when the zone went down.
        private void HandleFollowUpCast(AbilitySlot slot, int abilityId)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Cast, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Slot, (int)slot);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.X, MyPosition.x);
            line.Float(TelemetryKeys.Z, MyPosition.z);
            line.Int(TelemetryKeys.FollowUp, 1);
            MatchTelemetry.Instance.Log(line);
        }

        private void HandleCast(AbilitySlot slot, int abilityId)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Cast, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Slot, (int)slot);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.X, MyPosition.x);
            line.Float(TelemetryKeys.Z, MyPosition.z);
            MatchTelemetry.Instance.Log(line);

            if (slot != AbilitySlot.Ultimate)
                return;

            float sinceReady = ultimateReadyTime >= 0f ? Time.time - ultimateReadyTime : -1f;
            ultimateReadyTime = -1f; // Spent - the next `ultimateReady` starts a fresh wait.

            // For Invulnerability (id 25) this `ultimateUsed` line means "committed at this instant", not "was protected": the ultimate
            // arms a trap for armedSeconds, and the commit may protect nobody (an unanswered cast is wasted, no refund). Whether it DID
            // is answered by the `status` line that follows (or doesn't): TryConsumeReactiveInvulnerability applies
            // StatusKind.Invulnerability with abilityId 25 the moment a hit triggers it, so the `ultimateUsed(ab=25)` count minus the
            // `status(ab=25, effect=invulnerability)` count over a match IS the wasted-cast rate, with no new key.
            //
            // Count only the status line whose `effect` reads "invulnerability" (HandleStatusApplied names every status line's kind via
            // NameFor(kind)): if cachedStunSeconds is ever dialled up from its 0 default, the SAME trigger also writes a second status
            // line, StatusKind.Stun with the same abilityId 25, and counting every `status(ab=25)` line would double-count each trigger
            // and could push the wasted-cast rate negative.
            line.Begin(TelemetryKeys.UltimateUsed, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.SecondsSinceReady, sinceReady);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- status

        private void HandleStatusApplied(StatusKind kind, int sourceActor, int abilityId, float duration, float magnitude)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Status, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.Effect, NameFor(kind));
            line.Int(TelemetryKeys.SourceActor, sourceActor);
            line.Int(TelemetryKeys.AbilityId, abilityId);
            line.Float(TelemetryKeys.Duration, duration);
            line.Float(TelemetryKeys.DurationOrMagnitude, magnitude);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- overpower

        private void HandleOverpowerTriggered()
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Overpower, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, "triggered");
            line.Float(TelemetryKeys.ZoneDistance, ZoneDistance());
            line.Float(TelemetryKeys.HealthAtTrigger, playerHealth != null ? playerHealth.Health : 0f);
            MatchTelemetry.Instance.Log(line);
        }

        private void HandleOverpowerEnded(string reason)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Overpower, MatchTelemetry.Instance.Now);
            line.String(TelemetryKeys.State, "ended");
            line.String(TelemetryKeys.Reason, reason);
            line.Float(TelemetryKeys.ZoneDistance, ZoneDistance());
            line.Float(TelemetryKeys.HealthAtTrigger, playerHealth != null ? playerHealth.Health : 0f);
            MatchTelemetry.Instance.Log(line);
        }

        private float ZoneDistance()
        {
            if (BuildingManager.Instance == null || playerHealth == null)
                return float.PositiveInfinity;

            return BuildingManager.Instance.DistanceToOwnedZoneEdge(MyPosition, playerHealth.TeamId);
        }

        // ---------------------------------------------------------------- hit / dot (real player + dummy)

        /// <summary>PlayerHealth.Damaged, on this player's own (victim's) client.</summary>
        private void HandleDamaged(DamageResult result, DamageInfo info)
        {
            // PollHeal's "not taking damage that frame" guard, set here because nothing else exposes "was hit this frame" as a public flag.
            tookDamageThisFrame = true;

            int victimTeam = playerHealth != null ? playerHealth.TeamId : -1;
            float distance = DistanceToAttacker(info.SourceActorNumber);
            float vulnerability = statusEffects != null ? statusEffects.Vulnerability : 0f;
            bool overpowerActive = overPowerBuff != null && overPowerBuff.IsActive;

            RouteHit(photonView.OwnerActorNr, victimTeam, info, result, distance, vulnerability, overpowerActive);
        }

        /// <summary>DummyTarget.AnyDamaged: a dummy is not networked, so the only client that sees this event is the one whose local
        /// player fired the shot (the test range is where Tudor tunes weapons, so these hits matter as much as a real player's). Victim
        /// actor/team are -1: a dummy has no Photon identity (DummyTarget.ActorNumber is -1 by the same convention).</summary>
        private void HandleDummyDamaged(DummyTarget dummy, DamageResult result, DamageInfo info)
        {
            if (PhotonNetwork.LocalPlayer == null)
                return;

            // Only this player's own shots landing on a dummy are this client's to log; a dummy is plain scenery with no owner guard,
            // so this is the one place that check happens.
            if (info.SourceActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            float distance = Vector3.Distance(MyPosition, dummy.transform.position);
            RouteHit(-1, -1, info, result, distance, dummy.Vulnerability, overpowerActive: false);
        }

        /// <summary>Routes one landed hit to an immediate `hit` line for Projectile/Splash/Zone/Contact, or into a per-key
        /// DotAccumulator bucket for DamageSource.Burn. A lethal Burn tick flushes its own bucket as a `dot` line first, so the sums
        /// leading up to a kill are not lost, then still logs the normal `hit` line so every kill keeps a row.</summary>
        private void RouteHit(int victim, int victimTeam, DamageInfo info, DamageResult result,
                              float distance, float vulnerability, bool overpowerActive)
        {
            if (info.Source != DamageSource.Burn)
            {
                WriteHitLine(victim, victimTeam, info, result, distance, vulnerability, overpowerActive);
                return;
            }

            var key = (victim, info.SourceActorNumber, info.SourceTeamId, info.WeaponId, info.AbilityId, (int)info.Source);
            if (!dotsByKey.TryGetValue(key, out DotAccumulator dot))
            {
                dot = new DotAccumulator();
                dotsByKey[key] = dot;
            }

            double now = MatchTelemetry.Instance != null ? MatchTelemetry.Instance.Now : -1.0;
            dot.Merge(now, info.Amount, result.ArmorAbsorbed, result.HealthLost);

            if (!result.Lethal)
                return;

            WriteDotLine(key, dot);
            dot.Reset();
            WriteHitLine(victim, victimTeam, info, result, distance, vulnerability, overpowerActive);
        }

        private void WriteHitLine(int victim, int victimTeam, DamageInfo info, DamageResult result,
                                  float distance, float vulnerability, bool overpowerActive)
        {
            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Hit, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Attacker, info.SourceActorNumber);
            line.Int(TelemetryKeys.AttackerTeam, info.SourceTeamId);
            line.Int(TelemetryKeys.Victim, victim);
            line.Int(TelemetryKeys.VictimTeam, victimTeam);
            line.Int(TelemetryKeys.Weapon, info.WeaponId);
            line.Int(TelemetryKeys.AbilityId, info.AbilityId);
            line.String(TelemetryKeys.Source, NameFor(info.Source));
            line.Float(TelemetryKeys.Raw, info.Amount);
            line.Float(TelemetryKeys.ArmorAbsorbed, result.ArmorAbsorbed);
            line.Float(TelemetryKeys.HealthLost, result.HealthLost);
            line.Bool(TelemetryKeys.Lethal, result.Lethal);
            line.Float(TelemetryKeys.Distance, distance);
            line.Float(TelemetryKeys.Vulnerable, vulnerability);
            // Invulnerable is not written: PlayerHealth.ApplyDamage returns BEFORE Damaged fires when the victim is invulnerable, and a
            // dummy never checks (see TelemetryKeys.Invulnerable).
            line.Bool(TelemetryKeys.OverpowerActive, overpowerActive);
            // Appended LAST, and only when this hit touched a mark, so hit lines from non-marking weapons stay byte-identical.
            if (result.Mark != MarkOutcome.None)
                line.Int(TelemetryKeys.Mark, (int)result.Mark);
            MatchTelemetry.Instance.Log(line);
        }

        private void WriteDotLine((int Victim, int AttackerActor, int AttackerTeam, int WeaponId, int AbilityId, int Source) key,
                                  DotAccumulator dot)
        {
            if (MatchTelemetry.Instance == null || !dot.HasData)
                return;

            // A dummy (Victim -1) has no team; a real victim here is always this player's own.
            int victimTeam = key.Victim == -1 ? -1 : (playerHealth != null ? playerHealth.TeamId : -1);

            line.Begin(TelemetryKeys.Dot, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Attacker, key.AttackerActor);
            line.Int(TelemetryKeys.AttackerTeam, key.AttackerTeam);
            line.Int(TelemetryKeys.Victim, key.Victim);
            line.Int(TelemetryKeys.VictimTeam, victimTeam);
            line.Int(TelemetryKeys.Weapon, key.WeaponId);
            line.Int(TelemetryKeys.AbilityId, key.AbilityId);
            line.String(TelemetryKeys.Source, NameFor((DamageSource)key.Source));
            line.Int(TelemetryKeys.Ticks, dot.Ticks);
            line.Float(TelemetryKeys.Raw, dot.RawSum);
            line.Float(TelemetryKeys.ArmorAbsorbed, dot.ArmorSum);
            line.Float(TelemetryKeys.HealthLost, dot.HealthSum);
            line.Float(TelemetryKeys.FirstT, (float)dot.FirstT);
            line.Float(TelemetryKeys.LastT, (float)dot.LastT);
            MatchTelemetry.Instance.Log(line);
        }

        private void FlushDots()
        {
            if (MatchTelemetry.Instance == null)
                return;

            foreach (var pair in dotsByKey)
            {
                if (!pair.Value.HasData)
                    continue;

                WriteDotLine(pair.Key, pair.Value);
                pair.Value.Reset();
            }
        }

        /// <summary>Distance from the attacker's replicated position to this player (the victim): PlayerNetSync.NetworkPosition once it
        /// has received anything from its owner (HasReceivedFromOwner), else the attacker's raw transform. PositiveInfinity - which
        /// TelemetryLine.Float writes as JSON null - when the attacker's PhotonView cannot be found (already left the room).</summary>
        private float DistanceToAttacker(int attackerActor)
        {
            PhotonView attackerView = PlayerLookup.GetPhotonViewFor(attackerActor);
            if (attackerView == null)
                return float.PositiveInfinity;

            PlayerNetSync netSync = attackerView.GetComponent<PlayerNetSync>();
            Vector3 attackerPosition = netSync != null && netSync.HasReceivedFromOwner
                ? netSync.NetworkPosition
                : attackerView.transform.position;

            return Vector3.Distance(attackerPosition, MyPosition);
        }

        // ---------------------------------------------------------------- death / respawn

        /// <summary>PlayerHealth.Died, on this player's own (victim's) client.</summary>
        private void HandleDied(DamageInfo info)
        {
            deathTime = Time.time;

            // Continuous damage leading up to this kill belongs in the report as its own `dot` row, flushed BEFORE `death` (as shots
            // flush on death). A lethal Burn tick already flushed its own bucket in RouteHit; this catches every OTHER bucket still
            // accumulating.
            FlushDots();

            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Death, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Killer, info.SourceActorNumber);
            // info.SourceTeamId, not Teams.TryGetTeam(info.SourceActorNumber, ...): the latter reads -1 if the killer has left the room,
            // disagreeing with `hit`'s AttackerTeam (the value recorded AT the hit).
            line.Int(TelemetryKeys.KillerTeam, info.SourceTeamId);
            line.Int(TelemetryKeys.Weapon, info.WeaponId);
            line.Int(TelemetryKeys.AbilityId, info.AbilityId);
            line.Ints(TelemetryKeys.Assists, AssistArray());
            line.Float(TelemetryKeys.X, MyPosition.x);
            line.Float(TelemetryKeys.Z, MyPosition.z);
            line.Float(TelemetryKeys.TimeAlive, Time.time - aliveSinceTime);
            line.Int(TelemetryKeys.UnspentGold, goldWallet != null ? goldWallet.Balance : 0);
            WriteLoadout(useDeathKeys: true);
            MatchTelemetry.Instance.Log(line);

            // Shots flush on death (and on leave, in OnDestroy).
            FlushShots();
        }

        private int[] AssistArray()
        {
            // A freshness guard, not blind trust in subscriber ordering: PlayerCombatCredit.LastDeathAssisters is only meaningful for
            // THIS death if its LastDeathTime stamp (set in the same synchronous PlayerHealth.Died chain) matches - otherwise it is
            // empty or a previous life's list.
            if (combatCredit == null || combatCredit.LastDeathTime != deathTime)
                return System.Array.Empty<int>();

            IReadOnlyList<int> assisters = combatCredit.LastDeathAssisters;
            var array = new int[assisters.Count];
            for (int i = 0; i < assisters.Count; i++)
                array[i] = assisters[i];
            return array;
        }

        private void HandleAliveChanged(bool alive)
        {
            if (!alive)
                return; // Death itself is logged from HandleDied above, off PlayerHealth.Died.

            // A rejoined player's fresh body never saw the death (no Died event on this instance), so deathTime is
            // still 0 and the subtraction would read "dead since the process started". No known death = no time dead.
            float timeDead = deathTime > 0f ? Time.time - deathTime : 0f;
            aliveSinceTime = Time.time;

            if (MatchTelemetry.Instance == null)
                return;

            // A fresh start (ResetForMatchStart reviving a player who was dead when the match went live) fires this same
            // AliveChanged(true) (PlayerLifecycle.LastAliveChangeWasFreshStart). Its LastRespawnWasUnderAttackSpawn is never touched by a
            // fresh start and would read as the last REAL respawn's, so false is written instead (a fresh start always lands at the
            // plain team spawn), and the line is marked `fresh:true` so the report reads it as "brought back by going live", not an
            // ordinary respawn.
            bool freshStart = lifecycle != null && lifecycle.LastAliveChangeWasFreshStart;

            line.Begin(TelemetryKeys.Respawn, MatchTelemetry.Instance.Now);
            line.Float(TelemetryKeys.X, MyPosition.x);
            line.Float(TelemetryKeys.Z, MyPosition.z);
            line.Float(TelemetryKeys.TimeDead, timeDead);
            line.Bool(TelemetryKeys.UnderAttackSpawn, !freshStart && lifecycle != null && lifecycle.LastRespawnWasUnderAttackSpawn);
            if (freshStart)
                line.Bool(TelemetryKeys.Fresh, true);
            MatchTelemetry.Instance.Log(line);
        }

        // ---------------------------------------------------------------- sample

        private void WriteSample()
        {
            if (MatchTelemetry.Instance == null)
                return;

            bool recordPositions = config == null || config.RecordPositions;
            int zone = -1;
            BuildingManager.Instance?.TryGetZoneAt(MyPosition, out zone);

            line.Begin(TelemetryKeys.Sample, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Balance, goldWallet != null ? goldWallet.Balance : 0);
            if (recordPositions)
            {
                line.Float(TelemetryKeys.X, MyPosition.x);
                line.Float(TelemetryKeys.Z, MyPosition.z);
            }
            line.Bool(TelemetryKeys.Alive, lifecycle == null || lifecycle.IsAlive);
            line.Int(TelemetryKeys.Zone, zone);
            line.Int(TelemetryKeys.Team, playerHealth != null ? playerHealth.TeamId : -1);
            WriteLoadout(useDeathKeys: false);
            line.Float(TelemetryKeys.Health, playerHealth != null ? playerHealth.Health : 0f);
            line.Float(TelemetryKeys.Armor, playerHealth != null ? playerHealth.Armor : 0f);
            line.Float(TelemetryKeys.UltimateCharge, ultimateCharge != null ? ultimateCharge.Normalised : 0f);
            line.Float(TelemetryKeys.OverheatLevel, overheat != null ? overheat.Normalised : 0f);
            line.Int(TelemetryKeys.Ping, PhotonNetwork.GetPing());
            MatchTelemetry.Instance.Log(line);
        }

        /// <summary>This player's own loadout ids and both armor levels, written into whichever event is being built (callers Begin
        /// first). `sample` writes Weapon/Attachment/Mobility/Ultimate directly; `death` already uses those keys for the KILLING
        /// weapon/ability, so it uses the Loadout* keys (see TelemetryKeys).</summary>
        private void WriteLoadout(bool useDeathKeys)
        {
            int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
            int attachmentId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Attachment) : LoadoutProperties.Empty;
            int mobilityId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Mobility) : LoadoutProperties.Empty;
            int ultimateId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : LoadoutProperties.Empty;

            line.Int(useDeathKeys ? TelemetryKeys.LoadoutWeapon : TelemetryKeys.Weapon, weaponId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutAttachment : TelemetryKeys.Attachment, attachmentId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutMobility : TelemetryKeys.Mobility, mobilityId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutUltimate : TelemetryKeys.Ultimate, ultimateId);
            line.Int(TelemetryKeys.AbsorbLevel, playerHealth != null ? playerHealth.AbsorbLevel : 0);
            line.Int(TelemetryKeys.RechargeLevel, playerHealth != null ? playerHealth.RechargeLevel : 0);
        }
    }
}
