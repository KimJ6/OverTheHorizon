using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Tests
{
    public sealed class AutoTargetingTests
    {
        private static PlayerStepElement Player(SimulationStepContext step)
            => new PlayerStepElement { Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = step.Generation,
                ConsumedOrdinal = step.Ordinal, InputSequence = 1, StartStateTick = unchecked(step.Tick - 1), EndStateTick = step.Tick,
                StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                SteeringForward = new float3(0, 0, 1), SteeringRight = new float3(1, 0, 0), ViewForward = new float3(0, 0, 1) };
        private static SimulationProgress Begin(out SimulationStepContext step)
        {
            var progress = new SimulationProgress(); progress.Reset(1);
            step = progress.Begin(new SimulationTimingConfig(50)); return progress;
        }
        private static Entity Add(World world, ulong stableId, float3 position, bool targetable = true)
        {
            var entity = world.EntityManager.CreateEntity(typeof(CombatTargetCandidate), typeof(CombatTargetEvaluation));
            world.EntityManager.SetComponentData(entity, new CombatTargetCandidate { StableId = stableId, Anchor = position, Targetable = targetable ? (byte)1 : (byte)0 });
            return entity;
        }
        private static CombatTargetAssignment Run(World world, ulong[] creationOrder, bool corrupt = false)
        {
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var frames = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                var producer = executor.RegisterStage<AutoTargetingProducerSystem>(); producer.CorruptNextResult = corrupt;
                executor.RegisterStage<AutoTargetingResolveCommitSystem>(); executor.Seal();
                foreach (var id in creationOrder) Add(world, id, new float3(0, 0, 10));
                var progress = Begin(out var step); var writable = frames; writable[0] = Player(step);
                if (corrupt) { Assert.Throws<InvalidOperationException>(() => executor.Execute(step, frames, progress)); Assert.AreEqual(0ul, progress.CompletedOrdinal); return default; }
                executor.Execute(step, frames, progress); executor.ValidatePending(step, progress);
                var assignment = world.EntityManager.GetComponentData<CombatTargetAssignment>(executor.SessionEntity);
                progress.Complete(step); executor.AcknowledgeAuthoritativeCompletion(step, progress);
                Assert.AreEqual(1ul, progress.CompletedOrdinal); return assignment;
            }
        }
        private static CombatTargetAssignment RunWithPositions(World world, params (ulong StableId, float3 Position)[] candidates)
        {
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var frames = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                executor.RegisterStage<AutoTargetingProducerSystem>(); executor.RegisterStage<AutoTargetingResolveCommitSystem>(); executor.Seal();
                foreach (var candidate in candidates) Add(world, candidate.StableId, candidate.Position);
                var progress = Begin(out var step); var writable = frames; writable[0] = Player(step);
                executor.Execute(step, frames, progress); executor.ValidatePending(step, progress);
                var assignment = world.EntityManager.GetComponentData<CombatTargetAssignment>(executor.SessionEntity);
                progress.Complete(step); executor.AcknowledgeAuthoritativeCompletion(step, progress);
                Assert.AreEqual(1ul, progress.CompletedOrdinal); return assignment;
            }
        }
        [Test] public void JobResolveCommitsTheBestEligibleStableTarget()
        {
            using (var world = new World("Target normal"))
            {
                // Centered nearer candidate wins before the StableId tie-breaker.
                var assignment = Run(world, new ulong[] { 30, 20 });
                Assert.AreEqual(1, assignment.HasTarget); Assert.AreEqual(20ul, assignment.TargetStableId);
                Assert.AreEqual(1u, assignment.PlayerId); Assert.AreEqual(1u, assignment.WeaponId);
            }
        }
        [Test] public void CandidateCreationOrderDoesNotChangeStableAssignment()
        {
            using (var first = new World("Target order A")) using (var second = new World("Target order B"))
            {
                Assert.AreEqual(Run(first, new ulong[] { 30, 10, 20 }).TargetStableId,
                    Run(second, new ulong[] { 20, 30, 10 }).TargetStableId);
            }
        }
        [Test] public void EqualScoreUsesStableLogicalIdNotEntityIndex()
        {
            using (var world = new World("Target tie"))
            {
                var assignment = Run(world, new ulong[] { 99, 7, 42 });
                Assert.AreEqual(7ul, assignment.TargetStableId);
            }
        }
        [Test] public void ViewDotWinsBeforeDistance()
        {
            using (var world = new World("Target view priority"))
            {
                var assignment = RunWithPositions(world,
                    (10ul, new float3(0, 0, 30)), (20ul, new float3(10, 0, 10)));
                Assert.AreEqual(10ul, assignment.TargetStableId);
            }
        }
        [Test] public void DistanceWinsWhenViewDotIsEqual()
        {
            using (var world = new World("Target distance priority"))
            {
                var assignment = RunWithPositions(world,
                    (10ul, new float3(0, 0, 30)), (20ul, new float3(0, 0, 10)));
                Assert.AreEqual(20ul, assignment.TargetStableId);
            }
        }
        [Test] public void NoEligibleTargetCommitsExplicitNoTarget()
        {
            using (var world = new World("Target none"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            using (var frames = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                executor.RegisterStage<AutoTargetingProducerSystem>(); executor.RegisterStage<AutoTargetingResolveCommitSystem>(); executor.Seal();
                Add(world, 1, new float3(0, 0, 101)); Add(world, 2, new float3(0, 0, -10)); Add(world, 3, new float3(0, 0, 10), false);
                var progress = Begin(out var step); var writable = frames; writable[0] = Player(step); executor.Execute(step, frames, progress);
                var result = world.EntityManager.GetComponentData<CombatTargetAssignment>(executor.SessionEntity);
                Assert.AreEqual(0, result.HasTarget); Assert.AreEqual(0ul, result.TargetStableId);
            }
        }
        [Test] public void CorruptedJobStepIdentityBlocksAuthoritativeCompletion()
        {
            using (var world = new World("Target corrupt")) Run(world, new ulong[] { 1 }, true);
        }
    }
}
