using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// One home for every fact about the Barrier layer (Amendment 1's jersey barriers): its name, its bit, and the
    /// two combined masks callers actually need. Lazily cached, the same way GroundSnap.GroundMask is -
    /// LayerMask.NameToLayer must never run from a MonoBehaviour's static initialiser (see PlayerDisplacement's own
    /// blockMask comment: it throws TypeInitializationException the first time the type is touched), only from
    /// Awake, Start or a lazy static like this one. Logs once and falls back to 0 (no bit at all) if the layer is
    /// missing, rather than throwing and taking the whole match down with it.
    /// </summary>
    public static class ArenaLayers
    {
        public const string BarrierLayerName = "Barrier";

        private static int barrierBit = -1; // -1 = not yet resolved
        private static int wallsAndBarriers = -1;
        private static int bodiesWallsAndBarriers = -1;
        private static bool loggedMissing;

        /// <summary>The Barrier layer's own bit (1 &lt;&lt; its layer index), or 0 if the layer doesn't exist.</summary>
        public static int Barrier
        {
            get
            {
                if (barrierBit < 0)
                    barrierBit = Resolve();
                return barrierBit;
            }
        }

        /// <summary>Building + Barrier: what a Forced shove (a knockback) stops at, like a wall. Tudor's answer,
        /// 2026-09-19 (Q10): "no", a shoved player stops against a barrier rather than being carried over it -
        /// being shoved isn't the player's own move.</summary>
        public static int WallsAndBarriers
        {
            get
            {
                if (wallsAndBarriers < 0)
                    wallsAndBarriers = LayerMask.GetMask("Building") | Barrier;
                return wallsAndBarriers;
            }
        }

        /// <summary>Default + Building + Barrier: every layer a player's own body can be blocked by at once - a
        /// blink or portal's fit check, a dummy's knockback sweep.</summary>
        public static int BodiesWallsAndBarriers
        {
            get
            {
                if (bodiesWallsAndBarriers < 0)
                    bodiesWallsAndBarriers = LayerMask.GetMask("Default", "Building") | Barrier;
                return bodiesWallsAndBarriers;
            }
        }

        private static int Resolve()
        {
            int layer = LayerMask.NameToLayer(BarrierLayerName);
            if (layer >= 0)
                return 1 << layer;

            if (!loggedMissing)
            {
                Debug.LogError($"[ArenaLayers] The '{BarrierLayerName}' layer does not exist (arena step 4a's " +
                                "TagManager change should have added it at layer 8) - every barrier check falls " +
                                "back to no bit at all.");
                loggedMissing = true;
            }
            return 0;
        }
    }
}
