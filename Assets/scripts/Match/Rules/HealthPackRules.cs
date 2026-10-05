namespace Overpower.Match
{
    /// <summary>The rules of the Tier III health packs, with no engine types so every one is tested.
    ///
    /// A pack's state is ONE Room Property per pack, int[] { takenUntilMs, takerActor, requestId }; a pack with
    /// no property has never been taken. Only the master writes it (see HealthPackManager). Times are the
    /// server clock in ms, which wraps, so they are only ever compared as unchecked(now - until) >= 0.</summary>
    public static class HealthPackRules
    {
        public const int TakenUntilIndex = 0;
        public const int TakerActorIndex = 1;
        public const int RequestIdIndex = 2;
        public const int ValueLength = 3;

        /// <summary>The tier of the zones that carry a health pack (Tier III: the corner zones between the capitals and the centre).</summary>
        public const int PackTier = 3;

        /// <summary>Which zones get a pack built now, from the three answers about the room's mode: none while the mode is unknown or the mode has no packs
        /// (2v2 Dominion), otherwise every zone of the pack tier that has none yet. <paramref name="settled"/> says nothing more will ever be built in this
        /// room (the mode has no packs, or every zone is registered and has what it needs), so the manager stops asking each frame. One method, taking the
        /// answers, so swapping two of them is caught by a test of the value.</summary>
        public static System.Collections.Generic.List<int> ZonesToBuild(bool modeKnown, bool dominion, int teamCount, int zoneCount,
                                                                         System.Func<int, int> baseTierOf, System.Func<int, bool> hasPack, out bool settled)
        {
            var zones = new System.Collections.Generic.List<int>();
            settled = false;
            if (!Overpower.Dominion.DominionRules.MayBuildHealthPacks(modeKnown, dominion, teamCount))
            {
                settled = modeKnown; // known and no packs here: nothing to wait for; unknown: ask again next frame
                return zones;
            }
            bool allRegistered = zoneCount > 0;
            for (int zone = 0; zone < zoneCount; zone++)
            {
                int tier = baseTierOf(zone);
                if (tier == 0) allRegistered = false; // its tower has not registered yet
                if (tier == PackTier && !hasPack(zone)) zones.Add(zone);
            }
            settled = allRegistered && zones.Count == 0;
            return zones;
        }

        /// <summary>The master's answer to one request: whether it is granted, and if so the value to write.</summary>
        public readonly struct Decision
        {
            public readonly bool Granted;
            public readonly int[] NewValue;
            public Decision(bool granted, int[] newValue) { Granted = granted; NewValue = newValue; }
        }

        /// <summary>Available when nothing was ever taken here, or the take has run its course.</summary>
        public static bool IsAvailable(int[] value, int nowMs) =>
            value == null || value.Length < ValueLength || unchecked(nowMs - value[TakenUntilIndex]) >= 0;

        /// <summary>Same, but a take made before the match went live no longer counts: going live is a fresh start.
        /// liveAtMs 0 = not live yet (nothing is reset). A take's own time is takenUntil - respawn.</summary>
        public static bool IsAvailable(int[] value, int nowMs, int liveAtMs, int respawnMs)
        {
            if (value == null || value.Length < ValueLength) return true;
            if (liveAtMs != 0 && unchecked(value[TakenUntilIndex] - respawnMs - liveAtMs) < 0) return true;
            return IsAvailable(value, nowMs);
        }

        /// <summary>The player's own side: alive, missing at least minMissing health (and always at least a little), and a
        /// pack that is there in a zone that is in play.</summary>
        public static bool MayTake(bool alive, float health, float maxHealth, float minMissing, bool available, bool zoneInPlay)
        {
            float missing = maxHealth - health;
            return alive && missing > 0f && missing >= minMissing && available && zoneInPlay;
        }

        /// <summary>How much of the pack's heal actually lands: never past the max.</summary>
        public static float HealAmount(float packAmount, float current, float max)
        {
            float missing = max - current;
            if (missing <= 0f || packAmount <= 0f) return 0f;
            return missing < packAmount ? missing : packAmount;
        }

        /// <summary>The master: grant a request only if everything it can know holds and the pack is free
        /// at server-now (available = the caller's answer, which may include the go-live reset). The caller passes
        /// the pack's latest value (including any write of its own still on its way), so a second request for the
        /// same pack sees it taken.</summary>
        public static Decision Decide(int[] currentValue, int nowMs, bool requesterAlive, bool zoneInPlay,
                                      bool requesterInRange, int requesterActor, int requestId, int respawnMs,
                                      int liveAtMs)
        {
            if (!requesterAlive || !zoneInPlay || !requesterInRange || !IsAvailable(currentValue, nowMs, liveAtMs, respawnMs))
                return new Decision(false, null);
            return new Decision(true, new[] { unchecked(nowMs + respawnMs), requesterActor, requestId });
        }

        /// <summary>The taker's client heals when it READS the echo: the value names me, names a request I have sent
        /// (a retry may already be out, so the granted id can be older than my latest) that has not healed me yet,
        /// and the take is recent (a stale value from an earlier life of the room, or one further ahead than a take
        /// can be, heals nobody).</summary>
        public static bool EchoIsMyFreshTake(int[] value, int myActor, int myLatestRequestId, int lastHealedRequestId,
                                             int nowMs, int respawnMs)
        {
            if (value == null || value.Length < ValueLength) return false;
            int id = value[RequestIdIndex];
            if (value[TakerActorIndex] != myActor) return false;
            if (id <= lastHealedRequestId || id > myLatestRequestId) return false;
            int untilNow = unchecked(value[TakenUntilIndex] - nowMs); // ms of the take still to run
            return untilNow > 0 && untilNow <= respawnMs;
        }

        /// <summary>One outstanding request per pack; if no echo has come back for retryAfterSeconds, ask again.</summary>
        public static bool MayRequestNow(bool hasOutstanding, float secondsSinceSent, float retryAfterSeconds) =>
            !hasOutstanding || secondsSinceSent >= retryAfterSeconds;
    }
}
