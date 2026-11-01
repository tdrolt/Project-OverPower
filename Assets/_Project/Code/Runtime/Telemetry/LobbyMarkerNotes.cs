using System.Globalization;

namespace Overpower.Telemetry
{
    /// <summary>
    /// The notes of the lobby's own markers (the same `marker` line as the F1 marker): a lobby created, a seat taken or left, Start
    /// game, End warm-up, a new host, and a spectator or a bot the host has seen. The report lists them with the other markers; the spectator
    /// and bot notes are bookkeeping only - the aggregator reads them back (<see cref="TryReadSpectator"/>, <see cref="TryReadBot(string, out int)"/>) so
    /// a spectator or a bot is not reported as "a player with no log".
    /// </summary>
    public static class LobbyMarkerNotes
    {
        public const string StartGame = "start game";
        public const string EndWarmup = "end warm-up";

        private const string SpectatorPrefix = "spectator joined: actor ";
        private const string BotPrefix = "bot joined: actor ";

        public static string LobbyCreated(string lobbyName, string modeName) => "lobby created: " + lobbyName + " (" + modeName + ")";

        public static string SeatTaken(string seat) => "seat taken: " + seat;

        public static string SeatLeft(string seat) => "seat left: " + seat;

        /// <summary>The notes for this client's own seat going from <paramref name="oldSeat"/> to <paramref name="newSeat"/> (null = No role):
        /// none when it did not change, a leave, a take, or both for a move.</summary>
        public static string[] SeatChange(string oldSeat, string newSeat)
        {
            if (oldSeat == newSeat) return System.Array.Empty<string>();
            if (oldSeat == null) return new[] { SeatTaken(newSeat) };
            if (newSeat == null) return new[] { SeatLeft(oldSeat) };
            return new[] { SeatLeft(oldSeat), SeatTaken(newSeat) };
        }

        public static string HostChanged(int actor, string nick) => "host changed: actor " + actor.ToString(CultureInfo.InvariantCulture) + " " + nick;

        public static string SpectatorSeen(int actor) => SpectatorPrefix + actor.ToString(CultureInfo.InvariantCulture);

        /// <summary>The actor a spectator note names. False for any other note, and for a note that only starts the same way.</summary>
        public static bool TryReadSpectator(string note, out int actor)
        {
            actor = -1;
            if (note == null || !note.StartsWith(SpectatorPrefix, System.StringComparison.Ordinal)) return false;
            return int.TryParse(note.Substring(SpectatorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out actor);
        }

        public static string BotSeen(int actor, string nick) => BotPrefix + actor.ToString(CultureInfo.InvariantCulture) + " " + (nick ?? "");

        /// <summary>The actor a bot note names. False for any other note, and for a note that only starts the same way.</summary>
        public static bool TryReadBot(string note, out int actor) => TryReadBot(note, out actor, out _);

        /// <summary>The actor and the nick a bot note names (the nick is everything after the actor number).</summary>
        public static bool TryReadBot(string note, out int actor, out string nick)
        {
            actor = -1;
            nick = "";
            if (note == null || !note.StartsWith(BotPrefix, System.StringComparison.Ordinal)) return false;
            string rest = note.Substring(BotPrefix.Length);
            int space = rest.IndexOf(' ');
            string number = space < 0 ? rest : rest.Substring(0, space);
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) return false;
            actor = parsed;
            nick = space < 0 ? "" : rest.Substring(space + 1);
            return true;
        }

        /// <summary>The one line a file carries when its pending buffer overflowed.</summary>
        public static string EarlyLinesDropped(int count) =>
            count.ToString(CultureInfo.InvariantCulture) + " earliest lines were dropped before this file opened (the pending buffer was full)";
    }
}
