using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// A map's own camera rule. The follow camera turns every team's view so that the same direction is "up" on screen for everyone (CameraTracking, from where
    /// the team's spawn sits - the angled view of the triangle arena). A scene that carries one of these with "Own Spawn On Left" on plays on a straight view
    /// instead: the camera runs parallel to the lane, each player's own spawn is on the left of the screen and the enemy on the right, for both teams. A scene
    /// without one keeps the angled view, so Game Scene is unchanged.
    /// </summary>
    public sealed class SceneCameraConfig : MonoBehaviour
    {
        [SerializeField, Tooltip("On: the camera looks straight along the map's long walls, your own spawn is on the left of your screen and the enemy's on the right (White and " +
                                 "Purple see mirror images, and pressing right always walks towards the enemy). Off: the angled view the triangle arena has. Spectators watch from White's side.")]
        private bool ownSpawnOnLeft = true;

        public bool OwnSpawnOnLeft => ownSpawnOnLeft;

        /// <summary>Whether this scene asks for the straight view (false when it carries no SceneCameraConfig, e.g. Game Scene).</summary>
        public static bool SceneWantsOwnSpawnOnLeft()
        {
            var found = FindFirstObjectByType<SceneCameraConfig>();
            return found != null && found.ownSpawnOnLeft;
        }
    }

    /// <summary>The camera's turn about the vertical axis (Unity yaw, degrees: world direction (sin yaw, cos yaw) is the top of the screen), as pure rules.</summary>
    public static class CameraYawRules
    {
        /// <summary>The yaw of a camera that has the enemy on the RIGHT of the screen. toOwnSpawn is the horizontal vector from the middle of all spawns to this team's spawn,
        /// so the enemy lies the other way. Screen right is (cos yaw, -sin yaw); solving for -toOwnSpawn gives atan2(z, -x). White (spawn east) gets 180, Purple (west) 0.</summary>
        public static float OwnSpawnLeftYaw(float toOwnSpawnX, float toOwnSpawnZ) => Mathf.Atan2(toOwnSpawnZ, -toOwnSpawnX) * Mathf.Rad2Deg;

        /// <summary>The yaw of the angled view: the team's own spawn direction, plus the one offset that nudges all teams together.</summary>
        public static float AngledYaw(float toOwnSpawnX, float toOwnSpawnZ, float teamYawOffset) => Mathf.Atan2(toOwnSpawnX, toOwnSpawnZ) * Mathf.Rad2Deg + teamYawOffset;

        /// <summary>The yaw a team's camera takes: the straight view when the scene asks for it, else the angled one.</summary>
        public static float TeamYaw(bool ownSpawnOnLeft, float toOwnSpawnX, float toOwnSpawnZ, float teamYawOffset) =>
            ownSpawnOnLeft ? OwnSpawnLeftYaw(toOwnSpawnX, toOwnSpawnZ) : AngledYaw(toOwnSpawnX, toOwnSpawnZ, teamYawOffset);

        /// <summary>The fixed yaw a spectator (a seat with no body) watches from: White's straight view on a map that asks for one, else the theme's one angle.</summary>
        public static float SpectatorYaw(bool ownSpawnOnLeft, float themeAngle, float toWhiteSpawnX, float toWhiteSpawnZ) =>
            ownSpawnOnLeft ? OwnSpawnLeftYaw(toWhiteSpawnX, toWhiteSpawnZ) : themeAngle;
    }
}
