using NUnit.Framework;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class SlotTintRuleTests
    {
        private static readonly Color Active = new Color(1f, 0.85f, 0.25f, 1f);
        private static readonly Color Blocked = new Color(0.45f, 0.45f, 0.45f, 0.95f);
        private static readonly Color Ready = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        [Test]
        public void ActiveWinsOverNotReady()
        {
            // The invulnerability rework follow-up: an armed/shielded ultimate reads NotReady (its meter
            // was just spent by the cast) while IsActive is exactly the thing the amber glow exists to show.
            Assert.AreEqual(Active, SlotTintRule.BorderColor(active: true, CastBlock.NotReady, Active, Blocked, Ready));
        }

        [Test]
        public void ActiveWinsOverStunnedToo()
        {
            // Flamethrower/Dash/ZipGun/Invulnerability all keep IsActive true through a Stunned or Silenced
            // interrupt by design (see each module's own Interrupt comment) - active must win there too,
            // not only over NotReady.
            Assert.AreEqual(Active, SlotTintRule.BorderColor(active: true, CastBlock.Stunned, Active, Blocked, Ready));
        }

        [Test]
        public void ActiveWinsOverSilencedToo()
        {
            Assert.AreEqual(Active, SlotTintRule.BorderColor(active: true, CastBlock.Silenced, Active, Blocked, Ready));
        }

        [Test]
        public void BlockedShowsWhenNotActive()
        {
            Assert.AreEqual(Blocked, SlotTintRule.BorderColor(active: false, CastBlock.Silenced, Active, Blocked, Ready));
        }

        [Test]
        public void ReadyShowsWhenNeitherActiveNorBlocked()
        {
            Assert.AreEqual(Ready, SlotTintRule.BorderColor(active: false, CastBlock.None, Active, Blocked, Ready));
        }

        [Test]
        public void BlockReasonHiddenWhileActive()
        {
            // "not ready" under a glowing, running ultimate is noise - the meter's own fill already shows it.
            Assert.IsFalse(SlotTintRule.ShowsBlockReason(active: true, CastBlock.NotReady));
        }

        [Test]
        public void BlockReasonShownWhenBlockedAndNotActive()
        {
            Assert.IsTrue(SlotTintRule.ShowsBlockReason(active: false, CastBlock.Recharging));
        }

        [Test]
        public void NoBlockReasonWhenClear()
        {
            Assert.IsFalse(SlotTintRule.ShowsBlockReason(active: false, CastBlock.None));
        }
    }
}
