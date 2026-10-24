using System;
using System.Collections.Generic;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.LocalSimulation;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace OverTheHorizon.LocalCombatMissiles
{
    /// <summary>
    /// Opt-in PlayMode fixture for observing the real CombatStep missile pipeline.
    /// It owns only demo authoring data; gameplay motion remains in the ECS stages.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalSimulationBootstrap))]
    public sealed class MissileFlightDemoFixture : MonoBehaviour
    {
        [SerializeField, Min(1)] private int memberCount = 50;
        [SerializeField, Min(1)] private int cellCount = 4;
        [SerializeField, Min(0.001f)] private float launchDelaySeconds = 0.04f;
        [SerializeField] private bool drawDebug = true;
        [SerializeField, Min(1)] private int maximumDrawnMembers = 64;
        [Header("Observation")]
        [SerializeField] private Vector3 observationEmitterPosition = new Vector3(0f, 30f, -150f);
        [SerializeField] private bool drawTrails = true;
        [SerializeField, Min(0.02f)] private float trailDurationSeconds = 6f;
        [SerializeField, Min(0.01f)] private float trailSampleIntervalSeconds = 0.04f;
        [SerializeField, Min(1)] private int maximumTrailPoints = 55000;
        [Header("Pattern Diagnostics")]
        [SerializeField] private bool collectPatternDiagnostics = true;
        [SerializeField, Min(1)] private int maximumDiagnosticMembers = 300;
        [SerializeField, Min(0.02f)] private float diagnosticHistoryDurationSeconds = 6f;
        [SerializeField, Min(0.01f)] private float diagnosticSampleIntervalSeconds = 0.04f;
        [SerializeField, Min(1)] private int maximumDiagnosticHistoryRecords = 60000;

        private LocalSimulationBootstrap bootstrap;
        private bool subscribed;
        internal static MissileFlightDemoFixture Active { get; private set; }
        internal bool DrawDebug => drawDebug;
        internal int MaximumDrawnMembers => maximumDrawnMembers;
        internal bool DrawTrails => drawTrails;
        internal float TrailDurationSeconds => trailDurationSeconds;
        internal float TrailSampleIntervalSeconds => trailSampleIntervalSeconds;
        internal int MaximumTrailPoints => maximumTrailPoints;
        internal bool CollectPatternDiagnostics => collectPatternDiagnostics;
        internal int MaximumDiagnosticMembers => maximumDiagnosticMembers;
        internal float DiagnosticHistoryDurationSeconds => diagnosticHistoryDurationSeconds;
        internal float DiagnosticSampleIntervalSeconds => diagnosticSampleIntervalSeconds;
        internal int MaximumDiagnosticHistoryRecords => maximumDiagnosticHistoryRecords;

        private void Awake() => bootstrap = GetComponent<LocalSimulationBootstrap>();

        private void OnEnable()
        {
            if (bootstrap == null) throw new InvalidOperationException("Missile demo requires a LocalSimulationBootstrap.");
            if (!subscribed)
            {
                bootstrap.ConfigureExecution += ConfigureExecution;
                subscribed = true;
            }
            Active = this;
            MissileFlightTrailHistory.Configure(maximumTrailPoints);
        }

        private void OnDisable()
        {
            if (subscribed && bootstrap != null) bootstrap.ConfigureExecution -= ConfigureExecution;
            subscribed = false;
            if (ReferenceEquals(Active, this)) Active = null;
            MissileFlightDebugSnapshot.Clear();
            MissileFlightTrailHistory.Clear();
            MissileFlightPatternDiagnostics.Clear();
        }

        private void Update()
        {
            if (bootstrap == null) return;
            MissileFlightTrailHistory.ObserveLifecycle(bootstrap.Generation, bootstrap.State);
            MissileFlightPatternDiagnostics.ObserveLifecycle(bootstrap.Generation, bootstrap.State);
        }

        private void ConfigureExecution(CombatStepExecutor executor)
        {
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            var manager = executor.World.EntityManager;
            var header = manager.GetComponentData<SimulationStepHeader>(executor.SessionEntity);
            MissileSalvoTargetSampling.EnsureCalendar(manager, executor.SessionEntity);
            MissileMemberCommandScheduling.EnsureCalendar(manager, executor.SessionEntity);
            CreateScenario(manager, executor.SessionEntity, header);
            executor.RegisterStage<MissileFlightDebugSnapshotStage>();
            executor.RegisterStage<MissileFlightOutcomeDiagnosticsStage>();
        }

        private void CreateScenario(EntityManager manager, Entity session, SimulationStepHeader header)
        {
            int count = Mathf.Clamp(memberCount, 1, 300);
            int cells = Mathf.Clamp(cellCount, 1, 16);
            const ulong emitterStableId = 13001;
            var emitter = manager.CreateEntity(typeof(MissileEmitterIdentity), typeof(MissileEmitterRuntimePose));
            manager.SetComponentData(emitter, new MissileEmitterIdentity { StableId = emitterStableId });
            manager.SetComponentData(emitter, new MissileEmitterRuntimePose
            {
                Position = observationEmitterPosition, Rotation = quaternion.identity, CanFire = 1
            });
            var launchCells = manager.AddBuffer<MissileEmitterLaunchCell>(emitter);
            for (uint i = 0; i < cells; ++i)
            {
                float x = ((i % 4) - 1.5f) * 1.5f;
                float y = (i / 4) * 1.25f;
                launchCells.Add(new MissileEmitterLaunchCell
                {
                    CellOrdinal = i, LocalPosition = new float3(x, y, 0f), LocalForward = new float3(0f, 0f, 1f), Valid = 1, Ready = 1
                });
            }

            var profile = manager.CreateEntity(typeof(MissileApproachProfile));
            var tuning = new MissileApproachProfile
            {
                PredictionTime = 0.35f, MaxLateralBias = 26f, MaxVerticalBias = 18f, MaxCurveStrength = 0.08f,
                MaxCommandPhase = 0.12f, Speed = 40f, MaxSnapAngleRadians = math.radians(45f),
                AttackCommitMaxSnapAngleRadians = math.radians(10f), TurnRateRadiansPerSecond = math.radians(110f),
                MaxTurnRadiansPerTick = math.radians(12f), SegmentDuration = 0.12f, LaunchHoldDuration = 0.24f,
                AttackCommitDistance = 14f
            };
            manager.SetComponentData(profile, tuning);

            var key = new SalvoLogicalKey(emitterStableId, 1);
            var salvo = manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoLaunchSnapshot),
                typeof(SalvoProfileReference), typeof(SalvoAttackPlan), typeof(SalvoSchedule), typeof(MissileSalvoLaunchControl),
                typeof(MissileSalvoEmitterReference), typeof(SalvoTargetSamplingState));
            manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = header.Generation });
            manager.SetComponentData(salvo, new SalvoTargetContext
            {
                Target = new SessionPlayerKey(1, 1, checked((uint)header.Generation)), HasTarget = 1
            });
            manager.SetComponentData(salvo, new SalvoLaunchSnapshot
            {
                Position = observationEmitterPosition, Forward = new float3(0f, 0f, 1f), SessionGeneration = header.Generation
            });
            manager.SetComponentData(salvo, new SalvoProfileReference { Profile = profile });
            manager.SetComponentData(salvo, new SalvoSchedule { CreatedOrdinal = 1, MemberCount = (uint)count });
            manager.SetComponentData(salvo, new MissileSalvoEmitterReference { Emitter = emitter, SessionGeneration = header.Generation });
            MissileSalvoTargetSampling.Register(manager, session, salvo, 1);

            ulong salvoSeed = MissileSeedDerivation.DeriveSalvoSeed(0x130DUL, key);
            for (uint i = 0; i < count; ++i)
            {
                var member = manager.CreateEntity(typeof(MissileDemoMember), typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference),
                    typeof(MissileMemberProfileReference), typeof(MissileMemberVariation), typeof(MissileMemberTrajectoryVariation),
                    typeof(MissileMemberLaunchState), typeof(MissileMemberCommandState), typeof(MissileMemberGuidanceState),
                    typeof(MissileMemberMotionState), typeof(MissileMemberCommandRequest), typeof(MissileMemberDueState),
                    typeof(MissileMemberLaunchCellAssignment), typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberLaunchHoldState),
                    typeof(MissileContactShape));
                var memberKey = new MemberLogicalKey(key, i);
                float lateral = ((int)(i % 7) - 3) * 3.2f;
                float vertical = ((int)((i / 7) % 5) - 2) * 2.5f;
                manager.SetComponentData(member, new MissileMemberIdentity { Key = memberKey, SessionGeneration = header.Generation });
                manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = header.Generation, Active = 1 });
                manager.SetComponentData(member, new MissileMemberProfileReference { Profile = profile });
                manager.SetComponentData(member, new MissileMemberVariation
                {
                    Seed = MissileSeedDerivation.DeriveMemberSeed(salvoSeed, memberKey, MissileSeedDomain.MemberVariation)
                });
                manager.SetComponentData(member, new MissileMemberTrajectoryVariation
                {
                    Motif = (MissileTrajectoryMotif)(i % 4), LateralBias = lateral, VerticalBias = vertical,
                    CurveStrength = 0.02f + (i % 4) * 0.012f, CommandPhase = (i % 5) * 0.024f, Valid = 1
                });
                manager.SetComponentData(member, new MissileMemberLaunchState
                {
                    LaunchDelaySeconds = (i / (uint)cells) * launchDelaySeconds, Pending = 1
                });
                manager.SetComponentData(member, new MissileMemberLaunchCellAssignment { Cell = new MissileLaunchCellKey(emitterStableId, i % (uint)cells) });
                manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.LaunchBurst });
                manager.SetComponentData(member, new MissileMemberMotionState { Forward = new float3(0f, 0f, 1f), Speed = tuning.Speed });
                manager.SetComponentData(member, new MissileContactShape { Radius = 0.35f });
                manager.SetComponentData(member, new MissileMemberDueState { NodeIndex = -1 });
                MissileMemberCommandScheduling.RegisterMember(manager, session, member);
            }
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !ReferenceEquals(Active, this) || !drawDebug) return;
            foreach (var record in MissileFlightDebugSnapshot.Records)
            {
                Gizmos.color = ColorFor(record.State);
                Gizmos.DrawSphere(record.Position, 0.18f);
                Gizmos.DrawLine(record.Position, record.Position + record.Forward * 1.5f);
                Gizmos.color = record.Primitive == MissileFlightPrimitive.ConstantCurvatureArc ? Color.magenta : Color.cyan;
                Gizmos.DrawLine(record.PreviousPosition, record.Position);
            }
            if (!drawTrails) return;
            var previousByMember = new Dictionary<MemberLogicalKey, MissileFlightTrailPoint>();
            foreach (var point in MissileFlightTrailHistory.Points)
            {
                if (previousByMember.TryGetValue(point.Member, out var previous))
                {
                    Gizmos.color = TrailColor(point.State, point.Age01);
                    Gizmos.DrawLine(previous.Position, point.Position);
                }
                previousByMember[point.Member] = point;
            }
        }

        private static Color ColorFor(MissileGuidanceState state)
        {
            switch (state)
            {
                case MissileGuidanceState.LaunchBurst: return Color.white;
                case MissileGuidanceState.Acquire: return Color.yellow;
                case MissileGuidanceState.HomingArc: return Color.green;
                case MissileGuidanceState.AttackCommit: return Color.red;
                default: return Color.gray;
            }
        }

        private static Color TrailColor(MissileGuidanceState state, float age01)
            => Color.Lerp(ColorFor(state), Color.black, math.saturate(age01) * 0.72f);
    }

    public struct MissileDemoMember : IComponentData { }

    internal readonly struct MissileFlightDebugRecord
    {
        public readonly float3 Position;
        public readonly float3 PreviousPosition;
        public readonly float3 Forward;
        public readonly MissileFlightPrimitive Primitive;
        public readonly MissileGuidanceState State;
        public MissileFlightDebugRecord(float3 position, float3 previousPosition, float3 forward,
            MissileFlightPrimitive primitive, MissileGuidanceState state)
        { Position = position; PreviousPosition = previousPosition; Forward = forward; Primitive = primitive; State = state; }
    }

    internal static class MissileFlightDebugSnapshot
    {
        private static readonly List<MissileFlightDebugRecord> records = new List<MissileFlightDebugRecord>();
        public static IReadOnlyList<MissileFlightDebugRecord> Records => records;
        public static void Clear() => records.Clear();
        public static void Publish(MissileFlightDebugRecord record) => records.Add(record);
    }

    /// <summary>
    /// Presentation-owned bounded history. It records stable logical keys rather than Entity
    /// handles so terminal paths remain visible after M2-F despawns their runtime entities.
    /// </summary>
    public readonly struct MissileFlightTrailPoint
    {
        public readonly MemberLogicalKey Member;
        public readonly ulong SessionGeneration;
        public readonly ulong Ordinal;
        public readonly float3 Position;
        public readonly MissileGuidanceState State;
        public readonly MissileFlightPrimitive Primitive;
        public readonly float Age01;

        internal MissileFlightTrailPoint(MemberLogicalKey member, ulong sessionGeneration, ulong ordinal, float3 position,
            MissileGuidanceState state, MissileFlightPrimitive primitive, float age01)
        {
            Member = member; SessionGeneration = sessionGeneration; Ordinal = ordinal; Position = position;
            State = state; Primitive = primitive; Age01 = age01;
        }
    }

    public static class MissileFlightTrailHistory
    {
        private struct StoredPoint
        {
            public MemberLogicalKey Member;
            public ulong Generation, Ordinal;
            public float3 Position;
            public MissileGuidanceState State;
            public MissileFlightPrimitive Primitive;
        }

        private struct MemberSamples
        {
            public ulong LastOrdinal;
            public int RetainedCount;
        }

        private static readonly Queue<StoredPoint> points = new Queue<StoredPoint>();
        private static readonly Dictionary<MemberLogicalKey, MemberSamples> memberSamples = new Dictionary<MemberLogicalKey, MemberSamples>();
        private static ulong generation, latestOrdinal;
        private static int maximumPoints = 55000, maximumAgeTicks = 300, sampleIntervalTicks = 2;

        public static int Count => points.Count;

        public static IEnumerable<MissileFlightTrailPoint> Points
        {
            get
            {
                ulong earliest = latestOrdinal > (ulong)maximumAgeTicks ? latestOrdinal - (ulong)maximumAgeTicks : 0;
                foreach (var point in points)
                {
                    if (point.Generation != generation || point.Ordinal < earliest) continue;
                    float age = latestOrdinal <= point.Ordinal ? 0f : (latestOrdinal - point.Ordinal) / (float)maximumAgeTicks;
                    yield return new MissileFlightTrailPoint(point.Member, point.Generation, point.Ordinal, point.Position,
                        point.State, point.Primitive, math.saturate(age));
                }
            }
        }

        public static void Configure(int maximumPointCount)
        {
            maximumPoints = math.max(1, maximumPointCount);
            TrimToCapacity();
        }

        public static void Clear()
        {
            points.Clear();
            memberSamples.Clear();
            generation = latestOrdinal = 0;
        }

        public static void ObserveLifecycle(ulong sessionGeneration, SimulationLifecycle state)
        {
            if (state == SimulationLifecycle.Uninitialized || state == SimulationLifecycle.Stopped || state == SimulationLifecycle.Faulted)
                Clear();
            else if (generation != 0 && sessionGeneration != 0 && generation != sessionGeneration)
                Clear();
        }

        internal static void ConfigureSampling(int tickRate, float durationSeconds, float intervalSeconds, int maximumPointCount)
        {
            Configure(maximumPointCount);
            maximumAgeTicks = math.max(1, (int)math.ceil(math.max(0.02f, durationSeconds) * math.max(1, tickRate)));
            sampleIntervalTicks = math.max(1, (int)math.round(math.max(0.01f, intervalSeconds) * math.max(1, tickRate)));
        }

        internal static void Record(MemberLogicalKey member, ulong sessionGeneration, ulong ordinal, float3 position,
            MissileGuidanceState state, MissileFlightPrimitive primitive, bool force = false)
        {
            if (sessionGeneration == 0 || ordinal == 0 || !math.all(math.isfinite(position))) return;
            if (generation != sessionGeneration) Clear();
            generation = sessionGeneration;
            latestOrdinal = ordinal > latestOrdinal ? ordinal : latestOrdinal;
            memberSamples.TryGetValue(member, out var samples);
            if (!force && samples.RetainedCount > 0 && ordinal >= samples.LastOrdinal
                && ordinal - samples.LastOrdinal < (ulong)sampleIntervalTicks)
            { TrimExpired(); return; }
            points.Enqueue(new StoredPoint { Member = member, Generation = sessionGeneration, Ordinal = ordinal, Position = position,
                State = state, Primitive = primitive });
            samples.LastOrdinal = ordinal;
            ++samples.RetainedCount;
            memberSamples[member] = samples;
            TrimExpired();
            TrimToCapacity();
        }

        private static void TrimExpired()
        {
            ulong earliest = latestOrdinal > (ulong)maximumAgeTicks ? latestOrdinal - (ulong)maximumAgeTicks : 0;
            while (points.Count > 0 && (points.Peek().Generation != generation || points.Peek().Ordinal < earliest)) RemoveOldest();
        }

        private static void TrimToCapacity()
        {
            while (points.Count > maximumPoints) RemoveOldest();
        }

        private static void RemoveOldest()
        {
            var removed = points.Dequeue();
            if (!memberSamples.TryGetValue(removed.Member, out var samples)) return;
            --samples.RetainedCount;
            if (samples.RetainedCount <= 0) memberSamples.Remove(removed.Member);
            else memberSamples[removed.Member] = samples;
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissileMovementStage))]
    [UpdateBefore(typeof(MissileContactGatherStage))]
    public partial class MissileFlightDebugSnapshotStage : CombatStepSystem
    {
        private EntityQuery debugMembers;
        private readonly List<OrderedMember> orderedMembers = new List<OrderedMember>();

        private readonly struct OrderedMember
        {
            public readonly Entity Entity;
            public readonly MemberLogicalKey Key;

            public OrderedMember(Entity entity, MemberLogicalKey key)
            {
                Entity = entity;
                Key = key;
            }
        }

        protected override void OnCreate()
        {
            debugMembers = GetEntityQuery(typeof(MissileDemoMember), typeof(MissileRuntimeSpawnState), typeof(MissileRuntimeMovementState),
                typeof(MissileRuntimeGuidanceState), typeof(MissileMemberMotionState), typeof(MissileMemberPreviousPosition),
                typeof(MissileMemberIdentity), typeof(MissileMemberVariation), typeof(MissileMemberTrajectoryVariation),
                typeof(MissileMemberSpawnAcknowledgement), typeof(MissileMemberLaunchHoldState), typeof(MissileMemberCommandState),
                typeof(MissileMemberSalvoReference));
        }

        protected override void OnStep()
        {
            var fixture = MissileFlightDemoFixture.Active;
            MissileFlightDebugSnapshot.Clear();
            if (fixture == null || (!fixture.DrawDebug && !fixture.CollectPatternDiagnostics)) return;
            EntityManager.CompleteDependencyBeforeRO<MissileMemberMotionState>();
            if (fixture.DrawDebug)
                MissileFlightTrailHistory.ConfigureSampling(Step.TickRate, fixture.TrailDurationSeconds,
                    fixture.TrailSampleIntervalSeconds, fixture.MaximumTrailPoints);
            if (fixture.CollectPatternDiagnostics)
            {
                MissileFlightPatternDiagnostics.ConfigureSampling(Step.TickRate, fixture.DiagnosticHistoryDurationSeconds,
                    fixture.DiagnosticSampleIntervalSeconds, fixture.MaximumDiagnosticHistoryRecords, fixture.MaximumDiagnosticMembers);
                MissileFlightPatternDiagnostics.BeginFrame(Step.Generation, Step.Ordinal);
                RecordPlayerDiagnostic();
            }
            using var members = debugMembers.ToEntityArray(Unity.Collections.Allocator.Temp);
            orderedMembers.Clear();
            for (int i = 0; i < members.Length; ++i)
            {
                var member = members[i];
                orderedMembers.Add(new OrderedMember(member, EntityManager.GetComponentData<MissileMemberIdentity>(member).Key));
            }
            orderedMembers.Sort(CompareLogicalKey);
            int limit = math.min(members.Length, math.max(fixture.DrawDebug ? fixture.MaximumDrawnMembers : 0,
                fixture.CollectPatternDiagnostics ? fixture.MaximumDiagnosticMembers : 0));
            for (int i = 0; i < limit; ++i)
            {
                var member = orderedMembers[i].Entity;
                var motion = EntityManager.GetComponentData<MissileMemberMotionState>(member);
                var previous = EntityManager.GetComponentData<MissileMemberPreviousPosition>(member);
                var movement = EntityManager.GetComponentData<MissileRuntimeMovementState>(member);
                var guidance = EntityManager.GetComponentData<MissileRuntimeGuidanceState>(member);
                var identity = EntityManager.GetComponentData<MissileMemberIdentity>(member);
                var spawn = EntityManager.GetComponentData<MissileRuntimeSpawnState>(member);
                bool spawnedThisStep = spawn.SpawnOrdinal == Step.Ordinal;
                if (fixture.DrawDebug && i < fixture.MaximumDrawnMembers)
                {
                    MissileFlightDebugSnapshot.Publish(new MissileFlightDebugRecord(motion.Position, previous.Value, motion.Forward,
                        movement.ActiveSegment.Primitive, guidance.Value));
                    if (spawnedThisStep)
                        MissileFlightTrailHistory.Record(identity.Key, Step.Generation, Step.Ordinal, previous.Value,
                            guidance.Value, movement.ActiveSegment.Primitive, true);
                    MissileFlightTrailHistory.Record(identity.Key, Step.Generation, Step.Ordinal, motion.Position,
                        guidance.Value, movement.ActiveSegment.Primitive, spawnedThisStep);
                }
                if (fixture.CollectPatternDiagnostics && i < fixture.MaximumDiagnosticMembers)
                    RecordDiagnostic(member, identity, spawn, motion, movement, guidance);
            }
        }

        private void RecordPlayerDiagnostic()
        {
            if (!EntityManager.HasBuffer<PlayerStepElement>(SessionEntity)) return;
            var players = EntityManager.GetBuffer<PlayerStepElement>(SessionEntity);
            for (int i = 0; i < players.Length; ++i)
            {
                var player = players[i];
                if (player.Valid == 0 || player.Generation != Step.Generation || player.ConsumedOrdinal != Step.Ordinal) continue;
                MissileFlightPatternDiagnostics.RecordPlayer(new PlayerFlightDiagnosticRecord(Step.Generation, Step.Ordinal,
                    player.EndPosition, player.EndVelocity, player.SteeringForward, player.Axes));
                return;
            }
        }

        private void RecordDiagnostic(Entity member, MissileMemberIdentity identity, MissileRuntimeSpawnState spawn,
            MissileMemberMotionState motion, MissileRuntimeMovementState movement, MissileRuntimeGuidanceState guidance)
        {
            var variationSeed = EntityManager.GetComponentData<MissileMemberVariation>(member);
            var variation = EntityManager.GetComponentData<MissileMemberTrajectoryVariation>(member);
            var acknowledgement = EntityManager.GetComponentData<MissileMemberSpawnAcknowledgement>(member);
            var hold = EntityManager.GetComponentData<MissileMemberLaunchHoldState>(member);
            var command = EntityManager.GetComponentData<MissileMemberCommandState>(member);
            var reference = EntityManager.GetComponentData<MissileMemberSalvoReference>(member);
            SalvoAttackPlan plan = default;
            if (reference.Salvo != Entity.Null && EntityManager.Exists(reference.Salvo)
                && EntityManager.HasComponent<SalvoAttackPlan>(reference.Salvo))
                plan = EntityManager.GetComponentData<SalvoAttackPlan>(reference.Salvo);

            MissileFlightPatternDiagnostics.Record(new MissileFlightDiagnosticRecord(Step.Generation, Step.Ordinal,
                identity.Key, spawn.Cell, variationSeed.Seed, variation, acknowledgement.ActualSpawnOrdinal,
                hold.GuidanceEnableOrdinal, acknowledgement.Position, acknowledgement.LaunchForward, motion.Position, motion.Forward,
                motion.Speed, guidance.Value, movement.ActiveSegment.Primitive, movement.ActiveSegment.Curvature,
                movement.ActiveSegment.RotationAxis, command.LastRequestOrdinal, movement.ActiveSegment.StartOrdinal, plan));
        }

        private static int CompareLogicalKey(OrderedMember left, OrderedMember right)
        {
            int emitter = left.Key.Salvo.EmitterStableId.CompareTo(right.Key.Salvo.EmitterStableId);
            if (emitter != 0) return emitter;
            int salvo = left.Key.Salvo.SalvoSequence.CompareTo(right.Key.Salvo.SalvoSequence);
            return salvo != 0 ? salvo : left.Key.MemberOrdinal.CompareTo(right.Key.MemberOrdinal);
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissileOutcomeAndLifecycleStage))]
    [UpdateBefore(typeof(SimulationCompleteSystem))]
    public partial class MissileFlightOutcomeDiagnosticsStage : CombatStepSystem
    {
        protected override void OnStep()
        {
            var fixture = MissileFlightDemoFixture.Active;
            if (fixture == null || !fixture.CollectPatternDiagnostics
                || !EntityManager.HasBuffer<MissileOutcomeResult>(SessionEntity)) return;

            var outcomes = EntityManager.GetBuffer<MissileOutcomeResult>(SessionEntity);
            for (int i = 0; i < outcomes.Length; ++i)
                if (outcomes[i].SessionGeneration == Step.Generation && outcomes[i].Ordinal == Step.Ordinal)
                    MissileFlightPatternDiagnostics.RecordTerminal(outcomes[i]);
        }
    }
}
