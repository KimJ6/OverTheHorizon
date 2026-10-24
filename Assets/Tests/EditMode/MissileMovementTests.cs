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
    public sealed class MissileMovementTests
    {
        [Test]
        public void SpawnedStraightSegmentMovesOnceAndPreservesPreviousPosition()
        {
            using var fixture = new Fixture(50);
            var segment = Straight(new float3(1f, 2f, 3f), new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 5);
            Entity missile = fixture.CreateActiveMissile(segment, segment.StartPosition, segment.StartForward, 10f);

            fixture.Run();

            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(missile);
            var previous = fixture.Manager.GetComponentData<MissileMemberPreviousPosition>(missile);
            Assert.That(previous.Value, Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(math.distance(motion.Position, new float3(1f, 2f, 3.2f)), Is.LessThan(0.00001f));
            Assert.That(math.dot(motion.Forward, new float3(0f, 0f, 1f)), Is.GreaterThan(0.99999f));
        }

        [Test]
        public void ArcExecutionMatchesTheM1DPureIntegrator()
        {
            using var fixture = new Fixture(60);
            var request = GuidanceRequest(1f / 60f, 1);
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), Variation(), Profile(), request,
                out var segment, out _));
            Entity missile = fixture.CreateActiveMissile(segment, default, new float3(0f, 0f, 1f), segment.Speed);
            Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(segment, default, new float3(0f, 0f, 1f),
                out var expectedPosition, out var expectedForward));

            fixture.Run();

            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(missile);
            Assert.That(math.distance(motion.Position, expectedPosition), Is.LessThan(0.00001f));
            Assert.That(math.dot(motion.Forward, expectedForward), Is.GreaterThan(0.99999f));
        }

        [Test]
        public void FutureSegmentDoesNotMoveBeforeStartOrdinalButMovesAtStartOrdinal()
        {
            using var fixture = new Fixture(50);
            var segment = Straight(default, new float3(1f, 0f, 0f), 10f, 1f / 50f, 2, 5);
            Entity missile = fixture.CreateActiveMissile(segment, default, segment.StartForward, 10f);

            fixture.Run();
            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(missile).Position, Is.EqualTo(default(float3)));
            fixture.Run();
            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(missile).Position.x, Is.EqualTo(0.2f).Within(0.00001f));
        }

        [Test]
        public void ExpiredSegmentContinuesStraightAtTheLastTangentWithoutGuidance()
        {
            using var fixture = new Fixture(50);
            var segment = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 2);
            segment.EndOrdinal = 1;
            Entity missile = fixture.CreateActiveMissile(segment, default, segment.StartForward, 10f);

            fixture.Run();

            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(missile);
            Assert.That(motion.Position.z, Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(math.dot(motion.Forward, segment.StartForward), Is.GreaterThan(0.99999f));
        }

        [Test]
        public void SegmentWithMismatchedDeltaTimeIsRejectedWithoutMoving()
        {
            using var fixture = new Fixture(50);
            var segment = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 60f, 1, 5);
            Entity missile = fixture.CreateActiveMissile(segment, default, segment.StartForward, 10f);

            fixture.Run();

            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(missile).Position, Is.EqualTo(default(float3)));
            Assert.That(fixture.Manager.GetComponentData<MissileMemberPreviousPosition>(missile).Value, Is.EqualTo(default(float3)));
        }

        [Test]
        public void PendingOrWrongGenerationMembersAreExcludedFromMovement()
        {
            using var fixture = new Fixture(50);
            var segment = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 5);
            Entity pending = fixture.CreateActiveMissile(segment, default, segment.StartForward, 10f);
            var runtime = fixture.Manager.GetComponentData<MissileRuntimeSpawnState>(pending);
            runtime.SessionGeneration = 2;
            fixture.Manager.SetComponentData(pending, runtime);

            fixture.Run();

            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(pending).Position, Is.EqualTo(default(float3)));
        }

        [TestCase(50)]
        [TestCase(60)]
        [TestCase(120)]
        public void StraightMovementKeepsPhysicalDistanceAcrossSupportedTickRates(int tickRate)
        {
            using var fixture = new Fixture(tickRate);
            float deltaTime = 1f / tickRate;
            var segment = Straight(default, new float3(0f, 0f, 1f), 12f, deltaTime, 1, (ulong)tickRate + 1);
            Entity missile = fixture.CreateActiveMissile(segment, default, segment.StartForward, 12f);

            for (int i = 0; i < tickRate; ++i) fixture.Run();

            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(missile).Position.z, Is.EqualTo(12f).Within(0.0002f));
        }

        [TestCase(1)]
        [TestCase(50)]
        [TestCase(300)]
        public void ActiveMissilesMoveIndependentlyAtScale(int count)
        {
            using var fixture = new Fixture(60);
            var segment = Straight(default, new float3(0f, 0f, 1f), 6f, 1f / 60f, 1, 5);
            var missiles = new Entity[count];
            for (int i = 0; i < count; ++i)
                missiles[i] = fixture.CreateActiveMissile(segment, new float3(i, 0f, 0f), segment.StartForward, 6f);

            fixture.Run();

            for (int i = 0; i < count; ++i)
            {
                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(missiles[i]);
                Assert.That(motion.Position.x, Is.EqualTo(i).Within(0.00001f));
                Assert.That(motion.Position.z, Is.EqualTo(0.1f).Within(0.00001f));
            }
        }

        private static MissileFlightSegment Straight(float3 position, float3 forward, float speed, float deltaTime,
            ulong startOrdinal, ulong endOrdinal)
        {
            Assert.IsTrue(MissileGuidanceSolver.TryCreateStraightSegment(position, forward, speed, deltaTime,
                startOrdinal, endOrdinal, 1, out var segment));
            return segment;
        }

        private static MissileApproachProfile Profile() => new MissileApproachProfile
        {
            PredictionTime = 0.5f, MaxLateralBias = 20f, MaxVerticalBias = 10f, MaxCurveStrength = 1f,
            MaxCommandPhase = 0.4f, Speed = 10f, MaxSnapAngleRadians = 0f, AttackCommitMaxSnapAngleRadians = 0f,
            TurnRateRadiansPerSecond = math.radians(180f), MaxTurnRadiansPerTick = math.radians(30f),
            SegmentDuration = 0.5f, LaunchHoldDuration = 0.1f
        };

        private static SalvoAttackPlan Plan(float3 target) => new SalvoAttackPlan
        {
            PredictedTarget = target, Forward = new float3(0f, 0f, 1f), Right = new float3(1f, 0f, 0f),
            Up = new float3(0f, 1f, 0f), SourceSampleOrdinal = 1, SourceSampleRevision = 1, Valid = 1
        };

        private static MissileMemberTrajectoryVariation Variation()
            => new MissileMemberTrajectoryVariation { Motif = MissileTrajectoryMotif.CurvedWideApproach, CurveStrength = 1f, Valid = 1 };

        private static MissileGuidanceRequest GuidanceRequest(float deltaTime, ulong ordinal)
            => new MissileGuidanceRequest
            {
                Motion = new MissileGuidanceMotion { Position = default, Forward = new float3(0f, 0f, 1f), Speed = 10f },
                State = MissileGuidanceState.HomingArc, DeltaTime = deltaTime, StartOrdinal = ordinal, CommandSequence = 1
            };

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config;
            private readonly SimulationProgress progress = new SimulationProgress();
            private NativeArray<PlayerStepElement> players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);
            public readonly World World = new World("Missile M2-B Movement");
            public readonly CombatStepExecutor Executor;
            public EntityManager Manager => World.EntityManager;

            public Fixture(int tickRate)
            {
                config = new SimulationTimingConfig(tickRate);
                Executor = new CombatStepExecutor(World, 1, config);
                Executor.RegisterStage<MissilePlanAndCommandStage>();
                Executor.RegisterStage<MissileSpawnAndCommandCommitStage>();
                Executor.RegisterStage<MissileMovementStage>();
                Executor.RegisterStage<MissileContactGatherStage>();
                Executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
                Executor.Seal();
                MissileSalvoTargetSampling.EnsureCalendar(Manager, Executor.SessionEntity);
                MissileMemberCommandScheduling.EnsureCalendar(Manager, Executor.SessionEntity);
                progress.Reset(1);
            }

            public Entity CreateActiveMissile(MissileFlightSegment segment, float3 position, float3 forward, float speed)
            {
                Entity missile = Manager.CreateEntity(typeof(MissileRuntimeSpawnState), typeof(MissileRuntimeMovementState),
                    typeof(MissileMemberPreviousPosition), typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberIdentity),
                    typeof(MissileMemberSalvoReference), typeof(MissileMemberLaunchState), typeof(MissileMemberCommandState),
                    typeof(MissileMemberMotionState));
                Manager.SetComponentData(missile, new MissileRuntimeSpawnState { SessionGeneration = 1, SpawnOrdinal = 1 });
                Manager.SetComponentData(missile, new MissileRuntimeMovementState { ActiveSegment = segment, HasActiveSegment = 1 });
                Manager.SetComponentData(missile, new MissileMemberPreviousPosition { Value = position });
                Manager.SetComponentData(missile, new MissileMemberSpawnAcknowledgement { Acknowledged = 1, SessionGeneration = 1, ActualSpawnOrdinal = 1 });
                Manager.SetComponentData(missile, new MissileMemberIdentity { SessionGeneration = 1 });
                Manager.SetComponentData(missile, new MissileMemberSalvoReference { SessionGeneration = 1, Active = 1 });
                Manager.SetComponentData(missile, new MissileMemberCommandState { Active = 1, HasCurrentSegment = 1, CurrentSegment = segment });
                Manager.SetComponentData(missile, new MissileMemberMotionState { Position = position, Forward = forward, Speed = speed });
                return missile;
            }

            public void Run()
            {
                var step = progress.Begin(config);
                players[0] = new PlayerStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = step.Generation, ConsumedOrdinal = step.Ordinal,
                    InputSequence = 1, StartStateTick = unchecked(step.Tick - 1), EndStateTick = step.Tick,
                    StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                    SteeringForward = new float3(0f, 0f, 1f), SteeringRight = new float3(1f, 0f, 0f), ViewForward = new float3(0f, 0f, 1f)
                };
                Executor.Execute(step, players, progress);
                Executor.ValidatePending(step, progress);
                progress.Complete(step);
                Executor.AcknowledgeAuthoritativeCompletion(step, progress);
            }

            public void Dispose()
            {
                if (players.IsCreated) players.Dispose();
                Executor.Dispose();
                World.Dispose();
            }
        }
    }
}
