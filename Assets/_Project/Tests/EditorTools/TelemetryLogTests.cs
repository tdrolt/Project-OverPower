using System;
using System.IO;
using NUnit.Framework;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>T5 re-review (item 8): the parsing-level error handling TelemetryLog.Load's own
    /// class comment already documents - a non-numeric `t`, an unreadable file, and a session using
    /// a newer schema than this build understands are all counted, never a failure (design doc,
    /// Error handling). One focused fixture per case, in its own temp folder.</summary>
    public class TelemetryLogTests
    {
        private static string NewTempFolder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static string Session(int actor, int schema = 1) =>
            "{\"e\":\"session\",\"t\":0,\"schema\":" + schema + ",\"m\":\"M\",\"a\":" + actor +
            ",\"nick\":\"n" + actor + "\",\"tm\":0,\"master\":true,\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n";

        [Test]
        public void ANonNumericTIsCountedAsMalformedNotThrown()
        {
            string temp = NewTempFolder();
            try
            {
                // Opus review fix: the "t" parse used to sit outside the line's own try/catch, so a
                // non-numeric t (a corrupt or hand-edited line) threw uncaught instead of counting as
                // malformed like every other bad line - see TelemetryLog.Load's own comment.
                string lines = Session(1) + "{\"e\":\"sample\",\"t\":\"oops\",\"bal\":0}\n";
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                TelemetryLog log;
                Assert.DoesNotThrow(() => log = TelemetryLog.Load(temp));
                log = TelemetryLog.Load(temp);

                Assert.AreEqual(1, log.MalformedLineCount);
                Assert.AreEqual(0, log.UnknownEventCount);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        [Test]
        public void AFileTheGameStillHasOpenIsRead()
        {
            string temp = NewTempFolder();
            string path = Path.Combine(temp, "1.jsonl");
            // The game keeps its log open for the whole match, writing and sharing it.
            using (var writing = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Session(1) + "\n");
                writing.Write(bytes, 0, bytes.Length);
                writing.Flush();

                TelemetryLog log = TelemetryLog.Load(temp);
                Assert.AreEqual(0, log.UnreadableFileCount);
                Assert.AreEqual(1, log.Sessions.Count);
            }

            Directory.Delete(temp, true);
        }

        [Test]
        public void TheSessionLineSaysModeMapTeamSizeAndBot()
        {
            string temp = NewTempFolder();
            File.WriteAllText(Path.Combine(temp, "1001_Rex.jsonl"),
                "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1001,\"nick\":\"Rex\",\"tm\":1,\"master\":false,\"bot\":true,\"bbuild\":1," +
                "\"mode\":\"Dominion 3v3v3\",\"map\":\"Game Scene\",\"teamSize\":3}\n");
            TelemetrySession session = TelemetryLog.Load(temp).Sessions[0];
            Assert.IsTrue(session.Bot);
            Assert.AreEqual(1, session.BotBuild);
            Assert.AreEqual("Dominion 3v3v3", session.Mode);
            Assert.AreEqual("Game Scene", session.Map);
            Assert.AreEqual(3, session.TeamSize);

            File.WriteAllText(Path.Combine(temp, "1001_Rex.jsonl"), Session(1001));
            TelemetrySession old = TelemetryLog.Load(temp).Sessions[0];
            Assert.IsFalse(old.Bot);
            Assert.AreEqual(-1, old.BotBuild);
            Assert.AreEqual("", old.Mode, "a log from before the mode was written");
            Directory.Delete(temp, true);
        }

        [Test]
        public void AnUnreadableFileIsCountedNotThrown()
        {
            string temp = NewTempFolder();
            string goodPath = Path.Combine(temp, "1.jsonl");
            string lockedPath = Path.Combine(temp, "2.jsonl");
            File.WriteAllText(goodPath, Session(1));
            File.WriteAllText(lockedPath, Session(2));

            // An exclusive lock (no FileShare) makes File.ReadAllLines throw IOException for this
            // one file - a real-world equivalent of a file mid-write by another process, or briefly
            // locked by an antivirus scan.
            using (var exclusiveLock = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                TelemetryLog log = null;
                Assert.DoesNotThrow(() => log = TelemetryLog.Load(temp));

                Assert.AreEqual(1, log.UnreadableFileCount);
                // The one readable file's own session still comes through fine.
                Assert.AreEqual(1, log.Sessions.Count);
            }

            Directory.Delete(temp, true);
        }

        [Test]
        public void ANewerSchemaIsCountedNotRejected()
        {
            string temp = NewTempFolder();
            try
            {
                // TelemetryKeys.SchemaVersion is 1 - 999 stands in for "a future schema this build
                // has never heard of".
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), Session(1, schema: 999));

                var log = TelemetryLog.Load(temp);

                Assert.AreEqual(1, log.NewerSchemaCount);
                // Still parsed and included, not thrown away - see the design doc's own Error
                // handling section ("a newer schema... skipped with a count, never a failure").
                Assert.AreEqual(1, log.Sessions.Count);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
