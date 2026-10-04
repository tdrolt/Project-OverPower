namespace Overpower.Dominion
{
    /// <summary>
    /// The respawn shield (Task 1 rules; the component is Task 7). A player who respawns is shielded for a few seconds so a spawn camper
    /// cannot kill them the instant they appear. The shield ends at a server-clock time (an int in ms that wraps, so it is only compared as
    /// unchecked(now - end)); 0 means no shield is up. While it is up the player neither captures a zone nor blocks an enemy capture, so the
    /// shield cannot be used to hold a zone for free.
    /// </summary>
    public static class RespawnShieldRules
    {
        /// <summary>True while now is before the end time. 0 = never any shield (the cleared value).</summary>
        public static bool IsUp(int endMs, int nowMs) => endMs != 0 && unchecked(nowMs - endMs) < 0;

        /// <summary>The value the shield owner writes when it learns it hit an enemy: 0 = cleared, shield down. Attacking from inside the
        /// shield would be a free shot, so the first hit on an enemy (damage, stun, slow or push) drops it.</summary>
        public static int EndAfterDamageDealt() => 0;

        /// <summary>An up shield blocks incoming damage (and shows BLOCKED).</summary>
        public static bool BlocksDamage(bool up) => up;

        /// <summary>A shielded player neither captures nor blocks a capture; once the shield is down they count as anyone.</summary>
        public static bool CountsForCapture(bool shieldUp) => !shieldUp;

        /// <summary>The Player Property the shield owner writes: the server ms the shield ends (0 = none). Every client draws and judges from it.</summary>
        public const string ShieldKey = "dShd";
        /// <summary>The Player Property the shield owner stamps (server ms) each time a hit is stopped, so every client can pop BLOCKED over them.</summary>
        public const string BlockedKey = "dBlk";

        /// <summary>The server ms a shield started now ends. Never 0 (that value means no shield); the int wraps like the room's other times.</summary>
        public static int EndMs(int nowMs, float seconds)
        {
            int end = unchecked(nowMs + (int)System.Math.Round(seconds * 1000f));
            return end == 0 ? 1 : end;
        }

        /// <summary>A shield starts only for a player coming back from a death in a live Dominion match, a rejoiner coming back into a new
        /// body included (afterRejoin: they left while dead, so their first respawn is a death-respawn). A fresh start (a round's or break's
        /// reset), a first spawn or a joiner (no death before) gets none (A16).</summary>
        public static bool StartsAfterRespawn(bool dominion, bool matchLive, bool diedBefore, bool freshStart, bool afterRejoin = false) =>
            dominion && matchLive && (diedBefore || afterRejoin) && !freshStart;

        /// <summary>Who an incoming damage, status or push comes from, as the shielded victim sees it.</summary>
        public enum Origin { Enemy, Teammate, Self }

        /// <summary>Who a source actor is to the victim: the victim itself, a teammate, or anyone else. An unknown source counts as an enemy, the
        /// same fail-open Teams.AreSameTeam makes for damage.</summary>
        public static Origin OriginOf(bool isVictimItself, bool sameTeam) => isVictimItself ? Origin.Self : sameTeam ? Origin.Teammate : Origin.Enemy;

        /// <summary>What a victim does with an incoming hit, status or push: Blocked = stop it before anything changes; WriteStamp = also
        /// stamp dBlk now (at most once per popup length).</summary>
        public readonly struct HitDecision
        {
            public readonly bool Blocked;
            public readonly bool WriteStamp;
            public HitDecision(bool blocked, bool writeStamp) { Blocked = blocked; WriteStamp = writeStamp; }
        }

        /// <summary>An enemy's damage on a shielded victim is stopped and shows BLOCKED. A teammate's is left to the friendly-fire rule (ignored
        /// as before, no BLOCKED). The victim's own is stopped too, silently: no stamp, so nobody sees BLOCKED for a self-hit. With no shield up
        /// the hit is none of the shield's business.</summary>
        public static HitDecision OnIncomingHit(bool shieldUp, Origin origin, int lastStampMs, int nowMs, int popupMs)
        {
            if (!BlocksDamage(shieldUp) || origin == Origin.Teammate) return new HitDecision(false, false);
            if (origin == Origin.Self) return new HitDecision(true, false);
            return new HitDecision(true, StampDue(lastStampMs, nowMs, popupMs));
        }

        /// <summary>A24: a shielded victim is also untouchable by an enemy's stun, slow or push (any status or displacement), and BLOCKED shows
        /// for it. Own and teammate effects land as today (a shielded player can still be helped, and can still push or boost themselves).</summary>
        public static HitDecision OnIncomingEffect(bool shieldUp, Origin origin, int lastStampMs, int nowMs, int popupMs)
        {
            if (!shieldUp || origin != Origin.Enemy) return new HitDecision(false, false);
            return new HitDecision(true, StampDue(lastStampMs, nowMs, popupMs));
        }

        /// <summary>A25: a shielded attacker's bubble ends when something it did reaches an enemy, unless that enemy is shielded too (the effect
        /// was stopped, so nothing was hit) or the effect was set up before the attacker's respawn (A26). A hit on nobody never gets here.</summary>
        public static bool EndsOnEnemyAffected(bool attackerShieldUp, bool victimShieldUp, bool fromBeforeRespawn) =>
            attackerShieldUp && !victimShieldUp && !fromBeforeRespawn;

        /// <summary>A26: true when an effect (a mine, a fire field, a burn) was set up before the attacker's current shield began. The shield began
        /// ShieldSeconds before it ends, so the start is derived from dShd alone and every client that reads it agrees. placedMs 0 = a direct hit
        /// (a shot, a pulse), never from before. A cleared shield (0) has no start to be before.</summary>
        public static bool IsFromBeforeRespawn(int effectPlacedMs, int shieldEndMs, float shieldSeconds)
        {
            if (effectPlacedMs == 0 || shieldEndMs == 0) return false;
            int startMs = unchecked(shieldEndMs - (int)System.Math.Round(shieldSeconds * 1000f));
            return unchecked(effectPlacedMs - startMs) < 0;
        }

        /// <summary>Who stays on a zone's capture roster and who is held out while a shield is up. A counted player whose shield is up is held
        /// out; a held-out player who died is dropped (they come back through the trigger); one whose shield is down comes back.</summary>
        public static void SortRoster<T>(System.Collections.Generic.List<T> counted, System.Collections.Generic.List<T> heldOut,
                                         System.Func<T, bool> shieldUp, System.Func<T, bool> dead)
        {
            for (int i = counted.Count - 1; i >= 0; i--)
            {
                T player = counted[i];
                if (CountsForCapture(shieldUp(player))) continue;
                counted.RemoveAt(i);
                if (!heldOut.Contains(player)) heldOut.Add(player);
            }
            for (int i = heldOut.Count - 1; i >= 0; i--)
            {
                T player = heldOut[i];
                if (dead(player)) { heldOut.RemoveAt(i); continue; }
                if (!CountsForCapture(shieldUp(player))) continue;
                heldOut.RemoveAt(i);
                if (!counted.Contains(player)) counted.Add(player);
            }
        }

        /// <summary>True when a new dBlk stamp may be written: none yet, or the last one is a full popup old.</summary>
        public static bool StampDue(int lastStampMs, int nowMs, int popupMs) => lastStampMs == 0 || unchecked(nowMs - lastStampMs) >= popupMs;

        /// <summary>True when the stamp read from a player is one this client has not popped yet (0 = none ever).</summary>
        public static bool IsNewStamp(int seenMs, int stampMs) => stampMs != 0 && stampMs != seenMs;
    }
}
