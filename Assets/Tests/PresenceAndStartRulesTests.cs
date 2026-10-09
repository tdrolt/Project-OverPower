using System.Reflection;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Lobby;

namespace Overpower.Tests
{
    /// <summary>Three decisions that lived inside components and decided who wins, who gets a body and whether a Start lands: a dropped
    /// player counts as present until the dropped grace runs out; a rejoiner with a team seat and a team is looked after by the rejoin path, every
    /// other seat reacts to the start; a refused Start is resent on a seat change or after the resend wait and given up after the give-up time.
    /// Literal seconds, never the config's.</summary>
    public class PresenceAndStartRulesTests
    {
        // ---------------------------------------------------------------- a dropped player counts for the grace (the A3 last-team rule, A56)

        [Test] public void APlayerStillConnectedAlwaysCounts()
        {
            Assert.IsTrue(DominionRules.CountsAsPresent(isInactive: false, hasInactiveSince: false, inactiveSinceSeconds: 0f, nowSeconds: 100f, graceSeconds: 10f));
            Assert.IsTrue(DominionRules.CountsAsPresent(false, true, 50f, 100f, 10f), "a stale 'since' from an earlier drop means nothing while connected");
        }

        [Test] public void ADroppedPlayerCountsUntilTheGraceHasRunOut()
        {
            Assert.IsTrue(DominionRules.CountsAsPresent(true, true, 50f, 59.9f, 10f));
            Assert.IsFalse(DominionRules.CountsAsPresent(true, true, 50f, 60f, 10f), "exactly the grace: gone");
            Assert.IsFalse(DominionRules.CountsAsPresent(true, true, 50f, 90f, 10f));
        }

        [Test] public void ADroppedPlayerNobodyHasTimedYetStillCounts() =>
            Assert.IsTrue(DominionRules.CountsAsPresent(true, false, 0f, 100f, 10f), "the drop was seen this frame: the grace starts now");

        [Test] public void AZeroGraceDropsAPlayerTheMomentTheDropIsTimed() =>
            Assert.IsFalse(DominionRules.CountsAsPresent(true, true, 50f, 50f, 0f));

        [Test] public void TheDirectorCountsPlayersThroughTheTestedRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(DominionDirector), "StillCounts", typeof(DominionRules).GetMethod(nameof(DominionRules.CountsAsPresent))));

        // ---------------------------------------------------------------- who reacts to a start they joined after

        [Test] public void ARejoinerWithATeamSeatAndATeamIsLeftToTheRejoinPath() =>
            Assert.IsFalse(LobbySeatRules.RejoinerNeedsSeatReaction(hasRejoined: true, hasTeam: true, isSpectator: false));

        [Test] public void ARejoinerOnASpectatorSeatStartsTheSpectatorViewAgain() =>
            Assert.IsTrue(LobbySeatRules.RejoinerNeedsSeatReaction(true, hasTeam: true, isSpectator: true));

        [Test] public void ARejoinerWhoNeverSpawnedGetsItsBodyFromTheSeat() =>
            Assert.IsTrue(LobbySeatRules.RejoinerNeedsSeatReaction(true, hasTeam: false, isSpectator: false), "dropped in the lobby, missed the start edge");

        [Test] public void AFreshJoinerAlwaysReactsToTheSeat()
        {
            Assert.IsTrue(LobbySeatRules.RejoinerNeedsSeatReaction(false, true, false));
            Assert.IsTrue(LobbySeatRules.RejoinerNeedsSeatReaction(false, false, false));
        }

        [Test] public void LobbyStartActsOnTheRejoinerRulesAnswer() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(LobbyStart), "OnJoinedRoom", typeof(LobbySeatRules).GetMethod(nameof(LobbySeatRules.RejoinerNeedsSeatReaction))));

        // ---------------------------------------------------------------- a Start that is refused is resent, then given up

        [Test] public void TheFirstStartIsSentAtOnce() => Assert.IsTrue(LobbySeatRules.ShouldResendStart(sentAtSeconds: -1f, seatsChangedSinceSend: false, nowSeconds: 10f, resendSeconds: 0.5f));

        [Test] public void AStartIsResentWhenASeatChangedSinceItWasSent() =>
            Assert.IsTrue(LobbySeatRules.ShouldResendStart(10f, seatsChangedSinceSend: true, nowSeconds: 10.1f, resendSeconds: 0.5f));

        [Test] public void AStartIsResentOnceTheResendWaitHasPassedAndNotBefore()
        {
            Assert.IsFalse(LobbySeatRules.ShouldResendStart(10f, false, 10.49f, 0.5f));
            Assert.IsTrue(LobbySeatRules.ShouldResendStart(10f, false, 10.5f, 0.5f));
        }

        [Test] public void AStartIsGivenUpOnlyAfterTheGiveUpTime()
        {
            Assert.IsFalse(LobbySeatRules.StartGaveUp(beganSeconds: 10f, nowSeconds: 13f, giveUpSeconds: 3f), "exactly the give-up time still tries");
            Assert.IsTrue(LobbySeatRules.StartGaveUp(10f, 13.01f, 3f));
        }

        [Test] public void LobbyStartRetriesAndGivesUpThroughTheTestedRules()
        {
            MethodInfo resend = typeof(LobbySeatRules).GetMethod(nameof(LobbySeatRules.ShouldResendStart));
            MethodInfo gaveUp = typeof(LobbySeatRules).GetMethod(nameof(LobbySeatRules.StartGaveUp));
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyStart), "StartUntilItLands", resend), "the coroutine asks the resend rule");
            Assert.IsTrue(IlWiring.Uses(typeof(LobbyStart), "StartUntilItLands", gaveUp), "and the give-up rule");
        }
    }
}
