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
    /// Player prefab, owner only: logs everything this player's own client is the sole authority on
    /// (Principle 2 of the design doc - "a hit" is logged by the victim, "shots"/"casts" by the
    /// shooter/caster, all of which are always THIS player when this component is enabled at all).
    /// Every T3 event goes through MatchTelemetry.Instance.Log, which no-ops when telemetry is off
    /// or the session's file never opened - this class never checks that itself.
    ///
    /// REMOTE COPIES DO NOTHING: Awake disables this component outright when photonView.IsMine is
    /// false, before any GetComponent call or subscription runs - the same shape OverPowerBuff's own
    /// Awake already uses. Deleting Assets/scripts/Telemetry still leaves the game running (design
    /// doc, Principle 1): nothing outside this folder depends on this class existing.
    ///
    /// ALLOCATION: one TelemetryLine is built once and reused for every event this component ever
    /// raises (Begin/.../End is safe to call again immediately - see TelemetryLine's own class
    /// comment); the shots/dots accumulators are reset in place rather than reallocated on every
    /// flush (T3 review item 11); event-name strings come from static arrays instead of
    /// Enum.ToString(). The only heap traffic per frame is the two cheap bool reads in Update's
    /// polling (IsSilenced, IsFull), not a single line built or logged unless something actually
    /// happened or the sample interval elapsed.
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

        /// <summary>T3 review fix (item 2): PlayerDisplacement.TeleportTo writes rb.position
        /// directly; Unity does not sync transform.position from it until the next physics step, so
        /// a position read in the SAME frame as a teleport (respawn's own AliveChanged(true), fired
        /// synchronously right after TeleportToSpawnPoint) read the OLD position through transform
        /// while rb.position was already correct - measured live: a respawn logged the corpse's own
        /// x/z instead of the spawn point. MyPosition below is used for every "this player's own
        /// position" field, not just respawn's, since reading through the Rigidbody is never wrong
        /// and is sometimes measurably right where transform.position is not.</summary>
        private Rigidbody body;
        private Vector3 MyPosition => body != null ? body.position : transform.position;

        private readonly TelemetryLine line = new TelemetryLine();

        private float sampleTimer;

        // Polled state transitions (Update), never events of their own - see the design doc's
        // "Changes from the spec" note.
        private bool wasSilenced;
        private bool wasUltimateFull;

        // Time.time this ultimate last became ready (IsFull false -> true), or -1 while it either
        // has not filled yet or was already spent since - ultimateUsed's own "seconds since ready".
        private float ultimateReadyTime = -1f;

        // Time.time this life started - Start() for the very first life, AliveChanged(true) for
        // every respawn after. death's own "time alive".
        private float aliveSinceTime;

        // Time.time the most recent death happened - respawn's own "time dead", and the freshness
        // stamp AssistArray checks against PlayerCombatCredit.LastDeathTime (T3 review item 9).
        private float deathTime;

        // Index-matched to their enums (Combat/DamageInfo.cs, Combat/StatusEffectState.cs) - T3
        // review item 11: avoids a ToString()/ToLowerInvariant() allocation on every hit/dot/status
        // line. NameFor falls back to ToString() only if an enum ever grows past these arrays.
        private static readonly string[] DamageSourceNames = { "Projectile", "Splash", "Burn", "Zone", "Contact" };
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

        // Entries persist for the whole match and are Reset() in place on every flush (sample
        // interval, death/leave, BeforeClose) rather than removed - T3 review item 11: the dictionary
        // itself only ever grows to "how many distinct weapons this player fired", and reusing the
        // same ShotAccumulator instances avoids reallocating one every flush.
        private readonly Dictionary<int, ShotAccumulator> shotsByWeapon = new Dictionary<int, ShotAccumulator>();

        /// <summary>T3 review item 1 (VOLUME): continuous damage (DamageSource.Burn - status burn
        /// and FireField's DoT both use it) is bucketed here instead of logging one `hit` line per
        /// tick - measured at ~129 lines/second for a single burning victim at Editor framerate.
        /// Keyed exactly as the review specified: victim, attacker actor/team, weapon, ability,
        /// source. Same reset-in-place reasoning as shotsByWeapon above.</summary>
        // Source is stored as int, not DamageSource - re-review (T3): a DamageSource-valued tuple
        // field boxes the enum on every Equals/GetHashCode call a Dictionary lookup makes (an enum's
        // default GetHashCode goes through object.GetHashCode unless the underlying value type
        // itself is used), which is exactly the kind of per-frame allocation this class otherwise
        // goes out of its way to avoid (see the class comment's ALLOCATION paragraph). Cast back to
        // DamageSource only where a name is needed (NameFor) - see WriteDotLine.
        private readonly Dictionary<(int Victim, int AttackerActor, int AttackerTeam, int WeaponId, int AbilityId, int Source), DotAccumulator> dotsByKey =
            new Dictionary<(int, int, int, int, int, int), DotAccumulator>();

        // ---------------------------------------------------------------- goldEarned (Task T4)
        //
        // Territory/bounty/refund/debug/other totals, summed from GoldWallet.Credited every time it
        // fires (not once per interval) and flushed to a `goldEarned` line every sample interval -
        // see FlushGoldEarned. Per-zone attribution is separate: it accumulates every Update, one
        // frame's delta at a time, against the LIVE owners/tiers (see AccumulateZoneIncome) - the
        // corrected plan step 3, so a zone that changes hands mid-interval is split correctly instead
        // of attributed wholesale to whoever owns it at the end of the interval.

        private int territoryCreditedThisInterval;
        private int bountyCreditedThisInterval;
        private int refundCreditedThisInterval;
        private int debugCreditedThisInterval;
        private int otherCreditedThisInterval;

        // Reused every frame/flush - never reallocated once ZoneCount is known (same reasoning as
        // GoldWallet's own ownersScratch/teamGoldByTierScratch, which this mirrors exactly so the two
        // can never disagree about what a frame's income was).
        private int[] zoneOwnersScratch;
        private int[] teamGoldByTierScratch;
        private double[] perZoneGoldScratch;
        private int[] perZoneGoldWholeScratch;

        // ---------------------------------------------------------------- heal (Task T4)
        //
        // Health regenerated while alive and not hit this frame, bucketed by the tier of the zone
        // stood in at the moment it happened (index 0 = not standing in any zone - not one of the
        // plan's own tiers, kept so a heal outside any zone is still counted rather than silently
        // dropped) and flushed per sample interval - see PollHeal/FlushHeal.

        private double[] healByTierScratch;
        private int[] healByTierWholeScratch;
        private float lastHealthForHealPoll = -1f;
        private bool tookDamageThisFrame;
        private bool wasAliveLastFrameForHealPoll = true;

        private void Awake()
        {
            // Nobody but the owner should log their own combat/economy facts - see the class
            // comment. Every field below stays null on a remote copy, which is what Start's
            // subscriptions and Update's polling both rely on never running.
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
            // Not on the root - see WeaponFiring's own siblings (OverPowerBuff, PlayerLifecycle)
            // for the identical GetComponentInChildren lookup.
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
                abilityRunner.Cast += HandleCast;
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
            // T3 review item 10: flush shots/dots before the writer actually closes, regardless of
            // whether this object's own OnDestroy happens to run before or after MatchTelemetry's -
            // see BeforeClose's own comment.
            if (MatchTelemetry.Instance != null)
                MatchTelemetry.Instance.BeforeClose += HandleBeforeClose;

            // Static event (Task T3): a dummy is not networked and lives in exactly one client's
            // scene, so the only listener that could ever be right is that same client's own local
            // player - see DummyTarget.AnyDamaged's own comment.
            DummyTarget.AnyDamaged += HandleDummyDamaged;
        }

        private void OnDestroy()
        {
            // Opus review fix: Unity calls OnDestroy on every component being torn down, even one
            // Awake left permanently disabled - a remote copy's own fields below are all still null
            // (Start never ran to populate them), so every unsubscribe here was already a harmless
            // no-op, but the unconditional HandleBeforeClose() call at the end was not: it reached
            // MatchTelemetry.Instance (the scene singleton, shared by every player including remote
            // ones) and wrote an all-zero `goldEarned` line into THIS client's own file every time
            // any OTHER player's object was destroyed (left the room, or disconnected).
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
                abilityRunner.Cast -= HandleCast;
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

            // "On death/leave" - see the plan's own `shots` bullet. This player object is about to
            // stop existing (this client leaving the room, or the player itself being destroyed),
            // so anything accumulated since the last sample must not be lost. BeforeClose (above)
            // additionally covers the case where MatchTelemetry closes the writer before this
            // OnDestroy would otherwise run.
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
            // Every frame, not gated by the sample interval below - see AccumulateZoneIncome's own
            // comment (T4 corrected plan step 3) and PollHeal's (health can be hit and healed several
            // times inside one sample interval; only a per-frame poll sees every one of those).
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

        // ---------------------------------------------------------------- economy (Task T4)

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
                case PurchaseCategory.Equipment: return "equipment";
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
                default: return "unknown";
            }
        }

        private int CurrentZone()
        {
            int zone = -1;
            BuildingManager.Instance?.TryGetZoneAt(MyPosition, out zone);
            return zone;
        }

        /// <summary>Task T4 corrected plan step 3: called every Update (not once per sample interval)
        /// with THIS FRAME's delta time against the LIVE owners/tiers, exactly as GoldWallet.Update
        /// accrues its own balance from the same two arrays - so a zone that changes hands mid-
        /// interval is split correctly between the team that held it before and the team that holds
        /// it after, instead of the whole interval being misattributed to whichever team happens to
        /// own it at the moment the interval is flushed.</summary>
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

        /// <summary>Every sample interval (and once more at BeforeClose, for a trailing partial
        /// interval): the Territory/bounty/refund/debug/other totals summed since the last flush,
        /// plus the per-zone split accumulated frame-by-frame above. `zones` is rounded to whole
        /// gold per zone; the `terr` total (from GoldWallet.Credited(Territory), the same whole-gold
        /// crossings the wallet itself publishes) stays the authoritative total - see the T4 step 3
        /// check this is verified against. Writes nothing when every total is zero (opus review fix)
        /// - an all-zero line was previously written every interval regardless, including one extra
        /// redundant time whenever BeforeClose ran right after an interval had already flushed
        /// everything back to zero.</summary>
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

        // ---------------------------------------------------------------- heal (Task T4)

        /// <summary>Polled every Update: a health increase while alive, not hit this frame (see
        /// HandleDamaged's own tookDamageThisFrame flag), not the instantaneous jump a respawn's
        /// full heal produces (guarded by wasAliveLastFrameForHealPoll - respawn's own `respawn` event
        /// already records the fact of coming back to life; counting that jump again here as "healing"
        /// would hugely overstate the zone regen this event exists to measure), and not a jump bigger
        /// than any real zone regen could produce in one frame (opus review fix - see
        /// MaxPlausibleHealThisFrame: the F1 "Heal" button's own instant full heal was otherwise
        /// counted as regen from whichever zone tier the player happened to be standing in).</summary>
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

        // -1 = not yet computed. TerritoryConfig's tier rates are fixed for the whole match (a
        // designer retuning them in Play Mode is the one exception nobody has ever asked this poll
        // to react to live), so this is worked out once rather than re-scanning up to 4 tiers every
        // single frame.
        private float cachedMaxTierRegenPerSecond = -1f;

        /// <summary>Opus review fix: the most health any REAL zone regen could plausibly add in one
        /// frame - the fastest tier's healthRegenPerSecond times this frame's delta time, doubled for
        /// slack against a slow frame or two coalescing. A jump bigger than this (the F1 "Heal"
        /// button's instant full heal, or ResetForRespawn's own full-health snap outside the
        /// justRespawned guard's own window) is not regen and must not be counted as any zone's.</summary>
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

        /// <summary>Every sample interval (and at BeforeClose): tier -> whole health points
        /// regenerated, skipped entirely when nothing healed this interval (same "don't write an
        /// empty line" convention FlushShots already uses).</summary>
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

        /// <summary>T3 review item 3: newPull is true once per trigger pull (always true for a
        /// Simultaneous/shotgun weapon's single call, true only for round 0 of a burst/Sequential
        /// weapon's several calls) - only that edge increments Pulls, so a 3-round burst weapon
        /// reports pulls=1 per press instead of 3.</summary>
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

            // Rework step 4 (2026-09-18): for Invulnerability (id 25) specifically, this `ultimateUsed`
            // line now means "committed at this instant", not "was protected at this instant" - the
            // rework arms a trap for armedSeconds instead of shielding right away, and the commit may
            // end up protecting nobody at all (an unanswered cast is wasted, no refund). Whether it DID
            // protect anyone is answered by the `status` line that follows (or doesn't):
            // TryConsumeReactiveInvulnerability applies StatusKind.Invulnerability with abilityId 25 the
            // moment a hit triggers it, so `ultimateUsed(ab=25)` count minus `status(ab=25, effect=
            // invulnerability)` count over a match IS the wasted-cast rate, for free, with no new key
            // here.
            //
            // Review fix (2026-09-18): count only the status line whose `effect` field - the same field
            // HandleStatusApplied below always names a status line's kind with, via NameFor(kind) - reads
            // "invulnerability". If cachedStunSeconds is ever dialled up from its 0 default, the SAME
            // trigger also writes a SECOND status line, for StatusKind.Stun, with the SAME abilityId 25
            // (TryConsumeReactiveInvulnerability applies both from one trigger) - counting every
            // `status(ab=25)` line regardless of effect would then double-count each trigger and could
            // push the wasted-cast rate negative. Every other ultimate's line is unaffected - this one
            // only changed because the ability's own timing did.
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
            // Task T4: PollHeal's own "not taking damage that frame" guard - see its comment. Set
            // here rather than read from PlayerHealth directly because nothing else already exposes
            // "was hit this frame" as a public flag.
            tookDamageThisFrame = true;

            int victimTeam = playerHealth != null ? playerHealth.TeamId : -1;
            float distance = DistanceToAttacker(info.SourceActorNumber);
            float vulnerability = statusEffects != null ? statusEffects.Vulnerability : 0f;
            bool overpowerActive = overPowerBuff != null && overPowerBuff.IsActive;

            RouteHit(photonView.OwnerActorNr, victimTeam, info, result, distance, vulnerability, overpowerActive);
        }

        /// <summary>DummyTarget.AnyDamaged: a dummy has no owner of its own (it is not networked -
        /// see its class comment), so the only client that can ever see this event is the one whose
        /// local player actually fired the shot - the test range is where Tudor tunes weapons, so
        /// these hits matter as much as a real player's. Victim actor/team are -1: a dummy has no
        /// Photon identity to report (DummyTarget.ActorNumber is already -1 by the same convention).</summary>
        private void HandleDummyDamaged(DummyTarget dummy, DamageResult result, DamageInfo info)
        {
            if (PhotonNetwork.LocalPlayer == null)
                return;

            // Only this player's own shots landing on a dummy are this client's to log - see the
            // class comment. A dummy is plain scenery with no owner guard of its own, so this is
            // the one place that check has to happen.
            if (info.SourceActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                return;

            float distance = Vector3.Distance(MyPosition, dummy.transform.position);
            RouteHit(-1, -1, info, result, distance, dummy.Vulnerability, overpowerActive: false);
        }

        /// <summary>T3 review item 1 (VOLUME): routes one landed hit to an immediate `hit` line for
        /// Projectile/Splash/Zone/Contact, or into a per-key DotAccumulator bucket for
        /// DamageSource.Burn (status burn and FireField's DoT both tick every frame - logging one
        /// `hit` per tick measured ~129 lines/second for a single burning victim). A lethal Burn tick
        /// flushes its own bucket as a `dot` line first, so the sums leading up to a kill are not
        /// lost, then still logs the normal `hit` line so every kill keeps a row.</summary>
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
            // Invulnerable dropped (T3 review item 6): PlayerHealth.ApplyDamage returns BEFORE
            // Damaged fires when the victim is already invulnerable, so a real player's `hit` line
            // could never read true; a dummy never checks invulnerability at all, so its own reading
            // would not mean "this hit was blocked" either. See TelemetryKeys.Invulnerable's comment.
            line.Bool(TelemetryKeys.OverpowerActive, overpowerActive);
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

        /// <summary>Distance from the attacker's own replicated position to this player (the
        /// victim) - PlayerNetSync.NetworkPosition once it has received anything from its owner,
        /// falling back to the attacker's raw transform otherwise (see HasReceivedFromOwner's own
        /// comment). PositiveInfinity - which TelemetryLine.Float writes as JSON null - when the
        /// attacker's PhotonView cannot be found at all (already left the room).</summary>
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

            // Continuous damage leading up to this kill (if any) belongs in the report as its own
            // `dot` row, flushed BEFORE `death` - see FlushDots and the plan's own `shots` bullet for
            // the identical "flush before death" rule. A lethal Burn tick already flushed its own
            // bucket in RouteHit; this catches every OTHER bucket that was still accumulating.
            FlushDots();

            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Death, MatchTelemetry.Instance.Now);
            line.Int(TelemetryKeys.Killer, info.SourceActorNumber);
            // T3 review item 8: info.SourceTeamId, not Teams.TryGetTeam(info.SourceActorNumber, ...) -
            // the latter reads -1 if the killer has since left the room, disagreeing with `hit`'s own
            // AttackerTeam (which always uses SourceTeamId, the value recorded AT the hit).
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

            // "on death/leave" - see FlushShots's own callers.
            FlushShots();
        }

        private int[] AssistArray()
        {
            // T3 review item 9: a freshness guard, not blind trust in subscriber ordering.
            // PlayerCombatCredit.LastDeathAssisters is only meaningful for THIS death if its own
            // LastDeathTime stamp (set in the same synchronous PlayerHealth.Died chain, the same
            // Time.time) matches - otherwise it is empty or a previous life's list.
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

            float timeDead = Time.time - deathTime;
            aliveSinceTime = Time.time;

            if (MatchTelemetry.Instance == null)
                return;

            line.Begin(TelemetryKeys.Respawn, MatchTelemetry.Instance.Now);
            line.Float(TelemetryKeys.X, MyPosition.x);
            line.Float(TelemetryKeys.Z, MyPosition.z);
            line.Float(TelemetryKeys.TimeDead, timeDead);
            line.Bool(TelemetryKeys.UnderAttackSpawn, lifecycle != null && lifecycle.LastRespawnWasUnderAttackSpawn);
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

        /// <summary>Weapon/equipment/mobility/ultimate ids and both armor levels - this player's own
        /// loadout, written into whichever event is currently being built (callers Begin first).
        /// `sample` has no other use for Weapon/Equipment/Mobility/Ultimate, so it writes them
        /// directly; `death` already uses those same keys for the KILLING weapon/ability (matching
        /// `hit`'s convention), so it needs the separate Loadout* keys instead - see their own
        /// comment on TelemetryKeys.</summary>
        private void WriteLoadout(bool useDeathKeys)
        {
            int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
            int equipmentId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Equipment) : LoadoutProperties.Empty;
            int mobilityId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Mobility) : LoadoutProperties.Empty;
            int ultimateId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : LoadoutProperties.Empty;

            line.Int(useDeathKeys ? TelemetryKeys.LoadoutWeapon : TelemetryKeys.Weapon, weaponId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutEquipment : TelemetryKeys.Equipment, equipmentId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutMobility : TelemetryKeys.Mobility, mobilityId);
            line.Int(useDeathKeys ? TelemetryKeys.LoadoutUltimate : TelemetryKeys.Ultimate, ultimateId);
            line.Int(TelemetryKeys.AbsorbLevel, playerHealth != null ? playerHealth.AbsorbLevel : 0);
            line.Int(TelemetryKeys.RechargeLevel, playerHealth != null ? playerHealth.RechargeLevel : 0);
        }
    }
}
