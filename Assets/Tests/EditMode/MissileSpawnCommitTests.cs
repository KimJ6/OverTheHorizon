using System;
using NUnit.Framework;
using System.Reflection;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.LocalCombatMissiles;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Tests
{
    public sealed class MissileSpawnCommitTests
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
        public void DueLaunchPromotesLogicalMemberAndAcknowledgesOnlyAfterRuntimeStateIsPresent()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(700, new float3(10f, 0f, 20f), quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 700, 1);
                Entity member = fixture.CreateMember(salvo, 3, 0, 0f);
                fixture.Register(member);

                fixture.Run();

                Assert.IsTrue(fixture.Manager.HasComponent<MissileRuntimeSpawnState>(member));
                var runtime = fixture.Manager.GetComponentData<MissileRuntimeSpawnState>(member);
                var acknowledgement = fixture.Manager.GetComponentData<MissileMemberSpawnAcknowledgement>(member);
                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
                var command = fixture.Manager.GetComponentData<MissileMemberCommandState>(member);
                Assert.AreEqual(fixture.Ordinal, runtime.SpawnOrdinal);
                Assert.AreEqual(new MissileLaunchCellKey(700, 0), runtime.Cell);
                Assert.AreEqual(1, acknowledgement.Acknowledged);
                Assert.AreEqual(fixture.Ordinal, acknowledgement.ActualSpawnOrdinal);
                Assert.That(motion.Position, Is.EqualTo(new float3(10f, 0f, 23f)));
                Assert.That(math.dot(motion.Forward, new float3(0f, 0f, 1f)), Is.GreaterThan(0.99999f));
                Assert.AreEqual(fixture.Ordinal, command.CurrentSegment.StartOrdinal);
                Assert.AreEqual(MissileFlightPrimitive.Straight, command.CurrentSegment.Primitive);
                Assert.AreEqual(MissileCommandRequestKind.None, fixture.Manager.GetComponentData<MissileMemberCommandRequest>(member).Kind);
            }
        }

        [Test]
        public void SpawnTickContactPublishesOutcomeThenDespawnsTheTerminalMemberAndSalvo()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(711, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 711, 12);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);
                var bounds = fixture.Manager.GetBuffer<PlayerCombatBoundsStepElement>(fixture.Executor.SessionEntity);
                bounds.Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = 1, Ordinal = 1,
                    StartCenter = new float3(0f, 0f, 2.5f), EndCenter = new float3(0f, 0f, 2.5f), Radius = 0.25f
                });

                fixture.Run();

                var results = fixture.Manager.GetBuffer<MissileContactResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1, results.Length);
                Assert.AreEqual(1u, results[0].PlayerId);
                Assert.AreEqual(1ul, results[0].Ordinal);
                var outcomes = fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1, outcomes.Length);
                Assert.AreEqual(MissileTerminalReason.Contact, outcomes[0].Reason);
                Assert.AreEqual(results[0].Member, outcomes[0].Member);
                Assert.IsFalse(fixture.Manager.Exists(member));
                Assert.IsFalse(fixture.Manager.Exists(salvo));
                var lifecycle = fixture.Manager.GetComponentData<MissileLifecycleDiagnostics>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1u, lifecycle.ContactTerminalCount);
                Assert.AreEqual(1u, lifecycle.DespawnCount);
                Assert.AreEqual(1u, lifecycle.SalvoDespawnCount);

                fixture.Run();
                Assert.AreEqual(0, fixture.Manager.GetBuffer<MissileContactResult>(fixture.Executor.SessionEntity).Length);
                Assert.AreEqual(0, fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity).Length);
            }
        }

        [Test]
        public void TerminalMemberDoesNotDespawnItsSalvoWhileAnotherMemberIsPending()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(713, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 713, 14);
                Entity contactMember = fixture.CreateMember(salvo, 0, 0, 0f);
                Entity pendingMember = fixture.CreateMember(salvo, 1, 0, 1f);
                fixture.Register(contactMember, pendingMember);
                fixture.Manager.GetBuffer<PlayerCombatBoundsStepElement>(fixture.Executor.SessionEntity).Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = 1, Ordinal = 1,
                    StartCenter = new float3(0f, 0f, 2.5f), EndCenter = new float3(0f, 0f, 2.5f), Radius = 0.25f
                });

                fixture.Run();

                Assert.IsFalse(fixture.Manager.Exists(contactMember));
                Assert.IsTrue(fixture.Manager.Exists(pendingMember));
                Assert.IsTrue(fixture.Manager.Exists(salvo));
                Assert.AreEqual(1, fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity).Length);
            }
        }

        [Test]
        public void ExpireRequestDespawnsTheRuntimeMemberAndPublishesAnExpireOutcome()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(714, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 714, 15);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);
                fixture.Run();
                fixture.Manager.SetComponentData(member, new MissileMemberCommandRequest
                {
                    Kind = MissileCommandRequestKind.Expire, Ordinal = fixture.Ordinal + 1, Pending = 1
                });

                fixture.Run();

                Assert.IsFalse(fixture.Manager.Exists(member));
                Assert.IsFalse(fixture.Manager.Exists(salvo));
                var outcomes = fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1, outcomes.Length);
                Assert.AreEqual(MissileTerminalReason.Expire, outcomes[0].Reason);
            }
        }

        [Test]
        public void TrailHistoryIsBoundedAndGenerationScopedWithoutEntityReferences()
        {
            MissileFlightTrailHistory.Clear();
            try
            {
                var history = typeof(MissileFlightTrailHistory);
                var configure = history.GetMethod("ConfigureSampling", BindingFlags.Static | BindingFlags.NonPublic);
                var record = history.GetMethod("Record", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(configure);
                Assert.IsNotNull(record);
                configure.Invoke(null, new object[] { 50, 0.1f, 0.02f, 3 });
                var key = new MemberLogicalKey(new SalvoLogicalKey(715, 1), 0);
                for (ulong ordinal = 1; ordinal <= 4; ++ordinal)
                    record.Invoke(null, new object[] { key, 1ul, ordinal, new float3(ordinal, 0f, 0f),
                        MissileGuidanceState.HomingArc, MissileFlightPrimitive.ConstantCurvatureArc, true });

                Assert.AreEqual(3, MissileFlightTrailHistory.Count);
                MissileFlightTrailHistory.ObserveLifecycle(1, SimulationLifecycle.Paused);
                Assert.AreEqual(3, MissileFlightTrailHistory.Count);
                MissileFlightTrailHistory.ObserveLifecycle(2, SimulationLifecycle.Running);
                Assert.AreEqual(0, MissileFlightTrailHistory.Count);
            }
            finally { MissileFlightTrailHistory.Clear(); }
        }

        [Test]
        public void DistantStaticConstantVelocityAndFallingPlayerFramesDoNotStopAnActiveMissile()
        {
            AssertDistantPlayerDoesNotStopMissile(0f);
            AssertDistantPlayerDoesNotStopMissile(20f);
            AssertDistantPlayerDoesNotStopMissile(-500f);
        }

        [Test]
        public void SameTimeMultiPlayerContactUsesStableIdentityAndRejectsInvalidBounds()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(712, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 712, 13);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);
                var bounds = fixture.Manager.GetBuffer<PlayerCombatBoundsStepElement>(fixture.Executor.SessionEntity);
                bounds.Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 2, SlotGeneration = 1, Generation = 1, Ordinal = 1,
                    StartCenter = new float3(0f, 0f, 2.5f), EndCenter = new float3(0f, 0f, 2.5f), Radius = 0.25f
                });
                bounds.Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = 1, Ordinal = 1,
                    StartCenter = new float3(0f, 0f, 2.5f), EndCenter = new float3(0f, 0f, 2.5f), Radius = 0.25f
                });
                bounds.Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 3, SlotGeneration = 1, Generation = 99, Ordinal = 1,
                    StartCenter = new float3(0f, 0f, 2.5f), EndCenter = new float3(0f, 0f, 2.5f), Radius = 0.25f
                });

                fixture.Run();

                var result = fixture.Manager.GetBuffer<MissileContactResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1, result.Length);
                Assert.AreEqual(1u, result[0].PlayerId);
            }
        }

        [Test]
        public void CurrentEmitterPoseAndCellLocalPoseDetermineWorldMuzzle()
        {
            using (var fixture = new Fixture(60))
            {
                Entity emitter = fixture.CreateEmitter(701, new float3(10f, 3f, 20f), quaternion.RotateY(math.PI * 0.5f), 1);
                Entity salvo = fixture.CreateSalvo(emitter, 701, 2);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);

                fixture.Run();

                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
                Assert.That(math.distance(motion.Position, new float3(12f + 50f / 60f, 3f, 20f)), Is.LessThan(0.0001f));
                Assert.That(math.dot(motion.Forward, new float3(1f, 0f, 0f)), Is.GreaterThan(0.99999f));
            }
        }

        [Test]
        public void DifferentCellsSpawnInTheSameOrdinalButSameCellConflictCancelsAndDespawnsEveryParticipant()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(702, default, quaternion.identity, 2);
                Entity salvo = fixture.CreateSalvo(emitter, 702, 3);
                Entity first = fixture.CreateMember(salvo, 9, 0, 0f);
                Entity second = fixture.CreateMember(salvo, 1, 1, 0f);
                fixture.Register(first, second);
                fixture.Run();

                Assert.IsTrue(fixture.Manager.HasComponent<MissileRuntimeSpawnState>(first));
                Assert.IsTrue(fixture.Manager.HasComponent<MissileRuntimeSpawnState>(second));
            }

            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(703, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 703, 4);
                Entity first = fixture.CreateMember(salvo, 9, 0, 0f);
                Entity second = fixture.CreateMember(salvo, 1, 0, 0f);
                fixture.Register(first, second);
                fixture.Run();

                Assert.IsFalse(fixture.Manager.Exists(first));
                Assert.IsFalse(fixture.Manager.Exists(second));
                var outcomes = fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(2, outcomes.Length);
                Assert.AreEqual(MissileTerminalReason.Cancel, outcomes[0].Reason);
                Assert.AreEqual(MissileTerminalReason.Cancel, outcomes[1].Reason);
            }
        }

        [Test]
        public void InvalidEmitterStateCancelsLaunchWithoutPromotingMember()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(704, default, quaternion.identity, 1);
                var pose = fixture.Manager.GetComponentData<MissileEmitterRuntimePose>(emitter);
                pose.CanFire = 0;
                fixture.Manager.SetComponentData(emitter, pose);
                Entity salvo = fixture.CreateSalvo(emitter, 704, 5);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);

                fixture.Run();

                Assert.IsFalse(fixture.Manager.Exists(member));
                var outcomes = fixture.Manager.GetBuffer<MissileOutcomeResult>(fixture.Executor.SessionEntity);
                Assert.AreEqual(1, outcomes.Length);
                Assert.AreEqual(MissileTerminalReason.Cancel, outcomes[0].Reason);
            }
        }

        [Test]
        public void DestroyedEmitterDoesNotStopAnAlreadySpawnedMissile()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(707, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 707, 8);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);

                fixture.Run();
                float firstPosition = fixture.Manager.GetComponentData<MissileMemberMotionState>(member).Position.z;
                fixture.Manager.DestroyEntity(emitter);
                fixture.Run();

                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
                Assert.That(motion.Position.z, Is.EqualTo(firstPosition + 1f).Within(0.00001f));
            }
        }

        [Test]
        public void GuidanceEnableCommitsThePublishedSegmentFromTheActualRuntimeMotion()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(708, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 708, 9);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);

                for (int i = 0; i < 6; ++i) fixture.Run();

                var runtime = fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member);
                var previous = fixture.Manager.GetComponentData<MissileMemberPreviousPosition>(member);
                Assert.Greater(runtime.ActiveSegment.CommandSequence, 1u);
                Assert.That(math.distance(runtime.ActiveSegment.StartPosition, previous.Value), Is.LessThan(0.00001f));
                Assert.AreEqual(0, fixture.Manager.GetComponentData<MissileMemberLaunchHoldState>(member).Active);
                Assert.AreEqual(MissileGuidanceState.Acquire,
                    fixture.Manager.GetComponentData<MissileRuntimeGuidanceState>(member).Value);
                Assert.AreEqual(MissileCommandRequestKind.None, fixture.Manager.GetComponentData<MissileMemberCommandRequest>(member).Kind);
            }
        }

        [Test]
        public void RejectedCurrentSegmentSchedulesTheExistingCalendarAndRecoversOnTheNextStep()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(709, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 709, 10);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);
                fixture.Run();

                var command = fixture.Manager.GetComponentData<MissileMemberCommandState>(member);
                fixture.Manager.SetComponentData(member, new MissileMemberCommandRequest
                {
                    Kind = MissileCommandRequestKind.FlightSegment, Ordinal = 2,
                    CommandSequence = command.NextCommandSequence + 1, Pending = 1
                });
                fixture.Run();

                Assert.AreEqual(1u, fixture.Manager.GetComponentData<MissileCommandSchedulingCounters>(fixture.Executor.SessionEntity).ReplanRecoveryCount);
                Assert.AreEqual(3ul, fixture.Manager.GetComponentData<MissileMemberDueState>(member).NextDueOrdinal);
                Assert.AreEqual(MissileGuidanceState.LaunchBurst,
                    fixture.Manager.GetComponentData<MissileMemberGuidanceState>(member).Value);

                fixture.Run();
                var runtime = fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member);
                Assert.AreEqual(MissileFlightPrimitive.Straight, runtime.ActiveSegment.Primitive);
                Assert.Greater(runtime.ActiveSegment.CommandSequence, 1u);
                Assert.AreEqual(MissileCommandRequestKind.None, fixture.Manager.GetComponentData<MissileMemberCommandRequest>(member).Kind);
            }
        }

        [Test]
        public void CommittedRuntimeGuidanceAdvancesFromAcquireThroughHomingToAttackCommit()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(710, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 710, 11);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                Entity profileEntity = fixture.Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                var profile = fixture.Manager.GetComponentData<MissileApproachProfile>(profileEntity);
                profile.AttackCommitDistance = 200f;
                fixture.Manager.SetComponentData(profileEntity, profile);
                fixture.Register(member);

                for (int i = 0; i < 6; ++i) fixture.Run();
                Assert.AreEqual(MissileGuidanceState.Acquire,
                    fixture.Manager.GetComponentData<MissileRuntimeGuidanceState>(member).Value);

                for (int i = 0; i < 5; ++i) fixture.Run();
                Assert.AreEqual(MissileGuidanceState.HomingArc,
                    fixture.Manager.GetComponentData<MissileRuntimeGuidanceState>(member).Value);

                for (int i = 0; i < 5; ++i) fixture.Run();
                Assert.AreEqual(MissileGuidanceState.AttackCommit,
                    fixture.Manager.GetComponentData<MissileRuntimeGuidanceState>(member).Value);
            }
        }

        [Test]
        public void SessionTeardownRemovesPromotedMemberFromTheExecutorWorld()
        {
            using (var fixture = new Fixture(50))
            {
                Entity emitter = fixture.CreateEmitter(706, default, quaternion.identity, 1);
                Entity salvo = fixture.CreateSalvo(emitter, 706, 7);
                Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
                fixture.Register(member);
                fixture.Run();
                Assert.IsTrue(fixture.Manager.Exists(member));

                fixture.StopSession();

                Assert.IsFalse(fixture.Manager.Exists(member));
            }
        }

        [TestCase(50, 50)]
        [TestCase(60, 100)]
        [TestCase(120, 300)]
        public void UniqueCellsHaveOneDeterministicSpawnPerMemberAtSupportedTickRates(int tickRate, int count)
        {
            using (var fixture = new Fixture(tickRate))
            {
                Entity emitter = fixture.CreateEmitter(705, default, quaternion.identity, count);
                Entity salvo = fixture.CreateSalvo(emitter, 705, 6);
                var members = new Entity[count];
                for (uint i = 0; i < count; ++i) members[i] = fixture.CreateMember(salvo, (uint)(count - 1) - i, i, 0f);
                fixture.Register(members);

                fixture.Run();

                for (int i = 0; i < members.Length; ++i)
                {
                    Assert.IsTrue(fixture.Manager.HasComponent<MissileRuntimeSpawnState>(members[i]));
                    Assert.AreEqual(1ul, fixture.Manager.GetComponentData<MissileRuntimeSpawnState>(members[i]).SpawnOrdinal);
                }
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config;
            private readonly SimulationProgress progress = new SimulationProgress();
            private NativeArray<PlayerStepElement> players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);
            public readonly World World = new World("Missile M2-A Spawn");
            public readonly CombatStepExecutor Executor;
            public EntityManager Manager => World.EntityManager;
            public ulong Ordinal { get; private set; }

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
                Manager.AddBuffer<PlayerCombatBoundsStepElement>(Executor.SessionEntity);
                Manager.AddBuffer<MissileContactResult>(Executor.SessionEntity);
                Manager.AddComponentData(Executor.SessionEntity, new MissileContactDiagnostics { MinimumSeparation = float.PositiveInfinity });
                Manager.AddBuffer<MissileOutcomeResult>(Executor.SessionEntity);
                Manager.AddComponentData(Executor.SessionEntity, default(MissileLifecycleDiagnostics));
                progress.Reset(1);
            }

            public Entity CreateEmitter(ulong stableId, float3 position, quaternion rotation, int cellCount)
            {
                Entity emitter = Manager.CreateEntity(typeof(MissileEmitterIdentity), typeof(MissileEmitterRuntimePose));
                Manager.SetComponentData(emitter, new MissileEmitterIdentity { StableId = stableId });
                Manager.SetComponentData(emitter, new MissileEmitterRuntimePose { Position = position, Rotation = rotation, CanFire = 1 });
                var cells = Manager.AddBuffer<MissileEmitterLaunchCell>(emitter);
                for (uint i = 0; i < cellCount; ++i)
                    cells.Add(new MissileEmitterLaunchCell { CellOrdinal = i, LocalPosition = new float3(0f, 0f, 2f), LocalForward = new float3(0f, 0f, 1f), Valid = 1, Ready = 1 });
                return emitter;
            }

            public Entity CreateSalvo(Entity emitter, ulong emitterStableId, uint sequence)
            {
                Entity profile = Manager.CreateEntity(typeof(MissileApproachProfile));
                Manager.SetComponentData(profile, Profile);
                var key = new SalvoLogicalKey(emitterStableId, sequence);
                var sample = new SalvoTargetContext
                {
                    Target = new SessionPlayerKey(1, 1, 1), HasTarget = 1, SampledTarget = new SessionPlayerKey(1, 1, 1),
                    Position = new float3(0f, 0f, 100f), SampleOrdinal = 1, Revision = 1, SampleValid = 1
                };
                var snapshot = new SalvoLaunchSnapshot { Position = default, Forward = new float3(0f, 0f, 1f), SessionGeneration = 1 };
                Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(snapshot, sample, Profile, out var plan));
                Entity salvo = Manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoLaunchSnapshot),
                    typeof(SalvoProfileReference), typeof(SalvoAttackPlan), typeof(SalvoSchedule), typeof(MissileSalvoLaunchControl),
                    typeof(MissileSalvoEmitterReference));
                Manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
                Manager.SetComponentData(salvo, sample);
                Manager.SetComponentData(salvo, snapshot);
                Manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profile });
                Manager.SetComponentData(salvo, plan);
                Manager.SetComponentData(salvo, new SalvoSchedule { CreatedOrdinal = 1, MemberCount = 300 });
                Manager.SetComponentData(salvo, new MissileSalvoEmitterReference { Emitter = emitter, SessionGeneration = 1 });
                return salvo;
            }

            public Entity CreateMember(Entity salvo, uint memberOrdinal, uint cellOrdinal, float delay)
            {
                Entity profile = Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                SalvoLogicalKey key = Manager.GetComponentData<SalvoIdentity>(salvo).Key;
                Entity member = Manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference),
                    typeof(MissileMemberProfileReference), typeof(MissileMemberTrajectoryVariation), typeof(MissileMemberLaunchState),
                    typeof(MissileMemberCommandState), typeof(MissileMemberGuidanceState), typeof(MissileMemberMotionState),
                    typeof(MissileMemberCommandRequest), typeof(MissileMemberDueState), typeof(MissileMemberLaunchCellAssignment),
                    typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberLaunchHoldState), typeof(MissileContactShape));
                Manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, memberOrdinal), SessionGeneration = 1 });
                Manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
                Manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profile });
                Manager.SetComponentData(member, new MissileMemberTrajectoryVariation { Motif = MissileTrajectoryMotif.DirectPressure, Valid = 1 });
                Manager.SetComponentData(member, new MissileMemberLaunchState { LaunchDelaySeconds = delay, Pending = 1 });
                Manager.SetComponentData(member, new MissileMemberLaunchCellAssignment { Cell = new MissileLaunchCellKey(key.EmitterStableId, cellOrdinal) });
                Manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.LaunchBurst });
                Manager.SetComponentData(member, new MissileMemberMotionState { Forward = new float3(0f, 0f, 1f), Speed = Profile.Speed });
                Manager.SetComponentData(member, new MissileContactShape { Radius = 0.25f });
                Manager.SetComponentData(member, new MissileMemberDueState { NodeIndex = -1 });
                return member;
            }

            public void Register(params Entity[] members)
            {
                for (int i = 0; i < members.Length; ++i)
                    MissileMemberCommandScheduling.RegisterMember(Manager, Executor.SessionEntity, members[i]);
            }

            public void Run()
                => Run(new float3(0f, 0f, 100f), new float3(0f, 0f, 100f));

            public void Run(float3 playerStart, float3 playerEnd)
            {
                var step = progress.Begin(config);
                players[0] = new PlayerStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = step.Generation, ConsumedOrdinal = step.Ordinal,
                    InputSequence = 1, StartStateTick = unchecked(step.Tick - 1), EndStateTick = step.Tick,
                    StartPosition = playerStart, EndPosition = playerEnd,
                    StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                    SteeringForward = new float3(0f, 0f, 1f), SteeringRight = new float3(1f, 0f, 0f), ViewForward = new float3(0f, 0f, 1f)
                };
                Executor.Execute(step, players, progress);
                Executor.ValidatePending(step, progress);
                progress.Complete(step);
                Executor.AcknowledgeAuthoritativeCompletion(step, progress);
                Ordinal = step.Ordinal;
            }

            public void StopSession() => Executor.Dispose();

            public void Dispose()
            {
                if (players.IsCreated) players.Dispose();
                Executor.Dispose();
                World.Dispose();
            }
        }

        private static void AssertDistantPlayerDoesNotStopMissile(float playerVerticalVelocity)
        {
            using var fixture = new Fixture(50);
            Entity emitter = fixture.CreateEmitter(900 + (ulong)(playerVerticalVelocity + 100f), default, quaternion.identity, 1);
            Entity salvo = fixture.CreateSalvo(emitter, 900 + (ulong)(playerVerticalVelocity + 100f), 1);
            Entity member = fixture.CreateMember(salvo, 0, 0, 0f);
            fixture.Register(member);

            float previousZ = float.NegativeInfinity;
            float firstDistance = 0f;
            for (int i = 0; i < 12; ++i)
            {
                float dt = 1f / 50f;
                float3 start = new float3(0f, playerVerticalVelocity * i * dt, 100f);
                float3 end = new float3(0f, playerVerticalVelocity * (i + 1) * dt, 100f);
                var bounds = fixture.Manager.GetBuffer<PlayerCombatBoundsStepElement>(fixture.Executor.SessionEntity);
                bounds.Clear();
                bounds.Add(new PlayerCombatBoundsStepElement
                {
                    Valid = 1, PlayerId = 1, SlotGeneration = 1, Generation = 1, Ordinal = fixture.Ordinal + 1,
                    StartCenter = start, EndCenter = end, Radius = 0.25f
                });
                fixture.Run(start, end);
                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
                Assert.Greater(motion.Position.z, previousZ);
                Assert.AreEqual(fixture.Ordinal, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).LastMovementOrdinal);
                Assert.AreEqual(0, fixture.Manager.GetComponentData<MissileRuntimeMovementState>(member).ContactPending);
                float distance = math.distance(motion.Position, end);
                if (i == 0) firstDistance = distance;
                previousZ = motion.Position.z;
            }
            if (playerVerticalVelocity < 0f)
            {
                var motion = fixture.Manager.GetComponentData<MissileMemberMotionState>(member);
                float3 finalPlayer = new float3(0f, playerVerticalVelocity * 12f / 50f, 100f);
                Assert.Greater(math.distance(motion.Position, finalPlayer), firstDistance);
            }
        }
    }
}
