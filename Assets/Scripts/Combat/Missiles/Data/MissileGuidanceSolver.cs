using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    /// <summary>
    /// Pure M1-D Guidance and segment math. It emits commands only; M1-E/M2 own command
    /// scheduling, Entity storage, and movement execution.
    /// </summary>
    public static class MissileGuidanceSolver
    {
        public const float DirectionEpsilonSq = 0.000001f;
        public const float CurvatureEpsilon = 0.0001f;
        private static readonly float3 WorldUp = new float3(0f, 1f, 0f);
        private static readonly float3 WorldRight = new float3(1f, 0f, 0f);

        public static bool TryCreateSegmentForMember(EntityManager manager, Entity member, ulong sessionGeneration,
            MissileMemberTrajectoryVariation variation, MissileGuidanceRequest request,
            out MissileFlightSegment segment, out MissileGuidanceDecision decision)
        {
            segment = default;
            decision = MissileGuidanceDecision.Rejected;
            return MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, sessionGeneration,
                out _, out var plan, out var profile)
                && TryCreateSegment(plan, variation, profile, request, out segment, out decision);
        }

        /// <summary>Builds one segment from an already-current shared plan. Callers that own entities use TryCreateSegmentForMember.</summary>
        public static bool TryCreateSegment(SalvoAttackPlan plan, MissileMemberTrajectoryVariation variation,
            MissileApproachProfile profile, MissileGuidanceRequest request,
            out MissileFlightSegment segment, out MissileGuidanceDecision decision)
        {
            segment = default;
            decision = MissileGuidanceDecision.Rejected;
            if (!MissileSalvoApproachPlanning.IsValidProfile(profile) || !IsValidVariation(variation, profile)
                || !IsFinite(request.Motion.Position) || !math.isfinite(request.Motion.Speed)
                || request.Motion.Speed < 0f || request.Motion.Speed > MissileSalvoApproachPlanning.MaximumSpeed
                || !math.isfinite(request.DeltaTime) || request.DeltaTime <= 0f || request.DeltaTime > 1f
                || !TryNormalize(request.Motion.Forward, out float3 currentForward))
                return false;

            if (!TryGetDesiredDirection(plan, variation, request.Motion.Position, out float3 desiredDirection))
                return TryInitializeStraight(request.Motion.Position, currentForward, request.Motion.Speed, request.DeltaTime,
                    request.StartOrdinal, EndOrdinal(request, profile), request.CommandSequence,
                    MissileGuidanceDecision.StraightFallback, out segment, out decision);

            float angle = math.acos(math.clamp(math.dot(currentForward, desiredDirection), -1f, 1f));
            ulong endOrdinal = EndOrdinal(request, profile);
            float snapAngle = GetSnapAngle(profile, request.State);
            if (snapAngle > 0f && angle <= snapAngle)
                return TryInitializeStraight(request.Motion.Position, desiredDirection, request.Motion.Speed, request.DeltaTime,
                    request.StartOrdinal, endOrdinal, request.CommandSequence, MissileGuidanceDecision.Snap, out segment, out decision);

            float maximumStepAngle = math.min(profile.TurnRateRadiansPerSecond * request.DeltaTime, profile.MaxTurnRadiansPerTick);
            if (angle <= CurvatureEpsilon || maximumStepAngle <= CurvatureEpsilon || request.Motion.Speed <= 0f
                || variation.CurveStrength <= CurvatureEpsilon)
                return TryInitializeStraight(request.Motion.Position, currentForward, request.Motion.Speed, request.DeltaTime,
                    request.StartOrdinal, endOrdinal, request.CommandSequence, MissileGuidanceDecision.Straight, out segment, out decision);

            if (!TryBuildTurnAxis(currentForward, desiredDirection, out float3 axis))
                return TryInitializeStraight(request.Motion.Position, currentForward, request.Motion.Speed, request.DeltaTime,
                    request.StartOrdinal, endOrdinal, request.CommandSequence, MissileGuidanceDecision.StraightFallback, out segment, out decision);

            float maxCurvatureFromPolicy = maximumStepAngle / (request.Motion.Speed * request.DeltaTime);
            float curvature = math.min(variation.CurveStrength, maxCurvatureFromPolicy);
            curvature = math.min(curvature, profile.TurnRateRadiansPerSecond / request.Motion.Speed);
            if (!math.isfinite(curvature) || curvature <= CurvatureEpsilon)
                return TryInitializeStraight(request.Motion.Position, currentForward, request.Motion.Speed, request.DeltaTime,
                    request.StartOrdinal, endOrdinal, request.CommandSequence, MissileGuidanceDecision.StraightFallback, out segment, out decision);

            return TryInitializeArc(request.Motion.Position, currentForward, request.Motion.Speed, axis, curvature,
                request.DeltaTime, request.StartOrdinal, endOrdinal, request.CommandSequence,
                profile.TurnRateRadiansPerSecond, profile.MaxTurnRadiansPerTick, out segment, out decision);
        }

        public static bool TryGetDesiredDirection(SalvoAttackPlan plan, MissileMemberTrajectoryVariation variation,
            float3 currentPosition, out float3 desiredDirection)
        {
            desiredDirection = default;
            if (plan.Valid != 1 || variation.Valid != 1 || !IsFinite(currentPosition)
                || !IsFinite(plan.PredictedTarget) || !IsFinite(plan.Right) || !IsFinite(plan.Up)
                || !math.isfinite(variation.LateralBias) || !math.isfinite(variation.VerticalBias))
                return false;

            float3 aimPoint = plan.PredictedTarget + plan.Right * variation.LateralBias + plan.Up * variation.VerticalBias;
            return IsFinite(aimPoint) && TryNormalize(aimPoint - currentPosition, out desiredDirection);
        }

        public static bool TryIntegrateSegment(MissileFlightSegment segment, float3 position, float3 forward,
            out float3 nextPosition, out float3 nextForward)
        {
            nextPosition = position;
            nextForward = forward;
            if (segment.Valid != 1 || !IsFinite(position) || !TryNormalize(forward, out float3 normalizedForward)
                || !math.isfinite(segment.DeltaForward) || !math.isfinite(segment.DeltaSide))
                return false;

            if (segment.Primitive == MissileFlightPrimitive.Straight)
            {
                return TryIntegrateStraight(position, normalizedForward, segment.Speed, segment.StepDeltaTime,
                    out nextPosition, out nextForward);
            }

            if (segment.Primitive != MissileFlightPrimitive.ConstantCurvatureArc || !TryNormalize(segment.RotationAxis, out float3 axis)
                || math.abs(math.dot(axis, normalizedForward)) > 0.001f || !math.isfinite(segment.CosStep) || !math.isfinite(segment.SinStep))
                return false;

            float3 side = math.cross(axis, normalizedForward);
            nextPosition = position + normalizedForward * segment.DeltaForward + side * segment.DeltaSide;
            return IsFinite(nextPosition) && TryNormalize(normalizedForward * segment.CosStep + side * segment.SinStep, out nextForward);
        }

        /// <summary>Pure straight integration shared by runtime expiry fallback and segment execution.</summary>
        public static bool TryIntegrateStraight(float3 position, float3 forward, float speed, float deltaTime,
            out float3 nextPosition, out float3 nextForward)
        {
            nextPosition = position;
            nextForward = forward;
            if (!IsFinite(position) || !TryNormalize(forward, out var normalizedForward)
                || !math.isfinite(speed) || speed < 0f || !math.isfinite(deltaTime) || deltaTime <= 0f)
                return false;
            nextPosition = position + normalizedForward * (speed * deltaTime);
            nextForward = normalizedForward;
            return IsFinite(nextPosition);
        }

        public static bool TryCreateStraightFallback(MissileFlightSegment expired, float3 currentPosition, float3 currentForward,
            ulong startOrdinal, uint commandSequence, float deltaTime, out MissileFlightSegment segment)
        {
            MissileGuidanceDecision ignored;
            return TryInitializeStraight(currentPosition, currentForward, expired.Speed, deltaTime, startOrdinal,
                ulong.MaxValue, commandSequence, MissileGuidanceDecision.StraightFallback, out segment, out ignored);
        }

        /// <summary>
        /// Creates a finite launch-axis Straight without evaluating a target or a
        /// trajectory motif.  M1-F uses this for LaunchHold; M2 later executes it.
        /// </summary>
        public static bool TryCreateStraightSegment(float3 position, float3 forward, float speed, float deltaTime,
            ulong startOrdinal, ulong endOrdinal, uint commandSequence, out MissileFlightSegment segment)
        {
            segment = default;
            if (endOrdinal <= startOrdinal || endOrdinal == ulong.MaxValue) return false;
            MissileGuidanceDecision ignored;
            return TryInitializeStraight(position, forward, speed, deltaTime, startOrdinal, endOrdinal,
                commandSequence, MissileGuidanceDecision.Straight, out segment, out ignored);
        }

        private static bool TryInitializeStraight(float3 position, float3 forward, float speed, float deltaTime,
            ulong startOrdinal, ulong endOrdinal, uint commandSequence, MissileGuidanceDecision requestedDecision,
            out MissileFlightSegment segment, out MissileGuidanceDecision decision)
        {
            segment = default;
            decision = MissileGuidanceDecision.Rejected;
            if (!IsFinite(position) || !TryNormalize(forward, out forward) || !math.isfinite(speed) || speed < 0f
                || !math.isfinite(deltaTime) || deltaTime <= 0f || endOrdinal <= startOrdinal)
                return false;

            float deltaForward = speed * deltaTime;
            if (!math.isfinite(deltaForward)) return false;
            segment = new MissileFlightSegment
            {
                Primitive = MissileFlightPrimitive.Straight,
                StartPosition = position,
                StartForward = forward,
                Speed = speed,
                DeltaForward = deltaForward,
                StepDeltaTime = deltaTime,
                StartOrdinal = startOrdinal,
                EndOrdinal = endOrdinal,
                CommandSequence = commandSequence,
                Valid = 1
            };
            decision = requestedDecision;
            return true;
        }

        private static bool TryInitializeArc(float3 position, float3 forward, float speed, float3 axis, float curvature,
            float deltaTime, ulong startOrdinal, ulong endOrdinal, uint commandSequence,
            float maximumTurnRate, float maximumTurnPerTick,
            out MissileFlightSegment segment, out MissileGuidanceDecision decision)
        {
            segment = default;
            decision = MissileGuidanceDecision.Rejected;
            if (!IsFinite(position) || !TryNormalize(forward, out float3 normalizedForward)
                || !TryNormalize(axis, out float3 normalizedAxis)
                || !math.isfinite(speed) || speed <= 0f || !math.isfinite(curvature)
                || !math.isfinite(deltaTime) || deltaTime <= 0f || endOrdinal <= startOrdinal)
                return false;

            forward = normalizedForward;
            axis = normalizedAxis - normalizedForward * math.dot(normalizedAxis, normalizedForward);
            if (!TryNormalize(axis, out float3 orthogonalAxis) || math.abs(curvature) <= CurvatureEpsilon)
                return false;
            axis = orthogonalAxis;

            float theta = speed * curvature * deltaTime;
            if (!math.isfinite(theta) || math.abs(speed * curvature) > maximumTurnRate + 0.0001f
                || math.abs(theta) > maximumTurnPerTick + 0.0001f)
                return false;

            float sin = math.sin(theta);
            float halfSin = math.sin(theta * 0.5f);
            float deltaForward = sin / curvature;
            float deltaSide = 2f * halfSin * halfSin / curvature;
            if (!math.isfinite(deltaForward) || !math.isfinite(deltaSide)) return false;

            segment = new MissileFlightSegment
            {
                Primitive = MissileFlightPrimitive.ConstantCurvatureArc,
                StartPosition = position,
                StartForward = forward,
                RotationAxis = axis,
                Speed = speed,
                Curvature = curvature,
                CosStep = math.cos(theta),
                SinStep = sin,
                DeltaForward = deltaForward,
                DeltaSide = deltaSide,
                StepDeltaTime = deltaTime,
                StartOrdinal = startOrdinal,
                EndOrdinal = endOrdinal,
                CommandSequence = commandSequence,
                Valid = 1
            };
            decision = MissileGuidanceDecision.Arc;
            return true;
        }

        private static bool IsValidVariation(MissileMemberTrajectoryVariation variation, MissileApproachProfile profile)
            => variation.Valid == 1 && math.isfinite(variation.LateralBias) && math.isfinite(variation.VerticalBias)
                && math.isfinite(variation.CurveStrength) && math.isfinite(variation.CommandPhase)
                && math.abs(variation.LateralBias) <= profile.MaxLateralBias
                && math.abs(variation.VerticalBias) <= profile.MaxVerticalBias
                && variation.CurveStrength >= 0f && variation.CurveStrength <= profile.MaxCurveStrength
                && variation.CommandPhase >= 0f && variation.CommandPhase <= profile.MaxCommandPhase;

        private static float GetSnapAngle(MissileApproachProfile profile, MissileGuidanceState state)
        {
            switch (state)
            {
                case MissileGuidanceState.Acquire:
                case MissileGuidanceState.HomingArc:
                    return profile.MaxSnapAngleRadians;
                case MissileGuidanceState.AttackCommit:
                    return profile.AttackCommitMaxSnapAngleRadians;
                default:
                    return 0f;
            }
        }

        private static ulong EndOrdinal(MissileGuidanceRequest request, MissileApproachProfile profile)
        {
            double ticks = math.ceil(profile.SegmentDuration / request.DeltaTime);
            if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks < 1d || ticks > ulong.MaxValue - request.StartOrdinal)
                return 0;
            return request.StartOrdinal + (ulong)ticks;
        }

        private static bool TryBuildTurnAxis(float3 forward, float3 desired, out float3 axis)
        {
            float3 candidate = math.cross(forward, desired);
            if (TryNormalize(candidate, out axis)) return true;
            if (math.dot(forward, desired) > -0.999f) return false;

            float3 helper = math.abs(math.dot(forward, WorldUp)) < 0.999f ? WorldUp : WorldRight;
            candidate = math.cross(forward, helper);
            return TryNormalize(candidate, out axis);
        }

        private static bool TryNormalize(float3 value, out float3 normalized)
        {
            normalized = default;
            if (!IsFinite(value) || math.lengthsq(value) < DirectionEpsilonSq) return false;
            normalized = math.normalize(value);
            return IsFinite(normalized);
        }

        private static bool IsFinite(float3 value) => math.all(math.isfinite(value));
    }
}
