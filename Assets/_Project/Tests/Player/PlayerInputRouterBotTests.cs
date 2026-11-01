using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The bot's way into PlayerInputRouter (BeginBotDrive, SetBotInput, PressBotVent): a held flag gives the same press and release
    /// events a key gives, through the same gates; only being dead stops a bot (the host's chat box and panels are its human's); and
    /// PlayerMotor takes the bot's world move as it is, never turned by the host's camera. Built in edit mode, so Awake never runs and
    /// there is no device map: the bot path must not need one.
    /// </summary>
    public class PlayerInputRouterBotTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const float Tolerance = 1e-5f;

        private GameObject body;
        private GameObject cameraObject;
        private PlayerInputRouter router;
        private int primaryPressed, primaryReleased, attachmentPressed, ultimatePressed, mobilityPressed, mobilityReleased, ventPressed;

        [SetUp]
        public void SetUp()
        {
            primaryPressed = primaryReleased = attachmentPressed = ultimatePressed = mobilityPressed = mobilityReleased = ventPressed = 0;
            body = new GameObject("Bot body");
            router = body.AddComponent<PlayerInputRouter>();
            router.PrimaryPressed += () => primaryPressed++;
            router.PrimaryReleased += () => primaryReleased++;
            router.AttachmentPressed += () => attachmentPressed++;
            router.UltimatePressed += () => ultimatePressed++;
            router.MobilityPressed += () => mobilityPressed++;
            router.MobilityReleased += () => mobilityReleased++;
            router.VentPressed += () => ventPressed++;
        }

        [TearDown]
        public void TearDown()
        {
            if (body != null) Object.DestroyImmediate(body);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }

        private void SetAlive(bool alive) =>
            typeof(PlayerInputRouter).GetMethod("HandleAliveChanged", Any).Invoke(router, new object[] { alive });

        [Test]
        public void ABotButtonGoingDownRaisesItsPressOnceAndComingUpRaisesItsRelease()
        {
            router.BeginBotDrive();

            router.SetBotInput(Vector3.zero, true, false, false, true);
            router.SetBotInput(Vector3.zero, true, false, false, true);
            Assert.AreEqual(1, primaryPressed, "held over two frames is one press, like a key");
            Assert.AreEqual(1, mobilityPressed);
            Assert.IsTrue(router.PrimaryHeld);
            Assert.IsTrue(router.MobilityHeld);

            router.SetBotInput(Vector3.zero, false, false, false, false);
            Assert.AreEqual(1, primaryReleased);
            Assert.AreEqual(1, mobilityReleased);
            Assert.IsFalse(router.PrimaryHeld);
            Assert.IsFalse(router.MobilityHeld);
        }

        [Test]
        public void TheAttachmentAndTheUltimateRaiseTheirPressAndReadAsHeldUntilLetGo()
        {
            router.BeginBotDrive();

            router.SetBotInput(Vector3.zero, false, true, true, false);
            Assert.AreEqual(1, attachmentPressed);
            Assert.AreEqual(1, ultimatePressed);
            Assert.IsTrue(router.AttachmentHeld);
            Assert.IsTrue(router.UltimateHeld);

            router.SetBotInput(Vector3.zero, false, false, false, false);
            Assert.IsFalse(router.AttachmentHeld);
            Assert.IsFalse(router.UltimateHeld);
            Assert.AreEqual(0, primaryPressed + primaryReleased + mobilityPressed + mobilityReleased, "no other button moved");
        }

        [Test]
        public void AListenerReadingHeldInsideThePressSeesTheButtonDown()
        {
            bool heldDuringPress = false;
            router.PrimaryPressed += () => heldDuringPress = router.PrimaryHeld;
            router.BeginBotDrive();

            router.SetBotInput(Vector3.zero, true, false, false, false);

            Assert.IsTrue(heldDuringPress, "a key reads as pressed inside its own press callback; a bot's button must too");
        }

        [Test]
        public void APanelHoldingInputFocusDoesNotStopABot()
        {
            router.SetToolFocus(new object(), true);
            router.BeginBotDrive();

            Assert.IsFalse(router.InputSuppressed);
            router.SetBotInput(Vector3.forward, true, false, false, false);
            Assert.AreEqual(1, primaryPressed);
            Assert.AreEqual(Vector3.forward, router.BotWorldMove);
        }

        [Test]
        public void APanelHoldingInputFocusStillStopsTheHumansInput()
        {
            router.SetToolFocus(new object(), true);

            Assert.IsTrue(router.InputSuppressed);
        }

        [Test]
        public void ADeadBotNeitherMovesNorPressesNorVents()
        {
            router.BeginBotDrive();
            SetAlive(false);

            router.SetBotInput(Vector3.right, true, true, true, true);
            router.PressBotVent();

            Assert.IsTrue(router.InputSuppressed);
            Assert.AreEqual(0, primaryPressed + attachmentPressed + ultimatePressed + mobilityPressed + ventPressed);
            Assert.AreEqual(Vector3.zero, router.BotWorldMove);
            Assert.AreEqual(Vector2.zero, router.MoveAxis);
            Assert.IsFalse(router.PrimaryHeld);
            Assert.IsFalse(router.MobilityHeld);
        }

        [Test]
        public void ABotsMoveIsFlatAndNeverLongerThanOne()
        {
            router.BeginBotDrive();

            router.SetBotInput(new Vector3(3f, 5f, 4f), false, false, false, false);

            Vector3 move = router.BotWorldMove;
            Assert.AreEqual(0f, move.y, Tolerance);
            Assert.AreEqual(0.6f, move.x, Tolerance);
            Assert.AreEqual(0.8f, move.z, Tolerance);
            Assert.AreEqual(0.6f, router.MoveAxis.x, Tolerance, "MoveAxis is the same move as (x, z)");
            Assert.AreEqual(0.8f, router.MoveAxis.y, Tolerance);
        }

        [Test]
        public void ABotVentPressRaisesVentPressed()
        {
            router.BeginBotDrive();

            router.PressBotVent();

            Assert.AreEqual(1, ventPressed);
        }

        [Test]
        public void BotInputIsIgnoredUntilTheBodyIsHandedToTheBot()
        {
            router.SetBotInput(Vector3.forward, true, true, true, true);
            router.PressBotVent();

            Assert.IsFalse(router.BotDriven);
            Assert.AreEqual(0, primaryPressed + attachmentPressed + ultimatePressed + mobilityPressed + ventPressed);
            Assert.AreEqual(Vector3.zero, router.BotWorldMove);
            Assert.IsFalse(router.PrimaryHeld);
        }

        [Test]
        public void ABotsMovementIsItsWorldMoveWhicheverWayTheHostsCameraFaces()
        {
            cameraObject = new GameObject("Host camera") { tag = "MainCamera" };
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.rotation = Quaternion.Euler(30f, 90f, 0f);
            PlayerMotor motor = body.AddComponent<PlayerMotor>();
            typeof(PlayerMotor).GetField("router", Any).SetValue(motor, router);
            router.BeginBotDrive();

            router.SetBotInput(Vector3.forward, false, false, false, false);

            Vector3 move = motor.MovementInput();
            Assert.AreEqual(0f, move.x, Tolerance, "a camera turned 90 degrees would make this sideways");
            Assert.AreEqual(1f, move.z, Tolerance);
        }
    }
}
