using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The pages of the How to play wiki, in page-list order: a title, a text and a picture each. One asset, so a page
    /// can be reworded or a picture swapped in the Inspector without code.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/How To Play Pages", fileName = "HowToPlayPages")]
    public sealed class HowToPlayPages : ScriptableObject
    {
        /// <summary>One page of the wiki.</summary>
        [Serializable]
        public struct Page
        {
            [Tooltip("The name of the page, shown in the page list and as the page's heading.")]
            public string title;
            [Tooltip("What the page says, a few sentences. It is shown as written.")]
            [TextArea(3, 12)] public string text;
            [Tooltip("The picture beside the text (1040 x 800 pixels).")]
            public Sprite picture;
        }

        [Tooltip("The pages, in the order of the page list.")]
        [SerializeField] private List<Page> pages = new List<Page>();

        public IReadOnlyList<Page> Pages => pages;
        public int Count => pages.Count;

        public List<string> Titles()
        {
            var titles = new List<string>(pages.Count);
            foreach (Page page in pages) titles.Add(page.title ?? "");
            return titles;
        }
    }
}
