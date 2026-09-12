using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    public enum StatusKind { Burn, Slow, Stun, Vulnerability, Invulnerability }

    public enum StackRule { Refresh, StackToCap, LongestWins }

    [System.Serializable]
    public struct StatusEffectSpec
    {
        public StatusKind kind;
        public float duration;
        public float magnitude;   // Slow: 0..1 speed loss. Vulnerability: 0..1 extra damage.
                                  // Burn: damage per second. Stun/Invulnerability: ignored.
    }

    /// <summary>
    /// Tracks every timed status one actor is carrying and enforces each kind's stacking rule.
    /// Plain C# on purpose, same reason as DamageResolver: it is unit tested without touching
    /// the Unity engine. A MonoBehaviour wrapper calls Tick from Update in a later task.
    ///
    /// Six equipment abilities do nothing but apply one of these kinds for a duration. Without
    /// this shared machinery they would each become their own copy of a timer, the same
    /// divergence problem the damage funnel was built to avoid.
    ///
    /// Stacking rule per kind (fixed, see the design table):
    ///   Burn            - Refresh: a fresh application replaces the remaining duration outright.
    ///   Slow            - StackToCap: magnitudes add, clamped to slowCap.
    ///   Stun            - LongestWins: only the longest remaining duration survives.
    ///   Vulnerability   - StackToCap: magnitudes add, clamped to vulnerabilityCap.
    ///   Invulnerability - LongestWins: only the longest remaining duration survives.
    /// </summary>
    public sealed class StatusEffectState
    {
        private struct Instance
        {
            public float magnitude;
            public float remaining;
        }

        private readonly float slowCap;
        private readonly float vulnerabilityCap;
        private readonly Dictionary<StatusKind, List<Instance>> stacksByKind =
            new Dictionary<StatusKind, List<Instance>>();

        public StatusEffectState(float slowCap, float vulnerabilityCap)
        {
            this.slowCap = slowCap;
            this.vulnerabilityCap = vulnerabilityCap;
        }

        public void Apply(in StatusEffectSpec spec)
        {
            var instance = new Instance { magnitude = spec.magnitude, remaining = spec.duration };

            switch (RuleFor(spec.kind))
            {
                case StackRule.Refresh:
                    // One stack only: a fresh application throws away whatever was left of the
                    // old one rather than adding to it.
                    stacksByKind[spec.kind] = new List<Instance> { instance };
                    break;

                case StackRule.StackToCap:
                    // Each application keeps its own duration so the total magnitude drops as
                    // individual stacks expire, but the reported total is capped.
                    if (!stacksByKind.TryGetValue(spec.kind, out var stacks))
                    {
                        stacks = new List<Instance>();
                        stacksByKind[spec.kind] = stacks;
                    }
                    stacks.Add(instance);
                    break;

                case StackRule.LongestWins:
                    // Only ever one stack. A shorter application landing on top of a longer one
                    // is simply discarded.
                    if (Remaining(spec.kind) < spec.duration)
                        stacksByKind[spec.kind] = new List<Instance> { instance };
                    break;
            }
        }

        public void Tick(float deltaTime)
        {
            foreach (var stacks in stacksByKind.Values)
            {
                for (int i = stacks.Count - 1; i >= 0; i--)
                {
                    var instance = stacks[i];
                    instance.remaining -= deltaTime;

                    if (instance.remaining <= 0f)
                        stacks.RemoveAt(i);
                    else
                        stacks[i] = instance;
                }
            }
        }

        public void ClearAll()
        {
            stacksByKind.Clear();
        }

        public bool IsActive(StatusKind kind)
        {
            return stacksByKind.TryGetValue(kind, out var stacks) && stacks.Count > 0;
        }

        public float Magnitude(StatusKind kind)
        {
            if (!stacksByKind.TryGetValue(kind, out var stacks) || stacks.Count == 0)
                return 0f;

            float sum = 0f;
            foreach (var instance in stacks)
                sum += instance.magnitude;

            if (kind == StatusKind.Slow)
                return Mathf.Min(sum, slowCap);
            if (kind == StatusKind.Vulnerability)
                return Mathf.Min(sum, vulnerabilityCap);

            return sum;
        }

        public float Remaining(StatusKind kind)
        {
            if (!stacksByKind.TryGetValue(kind, out var stacks) || stacks.Count == 0)
                return 0f;

            // The status stays active until every stack has expired, so Remaining is however
            // long the longest-lived stack has left.
            float longest = 0f;
            foreach (var instance in stacks)
                longest = Mathf.Max(longest, instance.remaining);

            return longest;
        }

        public int StackCount(StatusKind kind)
        {
            return stacksByKind.TryGetValue(kind, out var stacks) ? stacks.Count : 0;
        }

        /// <summary>
        /// Returns the burn damage owed for a step of length deltaTime without applying it -
        /// the caller routes the result through DamageResolver so burn stays on the one damage
        /// path. Call this before Tick(deltaTime) in the same step: it reports damage for the
        /// burn as it stood at the start of the step, then Tick ages the timer (and every other
        /// status) by the same amount. Clamping to the time actually remaining means a burn that
        /// expires mid-step still pays out exactly what it owes, no more and no less.
        /// </summary>
        public float ConsumeBurnDamage(float deltaTime)
        {
            if (!IsActive(StatusKind.Burn))
                return 0f;

            float activeTime = Mathf.Min(deltaTime, Remaining(StatusKind.Burn));
            return Magnitude(StatusKind.Burn) * activeTime;
        }

        private static StackRule RuleFor(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Burn: return StackRule.Refresh;
                case StatusKind.Slow: return StackRule.StackToCap;
                case StatusKind.Stun: return StackRule.LongestWins;
                case StatusKind.Vulnerability: return StackRule.StackToCap;
                case StatusKind.Invulnerability: return StackRule.LongestWins;
                default: return StackRule.Refresh;
            }
        }
    }
}
