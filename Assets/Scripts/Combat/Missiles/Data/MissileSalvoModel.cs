using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    /// <summary>Provided by the owning gameplay system; M1-A does not allocate emitter IDs.</summary>
    public struct MissileEmitterIdentity : IComponentData
    {
        public ulong StableId;
        public uint NextSalvoSequence;
    }

    /// <summary>Shared identity and session boundary for one future Salvo context.</summary>
    public struct SalvoIdentity : IComponentData
    {
        public SalvoLogicalKey Key;
        public ulong SessionGeneration;
    }

    /// <summary>Shared deterministic seed for the Salvo; no global RNG is consumed.</summary>
    public struct SalvoSeed : IComponentData
    {
        public ulong Value;
    }

    /// <summary>
    /// The assigned player and the latest shared sample for one Salvo. Target is the
    /// assignment input; SampledTarget is populated only while SampleValid is one.
    /// Members retain no copy of this data.
    /// </summary>
    public struct SalvoTargetContext : IComponentData
    {
        public SessionPlayerKey Target;
        public byte HasTarget;
        public SessionPlayerKey SampledTarget;
        public float3 Position;
        public float3 Velocity;
        public ulong SampleOrdinal;
        public uint Revision;
        public byte SampleValid;
    }

    /// <summary>
    /// Immutable launch-space input captured from the emitter when it creates the
    /// Salvo. It is a planning reference only; M1-E owns the future live spawn pose.
    /// </summary>
    public struct SalvoLaunchSnapshot : IComponentData
    {
        public float3 Position;
        public float3 Forward;
        public ulong SessionGeneration;
    }

    /// <summary>
    /// Shared immutable tuning read from the Salvo profile entity. Distances are metres,
    /// angles are radians, turn rate is radians per second, curve strength is radians per
    /// metre, and durations are seconds.
    /// </summary>
    public struct MissileApproachProfile : IComponentData
    {
        public float PredictionTime;
        public float MaxLateralBias;
        public float MaxVerticalBias;
        public float MaxCurveStrength;
        public float MaxCommandPhase;
        public float Speed;
        public float MaxSnapAngleRadians;
        public float AttackCommitMaxSnapAngleRadians;
        public float TurnRateRadiansPerSecond;
        public float MaxTurnRadiansPerTick;
        public float SegmentDuration;
        /// <summary>Time a spawned member follows its actual launch axis before Acquire.</summary>
        public float LaunchHoldDuration;
        /// <summary>Zero disables the optional final-approach state for this profile.</summary>
        public float AttackCommitDistance;
    }

    /// <summary>
    /// Shared approach frame and plan, derived from exactly one valid target sample.
    /// SourceSampleRevision is the plan revision; no duplicate plan counter is kept.
    /// </summary>
    public struct SalvoAttackPlan : IComponentData
    {
        public float3 PredictedTarget;
        public float3 Forward;
        public float3 Right;
        public float3 Up;
        public ulong SourceSampleOrdinal;
        public uint SourceSampleRevision;
        public byte Valid;
    }

    /// <summary>
    /// Composition metadata, not a launch scheduler. Due ticks and command queues are
    /// deliberately deferred to M1-E.
    /// </summary>
    public struct SalvoSchedule : IComponentData
    {
        public ulong CreatedOrdinal;
        public uint MemberCount;
    }

    /// <summary>
    /// Points at one immutable profile owner. Concrete tuning fields and authoring are
    /// intentionally deferred until their guidance and motion consumers are defined.
    /// </summary>
    public struct SalvoProfileReference : IComponentData
    {
        public Entity Profile;
    }

    /// <summary>Member-local identity and its deterministic seed input.</summary>
    public struct MissileMemberIdentity : IComponentData
    {
        public MemberLogicalKey Key;
        public ulong SessionGeneration;
    }

    /// <summary>
    /// The only M1-A membership representation: each Member names its Salvo. Entity
    /// version, generation, and logical key must all agree before the reference is used.
    /// </summary>
    public struct MissileMemberSalvoReference : IComponentData
    {
        public Entity Salvo;
        public ulong SessionGeneration;
        public byte Active;
    }

    /// <summary>Immutable variation input; M1-C interprets it into path offsets or roles.</summary>
    public struct MissileMemberVariation : IComponentData
    {
        public ulong Seed;
    }

    public enum MissileTrajectoryMotif : byte
    {
        DirectPressure,
        CurvedWideApproach,
        VerticalVariation,
        DelayedApproach
    }

    /// <summary>
    /// Deterministic Guidance input, not a waypoint or a movement command. It stores
    /// only Member-local offsets and never copies the shared Salvo target sample.
    /// </summary>
    public struct MissileMemberTrajectoryVariation : IComponentData
    {
        public MissileTrajectoryMotif Motif;
        public float LateralBias;
        public float VerticalBias;
        public float CurveStrength;
        public float CommandPhase;
        public byte Valid;
    }

    /// <summary>Members retain only the shared profile reference, never duplicated tuning values.</summary>
    public struct MissileMemberProfileReference : IComponentData
    {
        public Entity Profile;
    }

    public enum MissileGuidanceState : byte
    {
        LaunchBurst,
        Separation,
        CruiseDrift,
        Acquire,
        HomingArc,
        AttackCommit
    }

    public enum MissileGuidanceDecision : byte
    {
        Rejected,
        Straight,
        Arc,
        Snap,
        StraightFallback
    }

    public enum MissileProfileValidationFailure : byte
    {
        None,
        PredictionTime,
        MaxLateralBias,
        MaxVerticalBias,
        MaxCurveStrength,
        MaxCommandPhase,
        Speed,
        MaxSnapAngle,
        AttackCommitMaxSnapAngle,
        TurnRate,
        MaxTurnPerTick,
        SegmentDuration,
        LaunchHoldDuration,
        AttackCommitDistance
    }

    public enum MissileFlightPrimitive : byte
    {
        Straight,
        ConstantCurvatureArc
    }

    /// <summary>Transient current-motion input. M1-D deliberately does not make this an ECS runtime component.</summary>
    public struct MissileGuidanceMotion
    {
        public float3 Position;
        public float3 Forward;
        public float Speed;
    }

    /// <summary>All command boundaries are Simulation Step ordinals; no independent clock is introduced.</summary>
    public struct MissileGuidanceRequest
    {
        public MissileGuidanceMotion Motion;
        public MissileGuidanceState State;
        public float DeltaTime;
        public ulong StartOrdinal;
        public uint CommandSequence;
    }

    /// <summary>
    /// A compact motion command. M2 will keep live position and forward separately; these
    /// start values make this pure M1-D contract independently testable.
    /// </summary>
    public struct MissileFlightSegment
    {
        public MissileFlightPrimitive Primitive;
        public float3 StartPosition;
        public float3 StartForward;
        public float3 RotationAxis;
        public float Speed;
        public float Curvature;
        public float CosStep;
        public float SinStep;
        public float DeltaForward;
        public float DeltaSide;
        /// <summary>Simulation delta time used to precompute the per-step coefficients.</summary>
        public float StepDeltaTime;
        public ulong StartOrdinal;
        public ulong EndOrdinal;
        public uint CommandSequence;
        public byte Valid;
    }

    /// <summary>Logical Emitter outcome owned by the Salvo until M2 binds a live Emitter entity.</summary>
    public struct MissileSalvoLaunchControl : IComponentData
    {
        /// <summary>Emitter destruction wins over a pending launch at the same ordinal.</summary>
        public byte EmitterDestroyed;
    }

    /// <summary>Member-local launch timing. Pending is distinct from a request accepted by M2.</summary>
    public struct MissileMemberLaunchState : IComponentData
    {
        public float LaunchDelaySeconds;
        public ulong DueOrdinal;
        public byte Pending;
        public byte LaunchRequested;
        public byte Cancelled;
    }

    /// <summary>Emitter-owned launch-cell assignment for a logical Member.</summary>
    public struct MissileMemberLaunchCellAssignment : IComponentData
    {
        public MissileLaunchCellKey Cell;
    }

    /// <summary>Runtime binding from a Salvo's stable emitter identity to its live ECS owner.</summary>
    public struct MissileSalvoEmitterReference : IComponentData
    {
        public Entity Emitter;
        public ulong SessionGeneration;
    }

    /// <summary>Current world pose and firing availability of a live missile emitter.</summary>
    public struct MissileEmitterRuntimePose : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public byte CanFire;
    }

    /// <summary>Emitter-owned local launch-cell data. Cell keys remain logical and stable across ticks.</summary>
    [InternalBufferCapacity(4)]
    public struct MissileEmitterLaunchCell : IBufferElementData
    {
        public uint CellOrdinal;
        public float3 LocalPosition;
        public float3 LocalForward;
        public byte Valid;
        public byte Ready;
    }

    /// <summary>
    /// Marks the existing logical Member entity as an active runtime Missile. M2-A promotes
    /// this entity instead of creating a second identity that would need lifecycle syncing.
    /// </summary>
    public struct MissileRuntimeSpawnState : IComponentData
    {
        public Entity Emitter;
        public MissileLaunchCellKey Cell;
        public ulong SessionGeneration;
        public ulong SpawnOrdinal;
    }

    /// <summary>
    /// Runtime-only movement state. M2-B executes this acknowledged segment; M2-C is the
    /// only later stage allowed to replace it from a pending command request.
    /// </summary>
    public struct MissileRuntimeMovementState : IComponentData
    {
        public MissileFlightSegment ActiveSegment;
        public byte HasActiveSegment;
        /// <summary>Set only after a real movement integration succeeds for this ordinal.</summary>
        public ulong LastMovementOrdinal;
        /// <summary>Forward at the start of the latest integrated step; Contact must not reuse a segment-start tangent.</summary>
        public float3 PreviousForward;
        /// <summary>Contact is terminal for M2-E motion/contact processing until M2-F consumes the result.</summary>
        public byte ContactPending;
    }

    /// <summary>Last successfully committed flight policy; presentation never reads speculative planner state.</summary>
    public struct MissileRuntimeGuidanceState : IComponentData
    {
        public MissileGuidanceState Value;
    }

    /// <summary>Position at the start of the latest integrated step for later swept contact.</summary>
    public struct MissileMemberPreviousPosition : IComponentData
    {
        public float3 Value;
    }

    /// <summary>Explicit gameplay collision shape. A missing or invalid shape cannot produce Contact.</summary>
    public struct MissileContactShape : IComponentData
    {
        public float Radius;
    }

    /// <summary>Per-member diagnostic only; no near-miss gameplay is implied.</summary>
    public struct MissileContactProximity : IComponentData
    {
        public ulong Ordinal;
        public float MinimumSeparation;
    }

    /// <summary>Session-owned Contact output, valid until the next Contact gather clears it.</summary>
    [InternalBufferCapacity(16)]
    public struct MissileContactResult : IBufferElementData
    {
        public ulong SessionGeneration;
        public ulong Ordinal;
        public MemberLogicalKey Member;
        public uint PlayerId;
        public uint SlotGeneration;
        public float NormalizedTime;
        public float3 Point;
        public float3 Normal;
    }

    public enum MissileTerminalReason : byte
    {
        None,
        Contact,
        Expire,
        Cancel
    }

    /// <summary>
    /// Same-step terminal output. It is intentionally session-owned and short lived so a
    /// future Damage stage can consume an authoritative outcome without retaining Entities
    /// that have already been despawned.
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct MissileOutcomeResult : IBufferElementData
    {
        public ulong SessionGeneration;
        public ulong Ordinal;
        public MemberLogicalKey Member;
        public MissileTerminalReason Reason;
        public uint PlayerId;
        public uint SlotGeneration;
        public float3 Point;
        public float3 Normal;
    }

    /// <summary>Lifecycle counters aid verification only; they are not a performance measurement.</summary>
    public struct MissileLifecycleDiagnostics : IComponentData
    {
        public uint ContactTerminalCount;
        public uint ExpireTerminalCount;
        public uint CancelTerminalCount;
        public uint DespawnCount;
        public uint SalvoDespawnCount;
    }

    /// <summary>Diagnostics are counters, not a performance measurement.</summary>
    public struct MissileContactDiagnostics : IComponentData
    {
        public uint ActiveMissileSteps;
        public uint StraightChecks;
        public uint ArcChecks;
        public uint BroadphaseCandidates;
        public uint ArcSubdivisionCount;
        public uint IndeterminateArcCount;
        public uint HitCount;
        public float MinimumSeparation;
    }

    /// <summary>
    /// M2 writes this only after a real spawn has succeeded.  M1-F uses the same
    /// data as an explicit fixture contract, without creating a runtime Missile.
    /// </summary>
    public struct MissileMemberSpawnAcknowledgement : IComponentData
    {
        public MissileLaunchCellKey Cell;
        public ulong SessionGeneration;
        public ulong ActualSpawnOrdinal;
        public float3 Position;
        public float3 LaunchForward;
        public byte Acknowledged;
    }

    /// <summary>Tracks the finite launch-axis Straight sequence until Acquire is enabled.</summary>
    public struct MissileMemberLaunchHoldState : IComponentData
    {
        public ulong GuidanceEnableOrdinal;
        public byte Active;
    }

    /// <summary>Current logical command state. M2 will provide the live movement owner.</summary>
    public struct MissileMemberCommandState : IComponentData
    {
        public MissileFlightSegment CurrentSegment;
        public ulong LastRequestOrdinal;
        public uint NextCommandSequence;
        public byte Active;
        public byte HasCurrentSegment;
        public byte ReplanRequested;
    }

    public struct MissileMemberGuidanceState : IComponentData
    {
        public MissileGuidanceState Value;
    }

    /// <summary>Authoritative runtime motion; M1-E reads this snapshot when it creates future requests.</summary>
    public struct MissileMemberMotionState : IComponentData
    {
        public float3 Position;
        public float3 Forward;
        public float Speed;
    }

    public enum MissileCommandRequestKind : byte
    {
        None,
        Launch,
        FlightSegment,
        StraightFallback,
        Expire,
        Cancel
    }

    /// <summary>Single latest logical request for one Member. M2 will consume and acknowledge it.</summary>
    public struct MissileMemberCommandRequest : IComponentData
    {
        public MissileCommandRequestKind Kind;
        public ulong Ordinal;
        public uint CommandSequence;
        public MissileFlightSegment Segment;
        /// <summary>Applied with the segment only after M2-C accepts the request.</summary>
        public MissileGuidanceState NextGuidanceState;
        public byte Pending;
    }

    /// <summary>One scheduled due node per Member; the node itself lives in the session calendar buffer.</summary>
    public struct MissileMemberDueState : IComponentData
    {
        public ulong NextDueOrdinal;
        public int NodeIndex;
        public byte IsScheduled;
    }

    /// <summary>Session counters are diagnostics, not a profiler result.</summary>
    public struct MissileCommandSchedulingCounters : IComponentData
    {
        public uint LaunchDueCount;
        public uint CommandDueCount;
        public uint CommandGeneratedCount;
        public uint CommandCommitSuccessCount;
        public uint CommandRejectedCount;
        public uint DuplicateOrStaleCommandCount;
        public uint ExpireCount;
        public uint GuidanceSolverCallCount;
        public uint StraightFallbackCount;
        public uint ReplanRecoveryCount;
        public uint CancelledOrInvalidDueCount;
        public uint PeakDuePerStep;
    }

    public static class MissileSalvoModelValidation
    {
        public static bool TryResolveActiveMemberSalvo(EntityManager manager, Entity member, ulong sessionGeneration, out Entity salvo)
        {
            salvo = Entity.Null;
            if (!manager.Exists(member)
                || !manager.HasComponent<MissileMemberIdentity>(member)
                || !manager.HasComponent<MissileMemberSalvoReference>(member))
                return false;

            var identity = manager.GetComponentData<MissileMemberIdentity>(member);
            var reference = manager.GetComponentData<MissileMemberSalvoReference>(member);
            if (reference.Active != 1 || sessionGeneration == 0 || identity.SessionGeneration != sessionGeneration
                || reference.SessionGeneration != sessionGeneration || !identity.Key.IsValid || !manager.Exists(reference.Salvo)
                || !manager.HasComponent<SalvoIdentity>(reference.Salvo))
                return false;

            var salvoIdentity = manager.GetComponentData<SalvoIdentity>(reference.Salvo);
            if (salvoIdentity.SessionGeneration != sessionGeneration || !salvoIdentity.Key.IsValid
                || !identity.Key.Salvo.Equals(salvoIdentity.Key))
                return false;

            salvo = reference.Salvo;
            return true;
        }

        public static bool HasSharedProfile(EntityManager manager, Entity member, Entity salvo)
        {
            if (!manager.Exists(member) || !manager.Exists(salvo)
                || !manager.HasComponent<MissileMemberProfileReference>(member)
                || !manager.HasComponent<SalvoProfileReference>(salvo))
                return false;
            var memberProfile = manager.GetComponentData<MissileMemberProfileReference>(member).Profile;
            var salvoProfile = manager.GetComponentData<SalvoProfileReference>(salvo).Profile;
            return memberProfile != Entity.Null && memberProfile == salvoProfile && manager.Exists(memberProfile);
        }
    }
}
