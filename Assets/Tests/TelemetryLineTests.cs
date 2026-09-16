using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    public class TelemetryLineTests
    {
        [Test]
        public void BuildsOneFlatJsonObjectWithInvariantNumbers()
        {
            var line = new TelemetryLine();
            line.Begin("hit", 12.5).Int("a", 3).Float("raw", 11.25f).Bool("lethal", false).String("src", "Projectile");
            Assert.AreEqual("{\"e\":\"hit\",\"t\":12.5,\"a\":3,\"raw\":11.25,\"lethal\":false,\"src\":\"Projectile\"}", line.End());
        }

        [Test]
        public void EscapesQuotesBackslashesAndControlCharacters()
        {
            var line = new TelemetryLine();
            line.Begin("marker", 1).String("note", "a \"b\" \\ c\nd");
            Assert.AreEqual("{\"e\":\"marker\",\"t\":1,\"note\":\"a \\\"b\\\" \\\\ c\\nd\"}", line.End());
        }

        [Test]
        public void WritesIntArrays()
        {
            var line = new TelemetryLine();
            line.Begin("sample", 0).Ints("ids", new[] { 1, -1, 25 });
            Assert.AreEqual("{\"e\":\"sample\",\"t\":0,\"ids\":[1,-1,25]}", line.End());
        }

        [Test]
        public void NonFiniteFloatsBecomeNull()
        {
            var line = new TelemetryLine();
            line.Begin("x", 0).Float("f", float.NaN);
            Assert.AreEqual("{\"e\":\"x\",\"t\":0,\"f\":null}", line.End());
        }

        [Test]
        public void TheBuilderIsReusable()
        {
            var line = new TelemetryLine();
            line.Begin("a", 1).Int("n", 1); line.End();
            line.Begin("b", 2).Int("n", 2);
            Assert.AreEqual("{\"e\":\"b\",\"t\":2,\"n\":2}", line.End());
        }
    }
}
