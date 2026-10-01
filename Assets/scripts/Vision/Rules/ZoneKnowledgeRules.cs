using System;
using System.Collections.Generic;

namespace Overpower.Vision
{
    /// <summary>What a team knows about one zone, as plain values: who owns it, who is capturing it, how far the
    /// capture has got, whether it is under attack, and when it was captured.
    /// Progress01 is a plain number evaluated by the caller at the moment it builds the live list, not the stamped
    /// rate the room properties carry. A stamped rate keeps advancing on its own clock, so a copy of it kept for an
    /// unseen zone would keep filling; a plain number stays exactly what the team last saw.</summary>
    public readonly struct ZoneView
    {
        public readonly int OwnerTeam;
        public readonly int CapturingTeam;
        public readonly float Progress01;
        public readonly bool UnderAttack;
        public readonly int HeldSinceMs;

        public ZoneView(int ownerTeam, int capturingTeam, float progress01, bool underAttack, int heldSinceMs)
        {
            OwnerTeam = ownerTeam;
            CapturingTeam = capturingTeam;
            Progress01 = progress01;
            UnderAttack = underAttack;
            HeldSinceMs = heldSinceMs;
        }

        public static readonly ZoneView Neutral = new ZoneView(-1, -1, 0f, false, 0);
    }

    /// <summary>
    /// Zones in the fog (Tudor 2026-09-30: "if it is off you only update it if you have vision / own that territory /
    /// own the t4 territory and it pings revealing the information"). With the switch off, what my team knows about a
    /// zone updates only while my team sees it, owns it, or the centre scan is passing over it; otherwise it stays on
    /// what the team last knew.
    ///
    /// Starting state: the first Update copies the live state for every zone, switch or no switch. That is the live
    /// state at the moment the local game first reads it - the capitals owned and the rest neutral at a normal
    /// go-live, and simply whatever is true right then for someone joining late. No separate "start" data is kept.
    ///
    /// Index in the lists is the zone id.
    /// </summary>
    public sealed class ZoneKnowledge
    {
        private ZoneView[] known = new ZoneView[0];
        private bool started;

        /// <param name="live">The true state of every zone this frame, by zone id, with progress already evaluated at now.</param>
        /// <param name="scannedNow">Zones the centre scan wave is passing over right now. Each frame in it the zone takes the
        /// live state, so it ends on the state at the moment the wave leaves, and then freezes again.</param>
        public void Update(bool switchOn, int myTeam, IReadOnlyList<ZoneView> live, Func<int, bool> zoneSeen, IReadOnlyCollection<int> scannedNow)
        {
            if (!started || known.Length != live.Count)
            {
                // First read (or the zone count changed): the team starts out knowing the live state.
                known = new ZoneView[live.Count];
                for (int i = 0; i < known.Length; i++)
                    known[i] = live[i];
                started = true;
                return;
            }

            for (int i = 0; i < known.Length; i++)
            {
                // A zone my team owns is live (Tudor: "I can see if it gets attacked"). A zone my team knew as its own and
                // has now lost is live for that frame too: the team's income stops, so it knows. The frame after, the loss
                // is what it last knew and the zone freezes on that like any other unseen zone.
                bool mine = live[i].OwnerTeam == myTeam || known[i].OwnerTeam == myTeam;
                if (switchOn || mine || zoneSeen(i) || Contains(scannedNow, i))
                    known[i] = live[i];
            }
        }

        /// <summary>What my team knows about the zone right now. Neutral for an unknown zone id.</summary>
        public ZoneView Displayed(int zoneId) => zoneId >= 0 && zoneId < known.Length ? known[zoneId] : ZoneView.Neutral;

        private static bool Contains(IReadOnlyCollection<int> set, int zone)
        {
            if (set == null)
                return false;
            foreach (int z in set)
                if (z == zone)
                    return true;
            return false;
        }
    }
}
