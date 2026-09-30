using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Match
{
    /// <summary>Task T4: where a credit to this wallet came from, so telemetry (PlayerTelemetry's
    /// `goldEarned`) can split a player's income by source instead of only seeing one growing
    /// balance. Territory is the passive per-frame trickle GoldMath pays every owned zone;
    /// everything else is a discrete credit from one call to Add.</summary>
    public enum GoldSource { Territory, Bounty, Refund, Debug, Other }

    /// <summary>
    /// One player's gold (Task 2.2). OWNER-AUTHORITATIVE: the owner's own client is the only one
    /// that accrues and spends gold, publishing the result as a Player Custom Property ("gold") so
    /// every other client - and a late joiner - can read it. This deviates from the 2026-09-12
    /// plan's master-ticked design [C, assumptions-for-tudor.md "Gold authority"]: a master-ticked
    /// wallet needs a new RPC per player per tick (up to nine players, every frame), races
    /// TrySpend against the next income tick landing from the master, and freezes every wallet
    /// until a newly promoted master rebuilds all of them from scratch. Letting each player own
    /// its own number sidesteps all three for free, and a prototype has no anti-cheat requirement
    /// that would make "trust the owner" a problem.
    ///
    /// The flow mirrors PlayerLoadout: the OWNER applies every change locally first (so spending
    /// feels instant), then publishes it; every OTHER client only ever reads the published
    /// property; a LATE JOINER reads whatever value is already there.
    /// </summary>
    public class GoldWallet : MonoBehaviourPun, IInRoomCallbacks
    {
        public const string GoldKey = "gold";

        // How long the owner waits between two publishes that are ONLY driven by passive income -
        // gold trickles in a fraction of a point per frame (Task 2.2: as low as 1.667/s), and a
        // Custom Property write every single frame for every player in the room would be room
        // traffic nobody needs, for a number nobody needs to see move within a frame. TrySpend/Add
        // bypass this timer entirely (see their own calls to PublishBalance) - a shop purchase or a
        // bounty payout (Task 2.4) has to feel instant, income does not.
        private const float MinPassivePublishIntervalSeconds = 1f;

        [SerializeField, Tooltip("Shared per-tier numbers: starting gold and each tier's team " +
                 "income. Read live every frame (never cached at spawn) so a designer can retune " +
                 "income in Play Mode and see this player's rate change immediately.")]
        private TerritoryConfig territoryConfig;

        // Owner only. Null on every remote copy - a remote wallet has nothing of its own to
        // simulate, it only ever reads the owner's published property (see Balance below).
        private GoldAccrual accrual;

        private float lastPublishTime = -999f;
        private int lastPublishedBalance;

        // Rebuilt from TerritoryConfig.ForTier each Update rather than cached-and-invalidated: the
        // config rarely changes, but GoldMath.TeamIncomePerSecond wants an indexable list and there
        // are at most four tiers, so rebuilding is cheaper than tracking whether the asset moved.
        private int[] teamGoldByTierScratch;

        // Same idea as teamGoldByTierScratch above, for the owners array GoldMath.TeamIncomePerSecond
        // also wants: Update used to allocate a fresh int[ZoneCount] every single frame for every
        // player's wallet. ZoneCount does not change mid-match, so one buffer, resized only if it
        // ever does, replaces that per-frame allocation.
        private int[] ownersScratch;

        /// Owner: the accrual's live balance. Remote copy: the owner's last-published "gold"
        /// Player Property (0 if it has never been written, e.g. read for one frame before Start
        /// has run on either side).
        public int Balance => photonView.IsMine
            ? (accrual != null ? accrual.Balance : 0)
            : LoadoutProperties.ReadInt(photonView.Owner?.CustomProperties, GoldKey, 0);

        /// Owner only - always 0 on a remote copy, which has no local income simulation to report
        /// an instantaneous rate from (only its balance is Task 2.2's requirement).
        public double IncomePerSecond { get; private set; }

        /// Raised on every client whenever the balance THIS client reports changes: on the owner,
        /// every time passive income crosses a whole gold or TrySpend/Add fires; on a remote copy,
        /// every time the owner's "gold" property arrives with a new value.
        public event System.Action<int> BalanceChanged;

        /// Owner only (Task 2.4): raised exactly when THIS wallet is credited a capture bounty - a
        /// narrower signal than BalanceChanged/Add, which also fire for passive income and the F1
        /// "+1000 Gold" test button. Only the HUD toast ("Bounty +900") listens to this; the balance
        /// itself is credited through the ordinary Add(amount) call below, same as any other credit.
        public event System.Action<int> BountyReceived;

        /// <summary>Task T4: owner only - raised every time this wallet's own balance goes UP,
        /// whatever the reason (passive territory income crossing a whole gold, a bounty, a shop
        /// refund, the F1 debug credit, or any other Add call). PlayerTelemetry sums these by
        /// GoldSource into `goldEarned`'s per-source totals every sample interval. A remote copy
        /// never raises this - see Add/TrySpend's own "owner only" guards, which this piggybacks
        /// on rather than duplicating.</summary>
        public event System.Action<int, GoldSource> Credited;

        /// <summary>Task T4: owner only - raised every time TrySpend actually spends gold (a shop
        /// purchase). PlayerTelemetry's `purchase`/`refund` events already carry the amount
        /// themselves; this exists for anything else that wants "gold left this wallet" without
        /// caring why.</summary>
        public event System.Action<int> Spent;

        private void Awake()
        {
            if (territoryConfig == null)
                Debug.LogError($"[GoldWallet] {name}: Territory Config is not assigned - starting gold and all territory income will read as 0.");
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        private void Start()
        {
            if (!photonView.IsMine)
                return;

            // A fresh GoldWallet component is not always a fresh player: a scene reload or a rejoin
            // that re-instantiates the player object runs this Start again while the room still
            // remembers this actor's real "gold" Player Property from before. Always starting from
            // StartingGold and republishing would silently wipe that balance back to 0 the moment
            // the new component's Start ran - read it back first if the room already has it.
            bool hasExistingBalance = PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey(GoldKey);
            int startingBalance = hasExistingBalance
                ? LoadoutProperties.ReadInt(PhotonNetwork.LocalPlayer.CustomProperties, GoldKey, 0)
                : (territoryConfig != null ? territoryConfig.StartingGold : 0);

            accrual = new GoldAccrual(startingBalance);

            if (hasExistingBalance)
            {
                // Already published by whichever earlier Start wrote it - nothing changed, so
                // nothing to re-send. Recorded so PublishIfDue's own "did the balance change"
                // check does not see a spurious diff against the default 0 on the very next frame.
                lastPublishedBalance = startingBalance;
            }
            else
            {
                // A genuinely new player: every other client, and anyone who joins later, should
                // read the real starting balance instead of guessing it is 0 from a missing
                // property - the same reasoning PlayerLoadout's starting-weapon publish uses.
                PublishBalance();
            }

            // Task 2.4: only the owner's own wallet ever pays itself a bounty - a remote copy has no
            // accrual to credit anyway (see Balance's own comment). BuildingManager raises this event
            // on EVERY client whose applied snapshot changed a zone's owner, including a zone this
            // player's team did not just take, so HandleOwnershipChanged below is what filters that
            // down to "did MY team just take this zone, and was there a bounty on it".
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged += HandleOwnershipChanged;
            else
                Debug.LogError($"[GoldWallet] {name}: no BuildingManager in the scene - capture bounties will never be paid.");
        }

        private void OnDestroy()
        {
            if (BuildingManager.Instance != null)
                BuildingManager.Instance.OwnershipChanged -= HandleOwnershipChanged;
        }

        /// <summary>Task 2.4: pays this wallet's owner a capture bounty the moment their team takes a
        /// zone that had one. Not raised for a late joiner's first read of the room's snapshot (see
        /// BuildingManager.OwnershipChanged's own doc), so nobody is ever paid twice for a capture
        /// that already happened before they connected. bountyPaid is read off the snapshot ITSELF
        /// rather than recomputed here - BountyRule.PayoutOnCapture already ran once, on the master,
        /// inside BuildingManager.SetCaptured, against the write basis at the moment of capture; this
        /// is a plain client-side reaction to what the master decided, not a second, possibly
        /// differently-timed judgement of the same rule.</summary>
        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
        {
            if (!photonView.IsMine || accrual == null)
                return;
            if (!Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam) || newOwner != myTeam)
                return;

            int bounty = snapshot.BountyPaidOnLastCapture(zone);
            if (bounty <= 0)
                return;

            Add(bounty, GoldSource.Bounty);
            BountyReceived?.Invoke(bounty);
        }

        private void Update()
        {
            if (!photonView.IsMine || accrual == null)
                return;

            if (PhotonNetwork.InRoom && territoryConfig != null && BuildingManager.Instance != null &&
                BuildingManager.Instance.Current != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int team))
            {
                TerritorySnapshot current = BuildingManager.Instance.Current;
                if (ownersScratch == null || ownersScratch.Length != current.ZoneCount)
                    ownersScratch = new int[current.ZoneCount];
                for (int zone = 0; zone < ownersScratch.Length; zone++)
                    ownersScratch[zone] = current.OwnerOf(zone);

                int[] tiers = BuildingManager.Instance.TierByZone();
                int[] teamGoldByTier = TeamGoldByTierFromConfig();

                int teamIncome = GoldMath.TeamIncomePerSecond(team, ownersScratch, tiers, teamGoldByTier);
                double playerIncome = GoldMath.PlayerIncomePerSecond(teamIncome, territoryConfig.PlayersPerTeam);
                IncomePerSecond = playerIncome;

                // Dead players keep earning: territory income belongs to the team holding the
                // ground, not to whoever happens to be alive to stand on it right now [C,
                // assumptions-for-tudor.md]. Time.unscaledDeltaTime, not deltaTime, so a debug
                // Time.timeScale change (EditorApplication.isPaused, a slow-mo test) can never
                // speed up or freeze the economy.
                int balanceBeforeAccrual = accrual.Balance;
                accrual.Accrue(playerIncome, Time.unscaledDeltaTime);
                // Task T4: GoldAccrual only ever shows WHOLE gold (its own class comment) - the
                // fractional carry between frames means most frames credit nothing at all, and
                // this fires only the frame a whole gold point is actually crossed, exactly
                // matching what PublishIfDue below would (eventually) tell the room.
                int wholeGoldCrossed = accrual.Balance - balanceBeforeAccrual;
                if (wholeGoldCrossed > 0)
                    Credited?.Invoke(wholeGoldCrossed, GoldSource.Territory);
            }
            else
            {
                IncomePerSecond = 0.0;
            }

            PublishIfDue();
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

        /// Owner only. Refused - with a warning, never a silent no-op that looks like it worked -
        /// on a remote copy, the same contract Add shares below.
        public bool TrySpend(int amount)
        {
            if (!photonView.IsMine)
            {
                Debug.LogWarning($"[GoldWallet] TrySpend({amount}) refused on a remote copy - only the owner's own client may spend its gold.");
                return false;
            }
            if (accrual == null || !accrual.TrySpend(amount))
                return false;

            PublishBalance();
            Spent?.Invoke(amount);
            return true;
        }

        /// Owner only - see TrySpend's comment. Used by the bounty payout (Task 2.4), shop
        /// refunds and purchases (Task 2.5b/T4) and the F1 "+1000 Gold" test button below
        /// (Task 2.2 Step 7), so shop testing never waits on income. Task T4: source defaults to
        /// Other rather than being required, so every pre-existing call site (the bounty payout
        /// above already passes its own, the F1 button and LoadoutScreen's refunds are updated
        /// too) keeps compiling unchanged.
        public void Add(int amount, GoldSource source = GoldSource.Other)
        {
            if (!photonView.IsMine)
            {
                Debug.LogWarning($"[GoldWallet] Add({amount}) refused on a remote copy - only the owner's own client may credit its own gold.");
                return;
            }
            if (accrual == null || amount <= 0)
                return;

            accrual.Add(amount);
            PublishBalance();
            Credited?.Invoke(amount, source);
        }

        /// <summary>2.7b Decision 6: the fresh start at match-live - gold back to TerritoryConfig.StartingGold
        /// (a brand new GoldAccrual, so the fractional carry drops with it, same as a genuinely new player's
        /// first Start), no income rate carried over, and published at once rather than waiting for
        /// PublishIfDue's own throttle. Owner only, like every other mutator on this class.</summary>
        public void ResetForMatchStart()
        {
            if (!photonView.IsMine)
                return;

            accrual = new GoldAccrual(territoryConfig != null ? territoryConfig.StartingGold : 0);
            IncomePerSecond = 0.0;
            PublishBalance();
        }

        private void PublishIfDue()
        {
            if (accrual.Balance == lastPublishedBalance)
                return;
            if (Time.unscaledTime - lastPublishTime < MinPassivePublishIntervalSeconds)
                return;

            PublishBalance();
        }

        private void PublishBalance()
        {
            if (!PhotonNetwork.InRoom)
                return;

            int balance = accrual != null ? accrual.Balance : 0;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { GoldKey, balance } });
            lastPublishedBalance = balance;
            lastPublishTime = Time.unscaledTime;
            BalanceChanged?.Invoke(balance);
        }

        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (photonView.Owner == null || targetPlayer != photonView.Owner)
                return;

            // The owner already applied and reported this before publishing it - reacting to
            // Photon's echo of our own write would just raise BalanceChanged a second time for the
            // same value. Same guard PlayerLoadout/PlayerLifecycle use for their own properties.
            if (photonView.IsMine)
                return;

            if (!changedProps.ContainsKey(GoldKey))
                return;

            BalanceChanged?.Invoke(Balance);
        }

        // Unused IInRoomCallbacks members - same pattern as PlayerLoadout.
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }
}
