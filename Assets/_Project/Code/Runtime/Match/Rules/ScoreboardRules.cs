using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// The scoreboard's pure rules (D12), with no engine types so every one is tested.
    ///
    /// EVERY STAT IS COUNTED AND WRITTEN ONLY BY THE PLAYER'S OWN CLIENT, from the events only that client sees
    /// (the damage and takedown credit sent to it, its own death, a capture it was standing in). It publishes them
    /// as ONE Player Property, "sb" = int[] { kills, deaths, assists, damage, captures } (ScoreboardRules.Key and
    /// the index constants). Everyone else only reads it, so no RPC is needed and a late joiner gets it on join.
    /// </summary>
    public sealed class ScoreTally
    {
        private float damage;

        public int Kills { get; private set; }
        public int Deaths { get; private set; }
        public int Assists { get; private set; }
        public int Captures { get; private set; }

        /// <summary>Damage dealt is a float (armour + health removed); the board shows whole points.</summary>
        public int DamageRounded => (int)System.Math.Round(damage, System.MidpointRounding.AwayFromZero);

        public void AddKill() => Kills++;
        public void AddAssist() => Assists++;
        public void AddDeath() => Deaths++;

        public void AddDamage(float amount)
        {
            if (amount > 0f)
                damage += amount;
        }

        /// <summary>One credit message from a real player's hit: its damage, plus a kill (takedown 1) or an assist
        /// (takedown 2) when it carries one.</summary>
        public void AddCredit(float amount, int takedown)
        {
            AddDamage(amount);
            if (takedown == 1)
                AddKill();
            else if (takedown == 2)
                AddAssist();
        }

        /// <summary>A zone just flipped to zoneTeam. Counts only for a living player who was standing in that zone
        /// and is on the team it flipped to (every capture you were standing in counts).</summary>
        public void NoteCapture(bool standingInZone, bool alive, int zoneTeam, int myTeam)
        {
            if (standingInZone && alive && myTeam >= 0 && zoneTeam == myTeam)
                Captures++;
        }

        /// <summary>The go-live fresh start: the warm-up does not count.</summary>
        public void Reset()
        {
            Kills = Deaths = Assists = Captures = 0;
            damage = 0f;
        }

        /// <summary>A rejoined player's fresh body starts from the numbers the room still holds for them (their
        /// "sb" Player Property), not from zero - kills, deaths, assists, damage, captures, in ToArray's order. A missing,
        /// short or negative value leaves that number at 0.</summary>
        public void Restore(int[] values)
        {
            if (values == null)
                return;
            Kills = values.Length > 0 && values[0] > 0 ? values[0] : 0;
            Deaths = values.Length > 1 && values[1] > 0 ? values[1] : 0;
            Assists = values.Length > 2 && values[2] > 0 ? values[2] : 0;
            damage = values.Length > 3 && values[3] > 0 ? values[3] : 0f;
            Captures = values.Length > 4 && values[4] > 0 ? values[4] : 0;
        }

        /// <summary>The Player Property value. Allocates, so only called when actually publishing.</summary>
        public int[] ToArray() => new[] { Kills, Deaths, Assists, DamageRounded, Captures };
    }

    /// <summary>One line of the board: a player's team, name and the five numbers.</summary>
    public readonly struct ScoreRow
    {
        public readonly int Actor, Team, Kills, Deaths, Assists, Damage, Captures;
        public readonly string Name;

        public ScoreRow(int actor, int team, string name, int kills, int deaths, int assists, int damage, int captures)
        {
            Actor = actor; Team = team; Name = name;
            Kills = kills; Deaths = deaths; Assists = assists; Damage = damage; Captures = captures;
        }
    }

    /// <summary>How often a client may publish its score: at most perSecond times a second, but a kill, a death or
    /// a capture goes out at once. A change held back stays pending and goes out as soon as the interval is up.</summary>
    public sealed class ScorePublishThrottle
    {
        private readonly float minInterval;
        private bool pending;
        private bool urgentPending;
        private float lastPublish = float.NegativeInfinity;

        public ScorePublishThrottle(float perSecond)
        {
            minInterval = perSecond > 0f ? 1f / perSecond : 0f;
        }

        public void NoteChange(bool urgent)
        {
            pending = true;
            if (urgent)
                urgentPending = true;
        }

        public bool ShouldPublish(float now)
        {
            if (!pending)
                return false;
            return urgentPending || now - lastPublish >= minInterval;
        }

        public void MarkPublished(float now)
        {
            pending = false;
            urgentPending = false;
            lastPublish = now;
        }
    }

    public static class ScoreboardRules
    {
        /// <summary>The Player Property key.</summary>
        public const string Key = "sb";

        public const int KillsIndex = 0;
        public const int DeathsIndex = 1;
        public const int AssistsIndex = 2;
        public const int DamageIndex = 3;
        public const int CapturesIndex = 4;
        public const int ValueLength = 5;

        /// <summary>A row from a player's property value; a missing or too-short value reads as all zeros.</summary>
        public static ScoreRow RowFrom(int actor, int team, string name, int[] values)
        {
            if (values == null || values.Length < ValueLength)
                return new ScoreRow(actor, team, name, 0, 0, 0, 0, 0);

            return new ScoreRow(actor, team, name, values[KillsIndex], values[DeathsIndex], values[AssistsIndex],
                                values[DamageIndex], values[CapturesIndex]);
        }

        /// <summary>The table order: teams in team order (a player whose team is not known yet last), then within a
        /// team most kills, then most damage, then the lower actor number so the order never flickers on a tie.
        /// In place, no allocation (insertion sort - a room holds nine players at most).</summary>
        public static void Sort(List<ScoreRow> rows)
        {
            for (int i = 1; i < rows.Count; i++)
            {
                ScoreRow item = rows[i];
                int j = i - 1;
                while (j >= 0 && Compare(rows[j], item) > 0)
                {
                    rows[j + 1] = rows[j];
                    j--;
                }
                rows[j + 1] = item;
            }
        }

        private static int Compare(ScoreRow a, ScoreRow b)
        {
            int teamA = a.Team < 0 ? int.MaxValue : a.Team;
            int teamB = b.Team < 0 ? int.MaxValue : b.Team;
            if (teamA != teamB) return teamA < teamB ? -1 : 1;
            if (a.Kills != b.Kills) return a.Kills > b.Kills ? -1 : 1;
            if (a.Damage != b.Damage) return a.Damage > b.Damage ? -1 : 1;
            return a.Actor.CompareTo(b.Actor);
        }

        /// <summary>Holding Tab opens the board. Only typing in chat stops it: a dead player still sees it. The
        /// release is never gated (a board must always be able to close).</summary>
        public static bool MayOpen(bool typingInChat) => !typingInChat;
    }
}
