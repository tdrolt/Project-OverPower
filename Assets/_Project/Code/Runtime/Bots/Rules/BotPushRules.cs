using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// Pushing an enemy capital in Conquest beyond "enough of the team near it": an exposed capital (few defenders known) is pushed from
    /// further away, a bot near a Tier II zone next to an enemy capital goes on by itself when nobody is known to defend it, and a bot taking
    /// a capital or Tier II keeps going while its own capital is attacked once its capture is far enough along (a capital trade).
    /// </summary>
    public static class BotPushRules
    {
        /// <summary>An enemy capital is exposed when at most maxDefenders enemies are known near it.</summary>
        public static bool Exposed(int enemiesKnownNear, int maxDefenders) => enemiesKnownNear <= maxDefenders;

        /// <summary>Whether the bot pushes an enemy capital it may take without enough of its team near it: exposed, with at least
        /// pushTeammates of its team (itself included) within the wider reach; or forced.</summary>
        public static bool PushAnyway(bool exposed, int teammatesWithinWideReach, int pushTeammates, bool forced) =>
            (exposed && teammatesWithinWideReach >= pushTeammates) || forced;

        /// <summary>Forced on: the bot is near a Tier II zone next to that capital which its team holds or is taking, and no enemy is known
        /// near the capital.</summary>
        public static bool Forced(bool nearOurTierTwoNextToIt, int enemiesKnownNearCapital) =>
            nearOurTierTwoNextToIt && enemiesKnownNearCapital == 0;

        /// <summary>How far its team's taking of a zone has come, 0..1. A zone another team held when the bot started is drained first (the
        /// first half; fill01 is the owner's hold left) and then captured (the second half); a zone that was neutral is only captured. 0 when
        /// its team is not the one taking it.</summary>
        public static float CaptureDone01(bool startedOwned, bool stillOwned, bool oursInProgress, float fill01)
        {
            if (!oursInProgress) return 0f;
            float fill = Mathf.Clamp01(fill01);
            if (stillOwned) return (1f - fill) * 0.5f;
            return startedOwned ? 0.5f + fill * 0.5f : fill;
        }

        /// <summary>A capital trade: taking another team's capital or Tier II, the bot keeps going once its capture is at least tradeDone01
        /// along; otherwise it goes back when its own capital is being taken.</summary>
        public static bool KeepTaking(bool takingEnemyCapitalOrTierTwo, float captureDone01, float tradeDone01) =>
            takingEnemyCapitalOrTierTwo && captureDone01 >= tradeDone01;
    }
}
