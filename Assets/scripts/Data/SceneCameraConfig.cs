using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// A map's own camera rule. By default the follow camera turns every team's view so the same direction is "up" on screen for everyone
    /// (CameraTracking, from where the team's spawn sits - the triangle arena's angled view). A scene carrying this with "Own Spawn On Left" on uses
    /// a straight view instead: camera parallel to the lane, each player's own spawn on the left of the screen, for both teams. No component, angled view.
    /// </summary>
    public sealed class SceneCameraConfig : MonoBehaviour
    {
        [SerializeField, Tooltip("On: the camera looks straight along the map's long walls, your own spawn is on the left of your screen and the enemy's on the right (White and " +
                                 "Purple see mirror images, and pressing right always walks towards the enemy). Off: the angled view the triangle arena has. Spectators watch from White's side.")]
        private bool ownSpawnOnLeft = true;

        public bool OwnSpawnOnLeft => ownSpawnOnLeft;

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

        /// <summary>The horizontal vector from the middle of all the placed spawns to one team's spawn (the camera and the spectator both start from it). A
        /// missing spawn is left out of the middle; false when that team has no spawn to point at or no spawn is placed at all.</summary>
        public static bool ToSpawnFromCentre(Transform[] spawns, int team, out Vector2 toSpawn)
        {
            toSpawn = Vector2.zero;
            if (spawns == null || team < 0 || team >= spawns.Length || spawns[team] == null) return false;
            Vector3 centre = Vector3.zero;
            int counted = 0;
            foreach (Transform spawn in spawns)
            {
                if (spawn == null) continue;
                centre += spawn.position;
                counted++;
            }
            if (counted == 0) return false;
            centre /= counted;
            Vector3 delta = spawns[team].position - centre;
            toSpawn = new Vector2(delta.x, delta.z);
            return true;
        }

        /// <summary>The first team that has a spawn placed (-1 when none): the team a spectator watches from, so nothing assumes which index is which colour.</summary>
        public static int FirstTeamWithSpawn(Transform[] spawns)
        {
            if (spawns == null) return -1;
            for (int i = 0; i < spawns.Length; i++)
                if (spawns[i] != null) return i;
            return -1;
        }

        public static float AngledYaw(float toOwnSpawnX, float toOwnSpawnZ, float teamYawOffset) => Mathf.Atan2(toOwnSpawnX, toOwnSpawnZ) * Mathf.Rad2Deg + teamYawOffset;

        public static float TeamYaw(bool ownSpawnOnLeft, float toOwnSpawnX, float toOwnSpawnZ, float teamYawOffset) =>
            ownSpawnOnLeft ? OwnSpawnLeftYaw(toOwnSpawnX, toOwnSpawnZ) : AngledYaw(toOwnSpawnX, toOwnSpawnZ, teamYawOffset);

        /// <summary>The fixed yaw a spectator (a seat with no body) watches from: White's straight view on a map that asks for one, else the theme's one angle.</summary>
        public static float SpectatorYaw(bool ownSpawnOnLeft, float themeAngle, float toWhiteSpawnX, float toWhiteSpawnZ) =>
            ownSpawnOnLeft ? OwnSpawnLeftYaw(toWhiteSpawnX, toWhiteSpawnZ) : themeAngle;
    }
}
