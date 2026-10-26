using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapCutMaskTests
    {
        private static PhaseTwoCutGeometry ToyCut() => PhaseTwoCutGeometry.Build(
            new List<Vector2> { new Vector2(10f, 30f), new Vector2(10f - 8.660254f, 15f), new Vector2(10f + 8.660254f, 15f) },
            new Vector2(10f, 20f), Vector2.up, 2f, 2f, 1f, 0.2f);

        [Test]
        public void OpenGroundClosedGroundAndTheWallAreToldApart()
        {
            PhaseTwoCutGeometry cut = ToyCut();
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 20f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Closed, MinimapCutMask.Classify(new Vector2(10f, 26f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Wall, MinimapCutMask.Classify(new Vector2(7f, 22.1f), cut, 0.3f));
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 22.5f), cut, 0.3f), "the recess is open");
            Assert.AreEqual(MinimapCutMask.Pixel.Open, MinimapCutMask.Classify(new Vector2(10f, 5f), cut, 0.3f), "outside the arena isn't shaded");
        }

        [Test]
        public void ThePictureCoversTheBakedSquareBottomRowFirst()
        {
            var closed = new Color32(1, 2, 3, 200);
            var wall = new Color32(250, 250, 250, 255);
            // 40 x 40 pixels over a 20 m square centred on (10, 22): 0.5 m a pixel, pixel (0,0) at world (0.25, 12.25).
            Color32[] pixels = MinimapCutMask.Paint(40, new Vector2(10f, 22f), 20f, ToyCut(), 0.3f, closed, wall);
            Assert.AreEqual(1600, pixels.Length);
            Assert.AreEqual(0, pixels[15 * 40 + 19].a, "world (9.75, 19.75): open, transparent");
            Assert.AreEqual(closed, pixels[27 * 40 + 19], "world (9.75, 25.75): behind the wall");
            int wallPixels = 0;
            foreach (Color32 p in pixels)
                if (p.Equals(wall)) wallPixels++;
            Assert.Greater(wallPixels, 0);
        }
    }
}
