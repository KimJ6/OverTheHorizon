using System;
using Unity.Entities;
using Unity.Jobs;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    /// <summary>
    /// Unity runs this stage through ComponentSystemGroup.
    /// ECS-managed jobs stay on Dependency; external NativeContainer jobs are explicitly joined.
    /// </summary>
    [DisableAutoCreation]
    public abstract partial class CombatStepSystem : SystemBase
    {
        private CombatStepExecutor executor;
        private bool insideStep;
        // R2 only tracks jobs that access external NativeContainers. ECS component
        // dependencies remain on SystemBase.Dependency and are not force-completed here.
        private JobHandle externalJobDependency;
        private bool hasExternalJobDependency;
        protected SimulationStepContext Step { get; private set; }
        protected Entity SessionEntity { get; private set; }

        internal void Attach(CombatStepExecutor owner)
        {
            if (owner == null || executor != null)
                throw new InvalidOperationException("Combat stage already attached or missing owner.");
            executor = owner;
        }
        protected sealed override void OnUpdate()
        {
            var owner = executor;
            if (owner == null) throw new InvalidOperationException("Combat stage has no owner.");
            if (!owner.TryBeginStage(this)) return; // R1 fail-stop gate for Unity's normal group loop.

            Step = owner.CurrentStep;
            SessionEntity = owner.SessionEntity;
            Exception failure = null;
            insideStep = true;
            try { OnStep(); }
            catch (Exception error) { failure = error; }
            finally
            {
                insideStep = false;
                // R2: ECS-managed work remains on Dependency. Only jobs that touch
                // external NativeContainers are completed before their results are read.
                try { CompleteExternalStepJobs(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
            if (failure == null)
            {
                try { owner.RequireExecuting(); ValidateResult(); }
                catch (Exception error) { failure = error; }
            }
            if (failure != null) owner.ReportStageFailure(failure);
            else owner.ReportStageCompleted(this);
        }
        protected abstract void OnStep();
        // Called after registered external jobs complete. ECS Dependency work must not require an immediate host read.
        protected virtual void ValidateResult() { }
        protected void RegisterExternalStepJob(JobHandle job)
        {
            if (!insideStep || executor == null || !executor.IsExecuting)
                throw new InvalidOperationException("No executing combat stage.");
            // The explicit handle makes the external container safe to read in
            // ValidateResult; Dependency still carries the job for ECS scheduling.
            externalJobDependency = hasExternalJobDependency
                ? JobHandle.CombineDependencies(externalJobDependency, job)
                : job;
            hasExternalJobDependency = true;
            Dependency = JobHandle.CombineDependencies(Dependency, job);
        }
        private void CompleteExternalStepJobs()
        {
            if (!hasExternalJobDependency) return;
            // Clear first: if Complete rethrows a job exception, the faulted executor
            // must not retain a stale handle for a later cleanup/retry path.
            var dependency = externalJobDependency;
            externalJobDependency = default;
            hasExternalJobDependency = false;
            dependency.Complete();
        }
    }
}
