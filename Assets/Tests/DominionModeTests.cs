using ExitGames.Client.Photon;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Lobby;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 2: "is this room Dominion?" from the mode id in the room, with made-up definitions.</summary>
    public class DominionModeTests
    {
        private static GameModeDefinition Make(int id, GameModeFamily family, params int[] teams)
        {
            var d = ScriptableObject.CreateInstance<GameModeDefinition>();
            var so = new SerializedObject(d);
            so.FindProperty("id").intValue = id;
            so.FindProperty("family").enumValueIndex = (int)family;
            SerializedProperty arr = so.FindProperty("teams");
            arr.arraySize = teams.Length;
            for (int i = 0; i < teams.Length; i++) arr.GetArrayElementAtIndex(i).intValue = teams[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        private GameModeDefinition dominion, conquest;

        [SetUp] public void SetUp()
        {
            dominion = Make(40, GameModeFamily.Dominion, 0, 1, 2);
            conquest = Make(10, GameModeFamily.Conquest, 0, 1);
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(dominion);
            Object.DestroyImmediate(conquest);
        }

        private GameModeDefinition Lookup(int id) => id == 40 ? dominion : id == 10 ? conquest : null;

        [Test] public void ADominionModeIdIsDominion() =>
            Assert.IsTrue(DominionMode.IsDominion(new Hashtable { { LobbyKeys.Mode, 40 } }, Lookup));

        [Test] public void AConquestModeIdIsNot() =>
            Assert.IsFalse(DominionMode.IsDominion(new Hashtable { { LobbyKeys.Mode, 10 } }, Lookup));

        [Test] public void AMissingKeyIsNot() =>
            Assert.IsFalse(DominionMode.IsDominion(new Hashtable(), Lookup));

        [Test] public void AnUnknownIdOrAWrongTypeIsNot()
        {
            Assert.IsFalse(DominionMode.IsDominion(new Hashtable { { LobbyKeys.Mode, 99 } }, Lookup));
            Assert.IsFalse(DominionMode.IsDominion(new Hashtable { { LobbyKeys.Mode, "40" } }, Lookup));
            Assert.IsFalse(DominionMode.IsDominion(null, Lookup));
        }

        [Test] public void TheTeamsComeFromTheDefinition()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, DominionMode.TeamsOf(new Hashtable { { LobbyKeys.Mode, 40 } }, Lookup));
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionMode.TeamsOf(new Hashtable { { LobbyKeys.Mode, 10 } }, Lookup));
            Assert.AreEqual(0, DominionMode.TeamsOf(new Hashtable(), Lookup).Length);
        }
    }
}
