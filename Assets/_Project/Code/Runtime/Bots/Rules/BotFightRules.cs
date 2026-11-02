using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>Whom a bot fights: the closest enemy its team can see, kept until another is clearly closer (no flicking between two).</summary>
    public static class BotTargetRules
    {
        /// <summary>Whether an enemy is worth shooting at all: alive and not behind its respawn shield.</summary>
        public static bool Targetable(bool alive, bool shielded) => alive && !shielded;

        /// <summary>The actor to fight among (actor, distance) pairs its team can see; -1 when none.</summary>
        public static int Pick(IReadOnlyList<(int actor, float distance)> visible, int current, float switchMetres)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            float currentDistance = -1f;
            for (int i = 0; i < visible.Count; i++)
            {
                var (actor, distance) = visible[i];
                if (actor == current) currentDistance = distance;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = actor;
                }
            }
            if (currentDistance >= 0f && bestDistance > currentDistance - switchMetres) return current;
            return best;
        }
    }

    /// <summary>How a bot aims: a miss that settles while it keeps shooting the same target, a lead on slow shots, a limited turn rate.</summary>
    public static class BotAimRules
    {
        /// <summary>How far to the side its aim misses after shooting the same target for secondsOnTarget.</summary>
        public static float ErrorMetres(float secondsOnTarget, float start, float min, float settleSeconds) =>
            Mathf.Lerp(start, min, Mathf.Clamp01(secondsOnTarget / Mathf.Max(0.0001f, settleSeconds)));

        /// <summary>The point to aim at: the target plus fraction of how far it moves while the shot flies (no lead for an instant beam).</summary>
        public static Vector3 Lead(Vector3 target, Vector3 velocity, float distance, float projectileSpeed, float fraction)
        {
            if (projectileSpeed <= 0f) return target;
            float flight = distance / projectileSpeed;
            return target + velocity * (flight * fraction);
        }

        /// <summary>The flat aim direction after turning at most maxDegrees toward desired.</summary>
        public static Vector3 TurnToward(Vector3 current, Vector3 desired, float maxDegrees)
        {
            current.y = 0f;
            desired.y = 0f;
            if (desired.sqrMagnitude < 1e-6f) return current.normalized;
            if (current.sqrMagnitude < 1e-6f) return desired.normalized;
            return Vector3.RotateTowards(current.normalized, desired.normalized, maxDegrees * Mathf.Deg2Rad, 0f);
        }

        public static bool OnTarget(Vector3 aimDirection, Vector3 toTarget, float toleranceDegrees)
        {
            aimDirection.y = 0f;
            toTarget.y = 0f;
            return Vector3.Angle(aimDirection, toTarget) <= toleranceDegrees;
        }
    }

    /// <summary>Where a bot stands in a fight: inside its build's range, strafing from side to side.</summary>
    public static class BotRangeRules
    {
        /// <summary>Outside [min, max] it backs off or walks in; once moving it keeps on until deadBand metres inside the range, so it
        /// does not step back and forth at the edge.</summary>
        public static RangeMove Decide(float distance, float min, float max, RangeMove previous, float deadBand)
        {
            if (distance < min || (previous == RangeMove.BackOff && distance < min + deadBand)) return RangeMove.BackOff;
            if (distance > max || (previous == RangeMove.Approach && distance > max - deadBand)) return RangeMove.Approach;
            return RangeMove.Hold;
        }

        /// <summary>The strafe side (+1 right, -1 left) after secondsOnThisSide: it switches once switchAfter has passed.</summary>
        public static int StrafeSide(int side, float secondsOnThisSide, float switchAfter) =>
            secondsOnThisSide > switchAfter ? -side : side;
    }

    /// <summary>When a bot pulls the trigger: after its reaction, aimed, in range, able, and not overheating on purpose.</summary>
    public static class BotFireRules
    {
        public static bool ShouldFire(bool targetVisible, bool reacted, bool aimed, float distance, float weaponRange, float heat01, float stopHeat,
            float targetHealth01, float finishHealth, bool canAct) =>
            targetVisible && reacted && aimed && canAct && distance <= weaponRange && (heat01 < stopHeat || targetHealth01 < finishHealth);

        public static bool Reacted(float secondsSinceSeen, float reactionSeconds) => secondsSinceSeen >= reactionSeconds;
    }

    /// <summary>The Vent: one press per lockout, inside the window, only on a lockout where the bot rolled to hit it.</summary>
    public static class BotVentRules
    {
        public static bool PressNow(bool silenced, bool windowOpen, bool willHit, bool alreadyPressed) =>
            silenced && windowOpen && willHit && !alreadyPressed;
    }

    /// <summary>Fight or back off: below retreatAt it backs off until returnAt; cornered it always fights.</summary>
    public static class BotHealthRules
    {
        public static BotStance Stance(BotStance current, float health01, float retreatAt, float returnAt, bool cornered)
        {
            if (cornered) return BotStance.Fight;
            if (current == BotStance.Fight) return health01 < retreatAt ? BotStance.Retreat : BotStance.Fight;
            return health01 >= returnAt ? BotStance.Fight : BotStance.Retreat;
        }
    }

    /// <summary>Packs are taken when they matter and are close; a bot never crosses the map for one.</summary>
    public static class BotPackRules
    {
        public static bool WantsHealthPack(float health01, float threshold, bool enemyInSight, float distance, float maxDistance) =>
            health01 < threshold && !enemyInSight && distance <= maxDistance;

        public static bool WantsUltimatePack(bool meterFull, float distance, float maxDistance) => !meterFull && distance <= maxDistance;
    }

    /// <summary>Each build's rule for its attachment, movement ability and ultimate (BotConfig holds the numbers).</summary>
    public static partial class BotAbilityRules
    {
        public static bool Flame(bool hasTarget, float distance, float angleToTarget, float flameMetres, float halfConeDegrees) =>
            hasTarget && distance <= flameMetres && angleToTarget <= halfConeDegrees;

        public static bool Zip(bool hasTarget, float distance, float minMetres, float maxMetres, float health01, float minHealth, bool clearLine) =>
            hasTarget && clearLine && distance >= minMetres && distance <= maxMetres && health01 >= minHealth;

        public static bool Fence(int enemiesWithinFence, int enemiesWithinTwoRange) => enemiesWithinFence >= 1 || enemiesWithinTwoRange >= 2;

        public static bool Cover(bool inFight, float health01, float coverHealth, float targetDistance, float minMetres) =>
            inFight && health01 < coverHealth && targetDistance > minMetres;

        public static bool Sprint(bool travelling, float secondsSinceEnemySeen, float quietSeconds, float heat01, float maxHeat) =>
            travelling && secondsSinceEnemySeen >= quietSeconds && heat01 < maxHeat;

        public static bool HealZone(bool inFight, float ownHealth01, float lowestNearTeammateHealth01, float threshold) =>
            inFight && (ownHealth01 < threshold || lowestNearTeammateHealth01 < threshold);

        public static bool Scope(bool hasTarget, float distance, float minMetres) => hasTarget && distance > minMetres;

        public static bool Blink(float health01, float blinkHealth, float nearestEnemyMetres, float enemyMetres) =>
            health01 < blinkHealth && nearestEnemyMetres <= enemyMetres;

        /// <summary>The point metres straight away from enemy (flat), for an escape.</summary>
        public static Vector3 AwayFrom(Vector3 self, Vector3 enemy, float metres)
        {
            Vector3 away = self - enemy;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f) away = Vector3.back;
            return self + away.normalized * metres;
        }

        public static bool Raybeam(bool hasTarget, float distance, float maxMetres, float secondsInSight, float minSeen) =>
            hasTarget && distance <= maxMetres && secondsInSight >= minSeen;
    }
}
