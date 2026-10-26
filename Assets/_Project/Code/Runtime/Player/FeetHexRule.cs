namespace Overpower.Arena
{
    /// <summary>Whether a player's team hexagon shows on the floor under their feet: only with the Arena Look switch on, a known team
    /// (never a guessed colour) and a living player. The fog decides the rest, as for the player's mech.</summary>
    public static class FeetHexRule
    {
        public static bool Show(bool switchedOn, int team, bool alive) => switchedOn && team >= 0 && alive;
    }
}
