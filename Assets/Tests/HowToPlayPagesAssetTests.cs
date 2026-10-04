using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The How to play asset: structure only (six pages, each with a title, a text and a picture); never the words.</summary>
    public class HowToPlayPagesAssetTests
    {
        private const string PagesPath = "Assets/Gameplay/Config/HowToPlayPages.asset";
        private const string ThemePath = "Assets/Gameplay/Config/UiTheme.asset";
        private const string PictureFolder = "Assets/Gameplay/UI/HowToPlay/";

        private static HowToPlayPages Pages() => AssetDatabase.LoadAssetAtPath<HowToPlayPages>(PagesPath);

        [Test]
        public void TheAssetExistsAndHasSixPages()
        {
            HowToPlayPages pages = Pages();
            Assert.NotNull(pages, PagesPath);
            Assert.AreEqual(6, pages.Count);
        }

        [Test]
        public void EveryPageHasATitleATextAndAPicture()
        {
            HowToPlayPages pages = Pages();
            Assert.NotNull(pages);
            for (int i = 0; i < pages.Count; i++)
            {
                HowToPlayPages.Page page = pages.Pages[i];
                Assert.IsFalse(string.IsNullOrWhiteSpace(page.title), "page " + (i + 1) + " has no title");
                Assert.IsFalse(string.IsNullOrWhiteSpace(page.text), "page " + (i + 1) + " has no text");
                Assert.NotNull(page.picture, "page " + (i + 1) + " has no picture");
            }
        }

        [Test]
        public void NoTwoPagesShareATitleOrAPicture()
        {
            HowToPlayPages pages = Pages();
            Assert.NotNull(pages);
            var titles = new HashSet<string>();
            var pictures = new HashSet<Sprite>();
            foreach (HowToPlayPages.Page page in pages.Pages)
            {
                Assert.IsTrue(titles.Add(page.title), "title twice: " + page.title);
                Assert.IsTrue(pictures.Add(page.picture), "picture twice on " + page.title);
            }
        }

        [Test]
        public void ThePicturesAreSpritesWithoutMipmapsInTheHowToPlayFolder()
        {
            HowToPlayPages pages = Pages();
            Assert.NotNull(pages);
            foreach (HowToPlayPages.Page page in pages.Pages)
            {
                string path = AssetDatabase.GetAssetPath(page.picture);
                StringAssert.StartsWith(PictureFolder, path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, path);
                Assert.IsFalse(importer.mipmapEnabled, path + " has mipmaps");
            }
        }

        [Test]
        public void TheThemeHoldsTheAsset()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(ThemePath);
            Assert.NotNull(theme);
            Assert.AreSame(Pages(), theme.howToPlayPages, "the panels read their pages from the theme");
        }

        [Test]
        public void TheTitlesListHandsBackEveryTitleInOrder()
        {
            HowToPlayPages pages = Pages();
            Assert.NotNull(pages);
            List<string> titles = pages.Titles();
            Assert.AreEqual(pages.Count, titles.Count);
            for (int i = 0; i < titles.Count; i++) Assert.AreEqual(pages.Pages[i].title, titles[i]);
        }
    }
}
