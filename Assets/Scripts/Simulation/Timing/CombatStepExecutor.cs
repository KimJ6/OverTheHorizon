using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    public enum CombatExecutionState
    {
        Preparing, Ready, Executing,
        // R3: execution has succeeded, but only SimulationProgress may commit it.
        // This gate prevents another Execute call until that commit is acknowledged.
        PendingPublication,
        Faulted, Disposed
    }

    /// <summary>One generation in a caller-owned World. Failed effects require explicit World recovery.</summary>
    public sealed class CombatStepExecutor : IDisposable
    {
        private readonly World world;
        private readonly ulong generation;
        private readonly SimulationTimingConfig config;
        private readonly List<CombatStepSystem> owned = new List<CombatStepSystem>();
        // R1 ownership split: Unity owns ordering/execution; the executor owns the
        // invocation boundary and validates the sealed membership/order snapshot.
        private CombatFixedStepSystemGroup group;
        private CombatStepSystem[] orderedStages;
        private int nextStageIndex;
        private Exception stageFailure;
        private SimulationEntrySystem entry;
        private SimulationCompleteSystem exit;
        private Entity entity;
        private bool initialized, executing, timePushed;
        private SimulationStepContext executed;
        // R7: the pending publication belongs to the exact Progress instance
        // that reserved it. Matching values alone must not transfer authority.
        private SimulationProgress executingProgress;
        public CombatExecutionState State { get; private set; } = CombatExecutionState.Preparing;
        public Exception LastFailure { get; private set; }
        public World World => world;
        public Entity SessionEntity => entity;
        public bool IsExecuting => executing;
        internal SimulationStepContext CurrentStep => executed;

        private CombatStepExecutor(World world, ulong generation, SimulationTimingConfig config, bool deferred)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.");
            if (generation == 0 || config.Version < 1 || config.FixedDeltaTime <= 0)
                throw new ArgumentException("An explicit generation and constructed timing configuration are required.");
            this.world = world; this.generation = generation; this.config = config;
            if (world.GetExistingSystemManaged<CombatFixedStepSystemGroup>() != null
                || world.GetExistingSystemManaged<SimulationEntrySystem>() != null
                || world.GetExistingSystemManaged<SimulationCompleteSystem>() != null)
                throw new InvalidOperationException("Timing systems already exist in this World.");
        }
        public static void Create(World world, ulong generation, SimulationTimingConfig config, out CombatStepExecutor retained)
        {
            retained = null;
            retained = new CombatStepExecutor(world, generation, config, true);
            retained.Initialize();
        }
        public CombatStepExecutor(World world, ulong generation, SimulationTimingConfig config) : this(world, generation, config, true)
        {
            try { Initialize(); }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }
        private void Initialize()
        {
            world.PushTime(world.Time); timePushed = true; world.PopTime(); timePushed = false;
            entity = world.EntityManager.CreateEntity(typeof(SimulationStepHeader), typeof(SimulationStepReceipt));
            world.EntityManager.SetComponentData(entity, new SimulationStepHeader
            { Generation = generation, TickRate = config.TickRate, DeltaTime = config.FixedDeltaTime });
            var buffer = world.EntityManager.AddBuffer<PlayerStepElement>(entity); buffer.ResizeUninitialized(4);
            for (int i = 0; i < 4; ++i) buffer[i] = default;
            group = world.GetOrCreateSystemManaged<CombatFixedStepSystemGroup>(); group.RateManager = null;
            entry = CreateStage<SimulationEntrySystem>();
            exit = CreateStage<SimulationCompleteSystem>();
            initialized = true;
        }
        private T CreateStage<T>() where T : CombatStepSystem, new()
        {
            if (world.GetExistingSystemManaged<T>() != null)
                throw new InvalidOperationException("Stage already belongs to this World: " + typeof(T).Name);
            var stage = world.GetOrCreateSystemManaged<T>();
            stage.Attach(this);
            owned.Add(stage);
            return stage;
        }
        public T RegisterStage<T>() where T : CombatStepSystem, new()
        {
            if (!initialized || State != CombatExecutionState.Preparing)
                throw new InvalidOperationException("Stages can only be registered while preparing.");
            return CreateStage<T>();
        }
        public void Seal()
        {
            if (!initialized || State != CombatExecutionState.Preparing)
                throw new InvalidOperationException("Only a prepared executor can be sealed.");
            // Registration order is not a scheduling contract. Read Unity's sorted
            // result below; stages with dependencies must declare UpdateBefore/After.
            group.AddSystemToUpdateList(entry);
            for (int i = 2; i < owned.Count; ++i) group.AddSystemToUpdateList(owned[i]);
            group.AddSystemToUpdateList(exit);
            group.SortSystems();
            var stages = new List<CombatStepSystem>(owned.Count);
            foreach (var system in group.ManagedSystems)
            {
                if (!(system is CombatStepSystem stage) || !owned.Contains(stage))
                    throw new InvalidOperationException("Unexpected system in combat group.");
                stages.Add(stage);
            }
            if (stages.Count != owned.Count || !ReferenceEquals(stages[0], entry)
                || !ReferenceEquals(stages[stages.Count - 1], exit))
                throw new InvalidOperationException("Unity combat group order must be Entry -> stages -> Exit.");
            orderedStages = stages.ToArray(); // R1 snapshot used only for fail-stop validation.
            State = CombatExecutionState.Ready;
        }
        public void Execute(SimulationStepContext context, NativeArray<PlayerStepElement> players, SimulationProgress progress)
        {
            if (State != CombatExecutionState.Ready || !initialized || executing || !world.IsCreated)
                throw new InvalidOperationException("Executor is not ready.");
            if (context.Generation != generation || context.ConfigVersion != config.Version || context.TickRate != config.TickRate
                || context.DeltaTime != config.FixedDeltaTime)
                throw new InvalidOperationException("Step progress or session configuration mismatch.");
            // R3: only SimulationProgress can establish that this context is
            // the one open authoritative step. The executor never keeps a
            // second completed tick/ordinal ledger.
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            progress.ValidateCompletion(context);
            CanonicalPlayerFrameValidator.Validate(context, players);
            State = CombatExecutionState.Executing; executing = true; executed = context; executingProgress = progress;
            nextStageIndex = 0; stageFailure = null;
            Exception failure = null;
            try
            {
                var manager = world.EntityManager;
                manager.SetComponentData(entity, new SimulationStepHeader { Generation = generation, Tick = context.Tick,
                    Ordinal = context.Ordinal, DeltaTime = config.FixedDeltaTime, TickRate = config.TickRate });
                manager.SetComponentData(entity, default(SimulationStepReceipt));
                var buffer = manager.GetBuffer<PlayerStepElement>(entity);
                for (int i = 0; i < 4; ++i) buffer[i] = players[i];
                world.PushTime(new TimeData(context.EndTime, context.DeltaTime)); timePushed = true;
                if (group.RateManager != null) throw new InvalidOperationException("Combat rate manager was replaced.");
                ValidateGroupMembership(); // Reject late group mutation before it can run gameplay.
                group.Update(); // R1: the sole Unity ComponentSystemGroup update for this step.
                if (stageFailure != null) ExceptionDispatchInfo.Capture(stageFailure).Throw();
                RequireExecuting();
                if (orderedStages == null || nextStageIndex != orderedStages.Length)
                    throw new InvalidOperationException("Mandatory combat stage was skipped.");
                ValidateHeader(context);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { RestoreTime(); } catch (Exception error) { failure = Combine(failure, error); }
                executing = false;
            }
            if (failure != null) { Fault(failure); ExceptionDispatchInfo.Capture(failure).Throw(); }
            State = CombatExecutionState.PendingPublication;
        }
        internal void RequireExecuting()
        {
            if (!executing || State != CombatExecutionState.Executing || !world.IsCreated)
                throw new InvalidOperationException("Combat execution was cancelled or lost its World.", LastFailure);
        }
        internal bool TryBeginStage(CombatStepSystem stage)
        {
            // ComponentSystemGroup keeps iterating after a managed system reports a
            // failure. This gate prevents later stages from running gameplay.
            if (!executing || State != CombatExecutionState.Executing || stageFailure != null) return false;
            if (orderedStages == null || nextStageIndex >= orderedStages.Length
                || !ReferenceEquals(orderedStages[nextStageIndex], stage))
            {
                ReportStageFailure(new InvalidOperationException("Unity combat stage order is invalid or a mandatory stage was skipped: " + stage.GetType().Name));
                return false;
            }
            return true;
        }
        internal void ReportStageCompleted(CombatStepSystem stage)
        {
            if (!TryBeginStage(stage)) return;
            ++nextStageIndex;
        }
        internal void ReportStageFailure(Exception error)
        {
            if (stageFailure == null) stageFailure = error;
        }
        private void ValidateGroupMembership()
        {
            // Sort first so a late Add/Remove is visible before ComponentSystemGroup.Update.
            group.SortSystems();
            if (orderedStages == null || group.ManagedSystems.Count != orderedStages.Length)
                throw new InvalidOperationException("Combat group membership changed after sealing.");
            for (int i = 0; i < orderedStages.Length; ++i)
                if (!ReferenceEquals(group.ManagedSystems[i], orderedStages[i]))
                    throw new InvalidOperationException("Combat group membership changed after sealing.");
        }
        private void ValidateHeader(SimulationStepContext context)
        {
            if (!world.IsCreated || !world.EntityManager.Exists(entity))
                throw new InvalidOperationException("Timing entity was removed.");
            var h = world.EntityManager.GetComponentData<SimulationStepHeader>(entity);
            if (h.Generation != context.Generation || h.Tick != context.Tick || h.Ordinal != context.Ordinal
                || h.TickRate != config.TickRate || h.DeltaTime != config.FixedDeltaTime)
                throw new InvalidOperationException("The owned timing header was changed externally.");
        }
        public void ValidatePending(SimulationStepContext context, SimulationProgress progress)
        {
            // This validation deliberately happens while Progress is still open.
            // A failure here aborts without advancing the authoritative completion.
            if (State != CombatExecutionState.PendingPublication || !context.Matches(executed))
                throw new InvalidOperationException("Only the successfully executed pending step may be published.");
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (!ReferenceEquals(progress, executingProgress))
                throw new InvalidOperationException("Pending publication belongs to a different SimulationProgress instance.");
            try { progress.ValidateCompletion(context); ValidateHeader(context); }
            catch (Exception error) { Fault(error); throw; }
        }
        public void AcknowledgeAuthoritativeCompletion(SimulationStepContext context, SimulationProgress progress)
        {
            if (State != CombatExecutionState.PendingPublication || !context.Matches(executed))
                throw new InvalidOperationException("Only the successfully executed pending step may be acknowledged.");
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            if (!ReferenceEquals(progress, executingProgress))
                throw new InvalidOperationException("Pending acknowledgement belongs to a different SimulationProgress instance.");
            try
            {
                // R3: acknowledgement is a state release only. Progress wrote
                // the authoritative completion before this method is called.
                if (progress.StepOpen || progress.Faulted || progress.Generation != context.Generation
                    || progress.CompletedTick != context.Tick || progress.CompletedOrdinal != context.Ordinal)
                    throw new InvalidOperationException("SimulationProgress has not completed this step.");
                executingProgress = null; State = CombatExecutionState.Ready;
            }
            catch (Exception error) { Fault(error); throw; }
        }
        public void Invalidate(string reason)
        {
            if (State == CombatExecutionState.Disposed || State == CombatExecutionState.Faulted) return;
            Fault(new InvalidOperationException(reason));
        }
        private void Fault(Exception error) { LastFailure = LastFailure == null ? error : Combine(LastFailure, error); State = CombatExecutionState.Faulted; }
        private static Exception Combine(Exception first, Exception next) => first == null ? next : new AggregateException(first, next);
        private void RestoreTime()
        {
            if (!timePushed) return;
            if (world.IsCreated) world.PopTime();
            timePushed = false;
        }
        public void Dispose()
        {
            if (State == CombatExecutionState.Disposed) return;
            if (executing) throw new InvalidOperationException("Cannot release an executing combat runner.");
            Invalidate("Executor disposed.");
            RestoreTime();
            var errors = new List<Exception>();
            if (world.IsCreated)
            {
                for (int i = owned.Count - 1; i >= 0; --i)
                {
                    var stage = owned[i]; if (stage == null) continue;
                    // R1: detach before destruction so the standard group never retains a dead member.
                    try { group?.RemoveSystemFromUpdateList(stage); world.DestroySystemManaged(stage); owned[i] = null; }
                    catch (Exception error) { errors.Add(error); }
                }
                if (errors.Count == 0 && group != null)
                    try { world.DestroySystemManaged(group); group = null; } catch (Exception error) { errors.Add(error); }
                if (errors.Count == 0 && world.EntityManager.Exists(entity))
                    try { world.EntityManager.DestroyEntity(entity); entity = Entity.Null; } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count != 0) throw new AggregateException("Combat execution cleanup failed.", errors);
            State = CombatExecutionState.Disposed;
        }
    }
}
