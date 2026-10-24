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
        private static SimulationProgress OpenProgress(SimulationStepContext expected)
        {
            var progress = new SimulationProgress(); progress.Reset(expected.Generation);
            Assert.IsTrue(expected.Matches(progress.Begin(new SimulationTimingConfig(expected.TickRate, expected.ConfigVersion))));
            return progress;
        }
        private static void Run(CombatStepExecutor executor, SimulationProgress progress, SimulationStepContext context,
            NativeArray<PlayerStepElement> input) => executor.Execute(context, input, progress);
        private static void Complete(CombatStepExecutor executor, SimulationProgress progress, SimulationStepContext context)
        {
            // Mirror the runtime commit boundary so tests cannot accidentally give
            // the executor a second completion authority.
            executor.ValidatePending(context, progress);
            progress.Complete(context);
            executor.AcknowledgeAuthoritativeCompletion(context, progress);
        }
        private static void SeedProgress(SimulationProgress progress, uint tick, ulong ordinal)
        {
            typeof(SimulationProgress).GetProperty("CompletedTick").SetValue(progress, tick);
            typeof(SimulationProgress).GetProperty("CompletedOrdinal").SetValue(progress, ordinal);
            typeof(SimulationProgress).GetProperty("ReservedTick").SetValue(progress, tick);
        }
        [Test] public void StandardUnityGroupRegistersStagesWithoutAnAutomaticSecondUpdate()
        {
            using (var world = new World("R1 standard ECS group"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var stage = executor.RegisterStage<CountingCombatStage>();
                executor.Seal();
                var group = world.GetExistingSystemManaged<CombatFixedStepSystemGroup>();
                Assert.IsNotNull(group);
                bool found = false;
                foreach (var system in group.ManagedSystems)
                    if (ReferenceEquals(stage, system)) found = true;
                Assert.IsTrue(found, "Registered stages must belong to Unity's standard group.");
                group.Update();
                Assert.AreEqual(0, stage.Calls);
                var context = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var progress = OpenProgress(context);
                var writable = input;
                writable[0] = Player(1, context);
                Run(executor, progress, context, input);
                Assert.AreEqual(1, stage.Calls);
            }
        }

        [Test] public void ProgressIsTheOnlyCompletionAuthority()
        {
            using (var world = new World("Duplicate step"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var counter = executor.RegisterStage<CountingCombatStage>(); executor.Seal();
                var c = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c);
                Run(executor, progress, c, input);
                Assert.AreEqual(CombatExecutionState.PendingPublication, executor.State);
                Assert.IsTrue(progress.StepOpen); Assert.AreEqual(0ul, progress.CompletedOrdinal);
                Complete(executor, progress, c);
                Assert.AreEqual(1, counter.Calls); Assert.AreEqual(1ul, progress.CompletedOrdinal);
                var next = progress.Begin(new SimulationTimingConfig(c.TickRate, c.ConfigVersion));
                Assert.AreEqual(2ul, next.Ordinal); Assert.AreEqual(2u, next.Tick);
            }
        }
        [Test] public void PendingPublicationRequiresTheOriginalProgressInstance()
        {
            using (var world = new World("Progress identity"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var context = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var source = OpenProgress(context); var other = OpenProgress(context);
                var writable = input; writable[0] = Player(1, context);
                Run(executor, source, context, input);
                Assert.Throws<InvalidOperationException>(() => executor.ValidatePending(context, other));
                executor.ValidatePending(context, source);
                source.Complete(context);
                Assert.Throws<InvalidOperationException>(() => executor.AcknowledgeAuthoritativeCompletion(context, other));
                Assert.AreEqual(CombatExecutionState.PendingPublication, executor.State);
                executor.AcknowledgeAuthoritativeCompletion(context, source);
                Assert.AreEqual(CombatExecutionState.Ready, executor.State);
            }
        }
        [Test] public void LateGroupMembershipChangeIsRejectedBeforeGameplayRuns()
        {
            using (var world = new World("R1 group membership"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var stage = executor.RegisterStage<CountingCombatStage>();
                executor.Seal();
                var group = world.GetExistingSystemManaged<CombatFixedStepSystemGroup>();
                group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<ForeignCombatStage>());
                var context = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var progress = OpenProgress(context);
                var writable = input;
                writable[0] = Player(1, context);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, context, input));
                Assert.AreEqual(0, stage.Calls);
            }
        }
        [TestCase("generation")] [TestCase("rate")] [TestCase("version")] [TestCase("skip")] [TestCase("tick")]
        public void BadRequestDoesNotMutateHeaderOrInput(string kind)
        {
            using (var world = new World("Request validation"))
            using (var executor = CreateExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var expected = new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(50));
                var progress = OpenProgress(expected);
                var cfg = new SimulationTimingConfig(kind == "rate" ? 60 : 50, kind == "version" ? 2 : 1);
                var c = new SimulationStepContext(kind == "generation" ? 2ul : 1ul, kind == "tick" ? 2u : 1u, kind == "skip" ? 2ul : 1ul, cfg);
                var writable = input; writable[0] = Player(1, c);
                var before = world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c);
                if (nonFinite) { var p = writable[0]; p.EndPosition.x = float.NaN; writable[0] = p; }
                else writable[1] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                Assert.AreEqual(0, counter.Calls);
                Assert.AreEqual(0u, world.EntityManager.GetComponentData<SimulationStepReceipt>(executor.SessionEntity).Bits);
                writable[0] = Player(1, c); writable[1] = default;
                Run(executor, progress, c, input); Complete(executor, progress, c);
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c); var before = world.Time;
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                first.Fail = false; first.Enabled = true;
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                Assert.Throws<InvalidOperationException>(() => executor.AcknowledgeAuthoritativeCompletion(c, progress));
                Assert.AreEqual(disabled ? 0 : 1, first.Calls); Assert.AreEqual(0, next.Calls);
                Assert.AreEqual(before.ElapsedTime, world.Time.ElapsedTime); Assert.AreEqual(before.DeltaTime, world.Time.DeltaTime);
                Assert.AreEqual(CombatExecutionState.Faulted, executor.State); Assert.AreEqual(0ul, progress.CompletedOrdinal);
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c);
                if (fail) Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                else { Run(executor, progress, c, input); Complete(executor, progress, c); }
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c); Run(executor, progress, c, input);
                Assert.Throws<InvalidOperationException>(() => executor.AcknowledgeAuthoritativeCompletion(new SimulationStepContext(1, 2, 2, cfg), progress));
                Assert.AreEqual(CombatExecutionState.PendingPublication, executor.State);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                var header = world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
                header.Ordinal = 42; world.EntityManager.SetComponentData(executor.SessionEntity, header);
                Assert.Throws<InvalidOperationException>(() => executor.ValidatePending(c, progress));
                Assert.AreEqual(CombatExecutionState.Faulted, executor.State); Assert.AreEqual(0ul, progress.CompletedOrdinal);
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
                var config = new SimulationTimingConfig(50);
                var progress = new SimulationProgress(); progress.Reset(1); SeedProgress(progress, uint.MaxValue, ordinal);
                var writable = input;
                if (overflow) Assert.Throws<OverflowException>(() => progress.Begin(config));
                else
                {
                    var c = progress.Begin(config); writable[0] = Player(1, c);
                    Run(executor, progress, c, input); Complete(executor, progress, c);
                    Assert.AreEqual(0u, progress.CompletedTick);
                }
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c);
                try
                {
                    Assert.Catch(() => Run(executor, progress, c, input));
                    Assert.AreEqual(CombatExecutionState.Faulted, executor.State);
                    Assert.AreEqual(0ul, progress.CompletedOrdinal);
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
                var progress = OpenProgress(c);
                var writable = input; writable[0] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                Assert.AreEqual(0, next.Calls); Assert.Throws<InvalidOperationException>(() => executor.AcknowledgeAuthoritativeCompletion(c, progress));
                Assert.AreEqual(0ul, progress.CompletedOrdinal);
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
                var progress = OpenProgress(c);
                for (int i = 0; i < 4; ++i) writableInput[i] = Player((uint)i + 1, c);
                double before = world.Time.ElapsedTime; Run(executor, progress, c, input);
                Assert.AreEqual(before, world.Time.ElapsedTime);
                Assert.IsTrue(progress.StepOpen);
                Complete(executor, progress, c);
                Assert.AreEqual(1ul, progress.CompletedOrdinal);
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
                var progress = OpenProgress(c);
                world.GetExistingSystemManaged<SimulationCompleteSystem>().Enabled = false;
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
                Assert.Throws<InvalidOperationException>(() => executor.AcknowledgeAuthoritativeCompletion(c, progress));
                Assert.AreEqual(0ul, progress.CompletedOrdinal);
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
                var progress = OpenProgress(c);
                writableInput[0] = Player(1, c); writableInput[1] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => Run(executor, progress, c, input));
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
                    {
                        var progress = OpenProgress(r.Context);
                        Run(executor, progress, r.Context, input);
                    }
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
    [DisableAutoCreation]
    [UpdateAfter(typeof(CountingCombatStage))]
    [UpdateAfter(typeof(JobCombatStage))]
    [UpdateAfter(typeof(CancelCombatStage))]
    public partial class FollowingCombatStage : CombatStepSystem
    { public int Calls; protected override void OnStep() { ++Calls; } }
    [DisableAutoCreation] public partial class ForeignCombatStage : CombatStepSystem
    { protected override void OnStep() { } }
    [DisableAutoCreation] public partial class JobCombatStage : CombatStepSystem
    {
        private NativeArray<int> result;
        public bool Fail, Joined;
        protected override void OnCreate() { result = new NativeArray<int>(1, Allocator.Persistent); }
        protected override void OnDestroy() { Dependency.Complete(); if (result.IsCreated) result.Dispose(); }
        protected override void OnStep() { result[0] = 0; RegisterExternalStepJob(new ResultJob { Result = result, Value = Fail ? -1 : 1 }.Schedule()); }
        protected override void ValidateResult() { Joined = result[0] != 0; if (result[0] != 1) throw new InvalidOperationException("Injected job result failure."); }
        private struct ResultJob : IJob { public NativeArray<int> Result; public int Value; public void Execute() { Result[0] = Value; } }
    }
}
