using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Overpower.UI
{
    /// <summary>
    /// The death card's words and where it sits (DeathCard calls both every frame). Place works in screen pixels, origin bottom-left; a rect of
    /// zero width means "hide the card": nowhere on screen was clear of the open shop, which always wins.
    /// </summary>
    public static class DeathCardRules
    {
        /// <summary>The countdown line: the format with the whole seconds left, rounded up, so the last second reads 1, never 0, while still dead.</summary>
        public static string Text(string format, float secondsLeft)
        {
            int seconds = WholeSeconds(secondsLeft);
            try { return string.Format(format ?? "", seconds); }
            catch (FormatException) { return format + " " + seconds; }
        }

        /// <summary>The whole seconds the countdown shows: rounded up, never below 0.</summary>
        public static int WholeSeconds(float secondsLeft) => secondsLeft > 0f ? Mathf.CeilToInt(secondsLeft) : 0;

        /// <summary>What the countdown counts from: what is left of the running respawn wait, or 0 once no wait runs (the last value is stale).</summary>
        public static float SecondsLeft(bool waitRunning, float waitLeft) => waitRunning ? Mathf.Max(0f, waitLeft) : 0f;

        private static readonly Regex Spaces = new Regex(@"\s+");

        /// <summary>A text written for a full-screen panel (line breaks, trailing spaces) read as one line on the card.</summary>
        public static string OneLine(string text) => Spaces.Replace(text ?? "", " ").Trim();

        /// <summary>Where the card goes. Shop closed: top centre, under the Dominion HUD's top band when there is one. Shop open: bottom centre just
        /// above the ability bar, else just under the shop, else top centre, else under the shop over the ability bar; never over the shop or the
        /// top band, always on screen.</summary>
        /// <param name="shop">The shop panel's screen rect (ignored while closed).</param>
        /// <param name="topReserved">What is kept clear at the top: the Dominion HUD's top band and the HUD's toast while it shows, or an empty rect.</param>
        /// <param name="bottomReserved">The ability bar, or an empty rect.</param>
        /// <param name="card">The card's size in screen pixels.</param>
        /// <param name="margin">The gap kept to the screen edge, the shop, the band and the ability bar, in screen pixels.</param>
        public static Rect Place(bool shopOpen, Rect shop, Rect topReserved, Rect bottomReserved, Vector2 screen, Vector2 card, float margin)
        {
            float x = (screen.x - card.x) / 2f;
            float top = screen.y - margin;
            if (HasArea(topReserved)) top = Mathf.Min(top, topReserved.yMin - margin);
            var topCentre = new Rect(x, top - card.y, card.x, card.y);
            var aboveBar = new Rect(x, (HasArea(bottomReserved) ? bottomReserved.yMax : 0f) + margin, card.x, card.y);
            bool shopShown = shopOpen && HasArea(shop);
            var underShop = new Rect(x, shop.yMin - margin - card.y, card.x, card.y);

            if (!shopShown)
            {
                if (Clear(topCentre, false, shop, topReserved, bottomReserved, screen, true)) return topCentre;
                if (Clear(aboveBar, false, shop, topReserved, bottomReserved, screen, true)) return aboveBar;
                return Rect.zero;
            }
            if (Clear(aboveBar, true, shop, topReserved, bottomReserved, screen, true)) return aboveBar;
            if (Clear(underShop, true, shop, topReserved, bottomReserved, screen, true)) return underShop;
            if (Clear(topCentre, true, shop, topReserved, bottomReserved, screen, true)) return topCentre;
            if (Clear(underShop, true, shop, topReserved, bottomReserved, screen, false)) return underShop;
            return Rect.zero;
        }

        private static bool HasArea(Rect r) => r.width > 0f && r.height > 0f;

        private static bool Clear(Rect r, bool shopShown, Rect shop, Rect band, Rect bar, Vector2 screen, bool avoidBar)
        {
            if (r.xMin < 0f || r.yMin < 0f || r.xMax > screen.x || r.yMax > screen.y) return false;
            if (shopShown && r.Overlaps(shop)) return false;
            if (HasArea(band) && r.Overlaps(band)) return false;
            return !(avoidBar && HasArea(bar) && r.Overlaps(bar));
        }
    }
}
