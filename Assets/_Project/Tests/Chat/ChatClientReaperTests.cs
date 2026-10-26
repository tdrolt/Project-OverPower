using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The chat's reaper is never made while the game quits or Play Mode closes (Unity's "Some objects were not cleaned up when
    /// closing the scene" warning named it); a closing chat client is still told to disconnect then.</summary>
    public class ChatClientReaperTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ReaperName = "Chat client reaper";

        [Test]
        public void TheHelperIsMadeOnlyWhenMissingAndNeverWhileQuitting()
        {
            Assert.IsTrue(ChatClientReaper.HelperNeeded(false, false), "no reaper yet, the game running: make one");
            Assert.IsFalse(ChatClientReaper.HelperNeeded(true, false), "one is enough");
            Assert.IsFalse(ChatClientReaper.HelperNeeded(false, true), "quitting: nothing is made while the scene is torn down");
            Assert.IsFalse(ChatClientReaper.HelperNeeded(true, true));
        }

        [Test]
        public void EnsureAsksTheRuleAndQuittingIsWatchedFromTheStart()
        {
            MethodInfo rule = typeof(ChatClientReaper).GetMethod(nameof(ChatClientReaper.HelperNeeded));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(ChatClientReaper), nameof(ChatClientReaper.Ensure), rule), "Ensure makes the helper only on the rule's answer");

            MethodInfo watch = typeof(ChatClientReaper).GetMethod("WatchQuitting", Any);
            Assert.IsNotNull(watch);
            var start = watch.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(start, "subscribed as the game starts, before any chat connects");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, start.loadType);
            Assert.IsTrue(IlWiring.HandsOver(typeof(ChatClientReaper), "WatchQuitting", typeof(ChatClientReaper).GetMethod("OnQuitting", Any)),
                "Application.quitting raises the flag (it also fires when Play Mode stops in the Editor)");

            System.Type chatClient = System.Type.GetType("Photon.Chat.ChatClient, PhotonChat");
            Assert.IsNotNull(chatClient);
            MethodInfo disconnect = chatClient.GetMethod("Disconnect");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(ChatClientReaper), nameof(ChatClientReaper.Close), disconnect),
                "a closing client is still told to disconnect, quitting or not");
        }

        [Test]
        public void EnsureWhileQuittingMakesNoHelperObject()
        {
            FieldInfo quitting = typeof(ChatClientReaper).GetField("quitting", Any);
            FieldInfo instance = typeof(ChatClientReaper).GetField("instance", Any);
            Assert.IsNotNull(quitting);
            int before = Live();
            try
            {
                typeof(ChatClientReaper).GetMethod("OnQuitting", Any).Invoke(null, null);
                ChatClientReaper.Ensure();
                Assert.AreEqual(before, Live(), "no reaper is made while the game quits");
                Assert.IsFalse(ChatClientReaper.Busy);
            }
            finally
            {
                quitting.SetValue(null, false);
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name == ReaperName && !UnityEditor.EditorUtility.IsPersistent(g)).ToList())
                    Object.DestroyImmediate(go);
                instance.SetValue(null, null);
            }
        }

        [Test]
        public void CloseWhileQuittingTellsTheClientToGoAndMakesNoHelperObject()
        {
            // The path the game takes at shutdown: the chat's OnDisable / OnDestroy call Close, not Ensure.
            FieldInfo quitting = typeof(ChatClientReaper).GetField("quitting", Any);
            FieldInfo instance = typeof(ChatClientReaper).GetField("instance", Any);
            System.Type chatClient = System.Type.GetType("Photon.Chat.ChatClient, PhotonChat");
            Assert.IsNotNull(chatClient);
            object client = System.Activator.CreateInstance(chatClient, null, ExitGames.Client.Photon.ConnectionProtocol.Udp);
            MethodInfo close = typeof(ChatClientReaper).GetMethod(nameof(ChatClientReaper.Close));
            int before = Live();
            try
            {
                typeof(ChatClientReaper).GetMethod("OnQuitting", Any).Invoke(null, null);
                Assert.DoesNotThrow(() => close.Invoke(null, new[] { client }), "closing a chat while the game quits");
                Assert.AreEqual(before, Live(), "no reaper is made while the game quits");
                Assert.IsFalse(ChatClientReaper.Busy, "nothing is left waiting to close");
            }
            finally
            {
                quitting.SetValue(null, false);
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name == ReaperName && !UnityEditor.EditorUtility.IsPersistent(g)).ToList())
                    Object.DestroyImmediate(go);
                instance.SetValue(null, null);
            }
        }

        private static int Live() =>
            Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name == ReaperName && !UnityEditor.EditorUtility.IsPersistent(g));
    }
}
