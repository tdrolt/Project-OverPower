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
            public int field;
            public void StoresTheField() { field = 3; }
            public int ReadsTheField() => field;
        }

        private static readonly MethodInfo Target = typeof(Probe).GetMethod(nameof(Probe.Target));

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

        [Test] public void CallOffsetsListsEveryCallInOrder()
        {
            Assert.AreEqual(1, IlWiring.CallOffsets(typeof(Probe).GetMethod(nameof(Probe.CallsTheTarget)), Target).Count);
            Assert.AreEqual(0, IlWiring.CallOffsets(typeof(Probe).GetMethod(nameof(Probe.DoesNotCallTheTarget)), Target).Count);
        }

        [Test] public void AMethodOfAnotherAssemblyIsFoundByResolvingTheCall()
        {
            MethodInfo other = typeof(string).GetMethod(nameof(string.IsNullOrEmpty));
            Assert.IsFalse(IlWiring.Uses(typeof(Probe), nameof(Probe.CallsAnotherAssembly), other), "the byte compare cannot see across assemblies");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(Probe), nameof(Probe.CallsAnotherAssembly), other));
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(Probe), nameof(Probe.DoesNotCallTheTarget), other));
        }

        [Test] public void AMethodNameThatDoesNotExistFindsNothing() =>
            Assert.IsFalse(IlWiring.Uses(typeof(Probe), "NoSuchMethod", Target));
    }
}
