using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using System.Reflection;
using OverTheHorizon.Timing;
using OverTheHorizon.Simulation;
using OverTheHorizon.LocalSimulation;

namespace OverTheHorizon.Tests
{
    public sealed class CombatExecutorTests
    {
        private static CombatStepExecutor CreateExecutor(World world, ulong generation = 1)
        {
            var executor = new CombatStepExecutor(world, generation, new SimulationTimingConfig(50));
            executor.Seal(); return executor;
        }
        private static PlayerStepElement Player(uint id, SimulationStepContext c)
            => new PlayerStepElement { Valid = 1, PlayerId = id, SlotGeneration = 1, Generation = c.Generation,
                ConsumedOrdinal = c.Ordinal, InputSequence = 1, StartStateTick = unchecked(c.Tick - 1), EndStateTick = c.Tick,
                StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                SteeringForward = new float3(0, 0, 1), SteeringRight = new float3(1, 0, 0), ViewForward = new float3(0, 0, 1) };
        [Test] public void CompletedStepCannotBeExecutedOrPublishedTwice()
        {
            using (var world = new World("Duplicate step"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var counter = executor.RegisterStage<CountingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                executor.Execute(c, input); executor.PublishCompleted(c);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(1, counter.Calls); Assert.AreEqual(1ul, executor.CompletedOrdinal);
                Assert.AreEqual(1ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            }
        }
        [TestCase("generation")] [TestCase("rate")] [TestCase("version")] [TestCase("skip")] [TestCase("tick")]
        public void BadRequestDoesNotMutateHeaderOrInput(string kind)
        {
            using (var world = new World("Request validation"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var cfg = new SimulationTimingConfig(kind == "rate" ? 60 : 50, kind == "version" ? 2 : 1);
                var c = new SimulationStepContext(kind == "generation" ? 2ul : 1ul, kind == "tick" ? 2u : 1u, kind == "skip" ? 2ul : 1ul, cfg);
                var writable = input; writable[0] = Player(1, c);
                var before = world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.AreEqual(before, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity));
                Assert.AreEqual(0, world.EntityManager.GetBuffer<PlayerStepElement>(executor.SessionEntity)[0].Valid);
                Assert.AreEqual(CombatExecutionState.Ready, executor.State);
            }
        }
        [TestCase(false)] [TestCase(true)] public void InvalidFrameNeverRunsGameplayAndCanBeCorrected(bool nonFinite)
        {
            using (var world = new World("Invalid input gate"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var counter = executor.RegisterStage<CountingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                if (nonFinite) { var p = writable[0]; p.EndPosition.x = float.NaN; writable[0] = p; }
                else writable[1] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.AreEqual(0, counter.Calls);
                Assert.AreEqual(0u, world.EntityManager.GetComponentData<SimulationStepReceipt>(executor.SessionEntity).Bits);
                writable[0] = Player(1, c); writable[1] = default;
                executor.Execute(c, input); executor.PublishCompleted(c);
                Assert.AreEqual(1, counter.Calls);
            }
        }
        [TestCase(false)] [TestCase(true)] public void FailedOrSkippedStageStopsPipelineAndRetry(bool disabled)
        {
            using (var world = new World("Failed stage"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var first = executor.RegisterStage<CountingCombatStage>(); first.Fail = true; first.Enabled = !disabled;
                var next = executor.RegisterStage<FollowingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c); var before = world.Time;
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                first.Fail = false; first.Enabled = true;
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(disabled ? 0 : 1, first.Calls); Assert.AreEqual(0, next.Calls);
                Assert.AreEqual(before.ElapsedTime, world.Time.ElapsedTime); Assert.AreEqual(before.DeltaTime, world.Time.DeltaTime);
                Assert.AreEqual(CombatExecutionState.Faulted, executor.State); Assert.AreEqual(0ul, executor.CompletedOrdinal);
            }
        }
        [TestCase(false)] [TestCase(true)] public void StageJobResultIsJoinedBeforeFollowingStage(bool fail)
        {
            using (var world = new World("Job gate"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var job = executor.RegisterStage<JobCombatStage>(); job.Fail = fail;
                var next = executor.RegisterStage<FollowingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                if (fail) Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                else { executor.Execute(c, input); executor.PublishCompleted(c); }
                Assert.AreEqual(fail ? 0 : 1, next.Calls); Assert.IsTrue(job.Joined);
            }
        }
        [Test] public void PendingPublicationRejectsOtherRequestsAndHeaderTamperingFaults()
        {
            using (var world = new World("Pending publication"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg);
                var writable = input; writable[0] = Player(1, c); executor.Execute(c, input);
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(new SimulationStepContext(1, 2, 2, cfg)));
                Assert.AreEqual(CombatExecutionState.PendingPublication, executor.State);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                var header = world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
                header.CompletedOrdinal = 42; world.EntityManager.SetComponentData(executor.SessionEntity, header);
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(CombatExecutionState.Faulted, executor.State); Assert.AreEqual(0ul, executor.CompletedOrdinal);
            }
        }
        [TestCase(false)] [TestCase(true)] public void TickWrapsButOrdinalCannotOverflow(bool overflow)
        {
            using (var world = new World("Progress limit"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                // Test-only checkpoint seeding. Production exposes no rewind or arbitrary baseline API.
                ulong ordinal = overflow ? ulong.MaxValue : uint.MaxValue;
                typeof(CombatStepExecutor).GetProperty("CompletedTick").SetValue(executor, uint.MaxValue);
                typeof(CombatStepExecutor).GetProperty("CompletedOrdinal").SetValue(executor, ordinal);
                var header = world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
                header.Tick = header.CompletedTick = uint.MaxValue; header.Ordinal = header.CompletedOrdinal = ordinal;
                world.EntityManager.SetComponentData(executor.SessionEntity, header);
                var c = new SimulationStepContext(1, 0, overflow ? 1 : ordinal + 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                if (overflow) Assert.Throws<OverflowException>(() => executor.Execute(c, input));
                else { executor.Execute(c, input); executor.PublishCompleted(c); Assert.AreEqual(0u, executor.CompletedTick); }
            }
        }
        [Test] public void TimeRestorationFailureRetainsResourcesUntilExplicitRepair()
        {
            using (var world = new World("Time restoration failure"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                executor.RegisterStage<UnbalancedTimeStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                try
                {
                    Assert.Catch(() => executor.Execute(c, input));
                    Assert.AreEqual(CombatExecutionState.Faulted, executor.State);
                    Assert.AreEqual(0ul, executor.CompletedOrdinal);
                    Assert.Catch(executor.Dispose);
                    Assert.IsTrue(world.EntityManager.Exists(executor.SessionEntity));
                }
                finally
                {
                    // Repair the deliberately corrupted stack so retained cleanup can safely finish.
                    world.PushTime(world.Time);
                }
                Assert.DoesNotThrow(executor.Dispose);
                Assert.IsFalse(world.EntityManager.Exists(executor.SessionEntity));
            }
        }
        [Test] public void InvalidationInsideStageStopsFollowingStageAndPublication()
        {
            using (var world = new World("Cancellation"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var cancel = executor.RegisterStage<CancelCombatStage>(); cancel.Cancel = () => executor.Invalidate("Cancelled by owner.");
                var next = executor.RegisterStage<FollowingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var writable = input; writable[0] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.AreEqual(0, next.Calls); Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(0ul, executor.CompletedOrdinal);
            }
        }
        [Test] public void FourPlayersUseOneAuthorityUpdateAndRestoreWorldTime()
        {
            using (var world = new World("Timing test"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg);
                for (int i = 0; i < 4; ++i) writableInput[i] = Player((uint)i + 1, c);
                double before = world.Time.ElapsedTime; executor.Execute(c, input);
                Assert.AreEqual(before, world.Time.ElapsedTime);
                Assert.AreEqual(0ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
                executor.PublishCompleted(c);
                Assert.AreEqual(1ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            }
        }
        [Test] public void MissingMandatorySystemCannotReportSuccess()
        {
            using (var world = new World("Missing stage test"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg); writableInput[0] = Player(1, c);
                world.GetExistingSystemManaged<SimulationCompleteSystem>().Enabled = false;
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(0ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            }
        }
        [Test] public void DuplicatePlayerIdsAreRejected()
        {
            using (var world = new World("Duplicate ID test"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg);
                writableInput[0] = Player(1, c); writableInput[1] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, input));
            }
        }
        [Test] public void CanonicalTraceRoundTripsAndReplaysIntoIndependentWorld()
        {
            string path = Path.GetTempFileName();
            try
            {
                using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
                {
                    var writableInput = input;
                    var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(7, 1, 1, cfg);
                    writableInput[0] = Player(1, c); var p = input[0]; p.EndPosition = new float3(1, 2, 3); writableInput[0] = p;
                    var trace = new SimulationTraceBuffer(2); trace.Append(c, true, input); SimulationReplayStepSource.Export(trace, path);
                    var source = new SimulationReplayStepSource(path); Assert.IsTrue(source.TryRead(out var r));
                    Assert.AreEqual(p.EndPosition, r.P0.EndPosition); Assert.IsTrue(r.Completed); Assert.AreEqual(7ul, r.Context.Generation);
                    writableInput[0] = r.P0; writableInput[1] = r.P1; writableInput[2] = r.P2; writableInput[3] = r.P3;
                    using (var world = new World("Replay test")) using (var executor = CreateExecutor(world, 7))
                        executor.Execute(r.Context, input);
                    Assert.IsFalse(source.TryRead(out r));
                }
            }
            finally { File.Delete(path); }
        }
    }
    [DisableAutoCreation] public partial class CountingCombatStage : CombatStepSystem
    {
        public int Calls; public bool Fail;
        protected override void OnStep() { ++Calls; if (Fail) throw new InvalidOperationException("Injected stage failure."); }
    }
    [DisableAutoCreation] public partial class UnbalancedTimeStage : CombatStepSystem
    { protected override void OnStep() { World.PopTime(); } }
    [DisableAutoCreation] public partial class CancelCombatStage : CombatStepSystem
    { public Action Cancel; protected override void OnStep() { Cancel(); } }
    [DisableAutoCreation] public partial class FollowingCombatStage : CombatStepSystem
    { public int Calls; protected override void OnStep() { ++Calls; } }
    [DisableAutoCreation] public partial class JobCombatStage : CombatStepSystem
    {
        private NativeArray<int> result;
        public bool Fail, Joined;
        protected override void OnCreate() { result = new NativeArray<int>(1, Allocator.Persistent); }
        protected override void OnDestroy() { Dependency.Complete(); if (result.IsCreated) result.Dispose(); }
        protected override void OnStep() { result[0] = 0; RegisterStepJob(new ResultJob { Result = result, Value = Fail ? -1 : 1 }.Schedule()); }
        protected override void ValidateResult() { Joined = result[0] != 0; if (result[0] != 1) throw new InvalidOperationException("Injected job result failure."); }
        private struct ResultJob : IJob { public NativeArray<int> Result; public int Value; public void Execute() { Result[0] = Value; } }
    }
}
