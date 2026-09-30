using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Overpower.Tests
{
    /// <summary>The Vent action (R key), added to the Gameplay map for the overheat vent. Loads the
    /// real InputActionAsset (the same asset PlayerInputRouter reads) rather than just grepping the
    /// JSON, so this also catches "the action exists but nothing binds it right" - and checks that
    /// nothing else in the project has claimed r for itself either.</summary>
    public class VentInputActionTests
    {
        private const string AssetPath = "Assets/Gameplay/Input/OverpowerControls.inputactions";

        private static InputActionAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(asset, AssetPath);
            return asset;
        }

        [Test]
        public void TheGameplayMapHasAVentActionBoundToR()
        {
            InputActionAsset asset = Load();
            InputActionMap gameplay = asset.FindActionMap("Gameplay", throwIfNotFound: true);
            InputAction vent = gameplay.FindAction("Vent");
            Assert.IsNotNull(vent, "Gameplay map has no Vent action");

            bool boundToR = false;
            foreach (var binding in vent.bindings)
            {
                if (binding.path == "<Keyboard>/r")
                    boundToR = true;
            }
            Assert.IsTrue(boundToR, "Vent is not bound to <Keyboard>/r");
        }

        [Test]
        public void NothingElseInTheProjectBindsTheRKey()
        {
            InputActionAsset asset = Load();
            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    if (action.name == "Vent")
                        continue;

                    foreach (var binding in action.bindings)
                    {
                        Assert.AreNotEqual("<Keyboard>/r", binding.path, $"{map.name}/{action.name} also binds r");
                    }
                }
            }
        }

        [Test]
        public void TheAssetFileOnDiskListsTheVentAction()
        {
            string json = File.ReadAllText(AssetPath);
            StringAssert.Contains("\"name\": \"Vent\"", json);
            StringAssert.Contains("<Keyboard>/r", json);
        }
    }
}
