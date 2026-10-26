using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>When the floor's owner tiles are repainted: on a change of any zone's owner shown (a flip or what the team knows), spawn,
    /// out-of-play, live radius or centre, on a marked event, and on the first frame every zone is registered (a late joiner, a rejoin);
    /// never while nothing changed and never before every zone is there.</summary>
    public class OwnershipSignatureTests
    {
        private static ZoneState Zone(int owner, float x) => new ZoneState
        {
            OwnerShown = owner, Spawn = false, OutOfPlay = false, Radius = 8f, Centre = new Vector2(x, 2f), Registered = true,
        };

        private static ZoneState[] Painted() => new[] { Zone(-1, 0f), Zone(1, 20f), Zone(2, 40f) };

        [Test]
        public void OwnerKnowledgeRadiusOutOfPlaySpawnEachRepaint()
        {
            void Changes(string what, System.Func<ZoneState, ZoneState> change)
            {
                ZoneState[] painted = Painted();
                ZoneState[] now = Painted();
                now[1] = change(now[1]);
                Assert.IsTrue(OwnershipSignature.NeedsRepaint(painted, now, 3, false), what);
            }
            Changes("an owner flip", z => { z.OwnerShown = 0; return z; });
            Changes("the team learns the zone went neutral (what it knows changed)", z => { z.OwnerShown = -1; return z; });
            Changes("the radius shrinks with the cut", z => { z.Radius = 5.4f; return z; });
            Changes("the zone goes out of play", z => { z.OutOfPlay = true; return z; });
            Changes("the zone becomes a spawn", z => { z.Spawn = true; return z; });
            Changes("the tower moved", z => { z.Centre = new Vector2(21f, 2f); return z; });
            Assert.IsTrue(OwnershipSignature.NeedsRepaint(Painted(), Painted(), 3, true), "an ownership or live-state event marked it");
        }

        [Test]
        public void NothingChangedNoRepaint()
        {
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(Painted(), Painted(), 3, false));
            ZoneState[] now = Painted();
            now[2].OwnerShown = 0;
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(Painted(), now, 2, false), "only the first count zones are compared");
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(new ZoneState[0], new ZoneState[0], 0, true), "no zones, nothing to paint");
        }

        [Test]
        public void FirstFrameAfterRegistrationRepaints()
        {
            var painted = new ZoneState[3]; // nothing painted yet
            ZoneState[] now = Painted();
            now[2].Registered = false; // the last tower's Start has not run
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(painted, now, 3, true), "not before every zone is there, even when marked");
            now[2].Registered = true;
            Assert.IsTrue(OwnershipSignature.NeedsRepaint(painted, now, 3, false), "the first frame with every zone registered paints");
            System.Array.Copy(now, painted, 3);
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(painted, now, 3, false), "then not again");

            var neutralAtOrigin = new[] { new ZoneState { OwnerShown = 0, Registered = true } };
            Assert.IsTrue(OwnershipSignature.NeedsRepaint(new ZoneState[1], neutralAtOrigin, 1, false),
                "a zone whose values equal the defaults still paints once: unpainted is never mistaken for painted");
        }

        [Test]
        public void ForgetMakesTheNextRoomPaintOnItsFirstFrame()
        {
            ZoneState[] painted = Painted();
            ZoneState[] now = Painted();
            Assert.IsFalse(OwnershipSignature.NeedsRepaint(painted, now, 3, false), "the control: the same state paints nothing");
            OwnershipSignature.Forget(painted);
            Assert.IsTrue(OwnershipSignature.NeedsRepaint(painted, now, 3, false),
                "after leaving a room the next room paints on its first frame, even when its zones equal the last painted ones");
        }

        [Test]
        public void OwnerTilesShowFromTheWarmUpOn()
        {
            Assert.IsFalse(OwnershipSignature.ShowsOwnerTiles(false, true, 2), "no territory state: plain");
            Assert.IsFalse(OwnershipSignature.ShowsOwnerTiles(true, false, 0), "the room's lobby before the start: plain");
            Assert.IsTrue(OwnershipSignature.ShowsOwnerTiles(true, false, 1), "the warm-up: the towers show their owners, so does the floor");
            Assert.IsTrue(OwnershipSignature.ShowsOwnerTiles(true, true, 2), "the match");
            Assert.IsTrue(OwnershipSignature.ShowsOwnerTiles(true, true, 0), "a live room that never had a lobby stage");
        }
    }
}
