using NUnit.Framework;
using Overpower.Arena;
using UnityEngine;

namespace Overpower.Tests
{
    public class ArenaPieceShapesTests
    {
        [Test]
        public void ABarriersBoxBlocksTheWholeWorldBandWhateverItsLook()
        {
            foreach ((float positionY, float lookHeight) in new[] { (0.5f, 1f), (1f, 2f), (0.25f, 0.5f) })
            {
                ArenaPieceShapes.BarrierBlockingBox(-1f, 3f, positionY, lookHeight, out Vector3 centre, out Vector3 size);
                float worldCentre = positionY + centre.y * lookHeight;
                float worldHeight = size.y * lookHeight;
                Assert.AreEqual(1f, worldCentre, 1e-5f, $"look {lookHeight}");
                Assert.AreEqual(4f, worldHeight, 1e-5f, $"look {lookHeight}");
                Assert.AreEqual(1f, size.x);
                Assert.AreEqual(1f, size.z);
            }
        }
    }
}
