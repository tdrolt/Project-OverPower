using System;
using System.Collections.Generic;

namespace Overpower.Lobby
{
    /// <summary>
    /// Pure rules of the How to play wiki: which page comes before and after, that the ends do not wrap round, the "4 / 6" counter
    /// and the neighbouring pages' titles the buttons name. Pages are numbered from 0 here and from 1 in the counter.
    /// </summary>
    public static class HowToPlayRules
    {
        /// <summary>The index kept inside the pages (0 when there are none).</summary>
        public static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Max(0, Math.Min(index, count - 1));

        public static bool HasPrevious(int index, int count) => count > 1 && Clamp(index, count) > 0;

        public static bool HasNext(int index, int count) => count > 1 && Clamp(index, count) < count - 1;

        /// <summary>The page before; the first page stays where it is (it does not wrap to the last).</summary>
        public static int Previous(int index, int count) => HasPrevious(index, count) ? Clamp(index, count) - 1 : Clamp(index, count);

        /// <summary>The page after; the last page stays where it is (it does not wrap to the first).</summary>
        public static int Next(int index, int count) => HasNext(index, count) ? Clamp(index, count) + 1 : Clamp(index, count);

        /// <summary>"4 / 6" for the fourth of six pages (the format has {0} = the page number, {1} = how many). A format that does not fit
        /// is shown as it is rather than thrown at the player.</summary>
        public static string CounterText(int index, int count, string format)
        {
            int number = count <= 0 ? 0 : Clamp(index, count) + 1;
            try
            {
                return string.Format(format, number, Math.Max(0, count));
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>The title of the page before this one, or null on the first page.</summary>
        public static string PreviousTitle(IReadOnlyList<string> titles, int index) =>
            titles != null && HasPrevious(index, titles.Count) ? titles[Clamp(index, titles.Count) - 1] : null;

        /// <summary>The title of the page after this one, or null on the last page.</summary>
        public static string NextTitle(IReadOnlyList<string> titles, int index) =>
            titles != null && HasNext(index, titles.Count) ? titles[Clamp(index, titles.Count) + 1] : null;
    }
}
