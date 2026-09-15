using Unity.Profiling;

namespace SLVR.UIMotion
{
    /// <summary>Profiler markers shared by core motion paths.</summary>
    public static class UiMotionProfiler
    {
        public static readonly ProfilerMarker CreateTween = new ProfilerMarker("SLVR.UIMotion.CreateTween");
        public static readonly ProfilerMarker ReplaceChannel = new ProfilerMarker("SLVR.UIMotion.ReplaceChannel");
        public static readonly ProfilerMarker BudgetAdmission = new ProfilerMarker("SLVR.UIMotion.BudgetAdmission");
        public static readonly ProfilerMarker Lifecycle = new ProfilerMarker("SLVR.UIMotion.Lifecycle");
    }
}
