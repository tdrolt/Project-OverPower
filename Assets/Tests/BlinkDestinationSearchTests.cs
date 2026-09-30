using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class BlinkDestinationSearchTests
    {
        private static readonly Vector3 Origin = new Vector3(10f, 0f, -5f); // arbitrary, not (0,0,0) -
                                                                             // proves the maths does not
                                                                             // secretly rely on an origin at world zero.

        [Test]
        public void ClearSpotWithinRangeLandsExactlyOnTheRequestedPoint()
        {
            Vector3 requested = Origin + new Vector3(5f, 0f, 0f);

            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, requested, range: 9f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    landingPoint = candidateXZ; // pretend the ground sits exactly at y=0 everywhere.
                    return true;
                });

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.Adjusted);
            Assert.AreEqual(requested.x, result.Destination.x, 0.001f);
            Assert.AreEqual(requested.z, result.Destination.z, 0.001f);
        }

        [Test]
        public void BeyondRangeClampsToTheFullRangeTowardTheCursor()
        {
            Vector3 requested = Origin + new Vector3(15f, 0f, 0f);

            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, requested, range: 9f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    landingPoint = candidateXZ;
                    return true;
                });

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.Adjusted);
            Assert.AreEqual(Origin.x + 9f, result.Destination.x, 0.001f);
        }

        [Test]
        public void BlockedAtTheRequestedSpotWalksBackUntilClear()
        {
            // Everything past 5m from the origin is "inside a wall"; the search must stop at
            // exactly 5m, the first clear spot walking back from the (in-range) 9m request.
            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, Origin + new Vector3(9f, 0f, 0f), range: 9f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    landingPoint = candidateXZ;
                    return Vector3.Distance(Origin, candidateXZ) <= 5f + 0.0001f;
                });

            Assert.IsTrue(result.Found);
            Assert.IsTrue(result.Adjusted);
            Assert.AreEqual(Origin.x + 5f, result.Destination.x, 0.001f);
        }

        [Test]
        public void NothingValidAnywhereRefusesEvenAtTheCastersOwnSpot()
        {
            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, Origin + new Vector3(9f, 0f, 0f), range: 9f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    landingPoint = default;
                    return false;
                });

            Assert.IsFalse(result.Found);
        }

        [Test]
        public void CursorOnTheCasterOnlyProbesTheCastersOwnSpot()
        {
            int probeCount = 0;

            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, Origin, range: 9f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    probeCount++;
                    landingPoint = candidateXZ;
                    return true;
                });

            Assert.IsTrue(result.Found);
            Assert.IsFalse(result.Adjusted);
            Assert.AreEqual(1, probeCount);
            Assert.AreEqual(Origin.x, result.Destination.x, 0.001f);
        }

        [Test]
        public void SearchAlwaysTriesTheCastersOwnSpotLastWhenNothingElseWorks()
        {
            var triedDistances = new List<float>();

            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, Origin + new Vector3(1f, 0f, 0f), range: 1f, searchStep: 0.3f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    triedDistances.Add(Vector3.Distance(Origin, candidateXZ));
                    landingPoint = default;
                    return false;
                });

            Assert.IsFalse(result.Found);
            // 1, 0.7, 0.4, 0.1, 0 - five steps of 0.3 from 1m, clamped so the last is always exactly 0
            // rather than overshooting past the caster.
            Assert.AreEqual(5, triedDistances.Count);
            Assert.AreEqual(0f, triedDistances[triedDistances.Count - 1], 0.0001f);
        }

        [Test]
        public void NegativeRangeIsTreatedAsZeroRatherThanSearchingBackwards()
        {
            BlinkDestinationSearch.Result result = BlinkDestinationSearch.Find(
                Origin, Origin + new Vector3(5f, 0f, 0f), range: -3f, searchStep: 0.25f,
                probe: (Vector3 candidateXZ, out Vector3 landingPoint) =>
                {
                    landingPoint = candidateXZ;
                    return true;
                });

            Assert.IsTrue(result.Found);
            Assert.AreEqual(Origin.x, result.Destination.x, 0.001f);
        }
    }
}
