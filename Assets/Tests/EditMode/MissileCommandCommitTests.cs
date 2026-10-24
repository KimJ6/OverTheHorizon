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
    public sealed class MissileCommandCommitTests
    {
        [TestCase(50)]
        [TestCase(60)]
        [TestCase(120)]
        public void CurrentFlightSegmentCommitsBeforeMovementAtItsEffectiveOrdinal(int tickRate)
        {
            using var fixture = new Fixture(tickRate);
            float deltaTime = 1f / tickRate;
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, deltaTime, 1, 10, 1);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            var request = ArcRequest(deltaTime, 1, 2);
            Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(request.Segment, default, initial.StartForward,
                out var expectedPosition, out var expectedForward));
            fixture.SetRequest(member, request);

            fixture.Run();

            var active = fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ActiveSegment;
            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
            Assert.AreEqual(2u, active.CommandSequence);
            Assert.That(math.distance(motion.Position, expectedPosition), Is.LessThan(0.00001f));
            Assert.That(math.dot(motion.Forward, expectedForward), Is.GreaterThan(0.99999f));
            Assert.AreEqual(MissileCommandRequestKind.None, fixture.Manager.GetComponentData<MissileMemberCommandRequest>(member).Kind);
        }

        [Test]
        public void DuplicateOrStaleRequestDoesNotReplaceTheAppliedSegmentOrProducerSequence()
        {
            using var fixture = new Fixture(50);
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 10, 2);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            var duplicate = new MissileMemberCommandRequest
            {
                Kind = MissileCommandRequestKind.FlightSegment, Ordinal = 1, CommandSequence = 2, Segment = initial, Pending = 1
            };
            var command = fixture.Manager.GetComponentData<MissileMemberCommandState>(member);
            command.NextCommandSequence = 7;
            fixture.Manager.SetComponentData(member, command);
            fixture.SetRequest(member, duplicate);

            fixture.Run();

            Assert.AreEqual(2u, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ActiveSegment.CommandSequence);
            Assert.AreEqual(7u, fixture.Manager.GetComponentData<MissileMemberCommandState>(member).NextCommandSequence);
            Assert.AreEqual(1u, fixture.Counters().DuplicateOrStaleCommandCount);
        }

        [Test]
        public void WrongGenerationRequestIsRejectedWithoutChangingRuntimeMotion()
        {
            using var fixture = new Fixture(50);
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 10, 1);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            var runtime = fixture.Manager.GetComponentData<MissileRuntimeSpawnState>(member);
            runtime.SessionGeneration = 2;
            fixture.Manager.SetComponentData(member, runtime);
            fixture.SetRequest(member, ArcRequest(1f / 50f, 1, 2));

            fixture.Run();

            Assert.AreEqual(1u, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ActiveSegment.CommandSequence);
            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(member).Position, Is.EqualTo(default(float3)));
            Assert.AreEqual(1u, fixture.Counters().CommandRejectedCount);
        }

        [Test]
        public void StraightFallbackUsesTheCurrentTangentWithoutReplanning()
        {
            using var fixture = new Fixture(50);
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 10, 1);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            var fallback = Straight(default, initial.StartForward, 10f, 1f / 50f, 1, 10, 2);
            fixture.SetRequest(member, new MissileMemberCommandRequest
            {
                Kind = MissileCommandRequestKind.StraightFallback, Ordinal = 1, CommandSequence = 2, Segment = fallback, Pending = 1
            });

            fixture.Run();

            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
            Assert.AreEqual(2u, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ActiveSegment.CommandSequence);
            Assert.That(motion.Position.z, Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(math.dot(motion.Forward, initial.StartForward), Is.GreaterThan(0.99999f));
        }

        [Test]
        public void ExpireCommitStopsMovementWithoutDestroyingTheRuntimeEntity()
        {
            using var fixture = new Fixture(50);
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 10, 1);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            fixture.SetRequest(member, new MissileMemberCommandRequest { Kind = MissileCommandRequestKind.Expire, Ordinal = 1, Pending = 1 });

            fixture.Run();

            Assert.IsTrue(fixture.Manager.Exists(member));
            Assert.AreEqual(0, fixture.Manager.GetComponentData<MissileMemberCommandState>(member).Active);
            Assert.AreEqual(0, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).HasActiveSegment);
            Assert.That(fixture.Manager.GetComponentData<MissileMemberMotionState>(member).Position, Is.EqualTo(default(float3)));
            Assert.AreEqual(1u, fixture.Counters().ExpireCount);
        }

        [Test]
        public void LaunchHoldRejectsAnArcRequestAndKeepsTheLaunchAxis()
        {
            using var fixture = new Fixture(50);
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, 1f / 50f, 1, 10, 1);
            Entity member = fixture.CreateRuntimeMember(initial, default, initial.StartForward, 10f);
            fixture.Manager.AddComponentData(member, new MissileMemberLaunchHoldState { Active = 1, GuidanceEnableOrdinal = 5 });
            fixture.SetRequest(member, ArcRequest(1f / 50f, 1, 2));

            fixture.Run();

            var active = fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ActiveSegment;
            var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
            Assert.AreEqual(1u, active.CommandSequence);
            Assert.That(motion.Position.z, Is.EqualTo(0.2f).Within(0.00001f));
            Assert.AreEqual(1u, fixture.Counters().CommandRejectedCount);
        }

        [TestCase(50)]
        [TestCase(100)]
        [TestCase(300)]
        public void PendingCommandsCommitOnceForEveryRuntimeMemberAtScale(int count)
        {
            using var fixture = new Fixture(60);
            float deltaTime = 1f / 60f;
            var initial = Straight(default, new float3(0f, 0f, 1f), 10f, deltaTime, 1, 10, 1);
            var replacement = Straight(default, initial.StartForward, 10f, deltaTime, 1, 10, 2);
            var members = new Entity[count];
            for (int i = 0; i < count; ++i)
            {
                members[i] = fixture.CreateRuntimeMember(initial, new float3(i, 0f, 0f), initial.StartForward, 10f);
                var segment = replacement;
                segment.StartPosition = new float3(i, 0f, 0f);
                fixture.SetRequest(members[i], new MissileMemberCommandRequest
                {
                    Kind = MissileCommandRequestKind.FlightSegment, Ordinal = 1, CommandSequence = 2, Segment = segment, Pending = 1
                });
            }

            fixture.Run();

            for (int i = 0; i < count; ++i)
                Assert.AreEqual(2u, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(members[i]).ActiveSegment.CommandSequence);
            Assert.AreEqual((uint)count, fixture.Counters().CommandCommitSuccessCount);
        }

        private static MissileFlightSegment Straight(float3 position, float3 forward, float speed, float deltaTime,
            ulong startOrdinal, ulong endOrdinal, uint sequence)
        {
            Assert.IsTrue(MissileGuidanceSolver.TryCreateStraightSegment(position, forward, speed, deltaTime,
                startOrdinal, endOrdinal, sequence, out var segment));
            return segment;
        }

        private static MissileMemberCommandRequest ArcRequest(float deltaTime, ulong ordinal, uint sequence)
        {
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), Variation(), Profile(),
                new MissileGuidanceRequest
                {
                    Motion = new MissileGuidanceMotion { Position = default, Forward = new float3(0f, 0f, 1f), Speed = 10f },
                    State = MissileGuidanceState.HomingArc, DeltaTime = deltaTime, StartOrdinal = ordinal, CommandSequence = sequence
                }, out var segment, out _));
            return new MissileMemberCommandRequest
            {
                Kind = MissileCommandRequestKind.FlightSegment, Ordinal = ordinal, CommandSequence = sequence, Segment = segment, Pending = 1
            };
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

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config;
            private readonly SimulationProgress progress = new SimulationProgress();
            private NativeArray<PlayerStepElement> players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);
            public readonly World World = new World("Missile M2-C Command Commit");
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

            public Entity CreateRuntimeMember(MissileFlightSegment initial, float3 position, float3 forward, float speed)
            {
                var key = new SalvoLogicalKey(901, 1);
                Entity salvo = Manager.CreateEntity(typeof(SalvoIdentity));
                Manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
                Entity member = Manager.CreateEntity(typeof(MissileRuntimeSpawnState), typeof(MissileRuntimeMovementState),
                    typeof(MissileRuntimeGuidanceState),
                    typeof(MissileMemberPreviousPosition), typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference),
                    typeof(MissileMemberLaunchState), typeof(MissileMemberCommandState), typeof(MissileMemberGuidanceState), typeof(MissileMemberMotionState),
                    typeof(MissileMemberCommandRequest));
                Manager.SetComponentData(member, new MissileRuntimeSpawnState { SessionGeneration = 1, SpawnOrdinal = 1 });
                Manager.SetComponentData(member, new MissileRuntimeMovementState { ActiveSegment = initial, HasActiveSegment = 1 });
                Manager.SetComponentData(member, new MissileRuntimeGuidanceState { Value = MissileGuidanceState.LaunchBurst });
                Manager.SetComponentData(member, new MissileMemberPreviousPosition { Value = position });
                Manager.SetComponentData(member, new MissileMemberSpawnAcknowledgement
                { Acknowledged = 1, SessionGeneration = 1, ActualSpawnOrdinal = 1, Position = position, LaunchForward = forward });
                Manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, 1), SessionGeneration = 1 });
                Manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
                Manager.SetComponentData(member, new MissileMemberCommandState { Active = 1, HasCurrentSegment = 1, CurrentSegment = initial });
                Manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.LaunchBurst });
                Manager.SetComponentData(member, new MissileMemberMotionState { Position = position, Forward = forward, Speed = speed });
                return member;
            }

            public void SetRequest(Entity member, MissileMemberCommandRequest request) => Manager.SetComponentData(member, request);
            public MissileCommandSchedulingCounters Counters() => Manager.GetComponentData<MissileCommandSchedulingCounters>(Executor.SessionEntity);

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
