using System;
using UnityEngine;

namespace Overpower.Bots
{
    public enum RangeMove { Approach, Hold, BackOff }
    public enum BotStance { Fight, Retreat, Regroup }
    public enum ShopStepKind { Weapon, Attachment, Movement, Ultimate, Absorb, Recharge }

    /// <summary>One zone as a Conquest bot weighs it.</summary>
    public struct ConquestZone
    {
        public int Id;
        public Vector3 Centre;
        public int Tier;
        public bool Bounty;
        /// <summary>Its team may capture it right now (next to its territory, not under attack, in play).</summary>
        public bool MayCapture;
        public bool IsOwnCapital;
        public bool OwnCapitalThreatened;
        public bool IsEnemyCapital;
        /// <summary>For an enemy capital: how many of the bot's team (itself included) are near it.</summary>
        public int TeammatesNear;
        public bool IsCentre;
        /// <summary>A zone of its team (not the capital) that an enemy stands in or just left.</summary>
        public bool OursUnderAttack;
        /// <summary>Enemies its team saw near the zone lately (or the centre scan showed).</summary>
        public int EnemiesNear;
        /// <summary>For an enemy capital: pushed without enough of its team near it (BotPushRules: exposed, or straight on from a Tier II).</summary>
        public bool PushAnyway;
        /// <summary>A zone the bot is taking and finishes even while its own capital is being taken (BotPushRules.KeepTaking).</summary>
        public bool KeepTaking;
        /// <summary>Other bots of its team heading for this zone.</summary>
        public int TeammatesHeading;
    }

    /// <summary>One zone as a Dominion bot weighs it.</summary>
    public struct DominionZone
    {
        public int Id;
        public Vector3 Centre;
        public float PointsPerSecond;
        public bool Ours;
        public bool BeingTakenFromUs;
        public bool IsCentre;
        public bool Capturable;
        /// <summary>Enemies its team saw near the zone lately.</summary>
        public int EnemiesNear;
        /// <summary>Other bots of its team heading for this zone.</summary>
        public int TeammatesHeading;
    }

    /// <summary>A build as shop steps: catalogue ids and how many of each armour upgrade.</summary>
    public struct BuildPlan
    {
        public int BaselineId, FamilyId, UpgradeId, AttachmentId, MovementId, UltimateId, Absorb, Recharge;
    }

    /// <summary>What a body has equipped right now (0 = an empty ability slot).</summary>
    public struct BotLoadout
    {
        public int WeaponId, AttachmentId, MovementId, UltimateId, Absorb, Recharge;
    }

    public readonly struct ShopStep : IEquatable<ShopStep>
    {
        public readonly ShopStepKind Kind;
        public readonly int Id;
        public ShopStep(ShopStepKind kind, int id) { Kind = kind; Id = id; }
        public bool Equals(ShopStep other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object obj) => obj is ShopStep other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Id;
        public override string ToString() => Kind + " " + Id;
    }

    /// <summary>A step with its list price (what makes it "expensive") and what it costs this body now (0 when free).</summary>
    public readonly struct ShopOffer
    {
        public readonly ShopStep Step;
        public readonly int ListPrice;
        public readonly int Price;
        public ShopOffer(ShopStep step, int listPrice, int price) { Step = step; ListPrice = listPrice; Price = price; }
    }
}
