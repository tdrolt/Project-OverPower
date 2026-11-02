namespace Overpower.Bots
{
    /// <summary>Team play: fall back and regroup when outnumbered, join a teammate's fight, and what the centre scan shows the bot's team.</summary>
    public static class BotTeamRules
    {
        /// <summary>Outnumbered: the enemies near it are at least its team near it (itself included) plus the margin.</summary>
        public static bool Outnumbered(int enemiesNear, int teammatesNearWithSelf, int margin) => enemiesNear >= teammatesNearWithSelf + margin;

        /// <summary>The bot's stance from its health stance: cornered it fights; low health it backs off; outnumbered it regroups.</summary>
        public static BotStance Stance(BotStance healthStance, bool outnumbered, bool cornered)
        {
            if (cornered) return BotStance.Fight;
            if (healthStance == BotStance.Retreat) return BotStance.Retreat;
            return outnumbered ? BotStance.Regroup : BotStance.Fight;
        }

        /// <summary>Whether an enemy is in a fight with a teammate the bot should help: the teammate within helpMetres of the bot, the enemy
        /// within fightMetres of the teammate.</summary>
        public static bool FightingNearTeammate(float teammateMetres, float helpMetres, float enemyToTeammateMetres, float fightMetres) =>
            teammateMetres <= helpMetres && enemyToTeammateMetres <= fightMetres;

        /// <summary>Whether the wave running now shows enemies to the team: it is still travelling and it is the team's (the team held the
        /// centre when it started).</summary>
        public static bool ScanShowsUs(bool travelling, int holderTeam, int team) => travelling && team >= 0 && holderTeam == team;

        /// <summary>Where the band the front swept since the last look starts (CentreScanRules.FrontSwept's prevRadius): the radius seen last
        /// time on the same wave, 0 on a new one.</summary>
        public static float ScanSweptFrom(int lastWaveStartMs, int waveStartMs, float lastRadius) => waveStartMs == lastWaveStartMs ? lastRadius : 0f;
    }
}
