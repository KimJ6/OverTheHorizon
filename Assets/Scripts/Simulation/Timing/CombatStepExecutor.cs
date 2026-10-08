using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    /// <summary>A caller-supplied World and canonical data; no cameras, global Time, or Rigidbody dependencies.</summary>
    public sealed class CombatStepExecutor : IDisposable
    {
        private readonly World world;
        private CombatFixedStepSystemGroup group;
        private SimulationEntrySystem entry;
        private SimulationInputValidationSystem validation;
        private SimulationCompleteSystem exit;
        private Entity entity;
        private readonly StepJobFence fence = new StepJobFence();
        private bool disposed;
        private bool completionPending;
        private bool initialized, executing;
        private SimulationStepContext executed;
        public World World => world;
        public Entity SessionEntity => entity;
        public CombatFixedStepSystemGroup Group => group;
        public bool IsExecuting => executing;
        private CombatStepExecutor(World world, bool deferred)
        {
            if (world == null || !world.IsCreated) throw new ArgumentException("A live World is required.");
            this.world = world;
            if (world.GetExistingSystemManaged<CombatFixedStepSystemGroup>() != null
                || world.GetExistingSystemManaged<SimulationEntrySystem>() != null
                || world.GetExistingSystemManaged<SimulationInputValidationSystem>() != null
                || world.GetExistingSystemManaged<SimulationCompleteSystem>() != null)
                throw new InvalidOperationException("Timing systems already exist in this World.");
        }
        // The caller owns the instance before initialization allocates any native resources.
        public static void Create(World world, out CombatStepExecutor retained)
        {
            retained = null;
            retained = new CombatStepExecutor(world, true);
            retained.Initialize();
        }
        public CombatStepExecutor(World world) : this(world, true)
        {
            try
            {
                Initialize();
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }
        private void Initialize()
        {
            // Prepare the World time singleton/stack before the first authority step.
            world.PushTime(world.Time); world.PopTime();
            entity = world.EntityManager.CreateEntity(typeof(SimulationStepHeader), typeof(SimulationStepReceipt));
            var buffer = world.EntityManager.AddBuffer<PlayerStepElement>(entity); buffer.ResizeUninitialized(4);
            for (int i = 0; i < 4; ++i) buffer[i] = default;
            group = world.GetOrCreateSystemManaged<CombatFixedStepSystemGroup>(); group.RateManager = null;
            entry = world.GetOrCreateSystemManaged<SimulationEntrySystem>();
            validation = world.GetOrCreateSystemManaged<SimulationInputValidationSystem>();
            exit = world.GetOrCreateSystemManaged<SimulationCompleteSystem>();
            entry.SessionEntity = validation.SessionEntity = exit.SessionEntity = entity; validation.Fence = fence;
            group.AddSystemToUpdateList(entry); group.AddSystemToUpdateList(validation); group.AddSystemToUpdateList(exit);
            group.SortSystems();
            initialized = true;
        }
        public void Execute(SimulationStepContext context, SimulationTimingConfig config,
            uint completedTick, ulong completedOrdinal, NativeArray<PlayerStepElement> players)
        {
            if (disposed || !initialized || executing || !world.IsCreated || players.Length != 4 || completionPending)
                throw new InvalidOperationException("Executor is unavailable or the previous successful step is unpublished.");
            if (config.Version < 1 || context.ConfigVersion != config.Version || context.TickRate != config.TickRate
                || context.DeltaTime != config.FixedDeltaTime || context.Ordinal != checked(completedOrdinal + 1)
                || context.Tick != unchecked(completedTick + 1)) throw new InvalidOperationException("Step progress or configuration mismatch.");
            fence.Complete();
            var manager = world.EntityManager;
            if (!manager.Exists(entity)) throw new InvalidOperationException("Timing entity was removed.");
            manager.SetComponentData(entity, new SimulationStepHeader { Generation = context.Generation, Tick = context.Tick,
                Ordinal = context.Ordinal, DeltaTime = context.DeltaTime, TickRate = config.TickRate,
                CompletedTick = completedTick, CompletedOrdinal = completedOrdinal });
            manager.SetComponentData(entity, default(SimulationStepReceipt));
            var buffer = manager.GetBuffer<PlayerStepElement>(entity);
            for (int i = 0; i < 4; ++i) buffer[i] = players[i];
            world.PushTime(new TimeData(context.EndTime, context.DeltaTime));
            executing = true;
            try { group.Update(); }
            finally { try { fence.Complete(); } finally { try { world.PopTime(); } finally { executing = false; } } }
            uint bits = manager.GetComponentData<SimulationStepReceipt>(entity).Bits;
            if (bits != 7 || !validation.ValidationPassed)
                throw new InvalidOperationException("Mandatory ECS stages or input validation did not complete.");
            executed = context; completionPending = true;
        }
        public void PublishCompleted(SimulationStepContext context)
        {
            if (disposed || !world.IsCreated || !completionPending || !context.Matches(executed))
                throw new InvalidOperationException("Only the successfully executed pending step may be published.");
            var header = world.EntityManager.GetComponentData<SimulationStepHeader>(entity);
            if (header.Generation != context.Generation || header.Ordinal != context.Ordinal || header.Tick != context.Tick)
                throw new InvalidOperationException("The executed header was replaced before publication.");
            header.CompletedTick = context.Tick; header.CompletedOrdinal = context.Ordinal;
            world.EntityManager.SetComponentData(entity, header);
            completionPending = false;
        }
        public void CompleteJobs() { fence.Complete(); }
        public void Dispose()
        {
            if (disposed) return;
            if (executing) throw new InvalidOperationException("Cannot release an executing combat group.");
            // If completion fails, retain resources so a caller cannot free buffers still used by jobs.
            fence.Complete();
            var errors = new List<Exception>();
            if (world != null && world.IsCreated)
            {
                Release(() => { if (entry != null) { group?.RemoveSystemFromUpdateList(entry); world.DestroySystemManaged(entry); entry = null; } }, errors);
                Release(() => { if (validation != null) { group?.RemoveSystemFromUpdateList(validation); world.DestroySystemManaged(validation); validation = null; } }, errors);
                Release(() => { if (exit != null) { group?.RemoveSystemFromUpdateList(exit); world.DestroySystemManaged(exit); exit = null; } }, errors);
                if (errors.Count == 0)
                    Release(() => { if (group != null) { world.DestroySystemManaged(group); group = null; } }, errors);
                Release(() => { if (world.EntityManager.Exists(entity)) world.EntityManager.DestroyEntity(entity); entity = Entity.Null; }, errors);
            }
            if (errors.Count != 0) throw new AggregateException("Combat execution cleanup failed.", errors);
            disposed = true;
        }
        private static void Release(Action action, List<Exception> errors)
        { try { action(); } catch (Exception error) { errors.Add(error); } }
    }
}
