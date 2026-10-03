using System.Collections.Generic;
using System.Text;
using ExitGames.Client.Photon;

namespace Overpower.Chat
{
    /// <summary>Lobby Task 11 (D10): which chat channel a client listens to. One channel per lobby, named after the Photon room; none while no
    /// room is joined (the lobby list has no chat).</summary>
    public static class ChatChannelRule
    {
        /// <summary>The channel for a room name, or null outside a room.</summary>
        public static string ChannelFor(string roomName) => string.IsNullOrEmpty(roomName) ? null : roomName;

        /// <summary>True when a message that arrived on <paramref name="channel"/> belongs on screen: it is the channel this client is subscribed
        /// to now. A late message from a lobby just left (or from no channel at all) is dropped.</summary>
        public static bool Accepts(string subscribedChannel, string channel) =>
            subscribedChannel != null && channel != null && subscribedChannel == channel;
    }

    /// <summary>Lobby Task 13 (Task 11 review): when the chat panel may be open, and how high its canvas draws. The chat only opens while it has a
    /// channel (a lobby it is in) - on the name, list and create screens Enter does nothing - and it closes itself the moment the channel goes (room
    /// left) or a How to play / mode info page opens over it. Its canvas draws over the lobby screens only while the lobby room shows; in the match
    /// it sits under the scoreboard and the result / match-log panels.</summary>
    public static class ChatPanelRule
    {
        /// <summary>Above the lobby screens' canvas (LobbyUiKit.CanvasSortingOrder, 100).</summary>
        public const int LobbyRoomOrder = 101;

        /// <summary>In the match: above the HUD canvases (-10 .. -20) and under the result panels (0), the match-log box (10) and the scoreboard (30).</summary>
        public const int MatchOrder = -1;

        /// <summary>Enter opens the panel only when a channel is subscribed.</summary>
        public static bool MayOpen(string subscribedChannel) => subscribedChannel != null;

        /// <summary>An open panel closes when its channel is gone or a page (How to play / mode info) is open.</summary>
        public static bool MustClose(bool open, string subscribedChannel, bool pageOpen) =>
            open && (subscribedChannel == null || pageOpen);

        public static int SortingOrder(bool lobbyRoomShowing) => lobbyRoomShowing ? LobbyRoomOrder : MatchOrder;
    }

    /// <summary>What one chat line carries: who (display name, team, spectator seat) and what they typed. Photon Chat's own sender is the chat user id
    /// (the install's stable player id), which is never shown - everything shown comes from this.</summary>
    public readonly struct ChatMessage
    {
        public const int MaxTextLength = 200;
        public const int MaxNameLength = 16;

        public const string NameKey = "name";
        public const string TeamKey = "team";
        public const string SpecKey = "spec";
        public const string TextKey = "text";

        public ChatMessage(string name, int team, bool spectator, string text)
        {
            Name = name;
            Team = team;
            Spectator = spectator;
            Text = text;
        }

        public string Name { get; }
        public int Team { get; }
        public bool Spectator { get; }
        public string Text { get; }

        public Hashtable Encode() => new Hashtable
        {
            { NameKey, Name ?? "" },
            { TeamKey, Team },
            { SpecKey, Spectator },
            { TextKey, Text ?? "" },
        };

        /// <summary>Reads a received message. False for anything that is not exactly what Encode makes (an old plain-text message, a missing or
        /// wrongly typed part, blank text). Name and text are cut to their limits and stripped of markup and control characters.</summary>
        public static bool TryDecode(object raw, out ChatMessage message)
        {
            message = default;
            if (!(raw is Hashtable table)) return false;
            if (!(table.TryGetValue(NameKey, out object name) && name is string nameText)) return false;
            if (!(table.TryGetValue(TextKey, out object text) && text is string typed)) return false;
            if (!table.TryGetValue(TeamKey, out object team) || !TryInt(team, out int teamId)) return false;
            if (!(table.TryGetValue(SpecKey, out object spec) && spec is bool spectator)) return false;
            string cleanText = Clean(typed, MaxTextLength);
            if (cleanText.Trim().Length == 0) return false;
            message = new ChatMessage(Clean(nameText, MaxNameLength), teamId, spectator, cleanText);
            return true;
        }

        private static bool TryInt(object value, out int result)
        {
            switch (value)
            {
                case int i: result = i; return true;
                case short s: result = s; return true;
                case byte b: result = b; return true;
                case long l when l >= int.MinValue && l <= int.MaxValue: result = (int)l; return true;
                default: result = 0; return false;
            }
        }

        // '<' is dropped so nothing typed can be read as rich-text markup by the chat text.
        private static string Clean(string value, int max)
        {
            var sb = new StringBuilder(value.Length < max ? value.Length : max);
            foreach (char c in value)
            {
                if (sb.Length >= max) break;
                if (c == '<' || c == '>' || char.IsControl(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>How a chat line is written on screen, and the small helpers around it.</summary>
    public static class ChatLine
    {
        /// <summary>The name part of a line: "Kim" for a player, "[SPEC] Kim" for a spectator.</summary>
        public static string Speaker(ChatMessage message, string spectatorTag) =>
            message.Spectator ? spectatorTag + " " + message.Name : message.Name;

        /// <summary>The line as the chat text shows it: the speaker in bold in their colour (hex without the #), then what they typed.</summary>
        public static string Format(ChatMessage message, string spectatorTag, string colourHex) =>
            "<color=#" + colourHex + "><b>" + Speaker(message, spectatorTag) + ":</b></color> " + message.Text;

        /// <summary>A user id for a log: its first 8 characters, never the whole id.</summary>
        public static string ShortId(string userId) =>
            string.IsNullOrEmpty(userId) ? "" : userId.Length <= 8 ? userId : userId.Substring(0, 8);

        /// <summary>Keeps the newest <paramref name="max"/> lines.</summary>
        public static void Trim(List<string> lines, int max)
        {
            if (max < 0) max = 0;
            if (lines.Count > max) lines.RemoveRange(0, lines.Count - max);
        }
    }
}
