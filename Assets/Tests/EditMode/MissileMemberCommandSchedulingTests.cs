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
    public sealed class MissileMemberCommandSchedulingTests
    {
        private static readonly MissileApproachProfile Profile = new MissileApproachProfile
        {
            PredictionTime = 0.5f,
            MaxLateralBias = 20f,
            MaxVerticalBias = 10f,
            MaxCurveStrength = 1f,
            MaxCommandPhase = 0.4f,
            Speed = 50f,
            MaxSnapAngleRadians = math.radians(60f),
            AttackCommitMaxSnapAngleRadians = math.radians(15f),
            TurnRateRadiansPerSecond = math.radians(180f),
            MaxTurnRadiansPerTick = math.radians(30f),
            SegmentDuration = 0.1f,
            LaunchHoldDuration = 0.1f
        };

        [Test]
        public void SpawnAcknowledgementKeepsCommonLaunchAxisUntilStaggeredAcquire()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(90);
                Entity first = fixture.CreateMember(salvo, 0, 0f);
                Entity second = fixture.CreateMember(salvo, 1, 0f);
                var delayedVariation = fixture.Manager.GetComponentData<MissileMemberTrajectoryVariation>(second);
                delayedVariation.CommandPhase = 0.04f;
                fixture.Manager.SetComponentData(second, delayedVariation);
                fixture.Register(first, second);
                fixture.RunTo(1);

                var pose = new float3(3f, 2f, -4f);
                var axis = math.normalize(new float3(0f, 0.2f, 1f));
                Assert.IsTrue(fixture.Acknowledge(first, 1, pose, axis));
                Assert.IsTrue(fixture.Acknowledge(second, 1, pose, axis));

                Assert.That(math.dot(axis, fixture.Command(first).CurrentSegment.StartForward), Is.GreaterThan(0.99999f));
                Assert.That(math.dot(axis, fixture.Command(second).CurrentSegment.StartForward), Is.GreaterThan(0.99999f));
                Assert.AreEqual(6ul, fixture.Hold(first).GuidanceEnableOrdinal);
                Assert.AreEqual(8ul, fixture.Hold(second).GuidanceEnableOrdinal);
                Assert.AreEqual(0u, fixture.Counters.GuidanceSolverCallCount);

                fixture.RunTo(6);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
                Assert.AreEqual(MissileGuidanceState.Acquire, fixture.Guidance(first).Value);
                Assert.AreEqual(1, fixture.Hold(second).Active);
                var firstAcquire = fixture.Command(first).CurrentSegment;
                Assert.AreEqual(MissileFlightPrimitive.ConstantCurvatureArc, firstAcquire.Primitive);
                float firstTurn = math.abs(math.atan2(firstAcquire.SinStep, firstAcquire.CosStep));
                Assert.LessOrEqual(firstTurn, Profile.TurnRateRadiansPerSecond / 50f + 0.0001f);
                Assert.LessOrEqual(firstTurn, Profile.MaxTurnRadiansPerTick + 0.0001f);

                fixture.RunTo(8);
                Assert.AreEqual(2u, fixture.Counters.GuidanceSolverCallCount);
            }
        }

        [Test]
        public void LaunchHoldLongerThanSegmentDurationRenewsStraightWithoutGuidanceSolver()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(91);
                Entity profile = fixture.Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                var tuning = fixture.Manager.GetComponentData<MissileApproachProfile>(profile);
                tuning.LaunchHoldDuration = 0.35f;
                fixture.Manager.SetComponentData(profile, tuning);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                Assert.IsTrue(fixture.Acknowledge(member, 1, default, new float3(0f, 0f, 1f)));
                Assert.AreEqual(19ul, fixture.Hold(member).GuidanceEnableOrdinal);

                fixture.RunTo(18);
                Assert.AreEqual(0u, fixture.Counters.GuidanceSolverCallCount);
                Assert.AreEqual(MissileFlightPrimitive.Straight, fixture.Command(member).CurrentSegment.Primitive);
                Assert.LessOrEqual(fixture.Command(member).CurrentSegment.EndOrdinal - fixture.Command(member).CurrentSegment.StartOrdinal, 5ul);
                fixture.RunTo(19);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
            }
        }

        [Test]
        public void EmitterDestructionAfterLaunchRequestCancelsSpawnAcknowledgement()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(92);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                fixture.SetEmitterDestroyed(salvo);

                Assert.IsFalse(fixture.Acknowledge(member, 1, default, new float3(0f, 0f, 1f)));
                Assert.AreEqual(1, fixture.Launch(member).Cancelled);
                Assert.AreEqual(MissileCommandRequestKind.Cancel, fixture.Request(member).Kind);
                Assert.AreEqual(0, fixture.Command(member).Active);
            }
        }

        [TestCase(50, 6ul)]
        [TestCase(60, 7ul)]
        [TestCase(120, 13ul)]
        public void GuidanceEnableKeepsHoldPhysicalTimeAcrossSupportedTickRates(int tickRate, ulong expectedOrdinal)
        {
            Assert.AreEqual(expectedOrdinal, MissileMemberCommandScheduling.CalculateGuidanceEnableOrdinal(1, 0.1f, 0f, tickRate));
        }

        [Test]
        public void SpawnAcknowledgementRejectsPastAndFutureOrdinals()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(93);
                Entity past = fixture.CreateMember(salvo, 0, 0f);
                Entity future = fixture.CreateMember(salvo, 1, 0f);
                fixture.Register(past, future);
                fixture.RunTo(1);

                Assert.Throws<ArgumentException>(() => fixture.Acknowledge(past, 0, default, new float3(0f, 0f, 1f)));
                Assert.Throws<ArgumentException>(() => fixture.Acknowledge(future, 2, default, new float3(0f, 0f, 1f)));
                Assert.AreEqual(0, fixture.Command(past).Active);
                Assert.AreEqual(0, fixture.Command(future).Active);
            }
        }

        [Test]
        public void LaunchDueSupportsNonMonotonicDelaysWithoutDuplicates()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(1);
                Entity delayed = fixture.CreateMember(salvo, 0, 0.2f);
                Entity immediate = fixture.CreateMember(salvo, 1, 0f);
                Entity middle = fixture.CreateMember(salvo, 2, 0.1f);

                fixture.Register(delayed, middle, immediate);
                fixture.RunTo(1);
                AssertRequest(fixture, immediate, MissileCommandRequestKind.Launch, 1);
                Assert.That(fixture.Request(middle).Kind, Is.EqualTo(MissileCommandRequestKind.None));
                Assert.That(fixture.Request(delayed).Kind, Is.EqualTo(MissileCommandRequestKind.None));

                fixture.RunTo(6);
                AssertRequest(fixture, middle, MissileCommandRequestKind.Launch, 6);
                fixture.RunTo(11);
                AssertRequest(fixture, delayed, MissileCommandRequestKind.Launch, 11);
                fixture.RunTo(20);

                var counters = fixture.Counters;
                Assert.AreEqual(3u, counters.LaunchDueCount);
                Assert.AreEqual(0u, counters.GuidanceSolverCallCount);
            }
        }

        [TestCase(50, 11ul)]
        [TestCase(60, 13ul)]
        [TestCase(120, 25ul)]
        public void LaunchDelayUsesTheSamePhysicalTimeAtSupportedTickRates(int tickRate, ulong expectedDue)
        {
            Assert.AreEqual(expectedDue, MissileMemberCommandScheduling.CalculateLaunchDueOrdinal(1, 0.2f, tickRate));
        }

        [Test]
        public void InitialAndEndDueGenerateExactlyOneSegmentEach()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(2);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                fixture.Activate(member, 2);
                fixture.RunTo(2);

                var first = fixture.Command(member);
                Assert.AreEqual(1u, first.NextCommandSequence);
                Assert.AreEqual(2ul, first.CurrentSegment.StartOrdinal);
                Assert.Greater(first.CurrentSegment.EndOrdinal, 2ul);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);

                fixture.RunTo(first.CurrentSegment.EndOrdinal - 1);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
                fixture.RunTo(first.CurrentSegment.EndOrdinal);

                var second = fixture.Command(member);
                Assert.AreEqual(2u, second.NextCommandSequence);
                Assert.AreEqual(first.CurrentSegment.EndOrdinal, second.CurrentSegment.StartOrdinal);
                Assert.AreEqual(2u, fixture.Counters.GuidanceSolverCallCount);
            }
        }

        [Test]
        public void TargetLossKeepsCurrentSegmentUntilEndThenPublishesStraightFallback()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(3);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                fixture.Activate(member, 2);
                fixture.RunTo(2);
                var beforeLoss = fixture.Command(member).CurrentSegment;

                fixture.InvalidateTarget(salvo);
                fixture.RunTo(beforeLoss.EndOrdinal - 1);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
                fixture.RunTo(beforeLoss.EndOrdinal);

                AssertRequest(fixture, member, MissileCommandRequestKind.StraightFallback, beforeLoss.EndOrdinal);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
                Assert.AreEqual(1u, fixture.Counters.StraightFallbackCount);
            }
        }

        [Test]
        public void TargetRevisionChangeDoesNotReplanAnActiveSegmentEarly()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(4);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                fixture.Activate(member, 2);
                fixture.RunTo(2);
                var current = fixture.Command(member).CurrentSegment;

                fixture.ReplacePlanForNewRevision(salvo, 2);
                fixture.RunTo(current.EndOrdinal - 1);

                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
                Assert.AreEqual(1u, fixture.Command(member).NextCommandSequence);
            }
        }

        [Test]
        public void ChangedTargetWithoutCurrentPlanCannotUseTheOldPlanAtEndDue()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(40);
                Entity member = fixture.CreateMember(salvo, 0, 0f);
                fixture.Register(member);
                fixture.RunTo(1);
                fixture.Activate(member, 2);
                fixture.RunTo(2);
                var current = fixture.Command(member).CurrentSegment;

                fixture.ChangeTargetWithoutRefreshingPlan(salvo);
                fixture.RunTo(current.EndOrdinal);

                AssertRequest(fixture, member, MissileCommandRequestKind.StraightFallback, current.EndOrdinal);
                Assert.AreEqual(1u, fixture.Counters.GuidanceSolverCallCount);
            }
        }

        [Test]
        public void EmitterDestructionCancelsPendingMemberButKeepsLaunchedMemberActive()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(5);
                Entity launched = fixture.CreateMember(salvo, 0, 0f);
                Entity pending = fixture.CreateMember(salvo, 1, 0.04f);
                fixture.Register(launched, pending);
                fixture.RunTo(1);
                fixture.Activate(launched, 2);
                fixture.RunTo(2);
                fixture.SetEmitterDestroyed(salvo);
                fixture.RunTo(3);

                AssertRequest(fixture, pending, MissileCommandRequestKind.Cancel, 3);
                Assert.AreEqual(1, fixture.Launch(pending).Cancelled);
                Assert.AreEqual(1, fixture.Command(launched).Active);
                Assert.AreEqual(1, fixture.Command(launched).HasCurrentSegment);
            }
        }

        [Test]
        public void DeletedDueHoleDoesNotStarveAnotherMember()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(6);
                Entity deleted = fixture.CreateMember(salvo, 0, 0f);
                Entity surviving = fixture.CreateMember(salvo, 1, 0f);
                fixture.Register(deleted, surviving);
                fixture.Manager.DestroyEntity(deleted);

                fixture.RunTo(1);

                AssertRequest(fixture, surviving, MissileCommandRequestKind.Launch, 1);
                Assert.AreEqual(1u, fixture.Counters.LaunchDueCount);
                Assert.GreaterOrEqual(fixture.Counters.CancelledOrInvalidDueCount, 1u);
            }
        }

        [Test]
        public void SessionMismatchIsRejectedWithoutBlockingOtherDueMembers()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(7);
                Entity stale = fixture.CreateMember(salvo, 0, 0f);
                Entity valid = fixture.CreateMember(salvo, 1, 0f);
                fixture.Register(stale, valid);
                var reference = fixture.Manager.GetComponentData<MissileMemberSalvoReference>(stale);
                reference.SessionGeneration = 2;
                fixture.Manager.SetComponentData(stale, reference);

                fixture.RunTo(1);

                Assert.AreEqual(MissileCommandRequestKind.None, fixture.Request(stale).Kind);
                AssertRequest(fixture, valid, MissileCommandRequestKind.Launch, 1);
                Assert.GreaterOrEqual(fixture.Counters.CancelledOrInvalidDueCount, 1u);
            }
        }

        [Test]
        public void ThreeHundredMembersAreAllIssuedOneLaunchRequestWithoutStarvation()
        {
            using (var fixture = new Fixture(50))
            {
                Entity salvo = fixture.CreateSalvo(8);
                var members = new Entity[300];
                for (uint i = 0; i < members.Length; ++i)
                    members[i] = fixture.CreateMember(salvo, i, (i * 7u % 11u) * 0.02f);
                for (int i = members.Length - 1; i >= 0; --i) fixture.Register(members[i]);

                fixture.RunTo(11);
                for (int i = 0; i < members.Length; ++i)
                    AssertRequest(fixture, members[i], MissileCommandRequestKind.Launch, fixture.Launch(members[i]).DueOrdinal);
                fixture.RunTo(20);

                var counters = fixture.Counters;
                Assert.AreEqual(300u, counters.LaunchDueCount);
                Assert.AreEqual(0u, counters.GuidanceSolverCallCount);
                Assert.Greater(counters.PeakDuePerStep, 1u);
            }
        }

        private static void AssertRequest(Fixture fixture, Entity member, MissileCommandRequestKind kind, ulong ordinal)
        {
            var request = fixture.Request(member);
            Assert.AreEqual(kind, request.Kind);
            Assert.AreEqual(ordinal, request.Ordinal);
            Assert.AreEqual(1, request.Pending);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config;
            private readonly SimulationProgress progress = new SimulationProgress();
            private readonly NativeArray<PlayerStepElement> players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);

            public readonly World World = new World("Missile M1-E command scheduling");
            public readonly CombatStepExecutor Executor;
            public EntityManager Manager => World.EntityManager;
            public ulong Ordinal { get; private set; }
            public MissileCommandSchedulingCounters Counters => Manager.GetComponentData<MissileCommandSchedulingCounters>(Executor.SessionEntity);

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

            public Entity CreateSalvo(ulong sequence)
            {
                Entity profile = Manager.CreateEntity(typeof(MissileApproachProfile));
                Manager.SetComponentData(profile, Profile);
                Entity salvo = Manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoLaunchSnapshot),
                    typeof(SalvoProfileReference), typeof(SalvoAttackPlan), typeof(SalvoSchedule), typeof(MissileSalvoLaunchControl));
                var key = new SalvoLogicalKey(1000 + sequence, checked((uint)sequence));
                var sample = new SalvoTargetContext
                {
                    Target = new SessionPlayerKey(1, 1, 1), HasTarget = 1, SampledTarget = new SessionPlayerKey(1, 1, 1),
                    Position = new float3(10f, 0f, 100f), Velocity = default, SampleOrdinal = 1, Revision = 1, SampleValid = 1
                };
                var launch = new SalvoLaunchSnapshot { Position = default, Forward = new float3(0f, 0f, 1f), SessionGeneration = 1 };
                Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(launch, sample, Profile, out var plan));
                Manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
                Manager.SetComponentData(salvo, sample);
                Manager.SetComponentData(salvo, launch);
                Manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profile });
                Manager.SetComponentData(salvo, plan);
                Manager.SetComponentData(salvo, new SalvoSchedule { CreatedOrdinal = 1, MemberCount = 300 });
                return salvo;
            }

            public Entity CreateMember(Entity salvo, uint ordinal, float delay)
            {
                Entity profile = Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                SalvoLogicalKey salvoKey = Manager.GetComponentData<SalvoIdentity>(salvo).Key;
                Entity member = Manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference),
                    typeof(MissileMemberProfileReference), typeof(MissileMemberTrajectoryVariation), typeof(MissileMemberLaunchState),
                    typeof(MissileMemberCommandState), typeof(MissileMemberGuidanceState), typeof(MissileMemberMotionState),
                    typeof(MissileMemberCommandRequest), typeof(MissileMemberDueState), typeof(MissileMemberLaunchCellAssignment),
                    typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberLaunchHoldState));
                Manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(salvoKey, ordinal), SessionGeneration = 1 });
                Manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
                Manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profile });
                Manager.SetComponentData(member, new MissileMemberTrajectoryVariation
                {
                    Motif = MissileTrajectoryMotif.CurvedWideApproach, CurveStrength = 0.5f, Valid = 1
                });
                Manager.SetComponentData(member, new MissileMemberLaunchState { LaunchDelaySeconds = delay, Pending = 1 });
                Manager.SetComponentData(member, new MissileMemberLaunchCellAssignment
                {
                    Cell = new MissileLaunchCellKey(salvoKey.EmitterStableId, ordinal % 2u)
                });
                Manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.HomingArc });
                Manager.SetComponentData(member, new MissileMemberMotionState
                {
                    Position = default, Forward = new float3(0f, 0f, 1f), Speed = Profile.Speed
                });
                Manager.SetComponentData(member, new MissileMemberDueState { NodeIndex = -1 });
                return member;
            }

            public void Register(params Entity[] members)
            {
                foreach (Entity member in members)
                    MissileMemberCommandScheduling.RegisterMember(Manager, Executor.SessionEntity, member);
            }

            public void Activate(Entity member, ulong dueOrdinal)
                => MissileMemberCommandScheduling.ActivateLaunchedMember(Manager, Executor.SessionEntity, member, dueOrdinal);

            public bool Acknowledge(Entity member, ulong actualSpawnOrdinal, float3 position, float3 forward)
                => MissileMemberCommandScheduling.AcknowledgeSpawn(Manager, Executor.SessionEntity, member,
                    new SimulationStepContext(1, checked((uint)Ordinal), Ordinal, config),
                    new MissileMemberSpawnAcknowledgement
                    {
                        Cell = Manager.GetComponentData<MissileMemberLaunchCellAssignment>(member).Cell,
                        SessionGeneration = 1,
                        ActualSpawnOrdinal = actualSpawnOrdinal,
                        Position = position,
                        LaunchForward = forward
                    });

            public void RunTo(ulong targetOrdinal)
            {
                while (Ordinal < targetOrdinal) Run();
            }

            public MissileMemberCommandRequest Request(Entity member) => Manager.GetComponentData<MissileMemberCommandRequest>(member);
            public MissileMemberCommandState Command(Entity member) => Manager.GetComponentData<MissileMemberCommandState>(member);
            public MissileMemberLaunchState Launch(Entity member) => Manager.GetComponentData<MissileMemberLaunchState>(member);
            public MissileMemberLaunchHoldState Hold(Entity member) => Manager.GetComponentData<MissileMemberLaunchHoldState>(member);
            public MissileMemberGuidanceState Guidance(Entity member) => Manager.GetComponentData<MissileMemberGuidanceState>(member);

            public void InvalidateTarget(Entity salvo)
            {
                var target = Manager.GetComponentData<SalvoTargetContext>(salvo);
                target.SampleValid = 0;
                target.SampledTarget = default;
                target.SampleOrdinal = 0;
                target.Revision++;
                Manager.SetComponentData(salvo, target);
            }

            public void ReplacePlanForNewRevision(Entity salvo, uint revision)
            {
                var target = Manager.GetComponentData<SalvoTargetContext>(salvo);
                target.Revision = revision;
                target.SampleOrdinal = checked(target.SampleOrdinal + 1);
                target.Position += new float3(5f, 0f, 0f);
                Manager.SetComponentData(salvo, target);
                var launch = Manager.GetComponentData<SalvoLaunchSnapshot>(salvo);
                Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(launch, target, Profile, out var plan));
                Manager.SetComponentData(salvo, plan);
            }

            public void ChangeTargetWithoutRefreshingPlan(Entity salvo)
            {
                var target = Manager.GetComponentData<SalvoTargetContext>(salvo);
                target.Revision++;
                target.SampleOrdinal = checked(target.SampleOrdinal + 1);
                target.Position += new float3(5f, 0f, 0f);
                Manager.SetComponentData(salvo, target);
            }

            public void SetEmitterDestroyed(Entity salvo)
            {
                Manager.SetComponentData(salvo, new MissileSalvoLaunchControl { EmitterDestroyed = 1 });
            }

            private void Run()
            {
                var context = progress.Begin(config);
                var writable = players;
                for (int i = 0; i < writable.Length; ++i) writable[i] = default;
                writable[0] = new PlayerStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = context.Generation, ConsumedOrdinal = context.Ordinal,
                    InputSequence = 1, StartStateTick = unchecked(context.Tick - 1), EndStateTick = context.Tick,
                    StartPosition = new float3(10f, 0f, 100f), EndPosition = new float3(10f, 0f, 100f),
                    StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                    SteeringForward = new float3(0f, 0f, 1f), SteeringRight = new float3(1f, 0f, 0f), ViewForward = new float3(0f, 0f, 1f)
                };
                Executor.Execute(context, players, progress);
                Executor.ValidatePending(context, progress);
                progress.Complete(context);
                Executor.AcknowledgeAuthoritativeCompletion(context, progress);
                Ordinal = context.Ordinal;
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
