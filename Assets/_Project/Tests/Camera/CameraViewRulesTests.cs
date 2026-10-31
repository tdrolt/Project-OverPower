using NUnit.Framework;
using Overpower.UI;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 31 Oct (after the web playtest): a match starts with the camera at its farthest, the wheel zooms in down to the old
    /// start; Q and E held turn the view slowly around the player on top of the team's starting angle; the minimap stays fixed on that
    /// starting angle and your own marker is an arrowhead that turns with your aim.
    /// </summary>
    public class CameraViewRulesTests
    {
        [Test]
        public void AMatchStartsAtTheFarthestZoom() =>
            Assert.AreEqual(2f, CameraViewRules.StartZoom(0.5f, 2f));

        [TestCase(true, false, -1f, TestName = "Q alone turns left")]
        [TestCase(false, true, 1f, TestName = "E alone turns right")]
        [TestCase(true, true, 0f, TestName = "both keys cancel")]
        [TestCase(false, false, 0f, TestName = "no key, no turn")]
        public void QAndETurnOppositeWays(bool q, bool e, float expected) =>
            Assert.AreEqual(expected, CameraViewRules.TurnAxis(q, e));

        [Test]
        public void HoldingTurnsAtTheSetSpeed() =>
            Assert.AreEqual(30f, CameraViewRules.Turned(0f, 1f, 60f, 0.5f), 1e-4f);

        [Test]
        public void HoldingQTurnsBack() =>
            Assert.AreEqual(-15f, CameraViewRules.Turned(0f, -1f, 60f, 0.25f), 1e-4f);

        [Test]
        public void NoKeyKeepsTheTurn() =>
            Assert.AreEqual(42f, CameraViewRules.Turned(42f, 0f, 60f, 0.5f), 1e-4f);

        [Test]
        public void TheTurnWrapsRoundAFullCircle() =>
            Assert.AreEqual(-170f, CameraViewRules.Turned(170f, 1f, 60f, 1f / 3f), 1e-3f);

        [TestCase(false, false, true, TestName = "playing: Q/E turn")]
        [TestCase(true, false, false, TestName = "spectator seat: Q/E pick the player instead")]
        [TestCase(false, true, false, TestName = "typing or a panel open: no turn")]
        public void QAndETurnOnlyWhenTheyAreFree(bool spectatorSeat, bool keysBlocked, bool expected) =>
            Assert.AreEqual(expected, CameraViewRules.TurnsWithKeys(spectatorSeat, keysBlocked));

        [Test]
        public void TheScreenLooksAlongTheTeamAngleTurnedByThePlayer() =>
            Assert.AreEqual(150f, CameraViewRules.ScreenYaw(120f, 30f), 1e-4f);

        // ---- the arrowhead: one point in front, two at the back with a notch between them

        private const int Size = 128;

        [Test]
        public void TheArrowheadIsSolidNearItsPointAndInItsMiddle()
        {
            Assert.Greater(GeneratedSprites.ArrowheadAlpha(Size / 2f, Size - 8f, Size), 0.99f, "just below the point");
            Assert.Greater(GeneratedSprites.ArrowheadAlpha(Size / 2f, Size * 0.6f, Size), 0.99f, "the middle");
        }

        [Test]
        public void TheArrowheadIsSolidAtBothBackCorners()
        {
            Assert.Greater(GeneratedSprites.ArrowheadAlpha(12f, 12f, Size), 0.99f, "left back corner");
            Assert.Greater(GeneratedSprites.ArrowheadAlpha(Size - 12f, 12f, Size), 0.99f, "right back corner");
        }

        [Test]
        public void TheNotchBetweenTheBackCornersIsEmpty() =>
            Assert.Less(GeneratedSprites.ArrowheadAlpha(Size / 2f, 8f, Size), 0.01f);

        [Test]
        public void BesideThePointIsEmpty()
        {
            Assert.Less(GeneratedSprites.ArrowheadAlpha(4f, Size - 4f, Size), 0.01f);
            Assert.Less(GeneratedSprites.ArrowheadAlpha(Size - 4f, Size - 4f, Size), 0.01f);
        }

        // ---- Q and E in the controls

        private const string ControlsPath = "Assets/_Project/Shared/Input/OverpowerControls.inputactions";

        [TestCase("TurnCameraLeft", "<Keyboard>/q")]
        [TestCase("TurnCameraRight", "<Keyboard>/e")]
        public void TheGameplayMapBindsTheTurnKeys(string action, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            InputAction turn = asset.FindActionMap("Gameplay", throwIfNotFound: true).FindAction(action);
            Assert.IsNotNull(turn, action);
            Assert.AreEqual(path, turn.bindings[0].path);
        }

        // ---- the game asks the rules

        private static System.Reflection.MethodInfo Rule(string name) => typeof(CameraViewRules).GetMethod(name);

        [Test]
        public void TheCameraStartsFromTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "Start", Rule(nameof(CameraViewRules.StartZoom))));

        [Test]
        public void TheCameraTurnsWithTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "LateUpdate", Rule(nameof(CameraViewRules.Turned))));

        [Test]
        public void TheCameraAsksWhetherTheKeysAreFree() =>
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(CameraTracking), "LateUpdate", Rule(nameof(CameraViewRules.TurnsWithKeys))));

        [Test]
        public void TheScreenAngleComesFromTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(CameraTracking), "get_Yaw", Rule(nameof(CameraViewRules.ScreenYaw))));

        [Test]
        public void TheRouterReadsQAndEThroughTheRule() =>
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerInputRouter), "get_CameraTurnAxis", Rule(nameof(CameraViewRules.TurnAxis))));

        [Test]
        public void TheMinimapTurnsOnlyWithTheTeamAngle()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "ApplyYawIfChanged", typeof(CameraTracking).GetProperty(nameof(CameraTracking.MapYaw)).GetGetMethod()));
            Assert.IsFalse(IlWiring.Uses(typeof(MinimapView), "ApplyYawIfChanged", typeof(CameraTracking).GetProperty(nameof(CameraTracking.Yaw)).GetGetMethod()));
        }

        [Test]
        public void YourMarkerIsTheArrowhead() =>
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "TryBuild", typeof(GeneratedSprites).GetProperty(nameof(GeneratedSprites.Arrowhead)).GetGetMethod()));
    }
}
