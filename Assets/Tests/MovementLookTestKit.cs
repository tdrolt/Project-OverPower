using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A literal theme and look for the movement-look tests: the ability's row in the table is Gold, so a view that reads the row
    /// paints gold and one that hard-codes silver is caught. Everything made here is destroyed by Dispose.</summary>
    internal sealed class MovementLookTestKit : System.IDisposable
    {
        public const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static readonly Color Gold = new Color(0.81f, 0.52f, 0.23f, 1f);
        public static readonly Color Silver = new Color(0.31f, 0.62f, 0.44f, 1f);

        public readonly UiTheme Theme;
        public readonly ArenaLook Look;
        public readonly Material Fx;
        private readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        public MovementLookTestKit(int abilityId, LookShape shape)
        {
            Theme = Make(ScriptableObject.CreateInstance<UiTheme>());
            Look = Make(ScriptableObject.CreateInstance<ArenaLook>());
            Fx = Make(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")));
            Theme.immuneBarColor = Gold;
            Theme.movementSilverColor = Silver;
            Theme.arenaLook = Look;
            Look.fxAlpha = Fx;
            ((List<AbilityLook>)typeof(ArenaLook).GetField("abilityLooks", Any).GetValue(Look)).Add(new AbilityLook(abilityId, shape, LookFamily.Gold));
        }

        public T Make<T>(T thing) where T : UnityEngine.Object
        {
            made.Add(thing);
            return thing;
        }

        public GameObject NewObject(string name) => Make(new GameObject(name));

        /// <summary>Binds the definition id onto a module the way the runner would, without a whole ability asset (its OnValidate wants one).</summary>
        public void GiveDefinition(AbilityModule module, int id)
        {
            AbilityDefinition definition = Make(ScriptableObject.CreateInstance<AbilityDefinition>());
            typeof(AbilityDefinition).GetField("id", Any).SetValue(definition, id);
            typeof(AbilityModule).GetProperty(nameof(AbilityModule.Definition)).GetSetMethod(true).Invoke(module, new object[] { definition });
        }

        public static void SetTheme(Component component, UiTheme theme)
        {
            var so = new UnityEditor.SerializedObject(component);
            so.FindProperty("theme").objectReferenceValue = theme;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Edit mode never sends OnDestroy to a plain MonoBehaviour, so it is sent the way the game's Destroy does, then the object goes.</summary>
        public static void DestroyLikeTheGame(GameObject go)
        {
            if (go == null)
                return;
            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.GetType().GetMethod("OnDestroy", Any)?.Invoke(behaviour, null);
            UnityEngine.Object.DestroyImmediate(go);
        }

        public static int Live(string name) =>
            Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name == name && !UnityEditor.EditorUtility.IsPersistent(g));

        public static void AssertRgb(Color expected, Color actual, string what)
        {
            NUnit.Framework.Assert.AreEqual(expected.r, actual.r, 1f / 255f, what + " (red)");
            NUnit.Framework.Assert.AreEqual(expected.g, actual.g, 1f / 255f, what + " (green)");
            NUnit.Framework.Assert.AreEqual(expected.b, actual.b, 1f / 255f, what + " (blue)");
        }

        public void Dispose()
        {
            for (int i = made.Count - 1; i >= 0; i--)
                if (made[i] != null)
                {
                    if (made[i] is GameObject go)
                        DestroyLikeTheGame(go);
                    else
                        UnityEngine.Object.DestroyImmediate(made[i]);
                }
        }
    }
}
