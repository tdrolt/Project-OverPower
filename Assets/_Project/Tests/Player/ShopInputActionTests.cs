using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Overpower.Tests
{
    /// <summary>The Shop action is one key, B, and nothing else in the project shares it (two actions on one key would
    /// open the shop on the other action's press). Loads the real InputActionAsset PlayerInputRouter reads.</summary>
    public class ShopInputActionTests
    {
        private const string AssetPath = "Assets/_Project/Shared/Input/OverpowerControls.inputactions";
        private const string ShopKey = "<Keyboard>/b";

        private static InputActionAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(asset, AssetPath);
            return asset;
        }

        [Test]
        public void TheShopActionHasExactlyOneBindingAndItIsTheBKey()
        {
            InputAction shop = Load().FindActionMap("Gameplay", throwIfNotFound: true).FindAction("Shop");
            Assert.IsNotNull(shop, "Gameplay map has no Shop action");
            Assert.AreEqual(1, shop.bindings.Count, "the Shop action is bound once");
            Assert.AreEqual(ShopKey, shop.bindings[0].path);
        }

        [Test]
        public void NothingElseInTheProjectBindsTheBKey()
        {
            foreach (InputActionMap map in Load().actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    if (map.name == "Gameplay" && action.name == "Shop")
                        continue;

                    foreach (InputBinding binding in action.bindings)
                        Assert.AreNotEqual(ShopKey, binding.path, $"{map.name}/{action.name} also binds b");
                }
            }
        }
    }
}
