using System;
using System.Collections.Generic;
using OverTheHorizon.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    // M0 establishes only the executor membership and Unity-owned ordering. Planning,
    // spawning, guidance, movement, contact, and lifecycle effects begin in later milestones.
    [DisableAutoCreation]
    [UpdateAfter(typeof(SimulationEntrySystem))]
    [UpdateBefore(typeof(MissileSpawnAndCommandCommitStage))]
    public partial class MissilePlanAndCommandStage : CombatStepSystem
    {
        protected override void OnStep()
        {
            // The previous Movement job is the authoritative producer of this state.
            EntityManager.CompleteDependencyBeforeRO<MissileMemberMotionState>();
            MissileSalvoTargetSampling.ProcessDue(EntityManager, SessionEntity, Step);
            if (MissileMemberCommandScheduling.HasCalendar(EntityManager, SessionEntity))
                MissileMemberCommandScheduling.ProcessDue(EntityManager, SessionEntity, Step);
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissilePlanAndCommandStage))]
    [UpdateBefore(typeof(MissileMovementStage))]
    public partial class MissileSpawnAndCommandCommitStage : CombatStepSystem
    {
        private EntityQuery pendingLaunches;
        private EntityQuery pendingRuntimeCommands;
        private ulong activeSessionGeneration;

        protected override void OnCreate()
        {
            pendingLaunches = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MissileMemberCommandRequest>(),
                    ComponentType.ReadOnly<MissileMemberLaunchState>(),
                    ComponentType.ReadOnly<MissileMemberLaunchCellAssignment>(),
                    ComponentType.ReadOnly<MissileMemberSalvoReference>(),
                    ComponentType.ReadOnly<MissileMemberIdentity>()
                },
                None = new[] { ComponentType.ReadOnly<MissileRuntimeSpawnState>() }
            });
            pendingRuntimeCommands = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MissileMemberCommandRequest>(),
                    ComponentType.ReadOnly<MissileRuntimeSpawnState>(),
                    ComponentType.ReadOnly<MissileRuntimeMovementState>(),
                    ComponentType.ReadOnly<MissileMemberSpawnAcknowledgement>(),
                    ComponentType.ReadOnly<MissileMemberIdentity>(),
                    ComponentType.ReadOnly<MissileMemberSalvoReference>(),
                    ComponentType.ReadOnly<MissileMemberLaunchState>(),
                    ComponentType.ReadWrite<MissileMemberCommandState>(),
                    ComponentType.ReadWrite<MissileMemberGuidanceState>(),
                    ComponentType.ReadWrite<MissileRuntimeGuidanceState>(),
                    ComponentType.ReadWrite<MissileMemberMotionState>()
                }
            });
        }

        protected override void OnStep()
        {
            activeSessionGeneration = Step.Generation;
            // This stage has no scheduled jobs. Direct structural changes make the entity
            // promotion and the acknowledgement one synchronous, reviewable operation.
            using var members = pendingLaunches.ToEntityArray(Unity.Collections.Allocator.Temp);
            var cellCounts = new Unity.Collections.NativeParallelHashMap<MissileLaunchCellKey, int>(members.Length,
                Unity.Collections.Allocator.Temp);
            try
            {
                for (int i = 0; i < members.Length; ++i)
                {
                    if (!IsCurrentLaunchRequest(members[i])) continue;
                    var cell = EntityManager.GetComponentData<MissileMemberLaunchCellAssignment>(members[i]).Cell;
                    if (!cell.IsValid) continue;
                    if (cellCounts.TryGetValue(cell, out int count)) cellCounts[cell] = checked(count + 1);
                    else cellCounts.TryAdd(cell, 1);
                }

                for (int i = 0; i < members.Length; ++i)
                {
                    Entity member = members[i];
                    if (!IsCurrentLaunchRequest(member)) continue;
                    var assignedCell = EntityManager.GetComponentData<MissileMemberLaunchCellAssignment>(member).Cell;
                    if (assignedCell.IsValid && cellCounts.TryGetValue(assignedCell, out int count) && count > 1)
                    {
                        CancelLaunch(member);
                        continue;
                    }

                    if (!TryResolveMuzzle(member, out var emitter, out var cell, out var position, out var forward))
                    {
                        CancelLaunch(member);
                        continue;
                    }

                    EntityManager.AddComponentData(member, new MissileRuntimeSpawnState
                    {
                        Emitter = emitter,
                        Cell = cell,
                        SessionGeneration = Step.Generation,
                        SpawnOrdinal = Step.Ordinal
                    });

                    try
                    {
                        bool acknowledged = MissileMemberCommandScheduling.AcknowledgeSpawn(EntityManager, SessionEntity, member, Step,
                            new MissileMemberSpawnAcknowledgement
                            {
                                Cell = cell,
                                SessionGeneration = Step.Generation,
                                ActualSpawnOrdinal = Step.Ordinal,
                                Position = position,
                                LaunchForward = forward
                            });
                        if (!acknowledged) throw new System.InvalidOperationException("Spawn acknowledgement was rejected.");
                        var command = EntityManager.GetComponentData<MissileMemberCommandState>(member);
                        if (command.Active != 1 || command.HasCurrentSegment != 1)
                            throw new System.InvalidOperationException("Spawn acknowledgement did not establish an initial segment.");
                        EntityManager.AddComponentData(member, new MissileMemberPreviousPosition { Value = position });
                        EntityManager.AddComponentData(member, new MissileRuntimeMovementState
                        {
                            ActiveSegment = command.CurrentSegment,
                            HasActiveSegment = 1,
                            PreviousForward = forward
                        });
                        EntityManager.AddComponentData(member, new MissileContactProximity { MinimumSeparation = float.PositiveInfinity });
                        EntityManager.AddComponentData(member, new MissileRuntimeGuidanceState
                        {
                            Value = MissileGuidanceState.LaunchBurst
                        });
                        EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                    }
                    catch
                    {
                        if (EntityManager.Exists(member) && EntityManager.HasComponent<MissileRuntimeSpawnState>(member))
                            EntityManager.RemoveComponent<MissileRuntimeSpawnState>(member);
                        if (EntityManager.Exists(member) && EntityManager.HasComponent<MissileRuntimeMovementState>(member))
                            EntityManager.RemoveComponent<MissileRuntimeMovementState>(member);
                        if (EntityManager.Exists(member) && EntityManager.HasComponent<MissileMemberPreviousPosition>(member))
                            EntityManager.RemoveComponent<MissileMemberPreviousPosition>(member);
                        if (EntityManager.Exists(member) && EntityManager.HasComponent<MissileContactProximity>(member))
                            EntityManager.RemoveComponent<MissileContactProximity>(member);
                        if (EntityManager.Exists(member) && EntityManager.HasComponent<MissileRuntimeGuidanceState>(member))
                            EntityManager.RemoveComponent<MissileRuntimeGuidanceState>(member);
                        throw;
                    }
                }
            }
            finally { cellCounts.Dispose(); }
            CommitRuntimeCommands();
        }

        private void CommitRuntimeCommands()
        {
            // Plan has already completed the preceding Movement producer before it reads Motion.
            // These component-scoped fences keep this direct commit safe even when a caller observes
            // this stage independently of the normal group loop.
            EntityManager.CompleteDependencyBeforeRO<MissileMemberMotionState>();
            EntityManager.CompleteDependencyBeforeRW<MissileRuntimeMovementState>();
            using var members = pendingRuntimeCommands.ToEntityArray(Unity.Collections.Allocator.Temp);
            bool hasCounters = EntityManager.HasComponent<MissileCommandSchedulingCounters>(SessionEntity);
            var counters = hasCounters ? EntityManager.GetComponentData<MissileCommandSchedulingCounters>(SessionEntity) : default;
            for (int i = 0; i < members.Length; ++i)
            {
                Entity member = members[i];
                var request = EntityManager.GetComponentData<MissileMemberCommandRequest>(member);
                if (request.Pending != 1) continue;

                var result = TryCommitRuntimeCommand(member, request);
                if (result == CommandCommitResult.Success)
                {
                    counters.CommandCommitSuccessCount++;
                    EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                }
                else if (result == CommandCommitResult.StaleOrDuplicate)
                {
                    counters.CommandRejectedCount++;
                    counters.DuplicateOrStaleCommandCount++;
                    EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                }
                else if (result == CommandCommitResult.Rejected)
                {
                    counters.CommandRejectedCount++;
                    EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                }
                else if (result == CommandCommitResult.RecoverableRejected)
                {
                    counters.CommandRejectedCount++;
                    if (MissileMemberCommandScheduling.TryScheduleReplanNextStep(EntityManager, SessionEntity, member, Step))
                        counters.ReplanRecoveryCount++;
                    EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                }
                else if (result == CommandCommitResult.Expired)
                {
                    counters.CommandCommitSuccessCount++;
                    counters.ExpireCount++;
                    EntityManager.SetComponentData(member, default(MissileMemberCommandRequest));
                }
            }
            if (hasCounters) EntityManager.SetComponentData(SessionEntity, counters);
        }

        private CommandCommitResult TryCommitRuntimeCommand(Entity member, MissileMemberCommandRequest request)
        {
            var runtime = EntityManager.GetComponentData<MissileRuntimeSpawnState>(member);
            var acknowledgement = EntityManager.GetComponentData<MissileMemberSpawnAcknowledgement>(member);
            var identity = EntityManager.GetComponentData<MissileMemberIdentity>(member);
            var reference = EntityManager.GetComponentData<MissileMemberSalvoReference>(member);
            var launch = EntityManager.GetComponentData<MissileMemberLaunchState>(member);
            var command = EntityManager.GetComponentData<MissileMemberCommandState>(member);
            var guidance = EntityManager.GetComponentData<MissileMemberGuidanceState>(member);
            var runtimeMovement = EntityManager.GetComponentData<MissileRuntimeMovementState>(member);
            var runtimeGuidance = EntityManager.GetComponentData<MissileRuntimeGuidanceState>(member);
            var motion = EntityManager.GetComponentData<MissileMemberMotionState>(member);

            if (request.Ordinal < Step.Ordinal) return CommandCommitResult.StaleOrDuplicate;
            if (request.Ordinal != Step.Ordinal || runtime.SessionGeneration != Step.Generation
                || acknowledgement.Acknowledged != 1 || acknowledgement.SessionGeneration != Step.Generation
                || identity.SessionGeneration != Step.Generation || reference.SessionGeneration != Step.Generation
                || reference.Active != 1 || launch.Cancelled == 1
                || !MissileSalvoModelValidation.TryResolveActiveMemberSalvo(EntityManager, member, Step.Generation, out _))
                return CommandCommitResult.Rejected;

            if (request.Kind == MissileCommandRequestKind.Expire)
            {
                if (command.Active != 1) return CommandCommitResult.StaleOrDuplicate;
                command.Active = 0;
                runtimeMovement.HasActiveSegment = 0;
                EntityManager.SetComponentData(member, command);
                EntityManager.SetComponentData(member, runtimeMovement);
                return CommandCommitResult.Expired;
            }

            if (request.Kind == MissileCommandRequestKind.Cancel)
                return CommandCommitResult.Rejected; // Pending launch cancellation remains M1-E/M2-A ownership.

            if (request.Kind != MissileCommandRequestKind.FlightSegment && request.Kind != MissileCommandRequestKind.StraightFallback)
                return CommandCommitResult.Rejected;
            if (command.Active != 1 || runtimeMovement.HasActiveSegment != 1)
                return CommandCommitResult.Rejected;
            if (request.CommandSequence <= runtimeMovement.ActiveSegment.CommandSequence)
                return CommandCommitResult.StaleOrDuplicate;
            if (!IsApplicableSegment(request.Segment, request, motion))
            {
                guidance.Value = runtimeGuidance.Value;
                EntityManager.SetComponentData(member, guidance);
                return CommandCommitResult.RecoverableRejected;
            }

            if (EntityManager.HasComponent<MissileMemberLaunchHoldState>(member))
            {
                var hold = EntityManager.GetComponentData<MissileMemberLaunchHoldState>(member);
                if (hold.Active == 1 && !IsLaunchHoldStraight(request.Segment, acknowledgement.LaunchForward))
                {
                    guidance.Value = runtimeGuidance.Value;
                    EntityManager.SetComponentData(member, guidance);
                    return CommandCommitResult.RecoverableRejected;
                }
            }

            runtimeMovement.ActiveSegment = request.Segment;
            runtimeMovement.HasActiveSegment = 1;
            guidance.Value = request.NextGuidanceState;
            runtimeGuidance.Value = request.NextGuidanceState;
            EntityManager.SetComponentData(member, runtimeMovement);
            EntityManager.SetComponentData(member, guidance);
            EntityManager.SetComponentData(member, runtimeGuidance);
            return CommandCommitResult.Success;
        }

        private bool IsApplicableSegment(MissileFlightSegment segment, MissileMemberCommandRequest request,
            MissileMemberMotionState motion)
        {
            if (segment.Valid != 1 || segment.CommandSequence != request.CommandSequence
                || segment.StartOrdinal != request.Ordinal || segment.EndOrdinal <= segment.StartOrdinal
                || !math.isfinite(segment.Speed) || segment.Speed < 0f || !ApproximatelyEqual(segment.Speed, motion.Speed)
                || !ApproximatelyEqual(segment.StepDeltaTime, Step.DeltaTime)
                || !math.all(math.isfinite(segment.StartPosition)) || !math.all(math.isfinite(segment.StartForward))
                || math.lengthsq(segment.StartForward) <= MissileGuidanceSolver.DirectionEpsilonSq
                || math.distance(segment.StartPosition, motion.Position) > 0.0001f
                || math.dot(math.normalizesafe(segment.StartForward), math.normalizesafe(motion.Forward)) < 0.9999f)
                return false;
            return MissileGuidanceSolver.TryIntegrateSegment(segment, motion.Position, motion.Forward, out _, out _);
        }

        private static bool IsLaunchHoldStraight(MissileFlightSegment segment, float3 launchForward)
            => segment.Primitive == MissileFlightPrimitive.Straight
                && math.lengthsq(launchForward) > MissileGuidanceSolver.DirectionEpsilonSq
                && math.dot(math.normalizesafe(segment.StartForward), math.normalizesafe(launchForward)) > 0.9999f;

        private static bool ApproximatelyEqual(float left, float right)
        {
            if (!math.isfinite(left) || !math.isfinite(right)) return false;
            return math.abs(left - right) <= 0.00001f * math.max(1f, math.max(math.abs(left), math.abs(right)));
        }

        private enum CommandCommitResult : byte
        {
            None,
            Success,
            Rejected,
            RecoverableRejected,
            StaleOrDuplicate,
            Expired
        }

        private bool IsCurrentLaunchRequest(Entity member)
        {
            var request = EntityManager.GetComponentData<MissileMemberCommandRequest>(member);
            var launch = EntityManager.GetComponentData<MissileMemberLaunchState>(member);
            return request.Pending == 1 && request.Kind == MissileCommandRequestKind.Launch && request.Ordinal == Step.Ordinal
                && launch.LaunchRequested == 1 && launch.Cancelled == 0
                && MissileSalvoModelValidation.TryResolveActiveMemberSalvo(EntityManager, member, Step.Generation, out var salvo)
                && EntityManager.HasComponent<MissileSalvoEmitterReference>(salvo);
        }

        private bool TryResolveMuzzle(Entity member, out Entity emitter, out MissileLaunchCellKey key,
            out float3 position, out float3 forward)
        {
            emitter = Entity.Null;
            key = default;
            position = forward = default;
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(EntityManager, member, Step.Generation, out var salvo)
                || !EntityManager.HasComponent<MissileSalvoEmitterReference>(salvo)) return false;

            key = EntityManager.GetComponentData<MissileMemberLaunchCellAssignment>(member).Cell;
            var reference = EntityManager.GetComponentData<MissileSalvoEmitterReference>(salvo);
            if (!key.IsValid || reference.SessionGeneration != Step.Generation || reference.Emitter == Entity.Null
                || !EntityManager.Exists(reference.Emitter) || !EntityManager.HasComponent<MissileEmitterIdentity>(reference.Emitter)
                || !EntityManager.HasComponent<MissileEmitterRuntimePose>(reference.Emitter)
                || !EntityManager.HasBuffer<MissileEmitterLaunchCell>(reference.Emitter)) return false;

            var identity = EntityManager.GetComponentData<MissileEmitterIdentity>(reference.Emitter);
            var pose = EntityManager.GetComponentData<MissileEmitterRuntimePose>(reference.Emitter);
            if (identity.StableId != key.EmitterStableId || pose.CanFire != 1 || !math.all(math.isfinite(pose.Position))
                || !math.all(math.isfinite(pose.Rotation.value)) || math.lengthsq(pose.Rotation.value) <= 0.000001f) return false;

            var cells = EntityManager.GetBuffer<MissileEmitterLaunchCell>(reference.Emitter);
            for (int i = 0; i < cells.Length; ++i)
            {
                var cell = cells[i];
                if (cell.CellOrdinal != key.CellOrdinal || cell.Valid != 1 || cell.Ready != 1
                    || !math.all(math.isfinite(cell.LocalPosition)) || !math.all(math.isfinite(cell.LocalForward))
                    || math.lengthsq(cell.LocalForward) <= 0.000001f) continue;
                quaternion rotation = math.normalize(pose.Rotation);
                position = pose.Position + math.mul(rotation, cell.LocalPosition);
                forward = math.normalizesafe(math.mul(rotation, cell.LocalForward));
                if (!math.all(math.isfinite(position)) || math.lengthsq(forward) <= 0.000001f) return false;
                emitter = reference.Emitter;
                return true;
            }
            return false;
        }

        private void CancelLaunch(Entity member)
        {
            var launch = EntityManager.GetComponentData<MissileMemberLaunchState>(member);
            launch.Pending = 0;
            launch.LaunchRequested = 0;
            launch.Cancelled = 1;
            EntityManager.SetComponentData(member, launch);
            EntityManager.SetComponentData(member, new MissileMemberCommandRequest
            {
                Kind = MissileCommandRequestKind.Cancel,
                Ordinal = Step.Ordinal,
                Pending = 1
            });
            if (EntityManager.HasComponent<MissileCommandSchedulingCounters>(SessionEntity))
            {
                var counters = EntityManager.GetComponentData<MissileCommandSchedulingCounters>(SessionEntity);
                counters.CancelledOrInvalidDueCount++;
                EntityManager.SetComponentData(SessionEntity, counters);
            }
        }

        protected override void OnDestroy()
        {
            if (World != null && World.IsCreated && activeSessionGeneration != 0)
            {
                using var activeMembers = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MissileRuntimeSpawnState>())
                    .ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < activeMembers.Length; ++i)
                {
                    Entity member = activeMembers[i];
                    if (EntityManager.GetComponentData<MissileRuntimeSpawnState>(member).SessionGeneration == activeSessionGeneration)
                        EntityManager.DestroyEntity(member);
                }
            }
            base.OnDestroy();
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissileSpawnAndCommandCommitStage))]
    [UpdateBefore(typeof(MissileContactGatherStage))]
    public partial class MissileMovementStage : CombatStepSystem
    {
        protected override void OnStep()
        {
            Dependency = new MoveMissilesJob
            {
                SessionGeneration = Step.Generation,
                Ordinal = Step.Ordinal,
                DeltaTime = Step.DeltaTime
            }.ScheduleParallel(Dependency);
        }

        [BurstCompile]
        private partial struct MoveMissilesJob : IJobEntity
        {
            public ulong SessionGeneration;
            public ulong Ordinal;
            public float DeltaTime;
            private void Execute(Entity entity, in MissileRuntimeSpawnState runtime, in MissileMemberSpawnAcknowledgement acknowledgement,
                in MissileMemberIdentity identity, in MissileMemberSalvoReference salvoReference,
                in MissileMemberLaunchState launch, in MissileMemberCommandState command,
                ref MissileRuntimeMovementState runtimeMovement, ref MissileMemberMotionState motion,
                ref MissileMemberPreviousPosition previous)
            {
                if (runtime.SessionGeneration != SessionGeneration || acknowledgement.Acknowledged != 1
                    || acknowledgement.SessionGeneration != SessionGeneration || identity.SessionGeneration != SessionGeneration
                    || salvoReference.SessionGeneration != SessionGeneration || salvoReference.Active != 1
                    || launch.Cancelled == 1 || command.Active != 1 || runtimeMovement.HasActiveSegment != 1
                    || runtimeMovement.ContactPending != 0)
                    return;

                var segment = runtimeMovement.ActiveSegment;
                if (segment.Valid != 1 || Ordinal < segment.StartOrdinal) return;

                float3 nextPosition;
                float3 nextForward;
                if (Ordinal >= segment.EndOrdinal)
                {
                    // No M2-C replacement exists yet. Keep the final tangent without asking Guidance for a new plan.
                    if (!MissileGuidanceSolver.TryIntegrateStraight(motion.Position, motion.Forward, motion.Speed, DeltaTime,
                        out nextPosition, out nextForward)) return;
                }
                else
                {
                    // Arc coefficients are valid only for the speed and step duration that created them.
                    if (!ApproximatelyEqual(motion.Speed, segment.Speed) || !ApproximatelyEqual(DeltaTime, segment.StepDeltaTime))
                        return;
                    if (!MissileGuidanceSolver.TryIntegrateSegment(segment, motion.Position, motion.Forward,
                        out nextPosition, out nextForward)) return;
                }

                previous.Value = motion.Position;
                runtimeMovement.PreviousForward = motion.Forward;
                motion.Position = nextPosition;
                motion.Forward = nextForward;
                runtimeMovement.LastMovementOrdinal = Ordinal;
            }

            private static bool ApproximatelyEqual(float left, float right)
            {
                if (!math.isfinite(left) || !math.isfinite(right)) return false;
                return math.abs(left - right) <= 0.00001f * math.max(1f, math.max(math.abs(left), math.abs(right)));
            }
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissileMovementStage))]
    [UpdateBefore(typeof(MissileOutcomeAndLifecycleStage))]
    public partial class MissileContactGatherStage : CombatStepSystem
    {
        private EntityQuery activeMissiles;

        protected override void OnCreate()
        {
            activeMissiles = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MissileRuntimeSpawnState>(), ComponentType.ReadWrite<MissileRuntimeMovementState>(),
                    ComponentType.ReadOnly<MissileMemberPreviousPosition>(),
                    ComponentType.ReadOnly<MissileMemberMotionState>(), ComponentType.ReadOnly<MissileMemberIdentity>(),
                    ComponentType.ReadWrite<MissileContactProximity>(),
                    ComponentType.ReadOnly<MissileContactShape>()
                }
            });
        }

        protected override void OnStep()
        {
            if (!EntityManager.HasBuffer<PlayerCombatBoundsStepElement>(SessionEntity)
                || !EntityManager.HasBuffer<MissileContactResult>(SessionEntity)
                || !EntityManager.HasComponent<MissileContactDiagnostics>(SessionEntity)) return;

            EntityManager.CompleteDependencyBeforeRO<MissileMemberMotionState>();
            var bounds = EntityManager.GetBuffer<PlayerCombatBoundsStepElement>(SessionEntity);
            var results = EntityManager.GetBuffer<MissileContactResult>(SessionEntity);
            results.Clear();
            var diagnostics = new MissileContactDiagnostics { MinimumSeparation = float.PositiveInfinity };
            bool indeterminate = false;
            using var missiles = activeMissiles.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < missiles.Length; ++i) Gather(missiles[i], bounds, results, ref diagnostics, ref indeterminate);
            EntityManager.SetComponentData(SessionEntity, diagnostics);
            if (indeterminate) throw new InvalidOperationException("Missile arc contact was indeterminate; authoritative completion is blocked.");
        }

        private void Gather(Entity member, DynamicBuffer<PlayerCombatBoundsStepElement> bounds,
            DynamicBuffer<MissileContactResult> results, ref MissileContactDiagnostics diagnostics, ref bool indeterminate)
        {
            var runtime = EntityManager.GetComponentData<MissileRuntimeSpawnState>(member);
            var movement = EntityManager.GetComponentData<MissileRuntimeMovementState>(member);
            var identity = EntityManager.GetComponentData<MissileMemberIdentity>(member);
            if (movement.ContactPending != 0 || runtime.SessionGeneration != Step.Generation || identity.SessionGeneration != Step.Generation
                || movement.LastMovementOrdinal != Step.Ordinal) return;

            var previous = EntityManager.GetComponentData<MissileMemberPreviousPosition>(member);
            var motion = EntityManager.GetComponentData<MissileMemberMotionState>(member);
            var shape = EntityManager.GetComponentData<MissileContactShape>(member);
            if (!math.isfinite(shape.Radius) || shape.Radius <= 0f || !math.all(math.isfinite(previous.Value))
                || !math.all(math.isfinite(movement.PreviousForward)) || !math.all(math.isfinite(motion.Position))) return;

            ++diagnostics.ActiveMissileSteps;
            bool useArc = movement.ActiveSegment.Valid == 1 && movement.ActiveSegment.Primitive == MissileFlightPrimitive.ConstantCurvatureArc
                && Step.Ordinal < movement.ActiveSegment.EndOrdinal;
            bool found = false;
            MissileContactHit best = default;
            uint bestPlayerId = 0;
            uint bestSlotGeneration = 0;
            float memberMinimumSeparation = float.PositiveInfinity;
            for (int i = 0; i < bounds.Length; ++i)
            {
                var player = bounds[i];
                if (!IsValidUniqueBound(bounds, i, player)) continue;
                MissileContactSolveResult solve;
                MissileContactHit candidate;
                if (useArc)
                {
                    ++diagnostics.ArcChecks;
                    solve = MissileContactSolver.TryFindArc(movement.ActiveSegment, previous.Value, movement.PreviousForward, shape.Radius,
                        player.StartCenter, player.EndCenter, player.Radius, out candidate, out int subdivisions, out int broadphaseCandidates);
                    diagnostics.ArcSubdivisionCount += (uint)subdivisions;
                    diagnostics.BroadphaseCandidates += (uint)broadphaseCandidates;
                    if (solve == MissileContactSolveResult.Indeterminate) ++diagnostics.IndeterminateArcCount;
                }
                else
                {
                    ++diagnostics.StraightChecks;
                    solve = MissileContactSolver.TryFindStraight(previous.Value, motion.Position, shape.Radius,
                        player.StartCenter, player.EndCenter, player.Radius, out candidate);
                }
                if (solve == MissileContactSolveResult.Indeterminate)
                {
                    indeterminate = true;
                    continue;
                }
                memberMinimumSeparation = math.min(memberMinimumSeparation, candidate.MinimumSeparation);
                if (solve != MissileContactSolveResult.Contact || !IsEarlier(candidate.NormalizedTime, player.PlayerId, player.SlotGeneration,
                    found, best.NormalizedTime, bestPlayerId, bestSlotGeneration)) continue;
                best = candidate;
                bestPlayerId = player.PlayerId;
                bestSlotGeneration = player.SlotGeneration;
                found = true;
            }

            EntityManager.SetComponentData(member, new MissileContactProximity
            {
                Ordinal = Step.Ordinal,
                MinimumSeparation = memberMinimumSeparation
            });
            diagnostics.MinimumSeparation = math.min(diagnostics.MinimumSeparation, memberMinimumSeparation);
            if (!found) return;

            movement.ContactPending = 1;
            EntityManager.SetComponentData(member, movement);
            results.Add(new MissileContactResult
            {
                SessionGeneration = Step.Generation,
                Ordinal = Step.Ordinal,
                Member = identity.Key,
                PlayerId = bestPlayerId,
                SlotGeneration = bestSlotGeneration,
                NormalizedTime = best.NormalizedTime,
                Point = best.Point,
                Normal = best.Normal
            });
            ++diagnostics.HitCount;
        }

        private bool IsValidUniqueBound(DynamicBuffer<PlayerCombatBoundsStepElement> bounds, int index,
            PlayerCombatBoundsStepElement value)
        {
            if (value.Valid != 1 || value.Generation != Step.Generation || value.Ordinal != Step.Ordinal || value.PlayerId == 0
                || value.SlotGeneration == 0 || !math.isfinite(value.Radius) || value.Radius <= 0f
                || !math.all(math.isfinite(value.StartCenter)) || !math.all(math.isfinite(value.EndCenter))) return false;
            for (int i = 0; i < bounds.Length; ++i)
                if (i != index && bounds[i].Valid == 1 && bounds[i].PlayerId == value.PlayerId
                    && bounds[i].SlotGeneration == value.SlotGeneration) return false;
            return true;
        }

        private static bool IsEarlier(float time, uint playerId, uint slotGeneration, bool hasBest,
            float bestTime, uint bestPlayerId, uint bestSlotGeneration)
        {
            if (!hasBest) return true;
            const float tieEpsilon = 0.00001f;
            if (time < bestTime - tieEpsilon) return true;
            if (time > bestTime + tieEpsilon) return false;
            return playerId < bestPlayerId || (playerId == bestPlayerId && slotGeneration < bestSlotGeneration);
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(MissileContactGatherStage))]
    [UpdateBefore(typeof(SimulationCompleteSystem))]
    public partial class MissileOutcomeAndLifecycleStage : CombatStepSystem
    {
        private EntityQuery runtimeMembers;
        private EntityQuery cancelledPendingMembers;
        private EntityQuery allMembers;

        protected override void OnCreate()
        {
            runtimeMembers = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MissileRuntimeSpawnState>(), ComponentType.ReadOnly<MissileRuntimeMovementState>(),
                    ComponentType.ReadOnly<MissileMemberIdentity>(), ComponentType.ReadOnly<MissileMemberSalvoReference>(),
                    ComponentType.ReadOnly<MissileMemberCommandState>()
                }
            });
            cancelledPendingMembers = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MissileMemberIdentity>(), ComponentType.ReadOnly<MissileMemberSalvoReference>(),
                    ComponentType.ReadOnly<MissileMemberLaunchState>()
                },
                None = new[] { ComponentType.ReadOnly<MissileRuntimeSpawnState>() }
            });
            allMembers = GetEntityQuery(ComponentType.ReadOnly<MissileMemberSalvoReference>());
        }

        protected override void OnStep()
        {
            if (!EntityManager.HasBuffer<MissileContactResult>(SessionEntity)
                || !EntityManager.HasBuffer<MissileOutcomeResult>(SessionEntity)
                || !EntityManager.HasComponent<MissileLifecycleDiagnostics>(SessionEntity)) return;

            EntityManager.CompleteDependencyBeforeRO<MissileMemberMotionState>();
            var contacts = EntityManager.GetBuffer<MissileContactResult>(SessionEntity);
            var outcomes = EntityManager.GetBuffer<MissileOutcomeResult>(SessionEntity);
            outcomes.Clear();
            var diagnostics = default(MissileLifecycleDiagnostics);
            using var runtime = runtimeMembers.ToEntityArray(Allocator.Temp);
            using var cancelled = cancelledPendingMembers.ToEntityArray(Allocator.Temp);
            using var members = allMembers.ToEntityArray(Allocator.Temp);
            var terminal = new HashSet<Entity>();
            var affectedSalvos = new HashSet<Entity>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            try
            {
                for (int i = 0; i < contacts.Length; ++i)
                {
                    var contact = contacts[i];
                    if (contact.SessionGeneration != Step.Generation || contact.Ordinal != Step.Ordinal)
                        throw new InvalidOperationException("Missile Contact result does not belong to the executing step.");
                    Entity member = FindRuntimeMember(runtime, contact.Member);
                    if (member == Entity.Null || EntityManager.GetComponentData<MissileRuntimeMovementState>(member).ContactPending != 1)
                        throw new InvalidOperationException("Missile Contact result has no pending runtime Member.");
                    ScheduleTerminal(member, MissileTerminalReason.Contact, contact.PlayerId, contact.SlotGeneration,
                        contact.Point, contact.Normal, terminal, affectedSalvos, outcomes, ref diagnostics);
                }

                for (int i = 0; i < runtime.Length; ++i)
                {
                    var member = runtime[i];
                    if (terminal.Contains(member)) continue;
                    var movement = EntityManager.GetComponentData<MissileRuntimeMovementState>(member);
                    var command = EntityManager.GetComponentData<MissileMemberCommandState>(member);
                    if (movement.ContactPending == 0 && command.Active == 0)
                        ScheduleTerminal(member, MissileTerminalReason.Expire, 0, 0, default, default,
                            terminal, affectedSalvos, outcomes, ref diagnostics);
                }

                for (int i = 0; i < cancelled.Length; ++i)
                {
                    var member = cancelled[i];
                    if (terminal.Contains(member)) continue;
                    if (EntityManager.GetComponentData<MissileMemberLaunchState>(member).Cancelled == 1)
                        ScheduleTerminal(member, MissileTerminalReason.Cancel, 0, 0, default, default,
                            terminal, affectedSalvos, outcomes, ref diagnostics);
                }

                foreach (var member in terminal) ecb.DestroyEntity(member);
                foreach (var salvo in affectedSalvos)
                    if (salvo != Entity.Null && EntityManager.Exists(salvo) && !HasRemainingMember(salvo, members, terminal))
                    {
                        ecb.DestroyEntity(salvo);
                        ++diagnostics.SalvoDespawnCount;
                    }
                ecb.Playback(EntityManager);
            }
            finally { ecb.Dispose(); }
            EntityManager.SetComponentData(SessionEntity, diagnostics);
        }

        private Entity FindRuntimeMember(NativeArray<Entity> runtime, MemberLogicalKey key)
        {
            for (int i = 0; i < runtime.Length; ++i)
                if (EntityManager.GetComponentData<MissileMemberIdentity>(runtime[i]).Key.Equals(key)) return runtime[i];
            return Entity.Null;
        }

        private void ScheduleTerminal(Entity member, MissileTerminalReason reason, uint playerId, uint slotGeneration,
            float3 point, float3 normal, HashSet<Entity> terminal, HashSet<Entity> affectedSalvos,
            DynamicBuffer<MissileOutcomeResult> outcomes, ref MissileLifecycleDiagnostics diagnostics)
        {
            if (!terminal.Add(member)) return;
            var identity = EntityManager.GetComponentData<MissileMemberIdentity>(member);
            var reference = EntityManager.GetComponentData<MissileMemberSalvoReference>(member);
            if (identity.SessionGeneration != Step.Generation || reference.SessionGeneration != Step.Generation)
                throw new InvalidOperationException("Missile terminal Member does not belong to the executing session.");
            affectedSalvos.Add(reference.Salvo);
            outcomes.Add(new MissileOutcomeResult
            {
                SessionGeneration = Step.Generation,
                Ordinal = Step.Ordinal,
                Member = identity.Key,
                Reason = reason,
                PlayerId = playerId,
                SlotGeneration = slotGeneration,
                Point = point,
                Normal = normal
            });
            ++diagnostics.DespawnCount;
            switch (reason)
            {
                case MissileTerminalReason.Contact: ++diagnostics.ContactTerminalCount; break;
                case MissileTerminalReason.Expire: ++diagnostics.ExpireTerminalCount; break;
                case MissileTerminalReason.Cancel: ++diagnostics.CancelTerminalCount; break;
            }
        }

        private bool HasRemainingMember(Entity salvo, NativeArray<Entity> members, HashSet<Entity> terminal)
        {
            for (int i = 0; i < members.Length; ++i)
            {
                var member = members[i];
                if (!terminal.Contains(member) && EntityManager.Exists(member)
                    && EntityManager.GetComponentData<MissileMemberSalvoReference>(member).Salvo == salvo)
                    return true;
            }
            return false;
        }
    }
}
