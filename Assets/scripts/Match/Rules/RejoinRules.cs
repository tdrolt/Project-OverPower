using System;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Match
{
    /// <summary>Task 9e (Tudor D21): what a player saves so a crash or a quit does not lose their place - the room they
    /// were in, the id they were using, their name, and when they were last seen in it. Written next to the game
    /// (RejoinStore), read back on the name screen.</summary>
    [Serializable]
    public sealed class RejoinRecord
    {
        public string RoomName;
        public string UserId;
        public string Nick;
        /// <summary>Unix milliseconds (UTC) the player was last known to be in the room. Refreshed while they play, so a
        /// crash leaves it a few seconds behind the moment they really dropped.</summary>
        public long SavedAtMs;

        public string Serialize() => JsonUtility.ToJson(this);

        /// <summary>False (and a null record) for empty, unreadable or room-less text.</summary>
        public static bool TryParse(string text, out RejoinRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            try
            {
                RejoinRecord parsed = JsonUtility.FromJson<RejoinRecord>(text);
                if (parsed == null || string.IsNullOrEmpty(parsed.RoomName))
                    return false;
                record = parsed;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>The random id a player keeps on this PC: made the first time the game runs, saved, and used as the Photon
    /// user id - which is what lets the server hand a dropped player their own slot back. Not the name, not the address.</summary>
    public static class PlayerIdRule
    {
        /// <summary>The saved id (trimmed) when there is one, else a new one from <paramref name="create"/>. Never empty:
        /// a generator that returns nothing gets a fresh id anyway. <paramref name="created"/> tells the caller to save it.</summary>
        public static string Resolve(string saved, Func<string> create, out bool created)
        {
            if (!string.IsNullOrWhiteSpace(saved))
            {
                created = false;
                return saved.Trim();
            }

            created = true;
            string made = create != null ? create() : null;
            return string.IsNullOrWhiteSpace(made) ? NewId() : made.Trim();
        }

        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>A short stable fingerprint of where an id was made: the machine and the game folder. A build folder copied to
        /// another folder or PC gets a different tag, so it does not share the original's id (Photon refuses two connections
        /// with one id in a room).</summary>
        public static string Tag(string machine, string folder)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in (machine ?? "") + "|" + NormaliseFolder(folder))
                    hash = (hash ^ c) * 16777619u;
                return hash.ToString("x8");
            }
        }

        /// <summary>One spelling per folder: full path, no trailing separator, upper case (so "C:\Games\A", "c:/games/a/" agree).</summary>
        private static string NormaliseFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return "";
            try { folder = System.IO.Path.GetFullPath(folder); }
            catch (Exception) { /* keep the raw text */ }
            return folder.TrimEnd('\\', '/').ToUpperInvariant();
        }

        public static string Compose(string id, string tag) => id + "@" + tag;

        /// <summary>The id file holds "id@tag". The id is reused only when the tag matches this machine and folder; otherwise a new
        /// one is made. A legacy file with no tag is adopted (its id kept) and reported as created, so the caller rewrites it
        /// with a tag. Never empty.</summary>
        public static string ResolveTagged(string savedFile, string tag, Func<string> create, out bool created)
        {
            string text = savedFile == null ? "" : savedFile.Trim();
            if (text.Length > 0)
            {
                int at = text.LastIndexOf('@');
                if (at < 0)
                {
                    created = true;
                    return text;
                }
                if (at > 0 && text.Substring(at + 1) == tag)
                {
                    created = false;
                    return text.Substring(0, at);
                }
            }
            return Resolve(null, create, out created);
        }

        /// <summary>At most the first 8 characters - the only form of an id that may reach a log or the screen.</summary>
        public static string ForLog(string id) =>
            string.IsNullOrEmpty(id) ? "" : (id.Length <= 8 ? id : id.Substring(0, 8));
    }

    public enum JoinRefusalAction { None, RejoinSavedRoom, ShowMessage }

    public enum RejoinTeamAction { Keep, Repick }

    public static class RejoinRules
    {
        public const int InactiveJoinerErrorCode = 32749;
        public const int NoRandomMatchFoundCode = 32760;

        /// <summary>The two answers Photon gives a NORMAL join that ran into this user's own held place: "found inactive joiner" for a
        /// named room, "no random match found" for a random join (which skips the room that holds the place). Any other failure is
        /// not about a held place.</summary>
        public static bool IsHeldPlaceRefusal(int code) => code == InactiveJoinerErrorCode || code == NoRandomMatchFoundCode;

        /// <summary>Tudor (9e-3): a player rejoining a team that was KNOCKED OUT comes back as a spectator of it - dead, no respawn, the
        /// waiting state the rest of the team has (the ordinary death path already answers that for an eliminated team) - and is not
        /// moved. Only a team that was never in the match (left out at go-live) is re-picked, as for a new joiner.</summary>
        public static RejoinTeamAction TeamOnRejoin(bool teamsFixed, bool teamEliminated, bool teamInMatch) =>
            teamsFixed && !teamEliminated && !teamInMatch ? RejoinTeamAction.Repick : RejoinTeamAction.Keep;

        /// <summary>Task 9g (Tudor): the rejoiner onto a knocked-out team lands on the "You lost" panel (with Spectate) while the match
        /// still runs. Once the match is over the result screen decides win or lose instead (MatchDirector's winner reaction).</summary>
        public static bool LandsOnLosePanel(bool teamEliminated, MatchPhase phase) =>
            teamEliminated && phase != MatchPhase.Over;

        /// <summary>The body watchdog after a rejoin is finished once there IS a body and it is either alive (the first respawn is done) or
        /// in the last-stand wait (it will not respawn until the team retakes a base - no reason to keep polling for the whole match).
        /// Never before a second has passed, so a body that has not run its first frame yet is not mistaken for a finished one.</summary>
        public static bool BodyWatchIsDone(bool hasBody, bool alive, bool inLastStandWait, float secondsWatched) =>
            hasBody && secondsWatched > 1f && (alive || inLastStandWait);

        /// <summary>Photon refuses a normal join while this same user id still holds a place in that room
        /// (ErrorCode.JoinFailedFoundInactiveJoiner). That is not "no room": the player's own dropped place is there. With a saved
        /// match still on offer the answer is to rejoin it; without one, a clear message and back to the name screen.</summary>
        public static JoinRefusalAction OnJoinRefused(int code, bool savedMatchOffered)
        {
            if (code != InactiveJoinerErrorCode)
                return JoinRefusalAction.None;
            return savedMatchOffered ? JoinRefusalAction.RejoinSavedRoom : JoinRefusalAction.ShowMessage;
        }

        /// <summary>The name screen offers "Rejoin your match": there is a saved room, saved under this same id, and it was
        /// last seen younger than the window the room keeps a dropped player's slot (RoomOptions.PlayerTtl). After that the
        /// server has given the slot up and only a normal join is left. A record from the future (the clock moved) is not
        /// trusted.</summary>
        public static bool IsOffered(RejoinRecord record, string currentUserId, long nowMs, int ttlSeconds)
        {
            if (record == null || ttlSeconds <= 0 || string.IsNullOrEmpty(record.RoomName))
                return false;
            if (string.IsNullOrEmpty(currentUserId) || record.UserId != currentUserId)
                return false;

            long age = nowMs - record.SavedAtMs;
            return age >= 0 && age < ttlSeconds * 1000L;
        }

        /// <summary>RoomManager.OnLeftRoom runs both for a deliberate LeaveRoom (the client is heading back to the master
        /// server) and for a disconnect (a lost connection, or the game closing). Only the second keeps the player's place in
        /// the room for the rejoin window - so only the first resets the match properties and forgets the saved match.</summary>
        public static bool LeaveIsADisconnect(ClientState state) =>
            state != ClientState.DisconnectingFromGameServer && state != ClientState.Leaving;

        /// <summary>Photon's error code for a rejoin while the server still holds this user's old connection as active
        /// (ErrorCode.JoinFailedFoundActiveJoiner).</summary>
        public const int ActiveJoinerErrorCode = 32746;

        /// <summary>A rejoin refused only because the server has not yet timed the old connection out is worth retrying for a
        /// while; any other refusal (no such room, no place saved for this user) is final.</summary>
        public static bool ShouldRetryRejoin(int returnCode, int retriesSoFar, int maxRetries) =>
            returnCode == ActiveJoinerErrorCode && retriesSoFar < maxRetries;

        /// <summary>The seconds on the config to Photon's milliseconds. Zero means "no slot is kept" (the old behaviour).</summary>
        public static int PlayerTtlMs(float seconds) => seconds <= 0f ? 0 : Mathf.RoundToInt(seconds * 1000f);

        /// <summary>The "Connection lost - Rejoin" panel shows for a disconnect nobody asked for, from inside a room.
        /// Closing the game or leaving on purpose (a client-logic disconnect) never does.</summary>
        public static bool IsConnectionLoss(DisconnectCause cause, bool wasInRoom)
        {
            if (!wasInRoom)
                return false;
            return cause != DisconnectCause.None
                && cause != DisconnectCause.DisconnectByClientLogic
                && cause != DisconnectCause.ApplicationQuit;
        }

        /// <summary>A request counter that lives on the player's own machine restarts at 0 in a fresh process while the room
        /// still holds the last value it was sent (a Player Property survives the drop). Counting on from whichever is
        /// higher keeps the new requests from looking old to the side that de-duplicates by id.</summary>
        public static int SeedCounter(int current, int fromProperty) => Math.Max(0, Math.Max(current, fromProperty));

        /// <summary>After a rejoin PUN hands the player's old body back from the room's buffered spawn. If it has not
        /// arrived after <paramref name="limitSeconds"/> (nothing was buffered), the player spawns a fresh one.</summary>
        public static bool NeedsFallbackBody(bool hasBody, float secondsWaited, float limitSeconds) =>
            !hasBody && secondsWaited >= limitSeconds;
    }

    /// <summary>Task 9e: how a player whose connection dropped (a Photon "inactive" actor, kept for the rejoin window) counts
    /// in the match. The rule is the same everywhere: absent, not alive, not a lobby member - and never a reason for the
    /// match to wait.</summary>
    public static class PresenceRules
    {
        public static bool IsPresent(bool inactive) => !inactive;

        /// <summary>Tudor D-a: as <see cref="CountsAsDead(bool,bool,bool?)"/>, but a dropped player only counts as dead once they have
        /// been gone <paramref name="graceSeconds"/> - a quick reconnect inside the grace changes nothing for the team. A real
        /// death (alive false, or waiting in the last stand) is never hidden by the grace.</summary>
        public static bool CountsAsDead(bool inactive, bool waiting, bool? aliveProperty, float inactiveSeconds, float graceSeconds) =>
            waiting || !(aliveProperty ?? true) || (inactive && inactiveSeconds >= graceSeconds);

        /// <summary>Alive needs an active player: an inactive one still carries whatever "alive" flag they last wrote, which
        /// would read true. A missing flag means alive (a player who never died).</summary>
        public static bool CountsAsAlive(bool inactive, bool? aliveProperty) =>
            !inactive && (aliveProperty ?? true);

        /// <summary>MatchDirector's team status: dead is waiting (last stand), not alive, or inactive. An inactive member of
        /// a team with no base is therefore out like any other dead one, so the team's last living member dropping starts
        /// the last-stand knockout at once; with a base the team stays in and the member comes back through a respawn.</summary>
        public static bool CountsAsDead(bool inactive, bool waiting, bool? aliveProperty) =>
            inactive || waiting || !(aliveProperty ?? true);

        /// <summary>The warm-up lobby (who is on which team, whether the host may start): a dropped player is not "here".</summary>
        public static bool CountsInTheLobby(bool inactive) => !inactive;
    }
}
