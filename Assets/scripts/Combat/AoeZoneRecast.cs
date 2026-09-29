using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The AoE Zone ultimate's "throw" (Tudor's D11): while your zone is up, pressing the ultimate once
    /// more throws it to your cursor, at most a set range from you, and it stays there. These are the
    /// pure rules: whether a throw is allowed, where it lands, and how the throw is written down as a
    /// Player Property (so a client that arrives after the throw still finds the zone at the thrown spot).
    /// </summary>
    public static class AoeZoneRecast
    {
        /// <summary>The Player Property the thrower writes on themselves.</summary>
        public const string PropertyKey = "aozT";

        /// <summary>A throw needs your own zone still up, and only one throw per zone.</summary>
        public static bool MayRecast(bool ownZoneAlive, bool alreadyThrown) => ownZoneAlive && !alreadyThrown;

        /// <summary>The cursor point cut to at most range metres from the caster (sideways, same
        /// direction), at the caster's own height.</summary>
        public static Vector3 LandingPoint(Vector3 casterPosition, Vector3 cursorPoint, float range)
        {
            Vector3 flat = new Vector3(cursorPoint.x - casterPosition.x, 0f, cursorPoint.z - casterPosition.z);
            float distance = flat.magnitude;
            if (distance > range && distance > 0.0001f)
                flat *= range / distance;
            return new Vector3(casterPosition.x + flat.x, casterPosition.y, casterPosition.z + flat.z);
        }

        /// <summary>Property value: { zone view id, x, y, z }. Keyed to the zone's view id so an old
        /// value can never move a later zone.</summary>
        public static object[] Encode(int zoneViewId, Vector3 point)
            => new object[] { zoneViewId, point.x, point.y, point.z };

        /// <summary>True only for a well-formed value that names this zone.</summary>
        public static bool TryDecode(object raw, int zoneViewId, out Vector3 point)
        {
            point = default;
            if (!(raw is object[] v) || v.Length < 4 || !(v[0] is int id) || id != zoneViewId
                || !(v[1] is float x) || !(v[2] is float y) || !(v[3] is float z))
                return false;
            point = new Vector3(x, y, z);
            return true;
        }
    }
}
