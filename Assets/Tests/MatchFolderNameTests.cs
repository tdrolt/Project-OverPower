using System;
using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    /// <summary>Lobby Task 13 (D13): each match's logs sit in a folder named by date, mode, size and lobby, and one match is always one folder.</summary>
    public class MatchFolderNameTests
    {
        private static readonly DateTime Start = new DateTime(2026, 10, 2, 21, 30, 5);

        private static bool Never(string name) => false;

        [Test]
        public void TheNameSaysWhenWhichModeAndSizeAndWhichLobby()
        {
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", MatchFolderName.For(Start, "Conquest 3v3v3", "Tudor's lobby", Never));
        }

        [Test]
        public void UnsafePathCharactersAreRemoved()
        {
            string name = MatchFolderName.For(Start, "Conquest 3v3v3", "a\\b/c:d*e?f\"g<h>i|j\tk\u0001l", Never);
            foreach (char bad in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\t', '\u0001' })
                Assert.IsFalse(name.Contains(bad.ToString()), "still contains " + (int)bad);
            StringAssert.StartsWith("2026-10-02_2130_Conquest-3v3v3_", name);
            StringAssert.Contains("abcdefghij", name);
        }

        [Test]
        public void SpacesBecomeDashesAndApostrophesAreDropped()
        {
            string name = MatchFolderName.For(Start, "Conquest 3v3v3", "  Kim's   big  lobby\u2019s ", Never);
            StringAssert.EndsWith("_Kims-big-lobbys", name);
        }

        [Test]
        public void ALongLobbyNameIsCapped()
        {
            string name = MatchFolderName.For(Start, "Conquest 3v3v3", new string('x', 200), Never);
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_" + new string('x', MatchFolderName.MaxLobbyPart), name);
        }

        [Test]
        public void ABlankLobbyOrModeStillGivesAUsableName()
        {
            Assert.AreEqual("2026-10-02_2130_Match_Lobby", MatchFolderName.For(Start, null, "  ", Never));
            Assert.AreEqual("2026-10-02_2130_Match_Lobby", MatchFolderName.For(Start, "///", "***", Never));
        }

        [Test]
        public void AClashAddsTwoThenThree()
        {
            var taken = new HashSet<string> { "2026-10-02_2130_Conquest-3v3v3_Tudors-lobby" };
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)", MatchFolderName.For(Start, "Conquest 3v3v3", "Tudor's lobby", taken.Contains));
            taken.Add("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)");
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (3)", MatchFolderName.For(Start, "Conquest 3v3v3", "Tudor's lobby", taken.Contains));
        }

        // ---- one match = one folder (Resolve)

        private static Dictionary<string, string> Folders(params (string name, string id)[] entries)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, id) in entries) map[name] = id;
            return map;
        }

        [Test]
        public void ANewMatchGetsTheNameFromTheRule()
        {
            string name = MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", Folders(), out bool existing);
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", name);
            Assert.IsFalse(existing);
        }

        [Test]
        public void AFolderHoldingTheSameMatchIdIsReusedEvenAtAnotherMinute()
        {
            var folders = Folders(("2026-10-02_2129_Conquest-3v3v3_Tudors-lobby", "id-A"));
            string name = MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", folders, out bool existing);
            Assert.AreEqual("2026-10-02_2129_Conquest-3v3v3_Tudors-lobby", name, "the second client of one match on this PC finds the first one's folder");
            Assert.IsTrue(existing);
        }

        [Test]
        public void AFolderOfAnotherMatchWithTheSameNameGetsTwo()
        {
            var folders = Folders(("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", "id-B"));
            string name = MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", folders, out bool existing);
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)", name);
            Assert.IsFalse(existing);
        }

        [Test]
        public void TheSecondMatchsFolderIsFoundByItsIdAmongSeveralWithTheSameBaseName()
        {
            var folders = Folders(
                ("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", "id-B"),
                ("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)", "id-A"));
            string name = MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", folders, out bool existing);
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)", name);
            Assert.IsTrue(existing);
        }

        [Test]
        public void AFolderWithNoIdFileCountsAsAnotherMatch()
        {
            var folders = Folders(("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby", ""));
            string name = MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", folders, out bool existing);
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)", name, "an old folder (or one whose id is not written yet) is never joined by guessing");
            Assert.IsFalse(existing);
        }

        [Test]
        public void FolderNamesCompareWithoutCase()
        {
            var folders = Folders(("2026-10-02_2130_CONQUEST-3V3V3_TUDORS-LOBBY", "id-B"));
            Assert.AreEqual("2026-10-02_2130_Conquest-3v3v3_Tudors-lobby (2)",
                MatchFolderName.Resolve(Start, "Conquest 3v3v3", "Tudor's lobby", "id-A", folders, out _), "Windows folder names ignore case");
        }
    }
}
