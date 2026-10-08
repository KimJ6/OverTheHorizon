using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using OverTheHorizon.Timing;
using OverTheHorizon.Simulation;
using OverTheHorizon.LocalSimulation;

namespace OverTheHorizon.Tests
{
    public sealed class CombatExecutorTests
    {
        private static PlayerStepElement Player(uint id, SimulationStepContext c)
            => new PlayerStepElement { Valid = 1, PlayerId = id, SlotGeneration = 1, Generation = c.Generation,
                ConsumedOrdinal = c.Ordinal, InputSequence = 1, StartStateTick = unchecked(c.Tick - 1), EndStateTick = c.Tick,
                StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                SteeringForward = new float3(0, 0, 1), SteeringRight = new float3(1, 0, 0), ViewForward = new float3(0, 0, 1) };
        [Test] public void FourPlayersUseOneAuthorityUpdateAndRestoreWorldTime()
        {
            using (var world = new World("Timing test"))
            using (var executor = new CombatStepExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg);
                for (int i = 0; i < 4; ++i) writableInput[i] = Player((uint)i + 1, c);
                double before = world.Time.ElapsedTime; executor.Execute(c, cfg, 0, 0, input);
                Assert.AreEqual(before, world.Time.ElapsedTime);
                Assert.AreEqual(0ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
                executor.PublishCompleted(c);
                Assert.AreEqual(1ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            }
        }
        [Test] public void MissingMandatorySystemCannotReportSuccess()
        {
            using (var world = new World("Missing stage test"))
            using (var executor = new CombatStepExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg); writableInput[0] = Player(1, c);
                world.GetExistingSystemManaged<SimulationCompleteSystem>().Enabled = false;
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, cfg, 0, 0, input));
                Assert.Throws<InvalidOperationException>(() => executor.PublishCompleted(c));
                Assert.AreEqual(0ul, world.EntityManager.GetComponentData<SimulationStepHeader>(executor.SessionEntity).CompletedOrdinal);
            }
        }
        [Test] public void DuplicatePlayerIdsAreRejected()
        {
            using (var world = new World("Duplicate ID test"))
            using (var executor = new CombatStepExecutor(world))
            using (var input = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var writableInput = input;
                var cfg = new SimulationTimingConfig(50); var c = new SimulationStepContext(1, 1, 1, cfg);
                writableInput[0] = Player(1, c); writableInput[1] = Player(1, c);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(c, cfg, 0, 0, input));
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
                    using (var world = new World("Replay test")) using (var executor = new CombatStepExecutor(world))
                        executor.Execute(r.Context, cfg, 0, 0, input);
                    Assert.IsFalse(source.TryRead(out r));
                }
            }
            finally { File.Delete(path); }
        }
    }
}
