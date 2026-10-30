using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 30 Oct (on itch.io): Unity's default page added its own footer with a logo and a fullscreen button that does nothing
    /// inside itch.io's frame, next to itch.io's working one. The browser build uses the project's own page instead: the game fills
    /// the frame at 16:9 on the lobby colour, with no footer, so itch.io's button is the only fullscreen button.
    /// </summary>
    public class WebPageTemplateTests
    {
        private const string TemplatePath = "Assets/WebGLTemplates/OverPower/index.html";

        [Test]
        public void TheWebBuildUsesTheProjectsOwnPage() => Assert.AreEqual("PROJECT:OverPower", PlayerSettings.WebGL.template);

        [Test]
        public void ThePageHasNoFooterLogoOrSecondFullscreenButton()
        {
            string page = File.ReadAllText(TemplatePath);
            StringAssert.DoesNotContain("unity-footer", page);
            StringAssert.DoesNotContain("fullscreen-button", page);
            StringAssert.DoesNotContain("SetFullscreen", page);
            StringAssert.DoesNotContain("unity-logo", page);
        }

        [Test]
        public void ThePageStartsTheGameAndFillsTheFrameAt16By9()
        {
            string page = File.ReadAllText(TemplatePath);
            StringAssert.Contains("createUnityInstance", page);
            StringAssert.Contains("{{{ LOADER_FILENAME }}}", page);
            StringAssert.Contains("100vw", page);
            StringAssert.Contains("177.78vh", page, "never wider than 16:9 of the frame's height");
        }
    }
}
