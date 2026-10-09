using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Match
{
    /// <summary>The shop's two pages, switched by the two tabs at the top.</summary>
    public enum ShopPage { Weapons, AbilitiesAndArmor }

    /// <summary>Which page the shop reopens on. Kept for the whole session (a static field on the screen
    /// holds one of these), so P, close, P lands where the player last was. An impossible value falls back to the
    /// weapons page.</summary>
    public sealed class ShopPageMemory
    {
        public ShopPage Last { get; private set; } = ShopPage.Weapons;

        public void Remember(ShopPage page)
        {
            Last = System.Enum.IsDefined(typeof(ShopPage), page) ? page : ShopPage.Weapons;
        }
    }

    /// <summary>Where the hover pop-up's top-left corner goes, in canvas units (origin in the middle of the
    /// screen, y up). Just below and right of the cursor; flipped to the other side of the cursor when that would leave
    /// the screen; finally clamped, so a pop-up that fits is always fully on screen.</summary>
    public static class ShopPopupPlacement
    {
        public static Vector2 TopLeft(Vector2 pointer, Vector2 size, Vector2 halfScreen, Vector2 offset, float flipGap)
        {
            // Top-left corner just below-right of the cursor; flip above / left when it would leave the screen.
            float x = pointer.x + offset.x;
            float y = pointer.y + offset.y;
            if (x + size.x > halfScreen.x) x = pointer.x - flipGap - size.x;
            if (y - size.y < -halfScreen.y) y = pointer.y + flipGap + size.y;
            // Then keep it fully on screen (a box bigger than the screen is pinned to the top-left).
            x = Mathf.Clamp(x, -halfScreen.x, Mathf.Max(-halfScreen.x, halfScreen.x - size.x));
            y = Mathf.Clamp(y, Mathf.Min(halfScreen.y, -halfScreen.y + size.y), halfScreen.y);
            return new Vector2(x, y);
        }
    }

    /// <summary>The shop panel is wider than a 4:3 or 5:4 canvas (the canvas is 1920 units wide at 16:9
    /// and narrower on squarer screens), so its X button and outer nodes would clip. The panel is scaled down evenly
    /// until it fits, and never scaled up.</summary>
    public static class ShopPanelScale
    {
        public static float For(float canvasWidth, float panelWidth)
        {
            if (canvasWidth <= 0f || panelWidth <= 0f)
                return 1f;
            return Mathf.Min(1f, canvasWidth / panelWidth);
        }
    }

    /// <summary>The pop-up's text, from the three parts every shop item has: its name, one line on what it
    /// does, and its numbers. Empty parts are left out; nothing at all gives an empty string (no pop-up).</summary>
    public static class ShopPopupText
    {
        public static string Compose(string name, string description, string numbers, string numbersColorHex)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(name))
                sb.Append("<b>").Append(name).Append("</b>");
            if (!string.IsNullOrEmpty(description))
                sb.Append(sb.Length > 0 ? "\n" : "").Append(description);
            if (!string.IsNullOrEmpty(numbers))
                sb.Append(sb.Length > 0 ? "\n" : "").Append("<color=#").Append(numbersColorHex).Append('>').Append(numbers).Append("</color>");
            return sb.ToString();
        }
    }

    /// <summary>Number formatting the shop's pop-ups share (weapon stats, ability stats, armour rows).</summary>
    public static class ShopNumberFormat
    {
        /// <summary>At most two decimals, no trailing zeros, always a dot (never a culture's comma).</summary>
        public static string Compact(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The non-empty lines joined with a line break.</summary>
        public static string Lines(params string[] lines)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.ToString();
        }
    }

    /// <summary>The numbers block of an armour row's pop-up: the level you have now and what it gives, then
    /// what the next upgrade gives and its price (or that there is none left). The wording comes from UiTheme
    /// (formats: {0} = level, {1} = the value, {2} = the price line).</summary>
    public static class ArmorPopupText
    {
        public static string Numbers(bool absorbRow, ArmorConfig config, int absorbLevel, int rechargeLevel, string priceLine,
            string nowFormat, string nextFormat, string noNextText, int upgradeCap = int.MaxValue)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            var path = new ArmorUpgradePath(config, absorbLevel, rechargeLevel, upgradeCap);
            int level = absorbRow ? path.AbsorbLevel : path.RechargeLevel;
            bool canUpgrade = absorbRow ? path.CanUpgradeAbsorb : path.CanUpgradeRecharge;
            string Value(int lv) => ShopNumberFormat.Compact(absorbRow ? config.AbsorbFor(lv) : config.RechargeSecondsFor(lv));

            string now = string.Format(c, nowFormat, level, Value(level));
            string next = canUpgrade ? string.Format(c, nextFormat, level + 1, Value(level + 1), priceLine) : noNextText;
            return ShopNumberFormat.Lines(now, next);
        }
    }
}
