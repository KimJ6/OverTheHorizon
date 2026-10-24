using NUnit.Framework;
using OverTheHorizon.Combat.Missiles;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Tests
{
    public sealed class MissileGuidanceSolverTests
    {
        private static readonly MissileApproachProfile Profile = new MissileApproachProfile
        {
            PredictionTime = 0.5f,
            MaxLateralBias = 20f,
            MaxVerticalBias = 10f,
            MaxCurveStrength = 1f,
            MaxCommandPhase = 0.4f,
            Speed = 10f,
            MaxSnapAngleRadians = math.radians(60f),
            AttackCommitMaxSnapAngleRadians = math.radians(5f),
            TurnRateRadiansPerSecond = math.radians(90f),
            MaxTurnRadiansPerTick = math.radians(20f),
            SegmentDuration = 0.5f,
            LaunchHoldDuration = 0.1f
        };

        [Test]
        public void CurrentPlanResolutionAcceptsLowFrequencySampleButRejectsStaleRevisionOrdinalTargetAndSession()
        {
            using (var world = new World("Missile M1-D plan resolution"))
            {
                var manager = world.EntityManager;
                Entity member = CreateCurrentPlanMember(manager, out Entity salvo, out _);

                Assert.IsTrue(MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, 1,
                    out _, out _, out _));
                Assert.IsTrue(MissileGuidanceSolver.TryCreateSegmentForMember(manager, member, 1, Variation(),
                    Request(MissileGuidanceState.HomingArc), out _, out _));

                var targetContext = manager.GetComponentData<SalvoTargetContext>(salvo);
                targetContext.SampleValid = 0;
                manager.SetComponentData(salvo, targetContext);
                Assert.IsFalse(MissileGuidanceSolver.TryCreateSegmentForMember(manager, member, 1, Variation(),
                    Request(MissileGuidanceState.HomingArc), out _, out _));
                targetContext.SampleValid = 1;
                manager.SetComponentData(salvo, targetContext);

                var plan = manager.GetComponentData<SalvoAttackPlan>(salvo);
                plan.SourceSampleRevision++;
                manager.SetComponentData(salvo, plan);
                Assert.IsFalse(MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, 1,
                    out _, out _, out _));

                plan.SourceSampleRevision--;
                plan.SourceSampleOrdinal++;
                manager.SetComponentData(salvo, plan);
                Assert.IsFalse(MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, 1,
                    out _, out _, out _));

                plan.SourceSampleOrdinal--;
                manager.SetComponentData(salvo, plan);
                var sample = manager.GetComponentData<SalvoTargetContext>(salvo);
                sample.Target = new SessionPlayerKey(1, 2, 1);
                manager.SetComponentData(salvo, sample);
                Assert.IsFalse(MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, 1,
                    out _, out _, out _));

                sample.Target = sample.SampledTarget;
                manager.SetComponentData(salvo, sample);
                Assert.IsFalse(MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, 2,
                    out _, out _, out _));
            }
        }

        [Test]
        public void InvalidProfileAndVariationCannotProduceGuidanceCommands()
        {
            var invalid = Profile;
            invalid.MaxLateralBias = MissileSalvoApproachPlanning.MaximumBiasDistance + 1f;
            Assert.IsFalse(MissileSalvoApproachPlanning.IsValidProfile(invalid));
            Assert.IsFalse(MissileSalvoApproachPlanning.TryValidateProfile(invalid, out var failure));
            Assert.AreEqual(MissileProfileValidationFailure.MaxLateralBias, failure);
            invalid = Profile;
            invalid.TurnRateRadiansPerSecond = float.PositiveInfinity;
            Assert.IsFalse(MissileSalvoApproachPlanning.IsValidProfile(invalid));

            var invalidVariation = Variation();
            invalidVariation.CurveStrength = float.NaN;
            Assert.IsFalse(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(5f, 0f, 20f)), invalidVariation,
                Profile, Request(MissileGuidanceState.HomingArc), out _, out _));
        }

        [Test]
        public void HomingSnapsOnlyInsideItsConfiguredBoundaryAndAttackCommitUsesItsSmallerBoundary()
        {
            var shallowPlan = Plan(DirectionAtDegrees(10f) * 20f);
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(shallowPlan, Variation(), Profile,
                Request(MissileGuidanceState.HomingArc), out var homing, out var homingDecision));
            Assert.AreEqual(MissileGuidanceDecision.Snap, homingDecision);
            Assert.AreEqual(MissileFlightPrimitive.Straight, homing.Primitive);

            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(shallowPlan, Variation(), Profile,
                Request(MissileGuidanceState.AttackCommit), out var commit, out var commitDecision));
            Assert.AreEqual(MissileGuidanceDecision.Arc, commitDecision);
            Assert.AreEqual(MissileFlightPrimitive.ConstantCurvatureArc, commit.Primitive);
        }

        [Test]
        public void TurnRateAndTickAngleBoundTheArcEvenWhenCurveBiasIsLarge()
        {
            var profile = Profile;
            profile.MaxSnapAngleRadians = 0f;
            profile.AttackCommitMaxSnapAngleRadians = 0f;
            var variation = Variation();
            variation.CurveStrength = profile.MaxCurveStrength;
            var request = Request(MissileGuidanceState.HomingArc);
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), variation, profile,
                request, out var segment, out var decision));
            Assert.AreEqual(MissileGuidanceDecision.Arc, decision);
            Assert.LessOrEqual(math.abs(math.atan2(segment.SinStep, segment.CosStep)), profile.MaxTurnRadiansPerTick + 0.0001f);
            Assert.LessOrEqual(math.abs(segment.Speed * segment.Curvature), profile.TurnRateRadiansPerSecond + 0.0001f);
        }

        [Test]
        public void SameDirectionAndCurveEpsilonUseStraightWhileOppositeDirectionGetsDeterministicArc()
        {
            var profile = Profile;
            profile.MaxSnapAngleRadians = 0f;
            profile.AttackCommitMaxSnapAngleRadians = 0f;
            var epsilonVariation = Variation();
            epsilonVariation.CurveStrength = MissileGuidanceSolver.CurvatureEpsilon * 0.5f;
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(0f, 0f, 20f)), epsilonVariation, profile,
                Request(MissileGuidanceState.HomingArc), out var straight, out var straightDecision));
            Assert.AreEqual(MissileGuidanceDecision.Straight, straightDecision);
            Assert.AreEqual(MissileFlightPrimitive.Straight, straight.Primitive);

            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(0f, 0f, -20f)), Variation(), profile,
                Request(MissileGuidanceState.HomingArc), out var opposite, out var oppositeDecision));
            Assert.AreEqual(MissileGuidanceDecision.Arc, oppositeDecision);
            Assert.That(math.length(opposite.RotationAxis), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(math.dot(opposite.RotationAxis, opposite.StartForward), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void DegenerateAimAndForwardNeverCreateANonFiniteCommand()
        {
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(default), Variation(), Profile,
                Request(MissileGuidanceState.HomingArc), out var fallback, out var decision));
            Assert.AreEqual(MissileGuidanceDecision.StraightFallback, decision);
            Assert.AreEqual(MissileFlightPrimitive.Straight, fallback.Primitive);

            var badForward = Request(MissileGuidanceState.HomingArc);
            badForward.Motion.Forward = default;
            Assert.IsFalse(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(0f, 0f, 20f)), Variation(), Profile,
                badForward, out _, out _));
        }

        [Test]
        public void StraightAndArcIntegrationKeepFinitePositionAndUnitForward()
        {
            var straightProfile = Profile;
            straightProfile.MaxSnapAngleRadians = 0f;
            straightProfile.AttackCommitMaxSnapAngleRadians = 0f;
            var direct = Variation();
            direct.CurveStrength = 0f;
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(0f, 0f, 20f)), direct, straightProfile,
                Request(MissileGuidanceState.HomingArc), out var straight, out _));
            Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(straight, default, new float3(0f, 0f, 1f), out var straightPosition, out var straightForward));
            Assert.That(math.distance(new float3(0f, 0f, 0.2f), straightPosition), Is.LessThan(0.00001f));
            Assert.That(math.dot(new float3(0f, 0f, 1f), straightForward), Is.GreaterThan(0.99999f));

            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), Variation(), straightProfile,
                Request(MissileGuidanceState.HomingArc), out var arc, out _));
            Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(arc, default, new float3(0f, 0f, 1f), out var arcPosition, out var arcForward));
            Assert.IsTrue(math.all(math.isfinite(arcPosition)) && math.all(math.isfinite(arcForward)));
            Assert.That(math.length(arcForward), Is.EqualTo(1f).Within(0.0001f));
            Assert.Greater(arcForward.x, 0f);
        }

        [Test]
        public void ArcIntegrationMatchesPhysicalTimeAcrossSupportedTickRates()
        {
            SimulateOneSecond(50, out var position50, out var forward50);
            SimulateOneSecond(60, out var position60, out var forward60);
            SimulateOneSecond(120, out var position120, out var forward120);
            Assert.That(math.distance(position50, position60), Is.LessThan(0.002f));
            Assert.That(math.distance(position50, position120), Is.LessThan(0.002f));
            Assert.That(math.dot(forward50, forward60), Is.GreaterThan(0.9999f));
            Assert.That(math.dot(forward50, forward120), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void ExpiredSegmentFallsBackToIndefiniteStraightAtLastTangent()
        {
            var profile = Profile;
            profile.MaxSnapAngleRadians = 0f;
            profile.AttackCommitMaxSnapAngleRadians = 0f;
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), Variation(), profile,
                Request(MissileGuidanceState.HomingArc), out var arc, out _));
            Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(arc, default, new float3(0f, 0f, 1f), out var endPosition, out var endForward));
            Assert.IsTrue(MissileGuidanceSolver.TryCreateStraightFallback(arc, endPosition, endForward, arc.EndOrdinal,
                arc.CommandSequence + 1, 0.02f, out var fallback));
            Assert.AreEqual(MissileFlightPrimitive.Straight, fallback.Primitive);
            Assert.AreEqual(ulong.MaxValue, fallback.EndOrdinal);
            Assert.That(math.dot(endForward, fallback.StartForward), Is.GreaterThan(0.99999f));
        }

        private static Entity CreateCurrentPlanMember(EntityManager manager, out Entity salvo, out Entity profileEntity)
        {
            profileEntity = manager.CreateEntity(typeof(MissileApproachProfile));
            manager.SetComponentData(profileEntity, Profile);
            var key = new SalvoLogicalKey(55, 2);
            salvo = manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoProfileReference), typeof(SalvoAttackPlan));
            manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
            var sample = new SalvoTargetContext
            {
                Target = new SessionPlayerKey(1, 4, 1), HasTarget = 1,
                SampledTarget = new SessionPlayerKey(1, 4, 1), Position = new float3(0f, 0f, 20f),
                Velocity = default, SampleOrdinal = 3, Revision = 7, SampleValid = 1
            };
            manager.SetComponentData(salvo, sample);
            manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profileEntity });
            Assert.IsTrue(MissileSalvoApproachPlanning.TryBuildPlan(new SalvoLaunchSnapshot
            {
                Position = default, Forward = new float3(0f, 0f, 1f), SessionGeneration = 1
            }, sample, Profile, out var plan));
            manager.SetComponentData(salvo, plan);

            Entity member = manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference), typeof(MissileMemberProfileReference));
            manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, 1), SessionGeneration = 1 });
            manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
            manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profileEntity });
            return member;
        }

        private static SalvoAttackPlan Plan(float3 predictedTarget)
            => new SalvoAttackPlan
            {
                PredictedTarget = predictedTarget,
                Forward = new float3(0f, 0f, 1f), Right = new float3(1f, 0f, 0f), Up = new float3(0f, 1f, 0f),
                SourceSampleOrdinal = 1, SourceSampleRevision = 1, Valid = 1
            };

        private static MissileMemberTrajectoryVariation Variation()
            => new MissileMemberTrajectoryVariation { Motif = MissileTrajectoryMotif.CurvedWideApproach, CurveStrength = 1f, Valid = 1 };

        private static MissileGuidanceRequest Request(MissileGuidanceState state)
            => new MissileGuidanceRequest
            {
                Motion = new MissileGuidanceMotion { Position = default, Forward = new float3(0f, 0f, 1f), Speed = 10f },
                State = state, DeltaTime = 0.02f, StartOrdinal = 10, CommandSequence = 1
            };

        private static float3 DirectionAtDegrees(float degrees)
        {
            float radians = math.radians(degrees);
            return new float3(math.sin(radians), 0f, math.cos(radians));
        }

        private static void SimulateOneSecond(int tickRate, out float3 position, out float3 forward)
        {
            var profile = Profile;
            profile.MaxSnapAngleRadians = 0f;
            profile.AttackCommitMaxSnapAngleRadians = 0f;
            profile.MaxTurnRadiansPerTick = math.radians(20f);
            var request = Request(MissileGuidanceState.HomingArc);
            request.DeltaTime = 1f / tickRate;
            Assert.IsTrue(MissileGuidanceSolver.TryCreateSegment(Plan(new float3(20f, 0f, 20f)), Variation(), profile,
                request, out var segment, out _));

            position = default;
            forward = new float3(0f, 0f, 1f);
            for (int i = 0; i < tickRate; ++i)
                Assert.IsTrue(MissileGuidanceSolver.TryIntegrateSegment(segment, position, forward, out position, out forward));
        }
    }
}
