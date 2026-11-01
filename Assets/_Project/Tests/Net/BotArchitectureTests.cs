using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Overpower.Tests
{
    /// <summary>
    /// Two habits that silently break bots, checked over the whole runtime source so a new feature cannot slip back into them:
    /// reading Player.IsInactive (a bot is inactive to PUN but never dropped: ask Presence.IsDropped) and writing a player's
    /// properties straight to Photon (a bot's go to the room: PlayerProps.Set). Room writes stay as they are.
    /// </summary>
    public class BotArchitectureTests
    {
        private const string Runtime = "Assets/_Project/Code/Runtime";
        private const string BotNet = "Assets/_Project/Code/Runtime/Net/Bots/";

        private static IEnumerable<(string file, int line, string text)> Lines(Regex pattern)
        {
            foreach (string file in Directory.GetFiles(Runtime, "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (path.StartsWith(BotNet)) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*")) continue;
                    if (pattern.IsMatch(lines[i])) yield return (path, i + 1, trimmed);
                }
            }
        }

        private static string Report(IEnumerable<(string file, int line, string text)> offenders)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in offenders) sb.AppendLine($"{o.file}:{o.line}  {o.text}");
            return sb.ToString();
        }

        [Test]
        public void NothingReadsIsInactiveExceptThePresenceRule()
        {
            var offenders = new List<(string, int, string)>(Lines(new Regex(@"\.IsInactive\b")));
            Assert.IsEmpty(offenders, "Use Presence.IsDropped(player):\n" + Report(offenders));
        }

        [Test]
        public void PlayerPropertiesAreOnlyWrittenThroughPlayerProps()
        {
            var offenders = new List<(string, int, string)>();
            foreach (var hit in Lines(new Regex(@"\.SetCustomProperties\(|\.AddScore\(|\.SetScore\(")))
                if (!hit.text.Contains("CurrentRoom.SetCustomProperties(")) offenders.Add(hit);
            Assert.IsEmpty(offenders, "Use PlayerProps.Set(player, props):\n" + Report(offenders));
        }
    }
}
