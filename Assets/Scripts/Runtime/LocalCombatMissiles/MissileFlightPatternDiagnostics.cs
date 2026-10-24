using System;
using System.Collections.Generic;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.Timing;
using Unity.Mathematics;

namespace OverTheHorizon.LocalCombatMissiles
{
    /// <summary>
    /// Read-only snapshot of one active missile after its real movement step. This data never feeds Guidance, scheduling, or lifecycle.
    /// </summary>
    public readonly struct MissileFlightDiagnosticRecord
    {
        public readonly ulong Generation;
        public readonly ulong Ordinal;
        public readonly MemberLogicalKey Member;
        public readonly MissileLaunchCellKey Cell;
        public readonly ulong IndividualSeed;
        public readonly MissileMemberTrajectoryVariation Variation;
        public readonly ulong ActualSpawnOrdinal;
        public readonly ulong GuidanceEnableOrdinal;
        public readonly float3 LaunchPosition;
        public readonly float3 LaunchForward;
        public readonly float3 Position;
        public readonly float3 Forward;
        public readonly float Speed;
        public readonly MissileGuidanceState GuidanceState;
        public readonly MissileFlightPrimitive SegmentPrimitive;
        public readonly float SegmentCurvature;
        public readonly float3 SegmentRotationAxis;
        public readonly ulong CommandRequestOrdinal;
        public readonly ulong ActiveSegmentStartOrdinal;
        public readonly SalvoAttackPlan Plan;

        public MissileFlightDiagnosticRecord(ulong generation, ulong ordinal, MemberLogicalKey member, MissileLaunchCellKey cell,
            ulong individualSeed, MissileMemberTrajectoryVariation variation, ulong actualSpawnOrdinal, ulong guidanceEnableOrdinal,
            float3 launchPosition, float3 launchForward, float3 position, float3 forward, float speed,
            MissileGuidanceState guidanceState, MissileFlightPrimitive segmentPrimitive, float segmentCurvature,
            float3 segmentRotationAxis, ulong commandRequestOrdinal, ulong activeSegmentStartOrdinal, SalvoAttackPlan plan)
        {
            Generation = generation;
            Ordinal = ordinal;
            Member = member;
            Cell = cell;
            IndividualSeed = individualSeed;
            Variation = variation;
            ActualSpawnOrdinal = actualSpawnOrdinal;
            GuidanceEnableOrdinal = guidanceEnableOrdinal;
            LaunchPosition = launchPosition;
            LaunchForward = launchForward;
            Position = position;
            Forward = forward;
            Speed = speed;
            GuidanceState = guidanceState;
            SegmentPrimitive = segmentPrimitive;
            SegmentCurvature = segmentCurvature;
            SegmentRotationAxis = segmentRotationAxis;
            CommandRequestOrdinal = commandRequestOrdinal;
            ActiveSegmentStartOrdinal = activeSegmentStartOrdinal;
            Plan = plan;
        }
    }

    /// <summary>Terminal information survives ECS despawn so diagnostics can distinguish contact and expiry.</summary>
    public readonly struct MissileFlightTerminalDiagnostic
    {
        public readonly ulong Generation;
        public readonly ulong Ordinal;
        public readonly MemberLogicalKey Member;
        public readonly MissileTerminalReason Reason;
        public readonly float3 Point;

        public MissileFlightTerminalDiagnostic(MissileOutcomeResult outcome)
        {
            Generation = outcome.SessionGeneration;
            Ordinal = outcome.Ordinal;
            Member = outcome.Member;
            Reason = outcome.Reason;
            Point = outcome.Point;
        }
    }

    /// <summary>One canonical player state observed beside the missile frame. It is diagnostic-only.</summary>
    public readonly struct PlayerFlightDiagnosticRecord
    {
        public readonly ulong Generation;
        public readonly ulong Ordinal;
        public readonly float3 Position;
        public readonly float3 Velocity;
        public readonly float3 Forward;
        public readonly float3 Axes;

        public PlayerFlightDiagnosticRecord(ulong generation, ulong ordinal, float3 position, float3 velocity, float3 forward, float3 axes)
        {
            Generation = generation;
            Ordinal = ordinal;
            Position = position;
            Velocity = velocity;
            Forward = forward;
            Axes = axes;
        }
    }

    /// <summary>Bounded descriptive statistics. Eigenvalue/PCA classification remains an offline report decision.</summary>
    public readonly struct MissileFlightDistributionMetrics
    {
        public readonly int Count;
        public readonly float3 Center;
        public readonly float3 Minimum;
        public readonly float3 Maximum;
        public readonly float CovarianceXX;
        public readonly float CovarianceYY;
        public readonly float CovarianceZZ;
        public readonly float CovarianceXY;
        public readonly float CovarianceXZ;
        public readonly float CovarianceYZ;

        internal MissileFlightDistributionMetrics(int count, float3 center, float3 minimum, float3 maximum,
            float covarianceXX, float covarianceYY, float covarianceZZ, float covarianceXY, float covarianceXZ, float covarianceYZ)
        {
            Count = count;
            Center = center;
            Minimum = minimum;
            Maximum = maximum;
            CovarianceXX = covarianceXX;
            CovarianceYY = covarianceYY;
            CovarianceZZ = covarianceZZ;
            CovarianceXY = covarianceXY;
            CovarianceXZ = covarianceXZ;
            CovarianceYZ = covarianceYZ;
        }
    }

    /// <summary>
    /// Presentation-owned, bounded diagnostic store. Current-frame records answer the same-world-tick question;
    /// sampled history supports same-flight-age comparisons.
    /// </summary>
    public static class MissileFlightPatternDiagnostics
    {
        private static readonly List<MissileFlightDiagnosticRecord> currentFrame = new List<MissileFlightDiagnosticRecord>();
        private static readonly Queue<MissileFlightDiagnosticRecord> history = new Queue<MissileFlightDiagnosticRecord>();
        private static readonly Dictionary<MemberLogicalKey, ulong> lastSampleOrdinal = new Dictionary<MemberLogicalKey, ulong>();
        private static readonly Queue<MissileFlightTerminalDiagnostic> terminals = new Queue<MissileFlightTerminalDiagnostic>();
        private static readonly Queue<PlayerFlightDiagnosticRecord> playerHistory = new Queue<PlayerFlightDiagnosticRecord>();
        private static ulong generation;
        private static ulong latestOrdinal;
        private static int maximumHistoryRecords = 60000;
        private static int maximumTerminalRecords = 300;
        private static int maximumAgeTicks = 300;
        private static int sampleIntervalTicks = 2;

        public static IReadOnlyList<MissileFlightDiagnosticRecord> CurrentFrame => currentFrame;
        public static IEnumerable<MissileFlightDiagnosticRecord> History => history;
        public static IEnumerable<MissileFlightTerminalDiagnostic> Terminals => terminals;
        public static IEnumerable<PlayerFlightDiagnosticRecord> PlayerHistory => playerHistory;

        public static void ConfigureSampling(int tickRate, float durationSeconds, float intervalSeconds,
            int maximumHistoryRecordCount, int maximumTerminalRecordCount)
        {
            maximumHistoryRecords = math.max(1, maximumHistoryRecordCount);
            maximumTerminalRecords = math.max(1, maximumTerminalRecordCount);
            maximumAgeTicks = math.max(1, (int)math.ceil(math.max(0.02f, durationSeconds) * math.max(1, tickRate)));
            sampleIntervalTicks = math.max(1, (int)math.round(math.max(0.01f, intervalSeconds) * math.max(1, tickRate)));
            Trim();
        }

        public static void BeginFrame(ulong sessionGeneration, ulong ordinal)
        {
            if (sessionGeneration == 0 || ordinal == 0) return;
            if (generation != 0 && generation != sessionGeneration) Clear();
            generation = sessionGeneration;
            if (ordinal > latestOrdinal) latestOrdinal = ordinal;
            currentFrame.Clear();
            Trim();
        }

        public static void RecordPlayer(PlayerFlightDiagnosticRecord record)
        {
            if (record.Generation == 0 || record.Ordinal == 0 || record.Generation != generation
                || !IsFinite(record.Position) || !IsFinite(record.Velocity) || !IsFinite(record.Forward) || !IsFinite(record.Axes))
                return;
            playerHistory.Enqueue(record);
            while (playerHistory.Count > maximumAgeTicks + 1 || playerHistory.Count > maximumHistoryRecords)
                playerHistory.Dequeue();
        }

        public static void Record(MissileFlightDiagnosticRecord record)
        {
            if (record.Generation == 0 || record.Ordinal == 0 || record.Generation != generation || record.Ordinal != latestOrdinal)
                return;
            currentFrame.Add(record);
            if (lastSampleOrdinal.TryGetValue(record.Member, out ulong previous)
                && record.Ordinal >= previous && record.Ordinal - previous < (ulong)sampleIntervalTicks)
                return;

            lastSampleOrdinal[record.Member] = record.Ordinal;
            history.Enqueue(record);
            Trim();
        }

        public static void RecordTerminal(MissileOutcomeResult outcome)
        {
            if (outcome.SessionGeneration == 0 || outcome.SessionGeneration != generation || outcome.Ordinal == 0) return;
            terminals.Enqueue(new MissileFlightTerminalDiagnostic(outcome));
            while (terminals.Count > maximumTerminalRecords) terminals.Dequeue();
        }

        public static void ObserveLifecycle(ulong sessionGeneration, SimulationLifecycle state)
        {
            if (state == SimulationLifecycle.Uninitialized || state == SimulationLifecycle.Stopped || state == SimulationLifecycle.Faulted)
                Clear();
            else if (generation != 0 && sessionGeneration != 0 && generation != sessionGeneration)
                Clear();
        }

        public static void Clear()
        {
            currentFrame.Clear();
            history.Clear();
            lastSampleOrdinal.Clear();
            terminals.Clear();
            playerHistory.Clear();
            generation = latestOrdinal = 0;
        }

        public static bool TryGetApproachSpacePosition(MissileFlightDiagnosticRecord record, out float3 localPosition)
        {
            localPosition = default;
            if (record.Plan.Valid != 1 || !IsFinite(record.LaunchPosition) || !IsFinite(record.Position)
                || !IsFinite(record.Plan.Forward) || !IsFinite(record.Plan.Right) || !IsFinite(record.Plan.Up))
                return false;

            float3 delta = record.Position - record.LaunchPosition;
            localPosition = new float3(math.dot(delta, record.Plan.Right), math.dot(delta, record.Plan.Up), math.dot(delta, record.Plan.Forward));
            return IsFinite(localPosition);
        }

        public static MissileFlightDistributionMetrics CalculateMetrics(IReadOnlyList<MissileFlightDiagnosticRecord> records,
            bool useApproachSpace)
        {
            if (records == null || records.Count == 0) return default;
            int count = 0;
            float3 center = default;
            float3 minimum = new float3(float.PositiveInfinity);
            float3 maximum = new float3(float.NegativeInfinity);
            for (int i = 0; i < records.Count; ++i)
            {
                if (!TryGetPosition(records[i], useApproachSpace, out float3 position)) continue;
                ++count;
                center += position;
                minimum = math.min(minimum, position);
                maximum = math.max(maximum, position);
            }
            if (count == 0) return default;
            center /= count;

            float xx = 0f, yy = 0f, zz = 0f, xy = 0f, xz = 0f, yz = 0f;
            for (int i = 0; i < records.Count; ++i)
            {
                if (!TryGetPosition(records[i], useApproachSpace, out float3 position)) continue;
                float3 delta = position - center;
                xx += delta.x * delta.x;
                yy += delta.y * delta.y;
                zz += delta.z * delta.z;
                xy += delta.x * delta.y;
                xz += delta.x * delta.z;
                yz += delta.y * delta.z;
            }
            float inverse = 1f / count;
            return new MissileFlightDistributionMetrics(count, center, minimum, maximum,
                xx * inverse, yy * inverse, zz * inverse, xy * inverse, xz * inverse, yz * inverse);
        }

        private static bool TryGetPosition(MissileFlightDiagnosticRecord record, bool useApproachSpace, out float3 position)
        {
            position = record.Position;
            return useApproachSpace ? TryGetApproachSpacePosition(record, out position) : IsFinite(position);
        }

        private static void Trim()
        {
            ulong earliest = latestOrdinal > (ulong)maximumAgeTicks ? latestOrdinal - (ulong)maximumAgeTicks : 0;
            while (history.Count > 0 && (history.Peek().Generation != generation || history.Peek().Ordinal < earliest))
                history.Dequeue();
            while (history.Count > maximumHistoryRecords) history.Dequeue();
        }

        private static bool IsFinite(float3 value) => math.all(math.isfinite(value));
    }
}
