using Overpower.Match;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>One zone flip's ring of brightened floor tiles, running out from the tower.</summary>
    public struct Ripple
    {
        public Vector2 Centre;
        /// <summary>The zone's live radius plus the painted band: the ring stops at the edge of the zone's own tiles (a neighbouring
        /// zone's owned tiles within that distance of this tower light too; the shader has no zone per tile).</summary>
        public float Reach;
        /// <summary>The colour the floor now shows for the zone.</summary>
        public Color Colour;
        /// <summary>Time.time it began.</summary>
        public float Start;
        public bool Running;
    }

    /// <summary>The Arena Look's ripple numbers, read in one place so the painter hands each one to the right use.</summary>
    public struct RippleNumbers
    {
        /// <summary>How far past a zone's live radius its paint reaches, in metres.</summary>
        public float Band;
        public float Seconds;
        public float Glow;
        public float RingWidth;
    }

    /// <summary>When the owner this client's floor shows for a zone flips to a team, a ring of brightened tiles runs from its tower to the
    /// edge of its band in the new colour, then the floor rests as painted. Only a flip between two paints of the same room starts one
    /// (never a first paint, a late join or the room's lobby, whose painted state is forgotten), only to a team (neutral ground is unlit,
    /// so it never reads as owned; this also keeps a zone reset quiet) and only where the floor shows the owner (not out
    /// of play, not a spawn area). The owner is ZoneOwnerShown's, so a flip this team cannot know about stays hidden. A few run at once
    /// in a fixed set of shader slots.</summary>
    public static class RippleRule
    {
        public const int Slots = 4;

        public static RippleNumbers NumbersOf(ArenaLook look) => new RippleNumbers
        {
            Band = FloorTileRule.BandMetres(look),
            Seconds = look.rippleSeconds,
            Glow = look.rippleGlow,
            RingWidth = look.rippleRingWidth,
        };

        public static bool Starts(ZoneState painted, ZoneState now) =>
            painted.Registered && now.Registered && painted.OwnerShown != now.OwnerShown && now.OwnerShown >= 0 && !now.OutOfPlay && !now.Spawn;

        /// <summary>Exactly the colour the floor's tiles take for that owner (FloorTileRule's paint through OwnerPaintColours).</summary>
        public static Color ColourOf(int ownerShown, UiTheme theme) =>
            OwnerPaintColours.For(FloorTileRule.FloorPaint(ownerShown, false, TerritoryMap.Neutral, false), theme, theme.arenaNeutralLineColor, 0f);

        /// <summary>The ring's distance from the tower: out fast, easing into the band's edge at the end.</summary>
        public static float Radius(float elapsed, float seconds, float reach)
        {
            if (seconds <= 0f)
                return reach;
            float p = Mathf.Clamp01(elapsed / seconds);
            return reach * (1f - (1f - p) * (1f - p));
        }

        /// <summary>1 as it starts, fading to 0 as the ring reaches the edge; 0 outside its time.</summary>
        public static float Strength(float elapsed, float seconds)
        {
            if (seconds <= 0f || elapsed < 0f || elapsed >= seconds)
                return 0f;
            float p = elapsed / seconds;
            return 1f - p * p;
        }

        private static bool IsRunning(Ripple r, float time, float seconds) => r.Running && time - r.Start < seconds;

        /// <summary>The first slot with no ripple running, else the one that began first.</summary>
        public static int Slot(Ripple[] slots, float time, float seconds)
        {
            int oldest = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!IsRunning(slots[i], time, seconds))
                    return i;
                if (slots[i].Start < slots[oldest].Start)
                    oldest = i;
            }
            return oldest;
        }

        /// <summary>Starts a ripple for every zone whose shown owner flipped between the painted state and now; returns how many.
        /// Call it before the painted state is overwritten.</summary>
        public static int StartFlips(ZoneState[] painted, ZoneState[] now, int count, RippleNumbers numbers, UiTheme theme, Ripple[] slots, float time)
        {
            int started = 0;
            for (int i = 0; i < count; i++)
            {
                if (!Starts(painted[i], now[i]))
                    continue;
                slots[Slot(slots, time, numbers.Seconds)] = new Ripple
                {
                    Centre = now[i].Centre,
                    Reach = now[i].Radius + numbers.Band,
                    Colour = ColourOf(now[i].OwnerShown, theme),
                    Start = time,
                    Running = true,
                };
                started++;
            }
            return started;
        }

        /// <summary>The shader's view of every slot: shape = (centre x, centre z, ring radius, strength x glow), colour = (linear rgb,
        /// reach). A finished ripple frees its slot and hands over zero strength. True while any still runs.</summary>
        public static bool Pack(Ripple[] slots, float time, RippleNumbers numbers, Vector4[] shape, Vector4[] colour)
        {
            float seconds = numbers.Seconds, glow = numbers.Glow;
            bool any = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Ripple r = slots[i];
                float elapsed = time - r.Start;
                if (!IsRunning(r, time, seconds))
                {
                    slots[i].Running = false;
                    shape[i] = Vector4.zero;
                    colour[i] = Vector4.zero;
                    continue;
                }
                any = true;
                Color linear = r.Colour.linear;
                shape[i] = new Vector4(r.Centre.x, r.Centre.y, Radius(elapsed, seconds, r.Reach), Strength(elapsed, seconds) * glow);
                colour[i] = new Vector4(linear.r, linear.g, linear.b, r.Reach);
            }
            return any;
        }

        public static void StopAll(Ripple[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i].Running = false;
        }
    }
}
