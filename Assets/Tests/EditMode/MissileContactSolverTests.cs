using System;
using NUnit.Framework;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Tests
{
    public sealed class MissileContactSolverTests
    {
        [Test]
        public void StraightSweepHandlesStationaryMovingTunnelTangentAndInitialOverlap()
        {
            Assert.AreEqual(MissileContactSolveResult.Contact,
                MissileContactSolver.TryFindStraight(new float3(-10f, 0f, 0f), new float3(10f, 0f, 0f), 0.1f,
                    default, default, 0.5f, out var tunnel));
            Assert.That(tunnel.NormalizedTime, Is.InRange(0.45f, 0.55f));

            Assert.AreEqual(MissileContactSolveResult.Contact,
                MissileContactSolver.TryFindStraight(new float3(-2f, 0f, 0f), new float3(2f, 0f, 0f), 0.1f,
                    new float3(0f, -2f, 0f), new float3(0f, 2f, 0f), 0.2f, out _));
            Assert.AreEqual(MissileContactSolveResult.Contact,
                MissileContactSolver.TryFindStraight(default, new float3(1f, 0f, 0f), 0.2f,
                    new float3(0.3f, 0f, 0f), new float3(0.3f, 0f, 0f), 0.2f, out var overlap));
            Assert.That(overlap.NormalizedTime, Is.EqualTo(0f));
            Assert.AreEqual(MissileContactSolveResult.Contact,
                MissileContactSolver.TryFindStraight(new float3(-1f, 0.3f, 0f), new float3(1f, 0.3f, 0f), 0.1f,
                    default, default, 0.2f, out _));
            Assert.AreEqual(MissileContactSolveResult.NoContact,
                MissileContactSolver.TryFindStraight(new float3(-1f, 0.31f, 0f), new float3(1f, 0.31f, 0f), 0.1f,
                    default, default, 0.2f, out _));
        }

        [Test]
        public void ArcSweepFindsAnArcOnlyCrossingAndRejectsTheChordOnlyCrossing()
        {
            MissileFlightSegment segment = QuarterArc();
            var arcOnly = new float3(0.7071068f, 0f, -0.2928932f);
            Assert.AreEqual(MissileContactSolveResult.Contact,
                MissileContactSolver.TryFindArc(segment, default, new float3(1f, 0f, 0f), 0.02f,
                    arcOnly, arcOnly, 0.02f, out _, out int subdivisions, out _));
            Assert.Greater(subdivisions, 1);

            var chordOnly = new float3(0.5f, 0f, -0.5f);
            Assert.AreEqual(MissileContactSolveResult.NoContact,
                MissileContactSolver.TryFindArc(segment, default, new float3(1f, 0f, 0f), 0.02f,
                    chordOnly, chordOnly, 0.02f, out _, out _, out _));
        }

        [Test]
        public void ArcSweepDoesNotPromoteSagittaExpandedChordCandidateToAnExactHit()
        {
            var segment = QuarterArc();
            // The sixth conservative chord of the quarter arc is displaced from the
            // true curve. This point is within that chord's sagitta expansion, but
            // outside the actual quarter-circle plus contact tolerance.
            float angle0 = math.PI * 5f / 24f;
            float angle1 = math.PI * 6f / 24f;
            float angleMid = (angle0 + angle1) * 0.5f;
            var chordMid = new float3((math.sin(angle0) + math.sin(angle1)) * 0.5f, 0f,
                (math.cos(angle0) + math.cos(angle1)) * 0.5f - 1f);
            var arcMid = new float3(math.sin(angleMid), 0f, math.cos(angleMid) - 1f);
            var nearChord = chordMid + math.normalizesafe(chordMid - arcMid) * 0.023f;
            Assert.AreEqual(MissileContactSolveResult.NoContact,
                MissileContactSolver.TryFindArc(segment, default, new float3(1f, 0f, 0f), 0.01f,
                    nearChord, nearChord, 0.01f, out _, out _, out int candidates));
            Assert.Greater(candidates, 0);
        }

        [TestCase(50)]
        [TestCase(60)]
        [TestCase(120)]
        public void ProfileBoundArcRemainsWithinTheConservativeSubdivisionLimit(int tickRate)
        {
            var segment = new MissileFlightSegment
            {
                Primitive = MissileFlightPrimitive.ConstantCurvatureArc,
                RotationAxis = new float3(0f, 1f, 0f),
                Speed = 1000f,
                Curvature = math.PI * 4f / 1000f,
                StepDeltaTime = 1f / tickRate,
                Valid = 1
            };
            Assert.AreNotEqual(MissileContactSolveResult.Indeterminate,
                MissileContactSolver.TryFindArc(segment, default, new float3(1f, 0f, 0f), 0.1f,
                    new float3(100f, 0f, 0f), new float3(100f, 0f, 0f), 0.1f,
                    out _, out int subdivisions, out _));
            Assert.LessOrEqual(subdivisions, MissileContactSolver.MaximumArcSubdivisions);
        }

        [Test]
        public void ArcSweepRejectsDegenerateOrOverBoundedInputAsIndeterminate()
        {
            var invalid = QuarterArc();
            invalid.Curvature = 0f;
            Assert.AreEqual(MissileContactSolveResult.Indeterminate,
                MissileContactSolver.TryFindArc(invalid, default, new float3(1f, 0f, 0f), 0.1f,
                    default, default, 0.1f, out _, out _, out _));

            var overBounded = QuarterArc();
            overBounded.StepDeltaTime = math.PI * 8f;
            overBounded.Speed = 1f;
            overBounded.Curvature = 1f;
            Assert.AreEqual(MissileContactSolveResult.Indeterminate,
                MissileContactSolver.TryFindArc(overBounded, default, new float3(1f, 0f, 0f), 0.1f,
                    default, default, 0.1f, out _, out int subdivisions, out _));
            Assert.Greater(subdivisions, MissileContactSolver.MaximumArcSubdivisions);
        }

        [Test]
        public void IndeterminateArcFaultsTheCombatStepBeforeAuthoritativeCompletion()
        {
            using var world = new World("Indeterminate arc contact");
            var players = new NativeArray<PlayerStepElement>(4, Allocator.Temp);
            try
            {
                var config = new SimulationTimingConfig(50);
                using var executor = new CombatStepExecutor(world, 1, config);
                executor.RegisterStage<MissileContactGatherStage>();
                executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
                executor.Seal();
                var manager = world.EntityManager;
                manager.AddBuffer<PlayerCombatBoundsStepElement>(executor.SessionEntity).Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, Generation = 1, Ordinal = 1, PlayerId = 1, SlotGeneration = 1,
                    StartCenter = new float3(0f, 0f, 5f), EndCenter = new float3(0f, 0f, 5f), Radius = 0.25f
                });
                manager.AddBuffer<MissileContactResult>(executor.SessionEntity);
                manager.AddComponentData(executor.SessionEntity, new MissileContactDiagnostics { MinimumSeparation = float.PositiveInfinity });

                var segment = QuarterArc();
                segment.StepDeltaTime = math.PI * 8f;
                segment.EndOrdinal = 2;
                Entity missile = manager.CreateEntity(typeof(MissileRuntimeSpawnState), typeof(MissileRuntimeMovementState),
                    typeof(MissileMemberPreviousPosition), typeof(MissileMemberMotionState), typeof(MissileMemberIdentity),
                    typeof(MissileContactProximity), typeof(MissileContactShape));
                manager.SetComponentData(missile, new MissileRuntimeSpawnState { SessionGeneration = 1 });
                manager.SetComponentData(missile, new MissileRuntimeMovementState
                {
                    ActiveSegment = segment, PreviousForward = new float3(1f, 0f, 0f), LastMovementOrdinal = 1
                });
                manager.SetComponentData(missile, new MissileMemberPreviousPosition { Value = default });
                manager.SetComponentData(missile, new MissileMemberMotionState { Position = new float3(0f, 0f, 1f) });
                manager.SetComponentData(missile, new MissileMemberIdentity { SessionGeneration = 1 });
                manager.SetComponentData(missile, new MissileContactShape { Radius = 0.1f });

                var progress = new SimulationProgress();
                progress.Reset(1);
                var step = progress.Begin(config);
                players[0] = ValidPlayer(step);
                Assert.Throws<InvalidOperationException>(() => executor.Execute(step, players, progress));
                Assert.AreEqual(CombatExecutionState.Faulted, executor.State);
                Assert.AreEqual(0ul, progress.CompletedOrdinal);
                Assert.AreEqual(1u, manager.GetComponentData<MissileContactDiagnostics>(executor.SessionEntity).IndeterminateArcCount);
            }
            finally { players.Dispose(); }
        }

        [Test]
        public void ArcContactWritesTheSameStepResultForAnAcknowledgedRuntimeMissile()
        {
            using var world = new World("Arc contact result");
            var players = new NativeArray<PlayerStepElement>(4, Allocator.Temp);
            try
            {
                var config = new SimulationTimingConfig(50);
                using var executor = new CombatStepExecutor(world, 1, config);
                executor.RegisterStage<MissileContactGatherStage>();
                executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
                executor.Seal();
                var manager = world.EntityManager;
                manager.AddBuffer<PlayerCombatBoundsStepElement>(executor.SessionEntity).Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, Generation = 1, Ordinal = 1, PlayerId = 1, SlotGeneration = 1,
                    StartCenter = new float3(1f, 0f, -1f), EndCenter = new float3(1f, 0f, -1f), Radius = 0.1f
                });
                manager.AddBuffer<MissileContactResult>(executor.SessionEntity);
                manager.AddComponentData(executor.SessionEntity, new MissileContactDiagnostics { MinimumSeparation = float.PositiveInfinity });
                var segment = QuarterArc();
                segment.EndOrdinal = 2;
                Entity missile = manager.CreateEntity(typeof(MissileRuntimeSpawnState), typeof(MissileRuntimeMovementState),
                    typeof(MissileMemberPreviousPosition), typeof(MissileMemberMotionState), typeof(MissileMemberIdentity),
                    typeof(MissileContactProximity), typeof(MissileContactShape));
                manager.SetComponentData(missile, new MissileRuntimeSpawnState { SessionGeneration = 1, SpawnOrdinal = 1 });
                manager.SetComponentData(missile, new MissileRuntimeMovementState
                {
                    ActiveSegment = segment, PreviousForward = new float3(1f, 0f, 0f), LastMovementOrdinal = 1
                });
                manager.SetComponentData(missile, new MissileMemberPreviousPosition { Value = default });
                manager.SetComponentData(missile, new MissileMemberMotionState { Position = new float3(1f, 0f, -1f) });
                manager.SetComponentData(missile, new MissileMemberIdentity { SessionGeneration = 1 });
                manager.SetComponentData(missile, new MissileContactShape { Radius = 0.1f });
                var progress = new SimulationProgress();
                progress.Reset(1);
                var step = progress.Begin(config);
                players[0] = ValidPlayer(step);
                executor.Execute(step, players, progress);
                Assert.AreEqual(1, manager.GetBuffer<MissileContactResult>(executor.SessionEntity).Length);
                Assert.AreEqual(1, manager.GetComponentData<MissileRuntimeMovementState>(missile).ContactPending);
                executor.ValidatePending(step, progress);
                progress.Complete(step);
                executor.AcknowledgeAuthoritativeCompletion(step, progress);
            }
            finally { players.Dispose(); }
        }

        private static MissileFlightSegment QuarterArc()
        {
            float theta = math.PI * 0.5f;
            return new MissileFlightSegment
            {
                Primitive = MissileFlightPrimitive.ConstantCurvatureArc,
                RotationAxis = new float3(0f, 1f, 0f),
                Speed = 1f,
                Curvature = 1f,
                StepDeltaTime = theta,
                Valid = 1
            };
        }

        private static PlayerStepElement ValidPlayer(SimulationStepContext step) => new PlayerStepElement
        {
            Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = step.Generation, ConsumedOrdinal = step.Ordinal,
            InputSequence = 1, StartStateTick = unchecked(step.Tick - 1), EndStateTick = step.Tick,
            StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
            SteeringForward = new float3(0f, 0f, 1f), SteeringRight = new float3(1f, 0f, 0f), ViewForward = new float3(0f, 0f, 1f)
        };
    }
}
