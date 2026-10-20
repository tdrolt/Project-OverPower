namespace Overpower.Abilities
{
    /// <summary>How far the hexagon inside a portal has turned: from the server clock, so every client draws it at the same angle.</summary>
    public static class PortalGateLook
    {
        public static float InnerYaw(int serverMs, float degreesPerSecond)
        {
            double yaw = serverMs / 1000.0 * degreesPerSecond % 360.0;
            return (float)(yaw < 0.0 ? yaw + 360.0 : yaw);
        }
    }
}
