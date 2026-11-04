using System;
using System.IO;
using NUnit.Framework;
using Overpower.Telemetry;

namespace Overpower.Tests
{
    /// <summary>
    /// The match log's writer keeps its file open between flushes (a growing file reopened every two seconds is what stuttered a long match), yet
    /// every line still reaches the file, a sharing reader (the report) can read it meanwhile, a reader that does not share (the zip) can once
    /// it is released, and two writers of one file (a mode's map reloading the scene) never write over each other.
    /// </summary>
    public class TelemetryWriterTests
    {
        private string folder;

        [SetUp]
        public void MakeFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "TelemetryWriterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        private static string[] ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Test] public void EveryFlushedLineIsInTheFileWhileTheWriterStillHasItOpen()
        {
            string path = Path.Combine(folder, "7_Kim.jsonl");
            var writer = new TelemetryWriter();
            writer.Open(path);
            writer.Write("{\"e\":\"a\"}");
            writer.Write("{\"e\":\"b\"}");
            writer.Flush();
            writer.Write("{\"e\":\"c\"}");
            writer.Flush();

            CollectionAssert.AreEqual(new[] { "{\"e\":\"a\"}", "{\"e\":\"b\"}", "{\"e\":\"c\"}" }, ReadShared(path));
            Assert.AreEqual(3, writer.LineCount);
            writer.Close();
        }

        [Test] public void NothingReachesTheFileBeforeAFlush()
        {
            string path = Path.Combine(folder, "7_Kim.jsonl");
            var writer = new TelemetryWriter();
            writer.Open(path);
            writer.Write("{\"e\":\"a\"}");
            Assert.IsFalse(File.Exists(path));
            writer.Close();
            CollectionAssert.AreEqual(new[] { "{\"e\":\"a\"}" }, ReadShared(path), "closing flushes");
        }

        [Test] public void AReleasedFileOpensForAReaderThatDoesNotShareAndTheNextFlushAppends()
        {
            string path = Path.Combine(folder, "7_Kim.jsonl");
            var writer = new TelemetryWriter();
            writer.Open(path);
            writer.Write("{\"e\":\"a\"}");
            writer.Release();

            Assert.DoesNotThrow(() => File.ReadAllText(path), "the zip reads the file with no write sharing");

            writer.Write("{\"e\":\"b\"}");
            writer.Flush();
            CollectionAssert.AreEqual(new[] { "{\"e\":\"a\"}", "{\"e\":\"b\"}" }, ReadShared(path));
            Assert.IsTrue(writer.IsOpen, "releasing the file does not end the match's log");
            writer.Close();
        }

        [Test] public void TwoWritersOfOneFileAppendWithoutWritingOverEachOther()
        {
            string path = Path.Combine(folder, "7_Kim.jsonl");
            var oldScene = new TelemetryWriter();
            var newScene = new TelemetryWriter();
            oldScene.Open(path);
            newScene.Open(path);

            oldScene.Write("{\"e\":\"old 1\"}");
            oldScene.Flush();
            newScene.Write("{\"e\":\"new 1\"}");
            newScene.Flush();
            oldScene.Write("{\"e\":\"old 2\"}");
            oldScene.Flush();

            CollectionAssert.AreEqual(new[] { "{\"e\":\"old 1\"}", "{\"e\":\"new 1\"}", "{\"e\":\"old 2\"}" }, ReadShared(path));
            oldScene.Close();
            newScene.Close();
        }

        [Test] public void ASecondMatchInTheSameFileAppendsAfterTheFirst()
        {
            string path = Path.Combine(folder, "7_Kim.jsonl");
            var first = new TelemetryWriter();
            first.Open(path);
            first.Write("{\"e\":\"first\"}");
            first.Close();

            var second = new TelemetryWriter();
            second.Open(path);
            second.Write("{\"e\":\"second\"}");
            second.Close();

            CollectionAssert.AreEqual(new[] { "{\"e\":\"first\"}", "{\"e\":\"second\"}" }, ReadShared(path));
        }

        [Test] public void AWriterNeverOpenedWritesNothing()
        {
            var writer = new TelemetryWriter();
            writer.Write("{\"e\":\"a\"}");
            writer.Flush();
            writer.Release();
            Assert.AreEqual(0, writer.LineCount);
            Assert.AreEqual(0, Directory.GetFiles(folder).Length);
        }
    }
}
