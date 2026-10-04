using System;
using ExitGames.Client.Photon;
using Overpower.Match;

namespace Overpower.Dominion
{
    /// <summary>The Dominion values as the room holds them right now.</summary>
    public struct DominionRoomState
    {
        /// <summary>False until round 1 has been written (dRnd absent).</summary>
        public bool HasRound;
        public int Round;
        public DominionStage Stage;
        public int EndMs;
        public int[] Points;
        public int[] Wins;
        public int Winner;
        /// <summary>The server ms of the centre's next payout (dCtr); 0 = none written.</summary>
        public int CentreMs;
        /// <summary>dRz: the dEnd of the stage the zones were last reset for; 0 = never.</summary>
        public int ResetFor;
        /// <summary>dPseq: how many points writes the room has had; 0 = none yet.</summary>
        public int PointsSeq;
        /// <summary>dSd: the server ms the sudden-death circle starts shrinking; 0 = none written. A replay writes a new one.</summary>
        public int SuddenDeathMs;
        /// <summary>dSdT: the teams playing the current sudden death; null = none written (a room from before A33, or no sudden death yet).</summary>
        public int[] SuddenDeathTeams;
        /// <summary>dHist: every finished round's final points, rounds x team slots flattened (DominionHistory); empty until round 1 is over.</summary>
        public int[] History;

        /// <summary>The state from the room's properties. Missing keys read as: no round, stage None, no points or wins (arrays of three zeros),
        /// no winner (-1). A wrong type reads as missing.</summary>
        public static DominionRoomState Read(Hashtable props)
        {
            var state = new DominionRoomState
            {
                Points = new int[DominionKeys.TeamSlots],
                Wins = new int[DominionKeys.TeamSlots],
                Winner = -1,
            };
            if (props == null) return state;
            if (props.TryGetValue(DominionKeys.Round, out object round) && round is int r) { state.HasRound = true; state.Round = r; }
            if (props.TryGetValue(DominionKeys.Stage, out object stage) && stage is int s) state.Stage = (DominionStage)s;
            if (props.TryGetValue(DominionKeys.StageEnd, out object end) && end is int e) state.EndMs = e;
            if (props.TryGetValue(DominionKeys.Points, out object points) && points is int[] p) state.Points = p;
            if (props.TryGetValue(DominionKeys.Wins, out object wins) && wins is int[] w) state.Wins = w;
            if (props.TryGetValue(DominionKeys.Winner, out object winner) && winner is int win) state.Winner = win;
            if (props.TryGetValue(DominionKeys.CentrePayout, out object ctr) && ctr is int c) state.CentreMs = c;
            if (props.TryGetValue(DominionKeys.ZonesResetFor, out object rz) && rz is int z) state.ResetFor = z;
            if (props.TryGetValue(DominionKeys.PointsSeq, out object seq) && seq is int q) state.PointsSeq = q;
            if (props.TryGetValue(DominionKeys.SuddenDeathStart, out object sd) && sd is int sdMs) state.SuddenDeathMs = sdMs;
            if (props.TryGetValue(DominionKeys.SuddenDeathTeams, out object sdt) && sdt is int[] sdTeams) state.SuddenDeathTeams = sdTeams;
            if (props.TryGetValue(DominionKeys.History, out object hist) && hist is int[] h) state.History = h;
            return state;
        }
    }

    /// <summary>The numbers the stage flow needs from DominionConfig, passed in so the tests use made-up ones.</summary>
    public struct DominionFlowNumbers
    {
        public int RoundsToWin;
        public int MaxRounds;
        public float RoundSeconds;
        public float BreakSeconds;
        /// <summary>True when this match has a centre that pays lumps (3v3v3 on a map with a Tier 4 zone): a round start then writes dCtr.</summary>
        public bool HasCentre;
        public int CentreFirstMs;
        public int CentreIntervalMs;
        /// <summary>Seconds of "get ready" between sudden death being written and its circle starting to shrink (the break's countdown length).</summary>
        public float SuddenDeathCountdownSeconds;
        /// <summary>How close two sudden-death stamps (server ms) must be to count as the same moment (DominionConfig Same Instant Tolerance Seconds, in ms).</summary>
        public int SameInstantToleranceMs;
    }

    /// <summary>One check-and-set the master should send: what to write and what the room must still hold for it to apply.</summary>
    public sealed class DominionWrite
    {
        public Hashtable Props;
        public Hashtable Expected;
        public string What;
    }

    /// <summary>What a client does once, when it sees the stage change in the room.</summary>
    public enum DominionEdge { None, BreakStarted, RoundStarted }

    /// <summary>
    /// What the master writes into the room next, as pure rules (Dominion Task 2): given the room's Dominion values, the server clock, the
    /// config numbers and who is in the match, the one check-and-set to send now, or nothing. DominionDirector only reads the room, asks this and
    /// sends the answer; a new master asks the same question of the same room and carries on (A1). Every write expects the values it was
    /// computed from, so two clients that both think they are master cannot both advance one stage.
    /// </summary>
    public static class DominionRoomWrites
    {
        /// <summary>The write to send now, or null. Nothing is written for a Conquest room, before the match is live or while the server clock
        /// has not synced (0).</summary>
        /// <param name="teamsInMatch">The team ids fixed into the match (mTeams).</param>
        /// <param name="playersPerTeam">Players present per team id (index = team id).</param>
        /// <param name="suddenDeath">Who lives per team and when each team's last player fell (SuddenDeathRules.Tally, read from the players'
        /// alive flags and death stamps); null = not known, so sudden death is not judged.</param>
        public static DominionWrite Next(bool dominion, bool live, int nowMs, DominionRoomState room, DominionFlowNumbers cfg,
                                         int[] teamsInMatch, int[] playersPerTeam, SuddenDeathRules.Tally suddenDeath = null)
        {
            if (!dominion || !live || nowMs == 0) return null;

            if (!room.HasRound)
            {
                // Every match opens with the break (Task 6, A20), so round 1's free movement ability, attachment and ultimate can be picked
                // like every other round's. Going live already did the fresh start for players and zones, so dRz is written with the break's
                // dEnd: no second zone reset for this break; the round's start resets the zones as for any round.
                int breakEnd = DominionRules.StageEndMs(nowMs, cfg.BreakSeconds);
                return new DominionWrite
                {
                    What = "break before round 1",
                    Props = new Hashtable
                    {
                        { DominionKeys.Round, 1 },
                        { DominionKeys.Stage, (int)DominionStage.Break },
                        { DominionKeys.StageEnd, breakEnd },
                        { DominionKeys.ZonesResetFor, breakEnd },
                        { DominionKeys.Points, new int[DominionKeys.TeamSlots] },
                        { DominionKeys.Wins, new int[DominionKeys.TeamSlots] },
                        { DominionKeys.Winner, -1 },
                    },
                    Expected = new Hashtable { { DominionKeys.Round, null } },
                };
            }

            if (room.Stage == DominionStage.Over || room.Stage == DominionStage.None) return null;

            // A3: a team that empties keeps the match going; the last team with anyone in the room wins it at once.
            int lastTeam = OnlyTeamWithPlayers(teamsInMatch, playersPerTeam);
            if (lastTeam >= 0)
            {
                var over = new Hashtable
                {
                    { DominionKeys.Stage, (int)DominionStage.Over },
                    { DominionKeys.Winner, lastTeam },
                    { DominionKeys.Wins, Slots(room.Wins) },
                };
                // A round cut short by the others leaving is a row of the result table too (default A34): its points so far go into the history in
                // this write. In a break the round just played is already there; in sudden death every round is.
                if (room.Stage == DominionStage.Round) over[DominionKeys.History] = DominionHistory.Append(room.History, room.Points);
                return Stage(room, "last team standing", over);
            }

            if (room.Stage == DominionStage.SuddenDeath) return NextInSuddenDeath(room, nowMs, cfg, teamsInMatch, suddenDeath); // no clock: the players end it
            if (!MatchStartRules.HasReached(nowMs, room.EndMs)) return null;

            if (room.Stage == DominionStage.Break)
            {
                DominionWrite start = Stage(room, "break over, round starts", new Hashtable
                {
                    { DominionKeys.Stage, (int)DominionStage.Round },
                    { DominionKeys.StageEnd, DominionRules.StageEndMs(nowMs, cfg.RoundSeconds) },
                    { DominionKeys.Points, new int[DominionKeys.TeamSlots] },
                });
                AddCentre(start.Props, cfg, nowMs);
                return start;
            }

            // A round ended: score it, then break, match over or sudden death.
            int[] wins = Slots(room.Wins);
            int roundWinner = DominionRules.RoundWinner(room.Points);
            if (roundWinner >= 0 && roundWinner < wins.Length) wins[roundWinner]++;
            // The round's final points go into the history in the same write (dPts is cleared at the next round's start): the result table needs them.
            int[] history = DominionHistory.Append(room.History, room.Points);
            RoundOutcome outcome = DominionRules.AfterRound(room.Round, wins, cfg.RoundsToWin, cfg.MaxRounds, teamsInMatch);
            switch (outcome.Next)
            {
                case DominionStage.Over:
                    return Stage(room, "match won", new Hashtable
                    {
                        { DominionKeys.Stage, (int)DominionStage.Over },
                        { DominionKeys.Winner, outcome.Winner },
                        { DominionKeys.Wins, wins },
                        { DominionKeys.History, history },
                    }, scoresRound: true);
                case DominionStage.SuddenDeath:
                    return Stage(room, "sudden death", new Hashtable
                    {
                        { DominionKeys.Stage, (int)DominionStage.SuddenDeath },
                        { DominionKeys.StageEnd, 0 },
                        // A17: a short get-ready with the circle at full size before it starts to move.
                        { DominionKeys.SuddenDeathStart, DominionRules.StageEndMs(nowMs, cfg.SuddenDeathCountdownSeconds) },
                        { DominionKeys.SuddenDeathTeams, outcome.SuddenDeathTeams ?? DominionRules.SuddenDeathTeams(wins, teamsInMatch) }, // A33: stored, since a replay narrows it
                        { DominionKeys.Wins, wins },
                        { DominionKeys.History, history },
                    }, scoresRound: true);
                default:
                    return Stage(room, "round over, break", new Hashtable
                    {
                        { DominionKeys.Round, room.Round + 1 },
                        { DominionKeys.Stage, (int)DominionStage.Break },
                        { DominionKeys.StageEnd, DominionRules.StageEndMs(nowMs, cfg.BreakSeconds) },
                        { DominionKeys.Wins, wins },
                        { DominionKeys.History, history },
                        { DominionKeys.CentrePayout, null }, // null removes the key: the round's last payout time is stale in the break (the next round start writes a fresh one)
                    }, scoresRound: true);
            }
        }

        /// <summary>What a sudden-death write is called (DominionWrite.What) - the director settles on these.</summary>
        public const string WhatSuddenDeathWon = "sudden death won";
        public const string WhatSuddenDeathReplay = "sudden death replay";

        /// <summary>The key SuddenDeathVerdictSettle watches for a write: one per verdict (the win names its team), null for any write that is not a
        /// sudden-death verdict, so those are never held back.</summary>
        public static string SuddenDeathVerdictKey(DominionWrite write)
        {
            if (write == null) return null;
            if (write.What == WhatSuddenDeathReplay) return WhatSuddenDeathReplay;
            if (write.What == WhatSuddenDeathWon)
                return WhatSuddenDeathWon + ":" + (write.Props.TryGetValue(DominionKeys.Winner, out object team) ? team : "?");
            return null;
        }

        /// <summary>Sudden death has no clock: the master judges it from who is alive and, when nobody is, from the death stamps (Tudor A31). One team of
        /// the tied ones with anyone alive wins the match; when all have fallen the team whose last player fell latest wins, and only the exact same
        /// server moment starts it over (Tudor A8) with a new circle start; two or more alive writes nothing. Only after the judging beat, and the write expects
        /// the circle start it judged, so two masters cannot both replay or a replay cannot be followed by a stale win.</summary>
        private static DominionWrite NextInSuddenDeath(DominionRoomState room, int nowMs, DominionFlowNumbers cfg, int[] teamsInMatch, SuddenDeathRules.Tally tally)
        {
            if (tally == null || room.SuddenDeathMs == 0 || !SuddenDeathRules.MayEvaluate(room.SuddenDeathMs, nowMs)) return null;
            int[] playing = DominionRules.TeamsPlayingSuddenDeath(room.SuddenDeathTeams, room.Wins, teamsInMatch);
            SuddenDeathResult verdict = SuddenDeathRules.Judge(tally, playing, cfg.SameInstantToleranceMs);
            if (verdict.State == SuddenDeathState.Ongoing) return null;

            DominionWrite write = verdict.State == SuddenDeathState.Won
                ? Stage(room, WhatSuddenDeathWon, new Hashtable
                {
                    { DominionKeys.Stage, (int)DominionStage.Over },
                    { DominionKeys.Winner, verdict.Team },
                })
                : Stage(room, WhatSuddenDeathReplay, new Hashtable
                {
                    { DominionKeys.SuddenDeathStart, DominionRules.StageEndMs(nowMs, cfg.SuddenDeathCountdownSeconds) },
                    // A33: only the teams whose last players fell together play again; the others stay out (dead and waiting).
                    { DominionKeys.SuddenDeathTeams, verdict.ReplayTeams ?? playing },
                });
            write.Expected[DominionKeys.SuddenDeathStart] = room.SuddenDeathMs;
            return write;
        }

        /// <summary>A round start with a centre in play also writes the centre's first payout: the first delay after the round starts.</summary>
        private static void AddCentre(Hashtable props, DominionFlowNumbers cfg, int roundStartMs)
        {
            if (cfg.HasCentre)
                props[DominionKeys.CentrePayout] = DominionRules.NextCentrePayoutMs(roundStartMs, roundStartMs, cfg.CentreFirstMs, cfg.CentreIntervalMs);
        }

        /// <summary>True when the zones still have to be reset for the stage the room is in: a Round or a Break whose dEnd is not the one dRz
        /// names. Any master asks this, so a master that took over mid-way finishes the reset the old one never did.</summary>
        public static bool ZoneResetDue(DominionRoomState room) =>
            room.HasRound && (room.Stage == DominionStage.Round || room.Stage == DominionStage.Break) && room.ResetFor != room.EndMs;

        /// <summary>The write that records the zones were reset for this stage (after the master reset them), expecting the stage it saw.</summary>
        public static DominionWrite ZonesResetDone(DominionRoomState room) =>
            Stage(room, "zones reset", new Hashtable { { DominionKeys.ZonesResetFor, room.EndMs } });

        /// <summary>The write for a stage change: the props, expecting the stage, round and end time this client computed it from.</summary>
        private static DominionWrite Stage(DominionRoomState room, string what, Hashtable props, bool scoresRound = false)
        {
            var expected = new Hashtable
            {
                { DominionKeys.Stage, (int)room.Stage },
                { DominionKeys.Round, room.Round },
                { DominionKeys.StageEnd, room.EndMs },
            };
            // A write that scores the round also expects the points it scored on to still be the room's: if a points write was refused in
            // between, this one is refused too and the next try scores from the room (null: no points write yet, the key must be absent).
            if (scoresRound) expected[DominionKeys.PointsSeq] = room.PointsSeq == 0 ? null : (object)room.PointsSeq;
            return new DominionWrite { What = what, Props = props, Expected = expected };
        }

        /// <summary>A copy of the per-team array padded to the team slots (a short or missing array reads as zeros).</summary>
        private static int[] Slots(int[] values)
        {
            var copy = new int[Math.Max(DominionKeys.TeamSlots, values != null ? values.Length : 0)];
            if (values != null) Array.Copy(values, copy, values.Length);
            return copy;
        }

        /// <summary>The one team of the match that still has a player, or -1 (two or more teams have players, or nobody does, or the match has
        /// fewer than two teams).</summary>
        public static int OnlyTeamWithPlayers(int[] teamsInMatch, int[] playersPerTeam)
        {
            if (teamsInMatch == null || teamsInMatch.Length < 2 || playersPerTeam == null) return -1;
            int found = -1, count = 0;
            foreach (int team in teamsInMatch)
            {
                if (team < 0 || team >= playersPerTeam.Length || playersPerTeam[team] <= 0) continue;
                found = team;
                count++;
            }
            return count == 1 ? found : -1;
        }

        /// <summary>The Player Properties a late joiner writes together with the seat write when they take a TEAM seat during sudden death (Tudor A4):
        /// alive = false with the given death stamp (ArrivalDeathStamp: the earliest moment, so the joiner can never be the one who "fell last").
        /// Nobody respawns there, so they can only wait dead; without the flag the master would count them as alive (a player with no flag has never
        /// died) for the whole time the body loads. Null for any other join (a spectator seat, another stage), and no stamp (0) while the server
        /// clock is not synced (the master then waits for the body's own).</summary>
        public static Hashtable LateJoinerPlayerProps(DominionStage stage, bool takingTeamSeat, int stampMs)
        {
            if (stage != DominionStage.SuddenDeath || !takingTeamSeat) return null;
            var props = new Hashtable { { PlayerLifecycle.AliveKey, false } };
            if (stampMs != 0) props[PlayerLifecycle.LastStandAtKey] = stampMs;
            return props;
        }

        /// <summary>The moment no real fall in this sudden death can be earlier than, with room to spare: the circle's start minus the get-ready
        /// countdown (when the stage was written, before which nobody can fall) minus the same-instant tolerance and one more ms, so an arrival
        /// stamped with it is never within the tolerance of a real fall. 0 when no circle start is known.</summary>
        public static int EarliestFallMs(int suddenDeathStartMs, float countdownSeconds, int toleranceMs)
        {
            if (suddenDeathStartMs == 0) return 0;
            int moment = unchecked(suddenDeathStartMs - (int)Math.Round(Math.Max(0f, countdownSeconds) * 1000f) - Math.Max(0, toleranceMs) - 1);
            return moment == 0 ? 1 : moment; // 0 means "no stamp" everywhere
        }

        /// <summary>The death stamp of a player who ARRIVES dead in sudden death (a late joiner on a team seat, a rejoiner, a body loading in): a player
        /// already dead keeps the stamp they have (their real moment of falling), anyone else gets the earliest moment, so arriving never makes a team
        /// the one that fell last and wins on it. Outside sudden death, or with no earliest moment known, it is the time now.</summary>
        public static int ArrivalDeathStamp(DominionStage stage, bool alreadyDead, bool hasStamp, int existingStamp, int earliestMs, int nowMs)
        {
            if (stage != DominionStage.SuddenDeath) return nowMs;
            if (alreadyDead && hasStamp) return existingStamp;
            return earliestMs != 0 ? earliestMs : nowMs;
        }

        /// <summary>What a client does once on seeing the room go from (prevRound, prevStage) to (round, stage): a break starting is a full fresh
        /// start for every player, a round starting after a break puts everyone back at their spawn keeping their picks. The break before round 1 needs
        /// nothing (going live already did the fresh start), and nothing else is an edge.</summary>
        public static DominionEdge EdgeBetween(int prevRound, DominionStage prevStage, int round, DominionStage stage)
        {
            if (stage == DominionStage.Break && prevStage == DominionStage.Round) return DominionEdge.BreakStarted;
            if (stage == DominionStage.Round && prevStage == DominionStage.Break) return DominionEdge.RoundStarted;
            return DominionEdge.None;
        }

        /// <summary>True when the room just went into sudden death or just restarted it (a new circle start, dSd, after everyone fell in the same
        /// instant): every client then puts the tied teams back alive at their spawn and the others dead. The same stage with the same circle start is
        /// not an edge, so another key changing re-runs nothing.</summary>
        public static bool IsSuddenDeathStart(DominionStage prevStage, int prevSuddenDeathMs, DominionStage stage, int suddenDeathMs) =>
            stage == DominionStage.SuddenDeath && suddenDeathMs != 0 && (prevStage != DominionStage.SuddenDeath || prevSuddenDeathMs != suddenDeathMs);

        /// <summary>Whether the Tab scoreboard (kills, deaths, damage) carries on counting through this edge. Both Dominion edges keep it: the
        /// scoreboard covers the whole match and is zeroed only at go-live (Tudor's default A15), so the break's fresh start must not wipe it.</summary>
        public static bool KeepsScoreboard(DominionEdge edge) => edge == DominionEdge.BreakStarted || edge == DominionEdge.RoundStarted;
    }
}
