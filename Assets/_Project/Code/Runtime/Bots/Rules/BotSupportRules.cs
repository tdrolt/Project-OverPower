using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// The Laser bot as supporting fire: it pairs up with a teammate (a human first), follows behind it at a distance, and after a respawn
    /// waits a moment for a teammate about to come back so they walk out together.
    /// </summary>
    public static class BotSupportRules
    {
        /// <summary>The partner among (actor, distance, human) teammates: the current one while it is still among them; else the nearest
        /// human; else the nearest teammate; -1 with none.</summary>
        public static int PickPartner(int current, IReadOnlyList<(int actor, float distance, bool human)> teammates)
        {
            int nearestHuman = -1, nearest = -1;
            float humanDistance = float.MaxValue, anyDistance = float.MaxValue;
            for (int i = 0; i < teammates.Count; i++)
            {
                var (actor, distance, human) = teammates[i];
                if (actor == current) return current;
                if (human && distance < humanDistance)
                {
                    humanDistance = distance;
                    nearestHuman = actor;
                }
                if (distance < anyDistance)
                {
                    anyDistance = distance;
                    nearest = actor;
                }
            }
            return nearestHuman >= 0 ? nearestHuman : nearest;
        }

        /// <summary>Where to stand behind the partner (flat, at the partner's height): against its heading, or on the bot's own side when it
        /// has none, at the bot's distance from it held between minMetres and maxMetres.</summary>
        public static Vector3 FollowPoint(Vector3 partner, Vector3 heading, Vector3 self, float minMetres, float maxMetres)
        {
            Vector3 offset = self - partner;
            offset.y = 0f;
            heading.y = 0f;
            Vector3 back;
            if (heading.sqrMagnitude > 1e-6f) back = -heading.normalized;
            else if (offset.sqrMagnitude > 1e-6f) back = offset.normalized;
            else back = Vector3.back;
            return partner + back * Mathf.Clamp(offset.magnitude, minMetres, maxMetres);
        }

        /// <summary>Whether the bot that respawned secondsSinceRespawn ago still waits: within waitSeconds, for a teammate who is back within
        /// the time left (soonestTeammateSeconds: the shortest respawn wait among its teammates, float.MaxValue with none).</summary>
        public static bool WaitForTeammate(float secondsSinceRespawn, float waitSeconds, float soonestTeammateSeconds) =>
            secondsSinceRespawn < waitSeconds && soonestTeammateSeconds <= waitSeconds - secondsSinceRespawn;
    }
}
