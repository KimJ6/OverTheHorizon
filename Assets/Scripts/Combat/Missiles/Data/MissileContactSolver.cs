using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    public enum MissileContactSolveResult : byte
    {
        NoContact,
        Contact,
        Indeterminate
    }

    public struct MissileContactHit
    {
        public float NormalizedTime;
        public float3 Point;
        public float3 Normal;
        public float MinimumSeparation;
    }

    /// <summary>
    /// Pure same-step sweep tests. Player centers are linear over the canonical frame;
    /// an arc is subdivided until its chord differs by at most ContactTolerance.
    /// </summary>
    public static class MissileContactSolver
    {
        public const float ContactTolerance = 0.0025f;
        public const int MaximumArcSubdivisions = 32;
        // This caps only an exact candidate refinement.  The 32-piece cap above remains
        // the contract for the conservative chord broadphase.
        public const int MaximumArcNarrowphaseDepth = 10;
        private const float EpsilonSq = 0.00000001f;

        public static MissileContactSolveResult TryFindStraight(float3 missileStart, float3 missileEnd, float missileRadius,
            float3 playerStart, float3 playerEnd, float playerRadius, out MissileContactHit hit)
        {
            hit = default;
            if (!IsFinite(missileStart) || !IsFinite(missileEnd) || !IsFinite(playerStart) || !IsFinite(playerEnd)
                || !IsValidRadius(missileRadius) || !IsValidRadius(playerRadius))
                return MissileContactSolveResult.Indeterminate;

            if (TryFindRelativeSweep(missileStart, missileEnd, missileRadius, playerStart, playerEnd, playerRadius,
                0f, 1f, out hit)) return MissileContactSolveResult.Contact;
            hit.MinimumSeparation = MinimumRelativeDistance(missileStart, missileEnd, playerStart, playerEnd) - missileRadius - playerRadius;
            return MissileContactSolveResult.NoContact;
        }

        public static MissileContactSolveResult TryFindArc(MissileFlightSegment segment, float3 missileStart,
            float3 missileStartForward, float missileRadius, float3 playerStart, float3 playerEnd, float playerRadius,
            out MissileContactHit hit, out int subdivisionCount, out int broadphaseCandidates)
        {
            hit = default;
            subdivisionCount = 0;
            broadphaseCandidates = 0;
            if (!TryGetArcParameters(segment, missileStart, missileStartForward, out var axis, out float3 side, out float totalAngle)
                || !IsValidRadius(missileRadius) || !IsValidRadius(playerRadius) || !IsFinite(playerStart) || !IsFinite(playerEnd))
                return MissileContactSolveResult.Indeterminate;

            float curvature = math.abs(segment.Curvature);
            float maxPieceAngle = MaximumPieceAngle(curvature);
            if (!math.isfinite(maxPieceAngle) || maxPieceAngle <= 0f) return MissileContactSolveResult.Indeterminate;
            subdivisionCount = (int)math.ceil(math.abs(totalAngle) / maxPieceAngle);
            subdivisionCount = math.max(1, subdivisionCount);
            if (subdivisionCount > MaximumArcSubdivisions) return MissileContactSolveResult.Indeterminate;

            float minimumSeparation = float.PositiveInfinity;
            for (int i = 0; i < subdivisionCount; ++i)
            {
                float t0 = (float)i / subdivisionCount;
                float t1 = (float)(i + 1) / subdivisionCount;
                if (!TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, t0, out float3 m0)
                    || !TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, t1, out float3 m1))
                    return MissileContactSolveResult.Indeterminate;

                float3 p0 = math.lerp(playerStart, playerEnd, t0);
                float3 p1 = math.lerp(playerStart, playerEnd, t1);
                float sagitta = Sagitta(curvature, math.abs(totalAngle) / subdivisionCount);
                if (!math.isfinite(sagitta)) return MissileContactSolveResult.Indeterminate;
                if (TryFindRelativeSweep(m0, m1, missileRadius + sagitta, p0, p1, playerRadius,
                    t0, t1, out _))
                {
                    ++broadphaseCandidates;
                    var resolved = ResolveArcCandidate(segment, missileStart, missileStartForward, axis, side, totalAngle,
                        playerStart, playerEnd, missileRadius, playerRadius, t0, t1, 0, ref minimumSeparation, out hit);
                    if (resolved != MissileContactSolveResult.NoContact) return resolved;
                }
                minimumSeparation = math.min(minimumSeparation, MinimumRelativeDistance(m0, m1, p0, p1) - missileRadius - playerRadius);
            }
            hit.MinimumSeparation = minimumSeparation;
            return MissileContactSolveResult.NoContact;
        }

        private static MissileContactSolveResult ResolveArcCandidate(MissileFlightSegment segment, float3 missileStart,
            float3 missileStartForward, float3 axis, float3 side, float totalAngle, float3 playerStart, float3 playerEnd,
            float missileRadius, float playerRadius, float timeStart, float timeEnd, int depth,
            ref float minimumSeparation, out MissileContactHit hit)
        {
            hit = default;
            float midpoint = (timeStart + timeEnd) * 0.5f;
            if (!TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, timeStart, out float3 missileAtStart)
                || !TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, midpoint, out float3 missileAtMid)
                || !TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, timeEnd, out float3 missileAtEnd))
                return MissileContactSolveResult.Indeterminate;

            float3 playerAtStart = math.lerp(playerStart, playerEnd, timeStart);
            float3 playerAtMid = math.lerp(playerStart, playerEnd, midpoint);
            float3 playerAtEnd = math.lerp(playerStart, playerEnd, timeEnd);
            UpdateMinimumSeparation(missileAtStart, playerAtStart, missileRadius, playerRadius, ref minimumSeparation);
            UpdateMinimumSeparation(missileAtMid, playerAtMid, missileRadius, playerRadius, ref minimumSeparation);
            UpdateMinimumSeparation(missileAtEnd, playerAtEnd, missileRadius, playerRadius, ref minimumSeparation);

            if (depth >= MaximumArcNarrowphaseDepth)
            {
                if (IsExactContact(missileAtStart, playerAtStart, missileRadius, playerRadius))
                    return CreateExactHit(segment, missileStart, missileStartForward, axis, side, totalAngle, playerStart, playerEnd,
                        missileRadius, playerRadius, timeStart, ref minimumSeparation, out hit);
                if (IsExactContact(missileAtMid, playerAtMid, missileRadius, playerRadius))
                    return RefineExactContact(segment, missileStart, missileStartForward, axis, side, totalAngle, playerStart, playerEnd,
                        missileRadius, playerRadius, timeStart, midpoint, ref minimumSeparation, out hit);
                if (IsExactContact(missileAtEnd, playerAtEnd, missileRadius, playerRadius))
                    return RefineExactContact(segment, missileStart, missileStartForward, axis, side, totalAngle, playerStart, playerEnd,
                        missileRadius, playerRadius, midpoint, timeEnd, ref minimumSeparation, out hit);
                return MissileContactSolveResult.Indeterminate;
            }

            float halfAngle = math.abs(totalAngle) * (midpoint - timeStart);
            float sagitta = Sagitta(math.abs(segment.Curvature), halfAngle);
            if (!math.isfinite(sagitta)) return MissileContactSolveResult.Indeterminate;
            if (CouldContact(missileAtStart, missileAtMid, playerAtStart, playerAtMid, missileRadius, playerRadius, sagitta))
            {
                var left = ResolveArcCandidate(segment, missileStart, missileStartForward, axis, side, totalAngle,
                    playerStart, playerEnd, missileRadius, playerRadius, timeStart, midpoint, depth + 1, ref minimumSeparation, out hit);
                if (left != MissileContactSolveResult.NoContact) return left;
            }
            if (CouldContact(missileAtMid, missileAtEnd, playerAtMid, playerAtEnd, missileRadius, playerRadius, sagitta))
                return ResolveArcCandidate(segment, missileStart, missileStartForward, axis, side, totalAngle,
                    playerStart, playerEnd, missileRadius, playerRadius, midpoint, timeEnd, depth + 1, ref minimumSeparation, out hit);
            return MissileContactSolveResult.NoContact;
        }

        private static MissileContactSolveResult RefineExactContact(MissileFlightSegment segment, float3 missileStart,
            float3 missileStartForward, float3 axis, float3 side, float totalAngle, float3 playerStart, float3 playerEnd,
            float missileRadius, float playerRadius, float outsideTime, float insideTime, ref float minimumSeparation,
            out MissileContactHit hit)
        {
            for (int i = 0; i < 16; ++i)
            {
                float midpoint = (outsideTime + insideTime) * 0.5f;
                if (!TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, midpoint, out float3 missilePoint))
                {
                    hit = default;
                    return MissileContactSolveResult.Indeterminate;
                }
                float3 playerPoint = math.lerp(playerStart, playerEnd, midpoint);
                if (IsExactContact(missilePoint, playerPoint, missileRadius, playerRadius)) insideTime = midpoint;
                else outsideTime = midpoint;
            }
            return CreateExactHit(segment, missileStart, missileStartForward, axis, side, totalAngle, playerStart, playerEnd,
                missileRadius, playerRadius, insideTime, ref minimumSeparation, out hit);
        }

        private static MissileContactSolveResult CreateExactHit(MissileFlightSegment segment, float3 missileStart,
            float3 missileStartForward, float3 axis, float3 side, float totalAngle, float3 playerStart, float3 playerEnd,
            float missileRadius, float playerRadius, float time, ref float minimumSeparation, out MissileContactHit hit)
        {
            hit = default;
            if (!TryEvaluateArc(segment, missileStart, missileStartForward, axis, side, totalAngle, time, out float3 missilePoint))
                return MissileContactSolveResult.Indeterminate;
            float3 playerPoint = math.lerp(playerStart, playerEnd, time);
            if (!IsExactContact(missilePoint, playerPoint, missileRadius, playerRadius)) return MissileContactSolveResult.Indeterminate;
            UpdateMinimumSeparation(missilePoint, playerPoint, missileRadius, playerRadius, ref minimumSeparation);
            float3 normal = math.normalizesafe(missilePoint - playerPoint, missileStartForward);
            hit = new MissileContactHit
            {
                NormalizedTime = time,
                Point = missilePoint - normal * missileRadius,
                Normal = normal,
                MinimumSeparation = minimumSeparation
            };
            return MissileContactSolveResult.Contact;
        }

        private static bool CouldContact(float3 missileStart, float3 missileEnd, float3 playerStart, float3 playerEnd,
            float missileRadius, float playerRadius, float sagitta)
            => MinimumRelativeDistance(missileStart, missileEnd, playerStart, playerEnd)
                <= missileRadius + playerRadius + ContactTolerance + sagitta;

        private static bool IsExactContact(float3 missilePoint, float3 playerPoint, float missileRadius, float playerRadius)
        {
            float radius = missileRadius + playerRadius + ContactTolerance;
            return math.lengthsq(missilePoint - playerPoint) <= radius * radius;
        }

        private static void UpdateMinimumSeparation(float3 missilePoint, float3 playerPoint, float missileRadius,
            float playerRadius, ref float minimumSeparation)
            => minimumSeparation = math.min(minimumSeparation, math.distance(missilePoint, playerPoint) - missileRadius - playerRadius);

        private static bool TryFindRelativeSweep(float3 missileStart, float3 missileEnd, float missileRadius,
            float3 playerStart, float3 playerEnd, float playerRadius, float timeStart, float timeEnd, out MissileContactHit hit)
        {
            hit = default;
            float3 relativeStart = missileStart - playerStart;
            float3 relativeDelta = (missileEnd - missileStart) - (playerEnd - playerStart);
            float combinedRadius = missileRadius + playerRadius + ContactTolerance;
            float combinedRadiusSq = combinedRadius * combinedRadius;
            float startDistanceSq = math.lengthsq(relativeStart);
            float localTime;
            if (startDistanceSq <= combinedRadiusSq) localTime = 0f;
            else
            {
                float a = math.lengthsq(relativeDelta);
                if (!math.isfinite(a) || a <= EpsilonSq) return false;
                float b = math.dot(relativeStart, relativeDelta);
                float c = startDistanceSq - combinedRadiusSq;
                float discriminant = b * b - a * c;
                if (!math.isfinite(discriminant) || discriminant < 0f) return false;
                localTime = (-b - math.sqrt(math.max(0f, discriminant))) / a;
                if (localTime < 0f || localTime > 1f) return false;
            }

            float3 missilePoint = math.lerp(missileStart, missileEnd, localTime);
            float3 playerPoint = math.lerp(playerStart, playerEnd, localTime);
            float3 normal = math.normalizesafe(missilePoint - playerPoint, new float3(0f, 1f, 0f));
            hit = new MissileContactHit
            {
                NormalizedTime = math.lerp(timeStart, timeEnd, localTime),
                Point = missilePoint - normal * missileRadius,
                Normal = normal,
                MinimumSeparation = math.sqrt(math.max(0f, startDistanceSq)) - missileRadius - playerRadius
            };
            return true;
        }

        private static bool TryGetArcParameters(MissileFlightSegment segment, float3 start, float3 forward,
            out float3 axis, out float3 side, out float totalAngle)
        {
            axis = side = default;
            totalAngle = 0f;
            if (segment.Valid != 1 || segment.Primitive != MissileFlightPrimitive.ConstantCurvatureArc || !IsFinite(start)
                || !TryNormalize(forward, out forward) || !TryNormalize(segment.RotationAxis, out axis)
                || !math.isfinite(segment.Curvature) || math.abs(segment.Curvature) <= MissileGuidanceSolver.CurvatureEpsilon
                || !math.isfinite(segment.Speed) || segment.Speed <= 0f || !math.isfinite(segment.StepDeltaTime)
                || segment.StepDeltaTime <= 0f || math.abs(math.dot(axis, forward)) > 0.001f)
                return false;
            side = math.cross(axis, forward);
            if (!TryNormalize(side, out side)) return false;
            totalAngle = segment.Speed * segment.Curvature * segment.StepDeltaTime;
            return math.isfinite(totalAngle) && math.abs(totalAngle) > 0f;
        }

        private static bool TryEvaluateArc(MissileFlightSegment segment, float3 start, float3 forward, float3 axis,
            float3 side, float totalAngle, float t, out float3 position)
        {
            position = default;
            if (!math.isfinite(t) || t < 0f || t > 1f) return false;
            float angle = totalAngle * t;
            float sin = math.sin(angle);
            float oneMinusCos = 1f - math.cos(angle);
            position = start + forward * (sin / segment.Curvature) + side * (oneMinusCos / segment.Curvature);
            return IsFinite(position);
        }

        private static float MaximumPieceAngle(float curvature)
        {
            float radius = 1f / curvature;
            float ratio = math.clamp(ContactTolerance / radius, 0f, 2f);
            return 2f * math.acos(math.clamp(1f - ratio, -1f, 1f));
        }

        private static float Sagitta(float curvature, float angle)
            => (1f / curvature) * (1f - math.cos(angle * 0.5f));

        private static float MinimumRelativeDistance(float3 missileStart, float3 missileEnd, float3 playerStart, float3 playerEnd)
        {
            float3 relativeStart = missileStart - playerStart;
            float3 relativeDelta = (missileEnd - missileStart) - (playerEnd - playerStart);
            float lengthSq = math.lengthsq(relativeDelta);
            float t = lengthSq <= EpsilonSq ? 0f : math.clamp(-math.dot(relativeStart, relativeDelta) / lengthSq, 0f, 1f);
            return math.length(relativeStart + relativeDelta * t);
        }

        private static bool IsValidRadius(float radius) => math.isfinite(radius) && radius > 0f;
        private static bool IsFinite(float3 value) => math.all(math.isfinite(value));
        private static bool TryNormalize(float3 value, out float3 normalized)
        {
            normalized = default;
            if (!IsFinite(value) || math.lengthsq(value) <= EpsilonSq) return false;
            normalized = math.normalize(value);
            return IsFinite(normalized);
        }
    }
}
