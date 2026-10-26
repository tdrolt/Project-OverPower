using NUnit.Framework;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    public class MinimapLabelPlacementTests
    {
        [Test]
        public void AnApexUpTriangleEndsHalfItsRadiusBelowItsCentre()
        {
            Assert.AreEqual(50f, MinimapLabelPlacement.ShapeBottomBelowCentre(false, new Vector2(340f, 340f), 100f, 0f), 1e-3f);
        }

        [Test]
        public void ATriangleTurnedAThirdOfAWayLooksTheSameAndOneTurnedHalfWayHangsOnItsPoint()
        {
            Assert.AreEqual(50f, MinimapLabelPlacement.ShapeBottomBelowCentre(false, new Vector2(340f, 340f), 100f, 120f), 1e-3f);
            Assert.AreEqual(50f, MinimapLabelPlacement.ShapeBottomBelowCentre(false, new Vector2(340f, 340f), 100f, -240f), 1e-3f);
            Assert.AreEqual(100f, MinimapLabelPlacement.ShapeBottomBelowCentre(false, new Vector2(340f, 340f), 100f, 60f), 1e-3f);
        }

        [Test]
        public void ARectangleEndsHalfItsHeightBelowItsCentreAndHalfItsWidthWhenTurnedOnItsSide()
        {
            Assert.AreEqual(50f, MinimapLabelPlacement.ShapeBottomBelowCentre(true, new Vector2(200f, 100f), 100f, 0f), 1e-3f);
            Assert.AreEqual(100f, MinimapLabelPlacement.ShapeBottomBelowCentre(true, new Vector2(200f, 100f), 100f, 90f), 1e-3f);
            Assert.AreEqual(50f, MinimapLabelPlacement.ShapeBottomBelowCentre(true, new Vector2(200f, 100f), 100f, 180f), 1e-3f);
        }

        [Test]
        public void TheLabelHangsCentredUnderTheShapesLowestPointByTheGap()
        {
            var box = new Rect(-364f, -364f, 340f, 340f); // centre (-194, -194)
            Vector2 top = MinimapLabelPlacement.LabelTop(box, 50f, 8f);
            Assert.AreEqual(-194f, top.x, 1e-3f);
            Assert.AreEqual(-252f, top.y, 1e-3f);
        }

        [Test]
        public void UnderAnApexUpTriangleTheLabelSitsAboveTheBoxBottom()
        {
            var box = new Rect(-364f, -364f, 340f, 340f);
            float bottom = MinimapLabelPlacement.ShapeBottomBelowCentre(false, box.size, 169f, 0f);
            Assert.Greater(MinimapLabelPlacement.LabelTop(box, bottom, 8f).y, box.yMin);
        }

        [Test]
        public void TheTriangleRadiusIsTheDrawnTrianglesShareOfTheMinimapSize()
        {
            // The generated triangle's corners stand 2 px in from a 512 px sprite's edge.
            Assert.AreEqual(254f / 512f, GeneratedSprites.CentredTriangleRadiusFraction, 1e-5f);
        }

        [Test]
        public void TheHelperHangsTheLabelFromTheTopRightCorner()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var go = new GameObject("minimap label test", typeof(RectTransform));
            try
            {
                theme.minimapCornerMargin = 20f;
                theme.minimapFrameWidth = 4f;
                theme.minimapCornerSize = 300f;
                theme.minimapLinesGap = 6f;
                var rect = (RectTransform)go.transform;
                MinimapLabelPlacement.PlaceUnder(rect, theme, 250f, 40f);
                Assert.AreEqual(Vector2.one, rect.anchorMin);
                Assert.AreEqual(Vector2.one, rect.anchorMax);
                Assert.AreEqual(new Vector2(0.5f, 1f), rect.pivot);
                Assert.AreEqual(new Vector2(250f, 40f), rect.sizeDelta);
                Rect box = HudScreenLayout.CornerMinimapRect(20f, 4f, 300f);
                float expectedBottom = MinimapView.CornerShapeBottomBelowCentre(theme);
                Vector2 expected = MinimapLabelPlacement.LabelTop(box, expectedBottom, 6f);
                Assert.AreEqual(expected.x, rect.anchoredPosition.x, 1e-3f);
                Assert.AreEqual(expected.y, rect.anchoredPosition.y, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void WithNoCornerMapBuiltTheShapeIsTakenToFillItsBox()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            try
            {
                theme.minimapCornerSize = 300f;
                Assert.IsNull(MinimapView.Local);
                Assert.AreEqual(150f, MinimapView.CornerShapeBottomBelowCentre(theme), 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheLookUsesTheLinesFontOrTheHudFontAndTheHudOutline()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var go = new GameObject("minimap label look test", typeof(RectTransform));
            Material material = null;
            try
            {
                var text = go.AddComponent<TMPro.TextMeshProUGUI>();
                theme.textOutlineWidth = 0.3f;
                material = MinimapLabelPlacement.ApplyLook(text, theme);
                Assert.IsNotNull(material);
                Assert.AreSame(material, text.fontSharedMaterial);
                Assert.AreEqual(0.3f, material.GetFloat(TMPro.ShaderUtilities.ID_OutlineWidth), 1e-4f);
                Assert.AreEqual(TMPro.FontStyles.Bold, text.fontStyle & TMPro.FontStyles.Bold);
                Assert.IsFalse(text.raycastTarget);
            }
            finally
            {
                if (material != null) Object.DestroyImmediate(material);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void EveryLineEndsUpInOneFontWhateverFontItWasBuiltWith()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var go = new GameObject("minimap label font test", typeof(RectTransform));
            TMPro.TMP_FontAsset other = AnotherFont();
            Assert.IsNotNull(other, "the project has a second font");
            var made = new System.Collections.Generic.List<Material>();
            try
            {
                var text = go.AddComponent<TMPro.TextMeshProUGUI>();
                text.font = other; // a line built in another font (Dominion's lines start in the display font)
                made.Add(MinimapLabelPlacement.ApplyLook(text, theme));
                Assert.AreSame(TMPro.TMP_Settings.defaultFontAsset, text.font, "no lines font and no HUD font: the HUD's default font");

                theme.minimapLinesFont = other;
                made.Add(MinimapLabelPlacement.ApplyLook(text, theme));
                Assert.AreSame(other, text.font, "the lines font wins");
            }
            finally
            {
                foreach (Material m in made) if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(theme);
            }
        }

        // Any project font that is not TextMeshPro's default (the fonts are structure here, not the look).
        private static TMPro.TMP_FontAsset AnotherFont()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font != TMPro.TMP_Settings.defaultFontAsset) return font;
            }
            return null;
        }
    }
}
