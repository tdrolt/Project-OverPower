using UnityEngine;

/// <summary>
/// How the match camera starts and turns. A match starts at the farthest wheel zoom; Q and E, held, turn the view around the
/// player on top of the team's starting angle (CameraTracking's team yaw), which the minimap keeps as its fixed "up".
/// </summary>
public static class CameraViewRules
{
    /// <summary>The scroll zoom a match starts at: the farthest the wheel reaches.</summary>
    public static float StartZoom(float closest, float farthest) => Mathf.Max(closest, farthest);

    /// <summary>Held keys to a turn direction: Q turns the view left (-1), E right (+1); both or neither, no turn.</summary>
    public static float TurnAxis(bool qHeld, bool eHeld) => (eHeld ? 1f : 0f) - (qHeld ? 1f : 0f);

    /// <summary>The turn after one frame, kept within -180..180 degrees.</summary>
    public static float Turned(float turnDegrees, float axis, float degreesPerSecond, float deltaSeconds) =>
        Mathf.Repeat(turnDegrees + axis * degreesPerSecond * deltaSeconds + 180f, 360f) - 180f;

    /// <summary>Whether Q/E turn the view: never in a spectator seat (Q/E pick the player there) or while typing or a panel holds input.</summary>
    public static bool TurnsWithKeys(bool spectatorSeat, bool keysBlocked) => !spectatorSeat && !keysBlocked;

    /// <summary>The angle the screen looks along: the team's starting angle plus the player's own turn.</summary>
    public static float ScreenYaw(float teamYaw, float turnDegrees) => teamYaw + turnDegrees;
}
