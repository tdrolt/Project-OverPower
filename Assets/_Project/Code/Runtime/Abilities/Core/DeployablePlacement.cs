namespace Overpower.Abilities
{
    /// <summary>
    /// The tail NetworkedDeployable.Spawn appends to instantiationData after whatever the subclass passed: the placer's
    /// PhotonNetwork.ServerTimestamp, then the placer as int[] { actor, team }. The placer has to travel because the
    /// instantiate's sender is the host for every deployable a bot places. An int[] rather than two more ints, so data
    /// without it still reads as "timestamp last" and its owner falls back to the sender.
    /// </summary>
    public static class DeployablePlacement
    {
        public static object[] Append(object[] subclassData, int placedServerTimestampMs, int placerActor, int placerTeam)
        {
            int count = subclassData != null ? subclassData.Length : 0;
            var data = new object[count + 2];
            if (count > 0)
                System.Array.Copy(subclassData, data, count);
            data[count] = placedServerTimestampMs;
            data[count + 1] = new[] { placerActor, placerTeam };
            return data;
        }

        /// <summary>Splits Append's tail off raw and returns the subclass's own data at the indices it passed. hasPlacer is false
        /// when only the timestamp is there. False when not even the timestamp is: subclassData is then raw, whole.</summary>
        public static bool TrySplit(object[] raw, out object[] subclassData, out int placedServerTimestampMs,
                                    out bool hasPlacer, out int placerActor, out int placerTeam)
        {
            subclassData = raw;
            placedServerTimestampMs = 0;
            hasPlacer = false;
            placerActor = -1;
            placerTeam = -1;
            if (raw == null || raw.Length == 0)
                return false;

            int last = raw.Length - 1;
            if (last >= 1 && raw[last] is int[] placer && placer.Length == 2 && raw[last - 1] is int placedWithPlacer)
            {
                hasPlacer = true;
                placerActor = placer[0];
                placerTeam = placer[1];
                placedServerTimestampMs = placedWithPlacer;
                subclassData = Head(raw, last - 1);
                return true;
            }

            if (!(raw[last] is int placedMs))
                return false;
            placedServerTimestampMs = placedMs;
            subclassData = Head(raw, last);
            return true;
        }

        /// <summary>Whether a fresh-start sweep for sweptActor's body takes a deployable: only a copy this client controls, placed
        /// for that actor. Sweeping the local player's own body also takes what this client only inherited from a dropped player
        /// (controlled here, owned by them), which that sweep has always done.</summary>
        public static bool IsSweptFor(bool isOwnerClient, bool ownedByLocalPlayer, int placerActor, int sweptActor, bool sweptIsLocalPlayer) =>
            isOwnerClient && (placerActor == sweptActor || (sweptIsLocalPlayer && !ownedByLocalPlayer));

        private static object[] Head(object[] raw, int count)
        {
            var head = new object[count];
            System.Array.Copy(raw, head, count);
            return head;
        }
    }
}
