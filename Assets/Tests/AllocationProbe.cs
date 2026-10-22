namespace Overpower.Tests
{
    /// <summary>Counts the managed allocations an action makes on this thread, from the profiler's GC.Alloc samples. Not
    /// System.GC.GetAllocatedBytesForCurrentThread: in the Editor's Mono it reads 0 whatever is allocated, so a check built on it can never fail.</summary>
    internal static class AllocationProbe
    {
        public static int Count(System.Action action)
        {
            UnityEngine.Profiling.Recorder allocations = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            allocations.enabled = false;
            allocations.FilterToCurrentThread();
            allocations.enabled = true;
            try
            {
                action();
            }
            finally
            {
                allocations.enabled = false;
                allocations.CollectFromAllThreads();
            }
            return allocations.sampleBlockCount;
        }
    }
}
