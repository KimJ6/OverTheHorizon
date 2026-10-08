using Unity.Jobs;

namespace OverTheHorizon.Simulation
{
    /// <summary>Register every job reading step-owned buffers. ECS Dependency must also be maintained.</summary>
    public sealed class StepJobFence
    {
        private JobHandle handle;
        private bool pending;
        public void Register(JobHandle job)
        { handle = pending ? JobHandle.CombineDependencies(handle, job) : job; pending = true; }
        public void Complete()
        { if (pending) handle.Complete(); handle = default; pending = false; }
    }
}
