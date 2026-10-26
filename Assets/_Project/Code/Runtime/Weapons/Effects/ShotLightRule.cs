using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>One shot in flight as the light picker sees it: whether my team sees it, and its squared distance to the camera.</summary>
    public readonly struct ShotCandidate
    {
        public readonly bool Shown;
        public readonly float SqrDistance;

        public ShotCandidate(bool shown, float sqrDistance)
        {
            Shown = shown;
            SqrDistance = sqrDistance;
        }
    }

    /// <summary>Which shots get one of the few shot lights. A Light is not a Renderer, so the sight gate never hides it: only a shot my
    /// team sees may be lit, or the glow on the floor would give away a shooter in the fog.</summary>
    public static class ShotLightRule
    {
        /// <summary>A gated shot follows its gate; an ungated one is lit only when there is no sight at all (no fog, nothing hidden).</summary>
        public static bool Shown(bool hasGate, bool gateShown, bool hasSight) => hasGate ? gateShown : !hasSight;

        /// <summary>Where a shot's light hangs: straight under the shot, <paramref name="height"/> over the floor below it (never above the shot), so
        /// its glow lands on the floor under the slug rather than as a specular dot off toward the camera.</summary>
        public static Vector3 LightAt(Vector3 shot, float groundY, float height) =>
            new Vector3(shot.x, Mathf.Min(shot.y, groundY + height), shot.z);

        /// <summary>Fills <paramref name="chosen"/> with the indices of at most <paramref name="cap"/> shown shots, nearest first.</summary>
        public static void Pick(IReadOnlyList<ShotCandidate> shots, int cap, List<int> chosen)
        {
            chosen.Clear();
            // cap is a handful, so a repeated nearest-remaining scan beats sorting every shot in a nine-player SMG fight.
            while (chosen.Count < cap)
            {
                int best = -1;
                for (int i = 0; i < shots.Count; i++)
                {
                    if (!shots[i].Shown || chosen.Contains(i))
                        continue;
                    if (best < 0 || shots[i].SqrDistance < shots[best].SqrDistance)
                        best = i;
                }
                if (best < 0)
                    return;
                chosen.Add(best);
            }
        }
    }
}
