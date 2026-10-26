using System.Reflection;
using NUnit.Framework;

namespace Overpower.Tests
{
    /// <summary>The "game calls the tested rule" reader itself (IlWiring): scoped to a named method, a call made in another method of the same class
    /// does not count, and a call inside a lambda or an iterator of the named method does.</summary>
    public class IlWiringTests
    {
        private class Probe
        {
            public void CallsTheTarget() => Target();
            public void DoesNotCallTheTarget() { }
            public void CallsItInALambda() { System.Action later = () => Target(); later(); }
            public System.Collections.Generic.IEnumerable<int> CallsItInAnIterator() { Target(); yield return 1; }
            public static void Target() { }
            public bool CallsAnotherAssembly() => string.IsNullOrEmpty("x");
            public static bool Flag() => true;
            public void BranchesOnTheAnswer() { if (!Flag()) field = 1; }
            public void ThrowsTheAnswerAway() { Flag(); field = 2; }
            public int field;
            public bool flagField;
            public void BranchesOnAnotherLocal() { bool earlier = field > 5; bool answer = Flag(); if (earlier) field = 1; if (answer) field = 2; field += 0; }
            public void StoresFalse() { flagField = false; }
            public void StoresTrue() { flagField = true; }
            public void StoresTheField() { field = 3; }
            public int ReadsTheField() => field;
            public void HandsTheTargetOverAsADelegate() { System.Action later = Target; later(); }
            public static void Takes(int n, bool b = true) { }
            public void PassesFalse() => Takes(field, false);
            public void LeavesTheDefault() => Takes(field);
            public void PassesTrueThenFalse() { Takes(1, true); Takes(2, false); }
            public void PassesItsOwn(string s, bool b) => Takes(field, b);
            public void PassesAField(bool b) => Takes(field, flagField);
        }

        [Test] public void TheLastBoolArgumentOfEveryCallIsRead()
        {
            MethodInfo takes = typeof(Probe).GetMethod(nameof(Probe.Takes));
            Assert.IsTrue(IlWiring.CallsEndingWithBool(typeof(Probe), nameof(Probe.PassesFalse), takes, false));
            Assert.IsFalse(IlWiring.CallsEndingWithBool(typeof(Probe), nameof(Probe.PassesFalse), takes, true));
            Assert.IsTrue(IlWiring.CallsEndingWithBool(typeof(Probe), nameof(Probe.LeavesTheDefault), takes, true), "a left-out default is written by the compiler");
            Assert.IsFalse(IlWiring.CallsEndingWithBool(typeof(Probe), nameof(Probe.PassesTrueThenFalse), takes, true), "every call must match, not just one");
            Assert.IsFalse(IlWiring.CallsEndingWithBool(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), takes, true), "no call is not a pass");
            Assert.IsTrue(IlWiring.CallsEndingWithOwnParameter(typeof(Probe), nameof(Probe.PassesItsOwn), takes, 1));
            Assert.IsFalse(IlWiring.CallsEndingWithOwnParameter(typeof(Probe), nameof(Probe.PassesAField), takes, 0), "a field is not the parameter");
        }

        private static readonly MethodInfo Target = typeof(Probe).GetMethod(nameof(Probe.Target));

        [Test] public void AMethodNameThatIsNotThereIsAnErrorNotANo() =>
            Assert.Throws<System.ArgumentException>(() => IlWiring.Uses(typeof(Probe), "RenamedAway", Target),
                "a negative check on a renamed method must fail, not pass for ever");

        [Test] public void AMethodThatCallsTheTargetIsFound() =>
            Assert.IsTrue(IlWiring.Uses(typeof(Probe), nameof(Probe.CallsTheTarget), Target));

        [Test] public void AnotherMethodOfTheSameClassCallingTheTargetDoesNotCount()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Probe), Target), "the whole-class check would pass");
            Assert.IsFalse(IlWiring.Uses(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), Target), "but this method never calls it");
        }

        [Test] public void ACallInsideALambdaOrAnIteratorOfTheNamedMethodCounts()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(Probe), nameof(Probe.CallsItInALambda), Target));
            Assert.IsTrue(IlWiring.Uses(typeof(Probe), nameof(Probe.CallsItInAnIterator), Target));
        }

        [Test] public void AStoreIsFoundOnlyWhereTheFieldIsWritten()
        {
            FieldInfo field = typeof(Probe).GetField(nameof(Probe.field));
            Assert.IsTrue(IlWiring.Stores(typeof(Probe), nameof(Probe.StoresTheField), field));
            Assert.IsFalse(IlWiring.Stores(typeof(Probe), nameof(Probe.ReadsTheField), field), "a read is not a store");
            Assert.IsFalse(IlWiring.Stores(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), field));
        }

        [Test] public void ACallWhoseAnswerIsThrownAwayIsNotADecision()
        {
            MethodInfo flag = typeof(Probe).GetMethod(nameof(Probe.Flag));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(Probe), nameof(Probe.BranchesOnTheAnswer), flag));
            Assert.IsFalse(IlWiring.ResultDecidesABranch(typeof(Probe), nameof(Probe.ThrowsTheAnswerAway), flag), "it calls the rule, but nothing follows from the answer");
            Assert.IsTrue(IlWiring.Uses(typeof(Probe), nameof(Probe.ThrowsTheAnswerAway), flag), "which the plain 'uses' check cannot tell");
        }

        [Test] public void ABranchOnAnotherLocalThanTheAnswersIsNotADecision()
        {
            // "earlier" is branched on straight after the answer was stored into its own local; the answer itself is branched on later, after other code
            MethodInfo flag = typeof(Probe).GetMethod(nameof(Probe.Flag));
            Assert.IsFalse(IlWiring.ResultDecidesABranch(typeof(Probe), nameof(Probe.BranchesOnAnotherLocal), flag));
        }

        [Test] public void StoresBoolSeesWhichConstantWasStored()
        {
            FieldInfo flagField = typeof(Probe).GetField(nameof(Probe.flagField));
            Assert.IsTrue(IlWiring.StoresBool(typeof(Probe), nameof(Probe.StoresFalse), flagField, false));
            Assert.IsFalse(IlWiring.StoresBool(typeof(Probe), nameof(Probe.StoresFalse), flagField, true), "false was stored, not true");
            Assert.IsTrue(IlWiring.StoresBool(typeof(Probe), nameof(Probe.StoresTrue), flagField, true));
            Assert.IsFalse(IlWiring.StoresBool(typeof(Probe), nameof(Probe.StoresTrue), flagField, false));
            Assert.IsFalse(IlWiring.StoresBool(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), flagField, false));
        }

        [Test] public void CallOffsetsListsEveryCallInOrder()
        {
            Assert.AreEqual(1, IlWiring.CallOffsets(typeof(Probe).GetMethod(nameof(Probe.CallsTheTarget)), Target).Count);
            Assert.AreEqual(0, IlWiring.CallOffsets(typeof(Probe).GetMethod(nameof(Probe.DoesNotCallTheTarget)), Target).Count);
        }

        [Test] public void AMethodOfAnotherAssemblyIsFoundByResolvingTheCall()
        {
            MethodInfo other = typeof(string).GetMethod(nameof(string.IsNullOrEmpty));
            Assert.Throws<System.ArgumentException>(() => IlWiring.Uses(typeof(Probe), nameof(Probe.CallsAnotherAssembly), other),
                "the byte compare cannot see across assemblies, so it refuses instead of answering a false that can never fail");
            Assert.Throws<System.ArgumentException>(() => IlWiring.Uses(typeof(Probe), other));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(Probe), nameof(Probe.CallsAnotherAssembly), other));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), other));
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(Probe), other), "type-wide");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(Probe), typeof(Probe).GetMethod(nameof(Probe.Target))), "type-wide, lambdas included");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(Probe), typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })));
        }

        [Test] public void AMethodHandedOverAsADelegateIsFoundAndACallIsNot()
        {
            // a listener added with a method group (slider.onValueChanged.AddListener(OnVolumeChanged)) is an ldftn, not a call
            Assert.IsTrue(IlWiring.HandsOver(typeof(Probe), nameof(Probe.HandsTheTargetOverAsADelegate), Target));
            Assert.IsFalse(IlWiring.HandsOver(typeof(Probe), nameof(Probe.CallsTheTarget), Target), "a direct call is not a subscription");
            Assert.IsFalse(IlWiring.HandsOver(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), Target));
        }
    }
}
