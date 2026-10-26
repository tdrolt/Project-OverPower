using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>ArenaLook is the one asset for the world-look numbers: every serialized field must be written into the
    /// YAML (a field added in code but never saved reads its code default and an Inspector edit would not stick to it) and
    /// carry a full-sentence play-term tooltip.</summary>
    public class ArenaLookAssetTests
    {
        private const string AssetPath = "Assets/Gameplay/Config/ArenaLook.asset";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo[] SerializedFields(System.Type type) => type.GetFields(Instance)
            .Where(f => !f.IsNotSerialized && (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)).ToArray();

        [Test]
        public void EveryFieldIsInTheYamlWithATooltip()
        {
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<ArenaLook>(AssetPath), AssetPath);
            string yaml = File.ReadAllText(AssetPath);
            FieldInfo[] fields = SerializedFields(typeof(ArenaLook));
            Assert.GreaterOrEqual(fields.Length, 2, "the look table and the bubble look at least");
            foreach (FieldInfo f in fields)
            {
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                Assert.NotNull(tip, f.Name + " has no tooltip");
                Assert.Greater(tip.tooltip.Length, 40, f.Name + " tooltip is not a full sentence");
                StringAssert.EndsWith(".", tip.tooltip.TrimEnd(), f.Name + " tooltip does not end like a sentence");
                StringAssert.Contains("\n  " + f.Name + ":", yaml.Replace("\r\n", "\n"), f.Name + " is not in the YAML");
            }
        }

        [Test]
        public void EveryFieldOfASurfaceLookIsInTheYamlWithATooltip()
        {
            string yaml = File.ReadAllText(AssetPath).Replace("\r\n", "\n");
            FieldInfo[] fields = SerializedFields(typeof(SurfaceLook));
            Assert.AreEqual(3, fields.Length, "tint, gloss and sun shine");
            foreach (FieldInfo f in fields)
            {
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                Assert.NotNull(tip, f.Name + " has no tooltip");
                Assert.Greater(tip.tooltip.Length, 40, f.Name);
                StringAssert.EndsWith(".", tip.tooltip.TrimEnd(), f.Name + " tooltip does not end like a sentence");
                foreach (string surface in new[] { "floorSurface", "wallSurface", "blockSurface", "barrierSurface" })
                    StringAssert.IsMatch("\n  " + surface + ":\n(    .*\n)*?    " + f.Name + ":", yaml, surface + "." + f.Name + " is not in the YAML");
            }
        }

        [Test]
        public void EveryFieldOfOneTableEntryIsInTheYamlWithATooltip()
        {
            string yaml = File.ReadAllText(AssetPath).Replace("\r\n", "\n");
            FieldInfo[] fields = SerializedFields(typeof(AbilityLook));
            Assert.AreEqual(3, fields.Length, "id, shape and family");
            foreach (FieldInfo f in fields)
            {
                var tip = f.GetCustomAttribute<TooltipAttribute>();
                Assert.NotNull(tip, f.Name + " has no tooltip");
                Assert.Greater(tip.tooltip.Length, 40, f.Name);
                StringAssert.Contains(f.Name + ":", yaml, f.Name + " is not in the YAML");
            }
        }
    }
}
