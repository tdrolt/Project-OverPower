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

        /// <summary>The value the shield owner writes when it learns it dealt damage: 0 = cleared, shield down. Attacking from inside the
        /// shield would be a free shot, so the first hit dealt drops it.</summary>
        public static int EndAfterDamageDealt() => 0;

        /// <summary>An up shield blocks incoming damage (and shows BLOCKED).</summary>
        public static bool BlocksDamage(bool up) => up;

        /// <summary>A shielded player neither captures nor blocks a capture; once the shield is down they count as anyone.</summary>
        public static bool CountsForCapture(bool shieldUp) => !shieldUp;

        /// <summary>Casting an ability that hits nothing keeps the shield: only dealing damage ends it early.</summary>
        public static bool EndsOnAbilityWithoutHit => false;

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

        /// <summary>A shield starts only for a player coming back from a death in a live Dominion match. A fresh start (a round's or break's reset), a
        /// first spawn or a joiner (no death before) gets none (A16).</summary>
        public static bool StartsAfterRespawn(bool dominion, bool matchLive, bool diedBefore, bool freshStart) =>
            dominion && matchLive && diedBefore && !freshStart;

        /// <summary>Damage dealt (amount above 0, as the victim reported it) ends the shield; a cast that hit nobody reports nothing.</summary>
        public static bool ClearsOnDamageDealt(float amount) => amount > 0f;

        /// <summary>What a victim does with an incoming hit: Blocked = stop it before any health, armour or combat-clock change; WriteStamp = also
        /// stamp dBlk now (at most once per popup length).</summary>
        public readonly struct HitDecision
        {
            public readonly bool Blocked;
            public readonly bool WriteStamp;
            public HitDecision(bool blocked, bool writeStamp) { Blocked = blocked; WriteStamp = writeStamp; }
        }

        /// <summary>A hit on a shielded victim is stopped, whoever it is from, a teammate excepted (teammate hits are ignored as before, with no
        /// BLOCKED). With no shield up the hit is none of the shield's business.</summary>
        public static HitDecision OnIncomingHit(bool shieldUp, bool fromTeammate, int lastStampMs, int nowMs, int popupMs)
        {
            if (!BlocksDamage(shieldUp) || fromTeammate) return new HitDecision(false, false);
            return new HitDecision(true, StampDue(lastStampMs, nowMs, popupMs));
        }

        /// <summary>True when a new dBlk stamp may be written: none yet, or the last one is a full popup old.</summary>
        public static bool StampDue(int lastStampMs, int nowMs, int popupMs) => lastStampMs == 0 || unchecked(nowMs - lastStampMs) >= popupMs;

        /// <summary>True when the stamp read from a player is one this client has not popped yet (0 = none ever).</summary>
        public static bool IsNewStamp(int seenMs, int stampMs) => stampMs != 0 && stampMs != seenMs;
    }
}
