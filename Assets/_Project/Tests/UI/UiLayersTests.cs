using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Overpower.Chat;
using Overpower.UI;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>The one table of canvas sorting orders (UiLayers): the relations the screens rely on, so a panel cannot cover the shop again by
    /// accident, and no canvas order written as a bare number anywhere else.</summary>
    public class UiLayersTests
    {
        private static Dictionary<string, int> Table()
        {
            var table = new Dictionary<string, int>();
            foreach (FieldInfo f in typeof(UiLayers).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(int)) table[f.Name] = (int)f.GetRawConstantValue();
            return table;
        }

        private static UiTheme RealTheme() => AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Project/Design/Settings/UiTheme.asset");

        [Test]
        public void DeathCardUnderShopAboveHud()
        {
            Assert.Less(UiLayers.DeathCard, UiLayers.Shop, "a dead player sees and clicks the whole shop");
            Assert.Greater(UiLayers.DeathCard, UiLayers.Hud);
            Assert.Greater(UiLayers.DeathCard, UiLayers.Minimap);
            Assert.Greater(UiLayers.DeathCard, UiLayers.HitFeedback);
        }

        [Test]
        public void DeathVignetteUnderEveryOtherCanvas()
        {
            foreach (KeyValuePair<string, int> layer in Table())
                if (layer.Key != nameof(UiLayers.DeathVignette))
                    Assert.Less(UiLayers.DeathVignette, layer.Value, layer.Key + ": the vignette darkens the world only, never a screen panel");
        }

        [Test]
        public void DeathCardUnderDominionHudAndChat()
        {
            Assert.Less(UiLayers.DeathCard, UiLayers.DominionHud, "the round bar draws over the card (the card is placed clear of it)");
            Assert.Less(UiLayers.DeathCard, UiLayers.ChatInMatch);
            Assert.AreEqual(UiLayers.DominionHud, DominionHud.SortingOrder);
            Assert.AreEqual(UiLayers.ChatInMatch, ChatPanelRule.MatchOrder);
            Assert.AreEqual(UiLayers.ChatInLobbyRoom, ChatPanelRule.LobbyRoomOrder);
        }

        [Test]
        public void ResultPanelsAboveShop()
        {
            Assert.Greater(UiLayers.ResultPanels, UiLayers.Shop, "YOU WIN / YOU LOSE show over an open shop");
            Assert.Greater(UiLayers.SpectatorResult, UiLayers.Shop);
            Assert.Greater(RealTheme().dominionResultSortingOrder, UiLayers.Shop, "the Dominion result card");
            Assert.Greater(UiLayers.MatchLog, UiLayers.ResultPanels, "the saved-log box shows with the result");
        }

        [Test]
        public void QuitAboveAllButConnectionLost()
        {
            foreach (KeyValuePair<string, int> layer in Table())
                if (layer.Key != nameof(UiLayers.QuitConfirm) && layer.Key != nameof(UiLayers.ConnectionLost))
                    Assert.Less(layer.Value, UiLayers.QuitConfirm, layer.Key);
            UiTheme theme = RealTheme();
            Assert.Less(theme.scoreboardSortingOrder, UiLayers.QuitConfirm, "scoreboard");
            Assert.Less(theme.dominionResultSortingOrder, UiLayers.QuitConfirm, "Dominion result");
            Assert.Greater(UiLayers.ConnectionLost, UiLayers.QuitConfirm, "with no connection nothing else on screen can be acted on");
        }

        [Test]
        public void NoLiteralSortingOrderOutsideTheTable()
        {
            // an order written as a number: assigned, or handed to PopupCard.CreateCanvas (its third argument), or chosen in a ternary
            var literal = new Regex(@"sortingOrder\s*=\s*(-?\d|[^;]*\?\s*-?\d|[^;]*:\s*-?\d)|CreateCanvas\([^,]*,[^,]*,\s*-?\d", RegexOptions.IgnoreCase);
            var found = new List<string>();
            foreach (string path in Directory.GetFiles("Assets/_Project/Code", "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) == "UiLayers.cs") continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (literal.IsMatch(lines[i])) found.Add(path.Replace('\\', '/') + ":" + (i + 1));
            }
            Assert.IsEmpty(found, "every canvas order comes from UiLayers: " + string.Join(", ", found));
        }
    }
}
