using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    public enum CombatExecutionState { Preparing, Ready, Executing, PendingPublication, Faulted, Disposed }

    /// <summary>One generation in a caller-owned World. Failed effects require explicit World recovery.</summary>
    public sealed class CombatStepExecutor : IDisposable
    {
        private readonly World world;
        private readonly ulong generation;
        private readonly SimulationTimingConfig config;
        private readonly List<CombatStepSystem> owned = new List<CombatStepSystem>();
        private readonly StepJobFence fence = new StepJobFence();
        private CombatFixedStepSystemGroup group;
        private SimulationEntrySystem entry;
        private SimulationCompleteSystem exit;
        private Entity entity;
        private bool initialized, executing, timePushed;
        private SimulationStepContext executed;
        public CombatExecutionState State { get; private set; } = CombatExecutionState.Preparing;
        public uint CompletedTick { get; private set; }
        public ulong CompletedOrdinal { get; private set; }
        public Exception LastFailure { get; private set; }
        public World World => world;
        public Entity SessionEntity => entity;
        public bool IsExecuting => executing;
        internal SimulationStepContext CurrentStep => executed;
        internal StepJobFence Jobs => fence;

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
            group.Executor = this;
            entry = CreateStage<SimulationEntrySystem>();
            exit = CreateStage<SimulationCompleteSystem>();
            initialized = true;
        }
        private T CreateStage<T>() where T : CombatStepSystem, new()
        {
            if (world.GetExistingSystemManaged<T>() != null)
                throw new InvalidOperationException("Stage already belongs to this World: " + typeof(T).Name);
            var stage = world.GetOrCreateSystemManaged<T>(); owned.Add(stage); return stage;
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
            var stages = new CombatStepSystem[owned.Count]; stages[0] = entry;
            for (int i = 2; i < owned.Count; ++i) stages[i - 1] = owned[i];
            stages[stages.Length - 1] = exit; group.Stages = stages;
            State = CombatExecutionState.Ready;
        }
        public void Execute(SimulationStepContext context, NativeArray<PlayerStepElement> players)
        {
            if (State != CombatExecutionState.Ready || !initialized || executing || !world.IsCreated)
                throw new InvalidOperationException("Executor is not ready.");
            if (context.Generation != generation || context.ConfigVersion != config.Version || context.TickRate != config.TickRate
                || context.DeltaTime != config.FixedDeltaTime || context.Ordinal != checked(CompletedOrdinal + 1)
                || context.Tick != unchecked(CompletedTick + 1))
                throw new InvalidOperationException("Step progress or session configuration mismatch.");
            CanonicalPlayerFrameValidator.Validate(context, players);
            try { ValidateHeader(CompletedTick, CompletedOrdinal); }
            catch (Exception error) { Fault(error); throw; }
            State = CombatExecutionState.Executing; executing = true; executed = context;
            Exception failure = null;
            try
            {
                fence.Complete();
                var manager = world.EntityManager;
                manager.SetComponentData(entity, new SimulationStepHeader { Generation = generation, Tick = context.Tick,
                    Ordinal = context.Ordinal, DeltaTime = config.FixedDeltaTime, TickRate = config.TickRate,
                    CompletedTick = CompletedTick, CompletedOrdinal = CompletedOrdinal });
                manager.SetComponentData(entity, default(SimulationStepReceipt));
                var buffer = manager.GetBuffer<PlayerStepElement>(entity);
                for (int i = 0; i < 4; ++i) buffer[i] = players[i];
                world.PushTime(new TimeData(context.EndTime, context.DeltaTime)); timePushed = true;
                group.Completed = false;
                if (group.RateManager != null) throw new InvalidOperationException("Combat rate manager was replaced.");
                group.Update();
                RequireExecuting();
                if (!group.Completed) throw new InvalidOperationException("Mandatory combat runner did not execute.");
                ValidateHeader(context.Tick, context.Ordinal);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { fence.Complete(); } catch (Exception error) { failure = Combine(failure, error); }
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
        private void ValidateHeader(uint tick, ulong ordinal)
        {
            if (!world.IsCreated || !world.EntityManager.Exists(entity))
                throw new InvalidOperationException("Timing entity was removed.");
            var h = world.EntityManager.GetComponentData<SimulationStepHeader>(entity);
            if (h.Generation != generation || h.Tick != tick || h.Ordinal != ordinal
                || h.CompletedTick != CompletedTick || h.CompletedOrdinal != CompletedOrdinal
                || h.TickRate != config.TickRate || h.DeltaTime != config.FixedDeltaTime)
                throw new InvalidOperationException("The owned timing header was changed externally.");
        }
        public void ValidatePending(SimulationStepContext context)
        {
            if (State != CombatExecutionState.PendingPublication || !context.Matches(executed))
                throw new InvalidOperationException("Only the successfully executed pending step may be published.");
            try { ValidateHeader(context.Tick, context.Ordinal); }
            catch (Exception error) { Fault(error); throw; }
        }
        public void PublishCompleted(SimulationStepContext context)
        {
            ValidatePending(context);
            try
            {
                var header = world.EntityManager.GetComponentData<SimulationStepHeader>(entity);
                header.CompletedTick = context.Tick; header.CompletedOrdinal = context.Ordinal;
                world.EntityManager.SetComponentData(entity, header);
                CompletedTick = context.Tick; CompletedOrdinal = context.Ordinal;
                State = CombatExecutionState.Ready;
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
        public void CompleteJobs() { fence.Complete(); }
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
            fence.Complete(); RestoreTime();
            var errors = new List<Exception>();
            if (world.IsCreated)
            {
                for (int i = owned.Count - 1; i >= 0; --i)
                {
                    var stage = owned[i]; if (stage == null) continue;
                    try { world.DestroySystemManaged(stage); owned[i] = null; }
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
