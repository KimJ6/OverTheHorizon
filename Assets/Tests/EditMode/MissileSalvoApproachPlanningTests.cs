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
    public sealed class MissileSalvoApproachPlanningTests
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
            SegmentDuration = 0.5f,
            LaunchHoldDuration = 0.1f
        };

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config = new SimulationTimingConfig(50);
            private readonly SimulationProgress progress = new SimulationProgress();
            private readonly NativeArray<PlayerStepElement> players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);

            public readonly World World = new World("Missile M1-C approach planning");
            public readonly CombatStepExecutor Executor;
            public EntityManager Manager => World.EntityManager;

            public Fixture()
            {
                Executor = new CombatStepExecutor(World, 1, config);
                Executor.RegisterStage<MissilePlanAndCommandStage>();
                Executor.RegisterStage<MissileSpawnAndCommandCommitStage>();
                Executor.RegisterStage<MissileMovementStage>();
                Executor.RegisterStage<MissileContactGatherStage>();
                Executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
                Executor.Seal();
                MissileSalvoTargetSampling.EnsureCalendar(Manager, Executor.SessionEntity);
                progress.Reset(1);
            }

            public Entity CreatePlannedSalvo(SalvoLogicalKey key, float3 position, float3 forward)
            {
                Entity profile = Manager.CreateEntity(typeof(MissileApproachProfile));
                Manager.SetComponentData(profile, Profile);
                Entity salvo = Manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoTargetSamplingState),
                    typeof(SalvoProfileReference), typeof(SalvoLaunchSnapshot), typeof(SalvoAttackPlan));
                Manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
                Manager.SetComponentData(salvo, new SalvoTargetContext { Target = new SessionPlayerKey(1, 1, 1), HasTarget = 1 });
                Manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profile });
                Manager.SetComponentData(salvo, new SalvoLaunchSnapshot { Position = position, Forward = forward, SessionGeneration = 1 });
                MissileSalvoTargetSampling.Register(Manager, Executor.SessionEntity, salvo, 1);
                return salvo;
            }

            public void Run(float3 position, float3 velocity)
            {
                var context = progress.Begin(config);
                var writable = players;
                for (int i = 0; i < writable.Length; ++i) writable[i] = default;
                writable[0] = Player(context, 1, 1, position, velocity);
                Executor.Execute(context, players, progress);
                Executor.ValidatePending(context, progress);
                progress.Complete(context);
                Executor.AcknowledgeAuthoritativeCompletion(context, progress);
            }

            public void Dispose()
            {
                if (players.IsCreated) players.Dispose();
                Executor.Dispose();
                World.Dispose();
            }
        }

        [Test]
        public void SameSampleAndLaunchInputBuildTheSameApproachPlan()
        {
            var launch = new SalvoLaunchSnapshot { Position = new float3(5, 2, -1), Forward = new float3(0, 0, 1), SessionGeneration = 1 };
            var sample = Sample(new float3(10, 4, 20), new float3(2, 0, 0), 7, 3);

            Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(launch, sample, Profile, out var first));
            Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(launch, sample, Profile, out var second));
            Assert.AreEqual(first.PredictedTarget, second.PredictedTarget);
            Assert.AreEqual(first.Forward, second.Forward);
            Assert.AreEqual(first.Right, second.Right);
            Assert.AreEqual(first.Up, second.Up);
            Assert.AreEqual(3u, first.SourceSampleRevision);
        }

        [Test]
        public void ApproachFrameIsFiniteForHorizontalVerticalReverseAndZeroDistance()
        {
            AssertFrame(new float3(0, 0, 0), new float3(10, 0, 0), new float3(0, 0, 1));
            AssertFrame(new float3(0, 0, 0), new float3(0, 10, 0), new float3(0, 1, 0));
            AssertFrame(new float3(0, 0, 0), new float3(0, 0, -10), new float3(0, 0, 1));
            AssertFrame(new float3(2, 2, 2), new float3(2, 2, 2), new float3(1, 0, 0));
        }

        [Test]
        public void ApproachFrameIsRightHandedAndOrthogonalWhenWorldUpIsParallel()
        {
            Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildFrame(default, new float3(0, 10, 0), WorldUp(),
                out var forward, out var right, out var up));
            Assert.That(math.length(forward), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(math.length(right), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(math.length(up), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(math.dot(forward, right), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(math.dot(right, up), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(math.dot(up, forward), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(math.dot(math.cross(right, up), forward), Is.GreaterThan(0.999f));
        }

        [Test]
        public void MemberVariationIsDeterministicOrderIndependentAndBoundedByProfile()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreatePlannedSalvo(new SalvoLogicalKey(200, 1), default, new float3(0, 0, 1));
                fixture.Run(new float3(0, 0, 30), new float3(2, 0, 0));
                Entity profile = fixture.Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                Entity first = CreateMember(fixture.Manager, salvo, profile, 4, 0x1234ul);
                Entity second = CreateMember(fixture.Manager, salvo, profile, 5, 0x1234ul);

                Assert.IsTrue(MissileSalvoApproachPlanning.TryCreateMemberVariation(fixture.Manager, first, 1, out var firstVariation));
                Assert.IsTrue(MissileSalvoApproachPlanning.TryCreateMemberVariation(fixture.Manager, second, 1, out var secondVariation));
                Assert.AreEqual(firstVariation.Motif, secondVariation.Motif);
                Assert.AreEqual(firstVariation.LateralBias, secondVariation.LateralBias);
                Assert.AreEqual(firstVariation.VerticalBias, secondVariation.VerticalBias);
                Assert.LessOrEqual(math.abs(firstVariation.LateralBias), Profile.MaxLateralBias);
                Assert.LessOrEqual(math.abs(firstVariation.VerticalBias), Profile.MaxVerticalBias);
                Assert.That(firstVariation.CurveStrength, Is.InRange(0f, Profile.MaxCurveStrength));
                Assert.That(firstVariation.CommandPhase, Is.InRange(0f, Profile.MaxCommandPhase));
            }
        }

        [Test]
        public void DifferentMemberSeedsCanProduceDifferentApproachPointsWithoutCopyingTargetData()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreatePlannedSalvo(new SalvoLogicalKey(201, 1), default, new float3(0, 0, 1));
                fixture.Run(new float3(0, 0, 30), default);
                Entity profile = fixture.Manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
                Entity first = CreateMember(fixture.Manager, salvo, profile, 0, 0x1111ul);
                Entity second = CreateMember(fixture.Manager, salvo, profile, 1, 0x2222ul);
                Assert.IsTrue(MissileSalvoApproachPlanning.TryCreateMemberVariation(fixture.Manager, first, 1, out var firstVariation));
                Assert.IsTrue(MissileSalvoApproachPlanning.TryCreateMemberVariation(fixture.Manager, second, 1, out var secondVariation));
                var plan = fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo);
                Assert.IsTrue(MissileSalvoApproachPlanning.TryGetMemberAimPoint(plan, firstVariation, out var firstAim));
                Assert.IsTrue(MissileSalvoApproachPlanning.TryGetMemberAimPoint(plan, secondVariation, out var secondAim));
                Assert.AreNotEqual(firstAim, secondAim);
                Assert.IsFalse(fixture.Manager.HasComponent<SalvoTargetContext>(first));
                Assert.IsFalse(fixture.Manager.HasComponent<SalvoTargetContext>(second));
            }
        }

        [Test]
        public void TargetInvalidationAndExplicitChangeInvalidateThePlanBeforeItCanBeReused()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreatePlannedSalvo(new SalvoLogicalKey(202, 1), default, new float3(0, 0, 1));
                fixture.Run(new float3(0, 0, 30), default);
                Assert.AreEqual(1, fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo).Valid);

                MissileSalvoTargetSampling.AssignTarget(fixture.Manager, salvo, new SessionPlayerKey(1, 2, 1));
                Assert.AreEqual(0, fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo).Valid);
            }
        }

        [Test]
        public void NewTargetSampleRevisionUpdatesThePlanOnlyWhenDue()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreatePlannedSalvo(new SalvoLogicalKey(203, 1), default, new float3(0, 0, 1));
                fixture.Run(new float3(0, 0, 30), new float3(2, 0, 0));
                var initial = fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo);
                fixture.Run(new float3(10, 0, 30), new float3(2, 0, 0));
                var notDue = fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo);
                Assert.AreEqual(initial.SourceSampleRevision, notDue.SourceSampleRevision);

                for (int i = 0; i < 8 && fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo).SourceSampleRevision == initial.SourceSampleRevision; ++i)
                    fixture.Run(new float3(10, 0, 30), new float3(2, 0, 0));
                var updated = fixture.Manager.GetComponentData<SalvoAttackPlan>(salvo);
                Assert.Greater(updated.SourceSampleRevision, initial.SourceSampleRevision);
                Assert.AreNotEqual(initial.PredictedTarget, updated.PredictedTarget);
            }
        }

        [Test]
        public void GenerationMismatchAndInvalidProfileCannotProduceAPlan()
        {
            var launch = new SalvoLaunchSnapshot { Position = default, Forward = new float3(0, 0, 1), SessionGeneration = 1 };
            var wrongGeneration = Sample(new float3(0, 0, 20), default, 1, 1);
            wrongGeneration.SampledTarget = new SessionPlayerKey(2, 1, 1);
            Assert.IsFalse(MissileSalvoApproachPlanning.TryBuildPlan(launch, wrongGeneration, Profile, out _));

            var invalidProfile = Profile;
            invalidProfile.PredictionTime = float.NaN;
            Assert.IsFalse(MissileSalvoApproachPlanning.TryBuildPlan(launch, Sample(new float3(0, 0, 20), default, 1, 1), invalidProfile, out _));
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void LaunchHoldMustBeFiniteAndStrictlyPositive(float holdDuration)
        {
            var profile = Profile;
            profile.LaunchHoldDuration = holdDuration;
            Assert.IsFalse(MissileSalvoApproachPlanning.TryValidateProfile(profile, out var failure));
            Assert.AreEqual(MissileProfileValidationFailure.LaunchHoldDuration, failure);
        }

        private static PlayerStepElement Player(SimulationStepContext context, uint id, uint slot, float3 position, float3 velocity)
            => new PlayerStepElement
            {
                Valid = 1, PlayerId = id, SlotGeneration = slot, Generation = context.Generation, ConsumedOrdinal = context.Ordinal,
                InputSequence = 1, StartStateTick = unchecked(context.Tick - 1), EndStateTick = context.Tick,
                StartPosition = position, EndPosition = position, StartVelocity = velocity, EndVelocity = velocity,
                StartRotation = quaternion.identity, EndRotation = quaternion.identity, SteeringRotation = quaternion.identity,
                SteeringForward = new float3(0, 0, 1), SteeringRight = new float3(1, 0, 0), ViewForward = new float3(0, 0, 1)
            };

        private static SalvoTargetContext Sample(float3 position, float3 velocity, ulong ordinal, uint revision)
            => new SalvoTargetContext
            {
                Target = new SessionPlayerKey(1, 1, 1), HasTarget = 1, SampledTarget = new SessionPlayerKey(1, 1, 1),
                Position = position, Velocity = velocity, SampleOrdinal = ordinal, Revision = revision, SampleValid = 1
            };

        private static Entity CreateMember(EntityManager manager, Entity salvo, Entity profile, uint ordinal, ulong seed)
        {
            var key = manager.GetComponentData<SalvoIdentity>(salvo).Key;
            Entity member = manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference),
                typeof(MissileMemberProfileReference), typeof(MissileMemberVariation));
            manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, ordinal), SessionGeneration = 1 });
            manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
            manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profile });
            manager.SetComponentData(member, new MissileMemberVariation { Seed = seed });
            return member;
        }

        private static void AssertFrame(float3 origin, float3 target, float3 fallback)
        {
            Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildFrame(origin, target, fallback, out var forward, out var right, out var up));
            Assert.IsTrue(math.all(math.isfinite(forward)) && math.all(math.isfinite(right)) && math.all(math.isfinite(up)));
        }

        private static float3 WorldUp() => new float3(0, 1, 0);
    }
}
