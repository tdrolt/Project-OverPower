using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Overpower.Vision;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion on a host that also plays bot seats: presence never reads a bot as dropped, every body the client simulates takes the match-wide edges and
    /// the circle's cost, the respawn shield writes through its own body's player, and an effect of a seat the client plays reaches that seat's shield.
    /// Rules are pure; the wiring tests read the components' method bodies (IlWiring), so putting the old "the local player" shortcut back fails them.
    /// </summary>
    public class DominionBotSeatsTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo Method(System.Type type, string name) => type.GetMethod(name, Any);

        private static MethodInfo LocalPlayerGetter() => typeof(PhotonNetwork).GetProperty(nameof(PhotonNetwork.LocalPlayer)).GetGetMethod();

        // ---------------------------------------------------------------- the clock the bot brain reads

        [Test] public void SecondsUntilCountsFromNowToTheTarget() =>
            Assert.AreEqual(12.5f, DominionRules.SecondsUntil(targetMs: 100000, nowMs: 87500), 0.0001f);

        [Test] public void SecondsUntilIsZeroWhenNothingIsWritten() =>
            Assert.AreEqual(0f, DominionRules.SecondsUntil(targetMs: 0, nowMs: 87500), "dEnd 0 is sudden death or no stage: there is no end to wait for");

        [Test] public void SecondsUntilIsZeroBeforeTheServerClockHasSynced() =>
            Assert.AreEqual(0f, DominionRules.SecondsUntil(targetMs: 100000, nowMs: 0));

        [Test] public void SecondsUntilIsZeroOncePassedAndAtTheInstant()
        {
            Assert.AreEqual(0f, DominionRules.SecondsUntil(100000, 100000));
            Assert.AreEqual(0f, DominionRules.SecondsUntil(100000, 100500));
        }

        [Test] public void SecondsUntilSurvivesTheServerClockWrapping()
        {
            int now = int.MaxValue - 1000;
            int target = unchecked(now + 3000); // past the wrap, a negative number
            Assert.AreEqual(3f, DominionRules.SecondsUntil(target, now), 0.0001f);
            Assert.AreEqual(0f, DominionRules.SecondsUntil(now, target), "and a target that is behind stays behind");
        }

        [Test] public void TheDirectorsClockAnswersAreTheTestedRuleOverTheRoomsValues()
        {
            MethodInfo seconds = Method(typeof(DominionRules), nameof(DominionRules.SecondsUntil));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "get_SecondsLeftInStage", seconds));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "get_SecondsToCentrePayout", seconds));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(DominionDirector), "get_RoomState", typeof(Time).GetProperty(nameof(Time.frameCount)).GetGetMethod()),
                "the room is read once per frame, not once per question");
        }

        [Test] public void TheCentreZoneComesFromTheSameAnswerTheMastersPointsBeatUses()
        {
            MethodInfo inPlay = Method(typeof(DominionDirector), "CentreInPlay");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "get_CentreZone", inPlay));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "TryGetCentreZone", inPlay));
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "RunPoints", inPlay));
        }

        // ---------------------------------------------------------------- presence

        [Test] public void TheDirectorNeverCountsABotAsDropped()
        {
            MethodInfo isDropped = Method(typeof(Presence), nameof(Presence.IsDropped));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "TrackDroppedPlayers", isDropped), "the dropped-since stamps");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "StillCounts", isDropped), "the per-team count and the sudden-death tally");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "StillCounts", Method(typeof(DominionRules), nameof(DominionRules.CountsAsPresent))), "through the tested rule");
        }

        // ---------------------------------------------------------------- every body this client simulates takes the edges

        [Test] public void TheBreakAndRoundEdgesResetEveryControlledBodyNotJustTheLocalPlayer()
        {
            System.Type director = typeof(DominionDirector);
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodies", Method(typeof(BodyRole), nameof(BodyRole.ControlledBodies))));
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodies", Method(typeof(PlayerLifecycle), nameof(PlayerLifecycle.ResetForMatchStart))));
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodies", Method(typeof(PlayerLifecycle), nameof(PlayerLifecycle.ResetForRoundStart))));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(director, "ResetControlledBodies", LocalPlayerGetter()), "no body is picked by being the local player");
            Assert.IsTrue(IlWiring.Uses(director, "OnRoomPropertiesUpdate", Method(director, "ResetControlledBodies")));
        }

        [Test] public void SuddenDeathStartsEveryControlledBodyInItsTeamsState()
        {
            System.Type director = typeof(DominionDirector);
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodiesForSuddenDeath", Method(typeof(BodyRole), nameof(BodyRole.ControlledBodies))));
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodiesForSuddenDeath", Method(typeof(PlayerLifecycle), nameof(PlayerLifecycle.ResetForSuddenDeath))));
            Assert.IsTrue(IlWiring.Uses(director, "ResetControlledBodiesForSuddenDeath", Method(typeof(SuddenDeathRules), nameof(SuddenDeathRules.TeamPlays))));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(director, "ResetControlledBodiesForSuddenDeath", LocalPlayerGetter()));
            Assert.IsTrue(IlWiring.Uses(director, "OnRoomPropertiesUpdate", Method(director, "ResetControlledBodiesForSuddenDeath")));
        }

        [Test] public void OnlyTheHumansBodyForgetsWhatTheTeamKnewOfTheZones() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DominionDirector), "ResetControlledBodies", Method(typeof(BodyRole), nameof(BodyRole.IsLocalHuman))),
                "ZoneKnowledge is the screen of the person at this keyboard: a bot's reset does not wipe it");

        [Test] public void TheZoneKnowledgeResetIsStillOnBothEdges() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "ResetControlledBodies", Method(typeof(ZoneKnowledge), nameof(ZoneKnowledge.ResetKnowledge))));

        // ---------------------------------------------------------------- the circle's cost, one account per body

        [Test] public void NothingIsDueUntilAWholeTickHasPassed()
        {
            var meter = new OutsideDamageMeter();
            Assert.AreEqual(0f, meter.Add(2f, 0.2f, 0.5f));
            Assert.AreEqual(0f, meter.Add(2f, 0.2f, 0.5f));
        }

        [Test] public void TheTickHandsOverEverythingOwedAndStartsAgain()
        {
            var meter = new OutsideDamageMeter();
            meter.Add(2f, 0.2f, 0.5f);
            meter.Add(2f, 0.2f, 0.5f);
            Assert.AreEqual(6f, meter.Add(2f, 0.2f, 0.5f), 0.0001f, "three frames of 2 owed, handed over when 0.6 s have passed");
            Assert.AreEqual(0f, meter.Add(2f, 0.2f, 0.5f), "the account and the clock start over");
        }

        [Test] public void TheRateIsExactAtAnyFrameRate()
        {
            var slow = new OutsideDamageMeter();
            var fast = new OutsideDamageMeter();
            float perSecond = 10f, slowTotal = 0f, fastTotal = 0f; // frame lengths that are exact in binary, so the clock lands on the tick
            for (int i = 0; i < 2; i++) slowTotal += slow.Add(perSecond * 0.5f, 0.5f, 0.5f);
            for (int i = 0; i < 64; i++) fastTotal += fast.Add(perSecond * 0.015625f, 0.015625f, 0.5f);
            Assert.AreEqual(10f, slowTotal, 0.0001f, "two frames of half a second");
            Assert.AreEqual(10f, fastTotal, 0.0001f, "sixty-four frames of 1/64 s");
        }

        [Test] public void AMeterThatOwesNothingHandsOverNothing()
        {
            var meter = new OutsideDamageMeter();
            Assert.AreEqual(0f, meter.Add(0f, 0.6f, 0.5f), "inside the circle: the tick passes and nothing is due");
        }

        [Test] public void ResetForgetsWhatWasOwedAndTheClock()
        {
            var meter = new OutsideDamageMeter();
            meter.Add(3f, 0.4f, 0.5f);
            meter.Reset();
            Assert.AreEqual(0f, meter.Add(0f, 0.1f, 0.5f), "the 3 owed are forgotten");
            Assert.AreEqual(2f, meter.Add(2f, 0.4f, 0.5f), 0.0001f, "and the 0.4 s already counted are forgotten too: the tick comes 0.5 s after the reset");
        }

        [Test] public void TwoBodiesNeverShareAnAccount()
        {
            var human = new OutsideDamageMeter();
            var bot = new OutsideDamageMeter();
            human.Add(4f, 0.3f, 0.5f);
            Assert.AreEqual(0f, bot.Add(0f, 0.3f, 0.5f), "the bot is inside: owes nothing");
            Assert.AreEqual(4f, human.Add(0f, 0.3f, 0.5f), 0.0001f, "the human, outside, is charged on its own tick");
            Assert.AreEqual(0f, bot.Add(0f, 0.3f, 0.5f), "and the bot is still owed nothing");
        }

        [Test] public void TheCircleChargesEveryControlledBodyOnItsOwnMeter()
        {
            System.Type zone = typeof(SuddenDeathZone);
            Assert.IsTrue(IlWiring.Uses(zone, "ApplyOutsideDamage", Method(typeof(BodyRole), nameof(BodyRole.ControlledBodies))));
            Assert.IsTrue(IlWiring.Uses(zone, "ApplyOutsideDamage", Method(typeof(OutsideDamageMeter), nameof(OutsideDamageMeter.Add))));
            Assert.IsTrue(IlWiring.Uses(zone, "ApplyOutsideDamage", Method(typeof(SuddenDeathRules), nameof(SuddenDeathRules.DamageAt))));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(zone, "ApplyOutsideDamage", LocalPlayerGetter()), "the cost is not the local player's alone");
        }

        // ---------------------------------------------------------------- the spawn's centre

        private static SpawnHealArea MakeArea(int team, Vector3 at)
        {
            var go = new GameObject("test spawn area");
            go.transform.position = at;
            var area = go.AddComponent<SpawnHealArea>();
            area.Configure(team, SpawnHealArea.AreaShape.Circle, Vector2.one, 5f);
            SpawnHealArea.Register(area); // edit-mode tests do not run OnEnable
            return area;
        }

        private static void Remove(SpawnHealArea area)
        {
            SpawnHealArea.Unregister(area);
            Object.DestroyImmediate(area.gameObject);
        }

        [Test] public void ATeamsSpawnCentreIsWhereItsAreaIs()
        {
            SpawnHealArea area = MakeArea(1, new Vector3(4f, 2f, -6f));
            try
            {
                Assert.IsTrue(SpawnHealArea.TryGetCentre(1, out Vector3 centre));
                Assert.AreEqual(new Vector3(4f, 2f, -6f), centre);
            }
            finally { Remove(area); }
        }

        [Test] public void AnotherTeamsAreaIsNotThisTeamsSpawn()
        {
            SpawnHealArea area = MakeArea(1, new Vector3(4f, 2f, -6f));
            try
            {
                Assert.IsFalse(SpawnHealArea.TryGetCentre(0, out Vector3 centre));
                Assert.AreEqual(Vector3.zero, centre);
            }
            finally { Remove(area); }
        }

        [Test] public void NoTeamAtAllHasNoSpawnCentre() =>
            Assert.IsFalse(SpawnHealArea.TryGetCentre(-1, out _));

        [Test] public void WithoutAnAreaTheCentreFallsBackToTheCapitalTowerOfAThreeTeamMatch()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(SpawnHealArea), "TryGetCentre", Method(typeof(TerritoryMap), nameof(TerritoryMap.CapitalOf))));
            Assert.IsTrue(IlWiring.Uses(typeof(SpawnHealArea), "TryGetCentre", Method(typeof(BuildingManager), nameof(BuildingManager.TryGetZoneCentre))));
        }

        // ---------------------------------------------------------------- the respawn shield belongs to its body

        [Test] public void TheShieldsWritesGoThroughTheBodysOwnPlayer()
        {
            System.Type shield = typeof(RespawnShield);
            MethodInfo set = Method(typeof(PlayerProps), nameof(PlayerProps.Set));
            Assert.IsTrue(IlWiring.Uses(shield, "StartShield", set), "dShs and dShd");
            Assert.IsTrue(IlWiring.Uses(shield, "ClearShield", set), "the clear");
            Assert.IsTrue(IlWiring.Uses(shield, "WriteBlockedStamp", set), "dBlk");
            foreach (string method in new[] { "StartShield", "ClearShield", "WriteBlockedStamp" })
                Assert.IsFalse(IlWiring.CallsAcrossAssemblies(shield, method, LocalPlayerGetter()), method + " must not name the local player: a bot's shield is not the host human's");
        }

        [Test] public void NoShieldWriteGoesStraightToPhoton()
        {
            foreach (MethodInfo direct in typeof(Photon.Realtime.Player).GetMethods().Where(m => m.Name == "SetCustomProperties"))
                Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(RespawnShield), direct), "a bot's properties are not on the server");
        }

        [Test] public void TheBlockedStampWriterIsPerBodyNotShared()
        {
            MethodInfo writer = Method(typeof(RespawnShield), "WriteBlockedStamp");
            Assert.IsFalse(writer.IsStatic, "it needs its own body's owner");
            Assert.IsTrue(IlWiring.HandsOver(typeof(RespawnShield), "Awake", writer), "the judge is built over this body's writer");
        }

        [Test] public void TheShieldListensToItsOwnActorsFeedAndLeavesTheSameOne()
        {
            System.Type shield = typeof(RespawnShield);
            MethodInfo feedOf = Method(typeof(CombatFeed), nameof(CombatFeed.For));
            Assert.IsTrue(IlWiring.Uses(shield, "OnEnable", feedOf));
            Assert.IsFalse(IlWiring.Uses(shield, "OnDisable", feedOf), "leaving uses the feed it joined, not whatever the owner is by then");
            FieldInfo kept = shield.GetField("feed", Any);
            Assert.IsNotNull(kept);
            Assert.IsTrue(IlWiring.Stores(shield, "OnEnable", kept));
            Assert.IsTrue(IlWiring.Uses(shield, "OnDisable", kept));
            Assert.IsTrue(IlWiring.HandsOver(shield, "OnEnable", Method(shield, "HandleEnemyAffected")));
        }

        [Test] public void AnEffectIsReportedToTheShieldOfTheSeatThatDidItThroughItsOwnFeed()
        {
            System.Type shield = typeof(RespawnShield);
            Assert.IsTrue(IlWiring.Uses(shield, "NoteMyEffectOnCopy", Method(typeof(CombatFeed), nameof(CombatFeed.For))));
            Assert.IsTrue(IlWiring.Uses(shield, "NoteMyEffectOnCopy", Method(typeof(CombatFeed), nameof(CombatFeed.RaiseEnemyAffected))));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(shield, "NoteMyEffectOnCopy", LocalPlayerGetter()), "the attacker is the source's seat, not the local player");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(shield, "NoteMyEffectOnCopy", Method(shield, "IsSimulatedHere")), "a seat another client plays reports nothing here");
            Assert.IsTrue(IlWiring.Uses(shield, "IsSimulatedHere", Method(typeof(BodyRole), nameof(BodyRole.IsBot))));
        }

        [Test] public void AnEffectOfASeatThisClientPlaysOnAnotherSeatsBodyIsReported() =>
            Assert.IsTrue(RespawnShieldRules.EffectReport(victimIsSource: false, sourceSimulatedHere: true, victimAlive: true, sameTeam: false,
                                                          victimShielded: false, effectPlacedMs: 0, myShieldStartMs: 1000).Report,
                "a bot stunning the host's human, or another bot, ends the bot's own shield");

        [Test] public void AnEffectOnTheSourcesOwnBodyIsNeverReported() =>
            Assert.IsFalse(RespawnShieldRules.EffectReport(victimIsSource: true, sourceSimulatedHere: true, victimAlive: true, sameTeam: false,
                                                           victimShielded: false, effectPlacedMs: 0, myShieldStartMs: 1000).Report);

        [Test] public void AnEffectOfASeatAnotherClientPlaysIsNotReportedHere() =>
            Assert.IsFalse(RespawnShieldRules.EffectReport(victimIsSource: false, sourceSimulatedHere: false, victimAlive: true, sameTeam: false,
                                                           victimShielded: false, effectPlacedMs: 0, myShieldStartMs: 1000).Report);
    }
}
