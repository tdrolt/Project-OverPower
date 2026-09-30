using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>One thing a beam's ray passed through: a player, a dummy, or a piece of level
    /// geometry. Built from a RaycastHit by the Hitscan component, so the rules below can be tested
    /// without a scene.</summary>
    public readonly struct BeamContact
    {
        /// <summary>Metres from the muzzle to where the ray touched this.</summary>
        public readonly float Distance;

        /// <summary>What has health here, or null when the ray touched level geometry.</summary>
        public readonly IDamageable Target;

        /// <summary>Where the ray touched it in the world - the hit point damage and impact
        /// effects are reported at.</summary>
        public readonly Vector3 Point;

        /// <summary>True when Target is a structure (IStructure - CoverWall today) rather than a
        /// combatant. A structure blocks a beam exactly like a plain wall (Target == null) once it
        /// has taken the hit, regardless of Pierce's own Max Targets - see BeamResolver.Resolve.
        /// Always false for a null Target; a wall already stops the beam by that path and needs no
        /// second one. Set by the caller (Hitscan), which is the one place that actually knows
        /// whether Target implements IStructure - this struct stays a plain data carrier.</summary>
        public readonly bool IsStructure;

        public BeamContact(float distance, IDamageable target, Vector3 point, bool isStructure = false)
        {
            Distance = distance;
            Target = target;
            Point = point;
            IsStructure = isStructure;
        }
    }

    /// <summary>What one beam did: who it struck, in order, and where it visibly ended.</summary>
    public sealed class BeamResult
    {
        /// <summary>Every target the beam deals damage to, nearest first, each exactly once.</summary>
        public IReadOnlyList<BeamContact> Struck { get; }

        /// <summary>How far the beam reaches, in metres - to the wall that stopped it, to the last
        /// target it was allowed to strike, or its full range. The beam effect is drawn this long.</summary>
        public float Length { get; }

        /// <summary>True when a wall ended the beam rather than range or the pierce limit.</summary>
        public bool StoppedOnGeometry { get; }

        /// <summary>True when the beam struck at least one target - what the laser's heat refund
        /// asks. Touching a wall does not count as connecting.</summary>
        public bool Connected => Struck.Count > 0;

        public BeamResult(IReadOnlyList<BeamContact> struck, float length, bool stoppedOnGeometry)
        {
            Struck = struck;
            Length = length;
            StoppedOnGeometry = stoppedOnGeometry;
        }
    }

    /// <summary>
    /// The rules of an instant beam, in one place: given everything a ray passed through, which of
    /// those things take damage and where the beam ends.
    ///
    /// A projectile learns this one frame at a time as it sweeps forward. A beam learns it all at
    /// once, from a single physics query that reports hits in no particular order - so the order,
    /// the "each target once" rule and the stopping rules all have to be applied here explicitly.
    /// Getting any of them wrong is invisible in play (a beam that sometimes skips the nearer
    /// player, or double-damages a player with two hitboxes), which is why this is plain C# with
    /// unit tests rather than code inside the Hitscan component.
    ///
    /// Walls are not special-cased for the through-walls laser. That leaf simply never asks
    /// Physics about the Building layer, so no wall ever arrives here - see IgnoreWalls. A
    /// STRUCTURE (BeamContact.IsStructure - CoverWall) IS special-cased, on purpose: it is an
    /// IDamageable, so without the check below it would just be another pierceable target and a
    /// laser with Pierce's Max Targets at -1 would carry straight through it to whoever stood
    /// behind - contradicting Pierce's own "never lets a beam through a WALL" (Task 1.8b review
    /// finding). The through-walls laser is unaffected either way, since IgnoreWalls removes the
    /// Building layer from Hitscan's own raycast mask before Physics ever runs, so that beam never
    /// produces a structure contact to begin with.
    /// </summary>
    public static class BeamResolver
    {
        /// <summary>Max Targets value meaning "pierce everything in range". Any negative number
        /// reads the same way.</summary>
        public const int Unlimited = -1;

        /// <param name="contacts">Everything the ray passed through, in any order. Not modified.</param>
        /// <param name="maxRange">How far the beam reaches when nothing stops it.</param>
        /// <param name="maxTargets">How many targets it may strike before stopping. Negative is
        /// unlimited; 0 is read as 1, so a typo cannot make a weapon that can never hit anyone.</param>
        /// <param name="shooterActorNumber">Who fired, so the beam passes through its own shooter.</param>
        /// <param name="shooterTeamId">The shooter's team, so it passes through teammates. -1 means
        /// unknown, and then nobody counts as a teammate.</param>
        public static BeamResult Resolve(IReadOnlyList<BeamContact> contacts, float maxRange,
                                         int maxTargets, int shooterActorNumber, int shooterTeamId)
        {
            var ordered = new List<BeamContact>(contacts);
            ordered.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            int cap = maxTargets < 0 ? int.MaxValue : Mathf.Max(1, maxTargets);
            var struck = new List<BeamContact>();
            var alreadyStruck = new HashSet<IDamageable>();

            for (int i = 0; i < ordered.Count; i++)
            {
                BeamContact contact = ordered[i];
                if (contact.Distance > maxRange)
                    break; // Sorted, so everything after this is out of range too.

                if (contact.Target == null)
                    return new BeamResult(struck, contact.Distance, true);

                if (PassesThrough(contact.Target, shooterActorNumber, shooterTeamId))
                    continue;

                // A second collider of someone already struck: skipped, and it costs no pierce.
                if (!alreadyStruck.Add(contact.Target))
                    continue;

                struck.Add(contact);

                // A structure takes its hit, then stops the beam right there - it does not spend a
                // pierce and does not care what Max Targets says, the same way hitting a wall
                // (Target == null, above) is never subject to either. StoppedOnGeometry is false
                // here (not true, like the wall case) only to avoid Hitscan.Fire drawing a second,
                // redundant impact VFX on top of the one the Struck loop already plays for this hit.
                if (contact.IsStructure)
                    return new BeamResult(struck, contact.Distance, false);

                if (struck.Count >= cap)
                    return new BeamResult(struck, contact.Distance, false);
            }

            return new BeamResult(struck, Mathf.Max(0f, maxRange), false);
        }

        /// <summary>
        /// The shooter, a teammate, or a corpse: the beam carries on as if they were not there.
        /// Neither damaged nor a shield, and they do not use up a pierce.
        ///
        /// The self and teammate check is FriendlyFire.IsSelfOrTeammate, shared with
        /// ProjectileMotor.FliesThrough and ExplodeOnImpact.IsFriendly, so a laser cannot do what a
        /// bullet is forbidden to. The corpse check is BeamResolver's own addition on top of that
        /// shared rule: a dead body blocking shots was a fixed playtest bug, and a dead player is
        /// already off the hit layers, but a dead practice dummy keeps its collider while it waits
        /// to reset - a case the projectile sweep does not need to handle the same way, since a
        /// projectile that stops harmlessly on a freshly-dead dummy for a couple of seconds costs
        /// nothing, where a beam that cannot pierce past one would.
        /// </summary>
        private static bool PassesThrough(IDamageable target, int shooterActorNumber, int shooterTeamId)
        {
            if (!target.IsAlive)
                return true;

            return FriendlyFire.IsSelfOrTeammate(shooterActorNumber, target.ActorNumber,
                                                  shooterTeamId, target.TeamId);
        }
    }
}
