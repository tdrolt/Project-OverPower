using NUnit.Framework;

namespace Overpower.Tests
{
    public class HarnessTest
    {
        [Test]
        public void TestRunnerIsWired()
        {
            Assert.AreEqual(4, 2 + 2);
        }
    }
}
