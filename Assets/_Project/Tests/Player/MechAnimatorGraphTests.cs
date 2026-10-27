using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The mech's Animator graph against the bools animScript sets (idle is always !run): from any state, the graph settles on Shoot
    /// while shoot is on, else Run while running, else Idle, and never has two exits true at once (two at once makes the states loop).</summary>
    public class MechAnimatorGraphTests
    {
        private const string ControllerPath = "Assets/ThirdParty/SciFiWarriorPBRHPPolyart/Animators/SciFiWarrior.controller";
        private const string IdleState = "Idle_gunMiddle_AR";
        private const string RunState = "Run_gunMiddle_AR";
        internal const string ShootState = "Shoot_SingleShot_AR";

        private static AnimatorStateMachine Layer()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, ControllerPath);
            return controller.layers[0].stateMachine;
        }

        private static AnimatorState State(AnimatorStateMachine sm, string name)
        {
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
            Assert.IsNotNull(state, name);
            return state;
        }

        // Triggers (the pack's unused Dash) are never set by the game, so they read false here.
        private static bool Holds(AnimatorCondition c, bool run, bool shoot)
        {
            bool value;
            switch (c.parameter)
            {
                case "run": value = run; break;
                case "idle": value = !run; break;
                case "shoot": value = shoot; break;
                default: value = false; break;
            }
            if (c.mode == AnimatorConditionMode.If) return value;
            if (c.mode == AnimatorConditionMode.IfNot) return !value;
            Assert.Fail("unexpected condition mode " + c.mode + " on " + c.parameter);
            return false;
        }

        private static List<AnimatorStateTransition> Open(AnimatorState state, bool run, bool shoot) =>
            state.transitions.Where(t => !t.mute && !t.hasExitTime && t.destinationState != null && t.conditions.All(c => Holds(c, run, shoot))).ToList();

        private static string Wanted(bool run, bool shoot) => shoot ? ShootState : run ? RunState : IdleState;

        private static readonly (bool run, bool shoot)[] Inputs = { (false, false), (true, false), (false, true), (true, true) };

        [Test]
        public void NoStateHasTwoExitsTrueAtOnce()
        {
            AnimatorStateMachine sm = Layer();
            Assert.IsEmpty(sm.anyStateTransitions, "an Any State exit would compete with every state's own");
            foreach (string name in new[] { IdleState, RunState, ShootState })
                foreach (var (run, shoot) in Inputs)
                {
                    List<AnimatorStateTransition> open = Open(State(sm, name), run, shoot);
                    Assert.LessOrEqual(open.Count, 1, $"{name} with run={run} shoot={shoot}: " +
                                                     string.Join(", ", open.Select(t => t.destinationState.name)));
                }
        }

        [Test]
        public void EveryStateSettlesOnTheOneTheBoolsAskFor()
        {
            AnimatorStateMachine sm = Layer();
            foreach (string start in new[] { IdleState, RunState, ShootState })
                foreach (var (run, shoot) in Inputs)
                {
                    AnimatorState at = State(sm, start);
                    for (int step = 0; step < 3; step++)
                    {
                        List<AnimatorStateTransition> open = Open(at, run, shoot);
                        if (open.Count == 0) break;
                        at = open[0].destinationState;
                    }
                    Assert.AreEqual(Wanted(run, shoot), at.name, $"from {start} with run={run} shoot={shoot}");
                    Assert.IsEmpty(Open(at, run, shoot), $"{at.name} does not stay put with run={run} shoot={shoot}");
                }
        }

        [Test]
        public void TheShootMotionPlaysOnceAndHoldsItsLastFrame()
        {
            var clip = State(Layer(), ShootState).motion as AnimationClip;
            Assert.IsNotNull(clip);
            Assert.IsFalse(clip.isLooping, "a looping shoot clip replays on its own between shots, out of step with them");
        }

        // Only a shot that leaves (animScript.OnShotShown) may start the recoil. The bool alone can still turn on without one: a remote copy's
        // streamed bool landing before the shot's RPC, or a stun lifting inside a shot's pose.
        [Test]
        public void TheShootBoolOnlyRaisesTheGunAndNeverStartsTheRecoil()
        {
            AnimatorStateMachine sm = Layer();
            AnimatorState shoot = State(sm, ShootState);
            var into = sm.states.SelectMany(s => s.state.transitions).Where(t => t.destinationState == shoot).ToList();
            Assert.IsNotEmpty(into);
            foreach (AnimatorStateTransition t in into)
                Assert.GreaterOrEqual(t.offset, 0.9f, "a transition into Shoot must enter at the motion's end pose, not replay it");
        }

        [Test]
        public void TheShootStatesSpeedIsAFloatEachClientSetsForItself()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorState shoot = State(controller.layers[0].stateMachine, ShootState);
            Assert.IsTrue(shoot.speedParameterActive, "the Shoot state's speed must follow a parameter");
            AnimatorControllerParameter p = controller.parameters.FirstOrDefault(x => x.name == shoot.speedParameter);
            Assert.IsNotNull(p, shoot.speedParameter);
            Assert.AreEqual(AnimatorControllerParameterType.Float, p.type);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Multiplayer Player.prefab");
            var sync = prefab.GetComponentInChildren<Photon.Pun.PhotonAnimatorView>(true);
            Assert.IsNotNull(sync);
            Assert.AreEqual(Photon.Pun.PhotonAnimatorView.SynchronizeType.Disabled, sync.GetParameterSynchronizeType(shoot.speedParameter),
                "every client works the speed out from the shot it saw; streaming it would lag behind the shot");
        }

        [Test]
        public void TheMechsBoolsAreStillBoolsForTheNetworkSync()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            foreach (string name in new[] { "run", "idle", "shoot" })
            {
                AnimatorControllerParameter p = controller.parameters.FirstOrDefault(x => x.name == name);
                Assert.IsNotNull(p, name);
                Assert.AreEqual(AnimatorControllerParameterType.Bool, p.type, name + " must stay a bool: PhotonAnimatorView syncs bools, not triggers");
            }
        }
    }
}
