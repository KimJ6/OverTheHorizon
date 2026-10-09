using System;
using System.Runtime.ExceptionServices;
using Unity.Entities;
using Unity.Jobs;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    /// <summary>One mandatory, executor-owned stage. Register jobs before returning from OnStep.</summary>
    [DisableAutoCreation]
    public abstract partial class CombatStepSystem : SystemBase
    {
        private bool entered, returned;
        private StepJobFence fence;
        protected SimulationStepContext Step { get; private set; }
        protected Entity SessionEntity { get; private set; }

        internal void Run(SimulationStepContext step, Entity entity, StepJobFence jobs)
        {
            Step = step; SessionEntity = entity; fence = jobs; entered = returned = false;
            Exception failure = null;
            try { Update(); }
            catch (Exception error) { failure = error; }
            finally
            {
                // Include Dependency even if OnStartRunning or OnStep threw.
                try { jobs.Register(Dependency); jobs.Complete(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                fence = null;
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            if (!entered || !returned) throw new InvalidOperationException("Mandatory combat stage was skipped: " + GetType().Name);
            ValidateResult();
        }
        protected sealed override void OnUpdate()
        {
            if (fence == null || entered) throw new InvalidOperationException("Combat stage requires one executor invocation.");
            entered = true;
            OnStep();
            returned = true;
        }
        protected abstract void OnStep();
        // Called after the stage's jobs complete. Job failures must also have an explicit result contract.
        protected virtual void ValidateResult() { }
        protected void RegisterStepJob(JobHandle job)
        {
            if (fence == null || !entered || returned) throw new InvalidOperationException("No executing combat stage.");
            fence.Register(job);
            Dependency = JobHandle.CombineDependencies(Dependency, job);
        }
    }
}
