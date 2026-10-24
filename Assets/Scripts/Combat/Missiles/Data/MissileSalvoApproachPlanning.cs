using System;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    public enum MissileTrajectoryVariationPurpose : uint
    {
        Motif = 0x4D4F5446,
        Lateral = 0x4C415452,
        Vertical = 0x56455254,
        Curve = 0x43555256,
        CommandPhase = 0x50484153
    }

    /// <summary>
    /// M1-C converts a current shared target sample into one shared Salvo approach
    /// frame. It does not create flight segments, DesiredDirection, or movement.
    /// </summary>
    public static class MissileSalvoApproachPlanning
    {
        public const float MaximumPredictionTime = 5f;
        // Prototype safety bounds, not gameplay tuning. They bound authoring input before it
        // can overflow a shared plan or an M1-D segment calculation.
        public const float MaximumBiasDistance = 2000f;
        public const float MaximumCurveStrength = 2f;
        public const float MaximumCommandPhase = 2f;
        public const float MaximumSpeed = 1000f;
        public const float MaximumTurnRateRadiansPerSecond = math.PI * 4f;
        public const float MaximumTurnRadiansPerTick = math.PI * 0.5f;
        public const float MaximumSegmentDuration = 2f;
        public const float MaximumLaunchHoldDuration = 5f;
        private const float DirectionEpsilonSq = 0.000001f;
        private static readonly float3 WorldUp = new float3(0f, 1f, 0f);
        private static readonly float3 WorldForward = new float3(0f, 0f, 1f);

        internal static void RefreshIfPresent(EntityManager manager, Entity salvo)
        {
            if (!manager.Exists(salvo) || !manager.HasComponent<SalvoAttackPlan>(salvo)) return;
            if (!manager.HasComponent<SalvoIdentity>(salvo) || !manager.HasComponent<SalvoTargetContext>(salvo)
                || !manager.HasComponent<SalvoLaunchSnapshot>(salvo) || !manager.HasComponent<SalvoProfileReference>(salvo))
            {
                InvalidateIfPresent(manager, salvo);
                return;
            }

            var identity = manager.GetComponentData<SalvoIdentity>(salvo);
            var sample = manager.GetComponentData<SalvoTargetContext>(salvo);
            var launch = manager.GetComponentData<SalvoLaunchSnapshot>(salvo);
            var plan = manager.GetComponentData<SalvoAttackPlan>(salvo);
            if (identity.SessionGeneration == 0 || launch.SessionGeneration != identity.SessionGeneration
                || sample.SampleValid != 1 || !sample.SampledTarget.IsValid
                || sample.SampledTarget.SessionGeneration != identity.SessionGeneration)
            {
                Invalidate(ref plan);
                manager.SetComponentData(salvo, plan);
                return;
            }

            if (plan.Valid == 1 && plan.SourceSampleRevision == sample.Revision
                && plan.SourceSampleOrdinal == sample.SampleOrdinal)
                return;

            if (!TryGetProfile(manager, salvo, out var profile)
                || !TryBuildPlan(launch, sample, profile, out plan))
            {
                Invalidate(ref plan);
                manager.SetComponentData(salvo, plan);
                return;
            }

            manager.SetComponentData(salvo, plan);
        }

        internal static void InvalidateIfPresent(EntityManager manager, Entity salvo)
        {
            if (!manager.Exists(salvo) || !manager.HasComponent<SalvoAttackPlan>(salvo)) return;
            var plan = manager.GetComponentData<SalvoAttackPlan>(salvo);
            Invalidate(ref plan);
            manager.SetComponentData(salvo, plan);
        }

        public static bool TryBuildPlan(SalvoLaunchSnapshot launch, SalvoTargetContext sample,
            MissileApproachProfile profile, out SalvoAttackPlan plan)
        {
            plan = default;
            if (!IsValidProfile(profile) || launch.SessionGeneration == 0 || sample.SampleValid != 1
                || !sample.SampledTarget.IsValid || sample.SampledTarget.SessionGeneration != launch.SessionGeneration
                || sample.SampleOrdinal == 0 || !IsFinite(launch.Position) || !IsFinite(launch.Forward)
                || !IsFinite(sample.Position) || !IsFinite(sample.Velocity))
                return false;

            float3 predictedTarget = sample.Position + sample.Velocity * profile.PredictionTime;
            if (!IsFinite(predictedTarget) || !TryBuildFrame(launch.Position, predictedTarget, launch.Forward,
                out float3 forward, out float3 right, out float3 up))
                return false;

            plan = new SalvoAttackPlan
            {
                PredictedTarget = predictedTarget,
                Forward = forward,
                Right = right,
                Up = up,
                SourceSampleOrdinal = sample.SampleOrdinal,
                SourceSampleRevision = sample.Revision,
                Valid = 1
            };
            return true;
        }

        public static bool TryBuildFrame(float3 origin, float3 target, float3 fallbackForward,
            out float3 forward, out float3 right, out float3 up)
        {
            forward = right = up = default;
            if (!IsFinite(origin) || !IsFinite(target) || !IsFinite(fallbackForward)) return false;

            float3 approach = target - origin;
            forward = NormalizeOrDefault(approach, fallbackForward);
            if (math.lengthsq(forward) < DirectionEpsilonSq) forward = WorldForward;

            float3 helperUp = math.abs(math.dot(forward, WorldUp)) > 0.999f ? WorldForward : WorldUp;
            right = math.cross(helperUp, forward);
            if (math.lengthsq(right) < DirectionEpsilonSq)
            {
                helperUp = new float3(1f, 0f, 0f);
                right = math.cross(helperUp, forward);
            }
            if (math.lengthsq(right) < DirectionEpsilonSq) return false;

            right = math.normalize(right);
            up = math.normalize(math.cross(forward, right));
            return IsFinite(forward) && IsFinite(right) && IsFinite(up);
        }

        public static bool TryCreateMemberVariation(EntityManager manager, Entity member,
            ulong sessionGeneration, out MissileMemberTrajectoryVariation variation)
        {
            variation = default;
            if (!TryResolveCurrentPlanForMember(manager, member, sessionGeneration, out var salvo, out var plan, out var profile)
                || !manager.HasComponent<MissileMemberVariation>(member))
                return false;

            var seed = manager.GetComponentData<MissileMemberVariation>(member).Seed;
            if (plan.Valid != 1 || seed == 0) return false;

            variation.Motif = (MissileTrajectoryMotif)(Mix(seed, MissileTrajectoryVariationPurpose.Motif) % 4u);
            float lateralScale = 1f;
            float verticalScale = 1f;
            float curveScale = 1f;
            float phaseScale = 1f;
            switch (variation.Motif)
            {
                case MissileTrajectoryMotif.DirectPressure:
                    lateralScale = verticalScale = curveScale = phaseScale = 0.25f;
                    break;
                case MissileTrajectoryMotif.CurvedWideApproach:
                    verticalScale = 0.6f;
                    break;
                case MissileTrajectoryMotif.VerticalVariation:
                    lateralScale = 0.6f;
                    curveScale = 0.75f;
                    break;
                case MissileTrajectoryMotif.DelayedApproach:
                    lateralScale = verticalScale = curveScale = 0.5f;
                    break;
            }

            variation.LateralBias = SignedUnit(seed, MissileTrajectoryVariationPurpose.Lateral) * profile.MaxLateralBias * lateralScale;
            variation.VerticalBias = SignedUnit(seed, MissileTrajectoryVariationPurpose.Vertical) * profile.MaxVerticalBias * verticalScale;
            variation.CurveStrength = Unit(seed, MissileTrajectoryVariationPurpose.Curve) * profile.MaxCurveStrength * curveScale;
            variation.CommandPhase = Unit(seed, MissileTrajectoryVariationPurpose.CommandPhase) * profile.MaxCommandPhase * phaseScale;
            variation.Valid = 1;
            return true;
        }

        /// <summary>
        /// The only M1-D boundary for consuming a shared attack plan. A plan can be older
        /// than the current simulation step, but it must still describe the currently valid
        /// shared target sample and the member's active Salvo session.
        /// </summary>
        public static bool TryResolveCurrentPlanForMember(EntityManager manager, Entity member,
            ulong sessionGeneration, out Entity salvo, out SalvoAttackPlan plan, out MissileApproachProfile profile)
        {
            salvo = Entity.Null;
            plan = default;
            profile = default;
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, sessionGeneration, out salvo)
                || !MissileSalvoModelValidation.HasSharedProfile(manager, member, salvo)
                || !manager.HasComponent<SalvoTargetContext>(salvo)
                || !manager.HasComponent<SalvoAttackPlan>(salvo)
                || !TryGetProfile(manager, salvo, out profile))
                return false;

            var identity = manager.GetComponentData<SalvoIdentity>(salvo);
            var sample = manager.GetComponentData<SalvoTargetContext>(salvo);
            plan = manager.GetComponentData<SalvoAttackPlan>(salvo);
            if (identity.SessionGeneration != sessionGeneration || sample.HasTarget != 1 || !sample.Target.IsValid
                || sample.SampleValid != 1 || !sample.SampledTarget.IsValid
                || !sample.Target.Equals(sample.SampledTarget)
                || sample.Target.SessionGeneration != sessionGeneration
                || sample.SampledTarget.SessionGeneration != sessionGeneration
                || plan.Valid != 1 || plan.SourceSampleRevision != sample.Revision
                || plan.SourceSampleOrdinal != sample.SampleOrdinal || plan.SourceSampleOrdinal == 0
                || !IsValidPlan(plan))
            {
                plan = default;
                return false;
            }

            return true;
        }

        public static bool TryGetMemberAimPoint(SalvoAttackPlan plan, MissileMemberTrajectoryVariation variation, out float3 aimPoint)
        {
            aimPoint = default;
            if (plan.Valid != 1 || variation.Valid != 1) return false;
            aimPoint = plan.PredictedTarget + plan.Right * variation.LateralBias + plan.Up * variation.VerticalBias;
            return IsFinite(aimPoint);
        }

        public static bool IsValidProfile(MissileApproachProfile profile)
            => TryValidateProfile(profile, out _);

        /// <summary>
        /// Resolves immutable tuning without requiring a current target sample or attack
        /// plan. LaunchHold is valid even while the target is lost between request and
        /// real spawn acknowledgement.
        /// </summary>
        public static bool TryResolveProfileForMember(EntityManager manager, Entity member, ulong sessionGeneration,
            out MissileApproachProfile profile)
        {
            profile = default;
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, sessionGeneration, out var salvo)
                || !manager.HasComponent<MissileMemberProfileReference>(member)
                || !manager.HasComponent<SalvoProfileReference>(salvo))
                return false;

            Entity memberProfile = manager.GetComponentData<MissileMemberProfileReference>(member).Profile;
            Entity salvoProfile = manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
            if (memberProfile == Entity.Null || memberProfile != salvoProfile || !manager.Exists(memberProfile)
                || !manager.HasComponent<MissileApproachProfile>(memberProfile))
                return false;

            profile = manager.GetComponentData<MissileApproachProfile>(memberProfile);
            return IsValidProfile(profile);
        }

        /// <summary>Returns the first invalid profile field so authoring can reject bad input without treating it as runtime fallback.</summary>
        public static bool TryValidateProfile(MissileApproachProfile profile, out MissileProfileValidationFailure failure)
        {
            failure = MissileProfileValidationFailure.None;
            if (!IsFiniteInRange(profile.PredictionTime, 0f, MaximumPredictionTime)) failure = MissileProfileValidationFailure.PredictionTime;
            else if (!IsFiniteInRange(profile.MaxLateralBias, 0f, MaximumBiasDistance)) failure = MissileProfileValidationFailure.MaxLateralBias;
            else if (!IsFiniteInRange(profile.MaxVerticalBias, 0f, MaximumBiasDistance)) failure = MissileProfileValidationFailure.MaxVerticalBias;
            else if (!IsFiniteInRange(profile.MaxCurveStrength, 0f, MaximumCurveStrength)) failure = MissileProfileValidationFailure.MaxCurveStrength;
            else if (!IsFiniteInRange(profile.MaxCommandPhase, 0f, MaximumCommandPhase)) failure = MissileProfileValidationFailure.MaxCommandPhase;
            else if (!IsFiniteInRange(profile.Speed, 0f, MaximumSpeed)) failure = MissileProfileValidationFailure.Speed;
            else if (!IsFiniteInRange(profile.MaxSnapAngleRadians, 0f, math.PI)) failure = MissileProfileValidationFailure.MaxSnapAngle;
            else if (!IsFiniteInRange(profile.AttackCommitMaxSnapAngleRadians, 0f, profile.MaxSnapAngleRadians)) failure = MissileProfileValidationFailure.AttackCommitMaxSnapAngle;
            else if (!IsFiniteInRange(profile.TurnRateRadiansPerSecond, 0f, MaximumTurnRateRadiansPerSecond)) failure = MissileProfileValidationFailure.TurnRate;
            else if (!IsFiniteInRange(profile.MaxTurnRadiansPerTick, 0.000001f, MaximumTurnRadiansPerTick)) failure = MissileProfileValidationFailure.MaxTurnPerTick;
            else if (!IsFiniteInRange(profile.SegmentDuration, 0.000001f, MaximumSegmentDuration)) failure = MissileProfileValidationFailure.SegmentDuration;
            else if (!IsFiniteInRange(profile.LaunchHoldDuration, 0.000001f, MaximumLaunchHoldDuration)) failure = MissileProfileValidationFailure.LaunchHoldDuration;
            else if (!IsFiniteInRange(profile.AttackCommitDistance, 0f, MaximumBiasDistance)) failure = MissileProfileValidationFailure.AttackCommitDistance;
            return failure == MissileProfileValidationFailure.None;
        }

        private static bool TryGetProfile(EntityManager manager, Entity salvo, out MissileApproachProfile profile)
        {
            profile = default;
            var reference = manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
            if (reference == Entity.Null || !manager.Exists(reference) || !manager.HasComponent<MissileApproachProfile>(reference)) return false;
            profile = manager.GetComponentData<MissileApproachProfile>(reference);
            return IsValidProfile(profile);
        }

        private static float3 NormalizeOrDefault(float3 value, float3 fallback)
        {
            if (math.lengthsq(value) >= DirectionEpsilonSq) return math.normalize(value);
            if (math.lengthsq(fallback) >= DirectionEpsilonSq) return math.normalize(fallback);
            return WorldForward;
        }

        private static void Invalidate(ref SalvoAttackPlan plan)
        {
            plan = default;
        }

        private static bool IsFinite(float3 value) => math.all(math.isfinite(value));

        private static bool IsFiniteInRange(float value, float minimum, float maximum)
            => math.isfinite(value) && value >= minimum && value <= maximum;

        private static bool IsValidPlan(SalvoAttackPlan plan)
            => IsFinite(plan.PredictedTarget) && IsFinite(plan.Forward) && IsFinite(plan.Right) && IsFinite(plan.Up)
                && math.abs(math.lengthsq(plan.Forward) - 1f) <= 0.001f
                && math.abs(math.lengthsq(plan.Right) - 1f) <= 0.001f
                && math.abs(math.lengthsq(plan.Up) - 1f) <= 0.001f
                && math.abs(math.dot(plan.Forward, plan.Right)) <= 0.001f
                && math.abs(math.dot(plan.Right, plan.Up)) <= 0.001f
                && math.abs(math.dot(plan.Up, plan.Forward)) <= 0.001f;

        private static uint Mix(ulong seed, MissileTrajectoryVariationPurpose purpose)
        {
            ulong value = seed ^ (ulong)purpose;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9ul;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBul;
            return (uint)(value ^ (value >> 31));
        }

        private static float Unit(ulong seed, MissileTrajectoryVariationPurpose purpose)
            => (Mix(seed, purpose) & 0x00FFFFFFu) * (1f / 16777215f);

        private static float SignedUnit(ulong seed, MissileTrajectoryVariationPurpose purpose)
            => Unit(seed, purpose) * 2f - 1f;
    }
}
