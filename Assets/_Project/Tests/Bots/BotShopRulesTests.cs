using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Bots;

namespace Overpower.Tests
{
    /// <summary>
    /// What a bot buys: in Conquest the most expensive item of its build it can afford each time it may shop; in Dominion what each
    /// round opens on the way to its build.
    /// </summary>
    public class BotShopRulesTests
    {
        private static readonly BuildPlan Burst = new BuildPlan
        {
            BaselineId = 1, FamilyId = 5, UpgradeId = 7, AttachmentId = 20, MovementId = 16, UltimateId = 27, Absorb = 1, Recharge = 1,
        };

        [Test]
        public void TheMissingStepsFollowTheTreeAndTheArmourCap()
        {
            var fresh = new BotLoadout { WeaponId = 1 };
            List<ShopStep> missing = BotShopRules.Missing(Burst, fresh, 2);
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Weapon, 5));
            CollectionAssert.DoesNotContain(missing, new ShopStep(ShopStepKind.Weapon, 7), "the upgrade needs the family first");
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Attachment, 20));
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Movement, 16));
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Ultimate, 27));
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Absorb, 0));
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Recharge, 0));

            var done = new BotLoadout { WeaponId = 7, AttachmentId = 20, MovementId = 16, UltimateId = 27, Absorb = 1, Recharge = 1 };
            CollectionAssert.IsEmpty(BotShopRules.Missing(Burst, done, 2));
        }

        [Test]
        public void TheFamilyLeadsToTheUpgrade()
        {
            List<ShopStep> missing = BotShopRules.Missing(Burst, new BotLoadout { WeaponId = 5 }, 2);
            CollectionAssert.Contains(missing, new ShopStep(ShopStepKind.Weapon, 7));
            CollectionAssert.DoesNotContain(missing, new ShopStep(ShopStepKind.Weapon, 5));
        }

        [Test]
        public void NoArmourStepPastTheShopsCap()
        {
            var plan = new BuildPlan { BaselineId = 1, FamilyId = 5, UpgradeId = 7, Absorb = 2, Recharge = 0 };
            CollectionAssert.DoesNotContain(BotShopRules.Missing(plan, new BotLoadout { WeaponId = 1, Absorb = 1 }, 1), new ShopStep(ShopStepKind.Absorb, 0));
        }

        [Test]
        public void TheMostExpensiveAffordableItemIsBought()
        {
            var offers = new List<ShopOffer>
            {
                new ShopOffer(new ShopStep(ShopStepKind.Weapon, 5), 960, 960),
                new ShopOffer(new ShopStep(ShopStepKind.Attachment, 20), 640, 0),
                new ShopOffer(new ShopStep(ShopStepKind.Ultimate, 27), 640, 640),
                new ShopOffer(new ShopStep(ShopStepKind.Absorb, 0), 1120, 1120),
            };
            Assert.AreEqual(0, BotShopRules.MostExpensiveAffordable(offers, 1000), "960 is the dearest it can pay");
            Assert.AreEqual(3, BotShopRules.MostExpensiveAffordable(offers, 2000));
            Assert.AreEqual(1, BotShopRules.MostExpensiveAffordable(offers, 0), "the free first attachment");
            Assert.AreEqual(-1, BotShopRules.MostExpensiveAffordable(new List<ShopOffer>(), 500));
        }

        [Test]
        public void EachDominionRoundOpensItsShareOfTheBuild()
        {
            BotLoadout one = BotShopRules.DominionTarget(Burst, 0, 0);
            Assert.AreEqual(1, one.WeaponId, "round 1: the pistol");
            Assert.AreEqual(20, one.AttachmentId);
            Assert.AreEqual(16, one.MovementId);
            Assert.AreEqual(27, one.UltimateId);
            Assert.AreEqual(0, one.Absorb + one.Recharge);

            BotLoadout two = BotShopRules.DominionTarget(Burst, 1, 1);
            Assert.AreEqual(5, two.WeaponId);
            Assert.AreEqual(1, two.Absorb, "absorb comes before recharge");
            Assert.AreEqual(0, two.Recharge);

            BotLoadout three = BotShopRules.DominionTarget(Burst, 2, 2);
            Assert.AreEqual(7, three.WeaponId);
            Assert.AreEqual(1, three.Absorb);
            Assert.AreEqual(1, three.Recharge);
        }
    }
}
