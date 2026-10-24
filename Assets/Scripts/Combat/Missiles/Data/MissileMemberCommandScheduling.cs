using System;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Combat.Missiles
{
    /// <summary>Session-local due calendar for M1-E Member launch and command requests.</summary>
    public struct MissileMemberCommandCalendar : IComponentData
    {
        public ulong SessionGeneration;
        public int TickRate;
        public int FreeNodeHead;
    }

    [InternalBufferCapacity(0)]
    public struct MissileMemberCommandBucket : IBufferElementData
    {
        public int HeadNode;
    }

    [InternalBufferCapacity(0)]
    public struct MissileMemberDueNode : IBufferElementData
    {
        public Entity Member;
        public ulong DueOrdinal;
        public int NextNode;
    }

    /// <summary>
    /// M1-E V1 calendar: one pooled node per scheduled Member, bucketed by ordinal modulo
    /// TickRate. It processes only due nodes; it deliberately is not a general scheduler.
    /// </summary>
    public static class MissileMemberCommandScheduling
    {
        public const float MaximumLaunchDelaySeconds = 10f;
        // Float authoring values such as 0.2f can sit infinitesimally above an exact
        // tick boundary after multiplication. Keep that representation noise from
        // turning an intended boundary launch into a one-tick delay.
        private const double TickRoundingTolerance = 0.00001d;

        public static bool HasCalendar(EntityManager manager, Entity session)
            => manager.Exists(session) && manager.HasComponent<MissileMemberCommandCalendar>(session)
                && manager.HasBuffer<MissileMemberCommandBucket>(session)
                && manager.HasBuffer<MissileMemberDueNode>(session);

        public static void EnsureCalendar(EntityManager manager, Entity session)
        {
            if (!manager.Exists(session) || !manager.HasComponent<SimulationStepHeader>(session))
                throw new ArgumentException("A CombatStepExecutor session is required.", nameof(session));

            var header = manager.GetComponentData<SimulationStepHeader>(session);
            if (header.Generation == 0 || header.TickRate <= 0)
                throw new InvalidOperationException("The session timing header is not initialized.");

            if (!manager.HasComponent<MissileMemberCommandCalendar>(session))
            {
                manager.AddComponentData(session, new MissileMemberCommandCalendar
                {
                    SessionGeneration = header.Generation,
                    TickRate = header.TickRate,
                    FreeNodeHead = -1
                });
                var buckets = manager.AddBuffer<MissileMemberCommandBucket>(session);
                buckets.ResizeUninitialized(header.TickRate);
                for (int i = 0; i < buckets.Length; ++i) buckets[i] = new MissileMemberCommandBucket { HeadNode = -1 };
                manager.AddBuffer<MissileMemberDueNode>(session);
                manager.AddComponentData(session, default(MissileCommandSchedulingCounters));
                return;
            }

            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            if (calendar.SessionGeneration != header.Generation || calendar.TickRate != header.TickRate
                || !manager.HasBuffer<MissileMemberCommandBucket>(session)
                || manager.GetBuffer<MissileMemberCommandBucket>(session).Length != header.TickRate
                || !manager.HasBuffer<MissileMemberDueNode>(session)
                || !manager.HasComponent<MissileCommandSchedulingCounters>(session))
                throw new InvalidOperationException("Member command calendar does not match the current session.");
        }

        /// <summary>Converts a non-negative launch delay to the first tick boundary that does not precede it.</summary>
        public static ulong CalculateLaunchDueOrdinal(ulong createdOrdinal, float delaySeconds, int tickRate)
        {
            if (createdOrdinal == 0 || !math.isfinite(delaySeconds) || delaySeconds < 0f
                || delaySeconds > MaximumLaunchDelaySeconds || tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));

            double ticks = Math.Ceiling(delaySeconds * tickRate - TickRoundingTolerance);
            if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks < 0d || ticks > ulong.MaxValue - createdOrdinal)
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            return checked(createdOrdinal + (ulong)ticks);
        }

        /// <summary>Maps a successful real-spawn acknowledgement to the first permitted Acquire tick.</summary>
        public static ulong CalculateGuidanceEnableOrdinal(ulong actualSpawnOrdinal, float holdDurationSeconds,
            float commandPhaseSeconds, int tickRate)
        {
            if (actualSpawnOrdinal == 0 || !math.isfinite(holdDurationSeconds) || !math.isfinite(commandPhaseSeconds)
                || holdDurationSeconds <= 0f || holdDurationSeconds > MissileSalvoApproachPlanning.MaximumLaunchHoldDuration
                || commandPhaseSeconds < 0f || commandPhaseSeconds > MissileSalvoApproachPlanning.MaximumCommandPhase)
                throw new ArgumentOutOfRangeException(nameof(holdDurationSeconds));

            double total = holdDurationSeconds + commandPhaseSeconds;
            double ticks = Math.Ceiling(total * tickRate - TickRoundingTolerance);
            if (tickRate <= 0 || double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks < 1d
                || ticks > ulong.MaxValue - actualSpawnOrdinal)
                throw new ArgumentOutOfRangeException(nameof(holdDurationSeconds));
            return checked(actualSpawnOrdinal + (ulong)ticks);
        }

        /// <summary>Registers one pending Member. Members are independently bucketed, so delay order need not follow MemberOrdinal.</summary>
        public static void RegisterMember(EntityManager manager, Entity session, Entity member)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, calendar.SessionGeneration, out var salvo)
                || !manager.HasComponent<SalvoSchedule>(salvo)
                || !manager.HasComponent<MissileMemberLaunchState>(member)
                || !manager.HasComponent<MissileMemberDueState>(member)
                || !manager.HasComponent<MissileMemberCommandState>(member)
                || !manager.HasComponent<MissileMemberCommandRequest>(member))
                throw new ArgumentException("A complete Member and Salvo schedule are required.", nameof(member));

            var schedule = manager.GetComponentData<SalvoSchedule>(salvo);
            var launch = manager.GetComponentData<MissileMemberLaunchState>(member);
            var due = manager.GetComponentData<MissileMemberDueState>(member);
            if (schedule.CreatedOrdinal == 0 || launch.Pending != 1 || launch.Cancelled != 0 || launch.LaunchRequested != 0 || due.IsScheduled != 0)
                throw new InvalidOperationException("Only an unscheduled pending Member may be registered.");

            launch.DueOrdinal = CalculateLaunchDueOrdinal(schedule.CreatedOrdinal, launch.LaunchDelaySeconds, calendar.TickRate);
            manager.SetComponentData(member, launch);
            Link(manager, session, member, ref due, launch.DueOrdinal);
        }

        /// <summary>M2 will call this after it has spawned and initialized the real Member motion state.</summary>
        public static void ActivateLaunchedMember(EntityManager manager, Entity session, Entity member, ulong dueOrdinal)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            if (dueOrdinal == 0 || !MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, calendar.SessionGeneration, out _)
                || !manager.HasComponent<MissileMemberLaunchState>(member) || !manager.HasComponent<MissileMemberCommandState>(member)
                || !manager.HasComponent<MissileMemberDueState>(member))
                throw new ArgumentException("A launched Member and a due ordinal are required.", nameof(member));

            var launch = manager.GetComponentData<MissileMemberLaunchState>(member);
            var command = manager.GetComponentData<MissileMemberCommandState>(member);
            if (launch.LaunchRequested != 1 || launch.Cancelled != 0 || command.Active == 1)
                throw new InvalidOperationException("Only a non-cancelled launch request may become active.");

            command.Active = 1;
            command.HasCurrentSegment = 0;
            manager.SetComponentData(member, command);
            var due = manager.GetComponentData<MissileMemberDueState>(member);
            Link(manager, session, member, ref due, dueOrdinal);
        }

        /// <summary>
        /// Accepts M2's successful Spawn acknowledgement and emits the first finite
        /// launch-axis Straight. This does not spawn or move an Entity in M1-F.
        /// </summary>
        public static bool AcknowledgeSpawn(EntityManager manager, Entity session, Entity member,
            SimulationStepContext step, MissileMemberSpawnAcknowledgement acknowledgement)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            var header = manager.GetComponentData<SimulationStepHeader>(session);
            if (step.Generation != calendar.SessionGeneration || step.Generation != header.Generation
                || step.Ordinal == 0 || step.Ordinal != header.Ordinal || step.TickRate != calendar.TickRate
                || step.TickRate != header.TickRate || step.DeltaTime != header.DeltaTime)
                throw new ArgumentException("Spawn acknowledgement requires the executing Combat Step context.", nameof(step));
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, calendar.SessionGeneration, out var salvo)
                || !manager.HasComponent<MissileMemberLaunchState>(member) || !manager.HasComponent<MissileMemberCommandState>(member)
                || !manager.HasComponent<MissileMemberDueState>(member) || !manager.HasComponent<MissileMemberCommandRequest>(member)
                || !manager.HasComponent<MissileMemberLaunchCellAssignment>(member)
                || !manager.HasComponent<MissileMemberSpawnAcknowledgement>(member)
                || !manager.HasComponent<MissileMemberLaunchHoldState>(member)
                || !manager.HasComponent<MissileMemberTrajectoryVariation>(member)
                || !manager.HasComponent<MissileMemberMotionState>(member))
                throw new ArgumentException("A complete M1-F spawn acknowledgement contract is required.", nameof(member));

            var launch = manager.GetComponentData<MissileMemberLaunchState>(member);
            var command = manager.GetComponentData<MissileMemberCommandState>(member);
            var counters = manager.GetComponentData<MissileCommandSchedulingCounters>(session);
            var cell = manager.GetComponentData<MissileMemberLaunchCellAssignment>(member).Cell;
            var existingAcknowledgement = manager.GetComponentData<MissileMemberSpawnAcknowledgement>(member);
            var salvoIdentity = manager.GetComponentData<SalvoIdentity>(salvo);
            if (launch.LaunchRequested != 1 || launch.Cancelled == 1 || command.Active == 1 || existingAcknowledgement.Acknowledged == 1)
                throw new InvalidOperationException("Only one outstanding non-cancelled launch request may be acknowledged.");
            if (acknowledgement.SessionGeneration != calendar.SessionGeneration || acknowledgement.ActualSpawnOrdinal != step.Ordinal
                || acknowledgement.ActualSpawnOrdinal < launch.DueOrdinal
                || !cell.IsValid || acknowledgement.Cell != cell || acknowledgement.Cell.EmitterStableId != salvoIdentity.Key.EmitterStableId
                || !math.all(math.isfinite(acknowledgement.Position)) || !math.all(math.isfinite(acknowledgement.LaunchForward))
                || math.lengthsq(acknowledgement.LaunchForward) <= 0.000001f)
                throw new ArgumentException("The spawn acknowledgement does not match its requested Member launch.", nameof(acknowledgement));

            bool emitterDestroyed = manager.HasComponent<MissileSalvoLaunchControl>(salvo)
                && manager.GetComponentData<MissileSalvoLaunchControl>(salvo).EmitterDestroyed == 1;
            if (emitterDestroyed)
            {
                launch.Cancelled = 1;
                manager.SetComponentData(member, launch);
                WriteRequest(manager, member, MissileCommandRequestKind.Cancel, acknowledgement.ActualSpawnOrdinal, 0, default);
                counters.CancelledOrInvalidDueCount++;
                manager.SetComponentData(session, counters);
                return false;
            }

            if (!MissileSalvoApproachPlanning.TryResolveProfileForMember(manager, member, calendar.SessionGeneration, out var profile))
                throw new InvalidOperationException("A valid immutable Member profile is required for Spawn acknowledgement.");
            var variation = manager.GetComponentData<MissileMemberTrajectoryVariation>(member);
            if (variation.Valid != 1 || !math.isfinite(variation.CommandPhase))
                throw new InvalidOperationException("A valid Member CommandPhase is required for Spawn acknowledgement.");

            ulong enableOrdinal = CalculateGuidanceEnableOrdinal(acknowledgement.ActualSpawnOrdinal,
                profile.LaunchHoldDuration, variation.CommandPhase, calendar.TickRate);
            acknowledgement.LaunchForward = math.normalize(acknowledgement.LaunchForward);
            acknowledgement.Acknowledged = 1;
            manager.SetComponentData(member, acknowledgement);
            manager.SetComponentData(member, new MissileMemberLaunchHoldState { GuidanceEnableOrdinal = enableOrdinal, Active = 1 });
            manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.LaunchBurst });
            manager.SetComponentData(member, new MissileMemberMotionState
            {
                Position = acknowledgement.Position,
                Forward = acknowledgement.LaunchForward,
                Speed = profile.Speed
            });

            command.Active = 1;
            command.HasCurrentSegment = 0;
            command.ReplanRequested = 0;
            if (!TryPublishLaunchHoldSegment(manager, session, member, acknowledgement, profile, acknowledgement.ActualSpawnOrdinal,
                ref command, out _))
                throw new InvalidOperationException("LaunchHold Straight could not be created.");
            counters.CommandGeneratedCount++;
            manager.SetComponentData(session, counters);
            return true;
        }

        /// <summary>Requests an allowed early replan without changing the current segment before M2 commits the new request.</summary>
        public static void RequestReplan(EntityManager manager, Entity session, Entity member, ulong dueOrdinal)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            if (dueOrdinal == 0 || !MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, calendar.SessionGeneration, out _)
                || !manager.HasComponent<MissileMemberCommandState>(member) || !manager.HasComponent<MissileMemberDueState>(member))
                throw new ArgumentException("An active Member and a due ordinal are required.", nameof(member));

            var command = manager.GetComponentData<MissileMemberCommandState>(member);
            if (command.Active != 1) throw new InvalidOperationException("Only an active Member may request replan.");
            command.ReplanRequested = 1;
            manager.SetComponentData(member, command);
            var due = manager.GetComponentData<MissileMemberDueState>(member);
            Link(manager, session, member, ref due, dueOrdinal);
        }

        internal static void ProcessDue(EntityManager manager, Entity session, SimulationStepContext step)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            if (calendar.SessionGeneration != step.Generation || calendar.TickRate != step.TickRate)
                throw new InvalidOperationException("Member command calendar does not match the executing step.");

            var buckets = manager.GetBuffer<MissileMemberCommandBucket>(session);
            var nodes = manager.GetBuffer<MissileMemberDueNode>(session);
            var counters = manager.GetComponentData<MissileCommandSchedulingCounters>(session);
            int bucketIndex = (int)(step.Ordinal % (ulong)calendar.TickRate);
            int nodeIndex = buckets[bucketIndex].HeadNode;
            buckets[bucketIndex] = new MissileMemberCommandBucket { HeadNode = -1 };
            uint dueThisStep = 0;

            while (nodeIndex >= 0)
            {
                if (nodeIndex >= nodes.Length) throw new InvalidOperationException("Member command calendar node is corrupt.");
                var node = nodes[nodeIndex];
                int nextNode = node.NextNode;
                ReleaseNode(manager, session, nodes, nodeIndex);
                dueThisStep++;

                if (!manager.Exists(node.Member) || !manager.HasComponent<MissileMemberDueState>(node.Member))
                {
                    counters.CancelledOrInvalidDueCount++;
                    nodeIndex = nextNode;
                    continue;
                }

                var due = manager.GetComponentData<MissileMemberDueState>(node.Member);
                if (due.IsScheduled != 1 || due.NodeIndex != nodeIndex || due.NextDueOrdinal != node.DueOrdinal)
                {
                    counters.CancelledOrInvalidDueCount++;
                    nodeIndex = nextNode;
                    continue;
                }

                due.IsScheduled = 0;
                due.NodeIndex = -1;
                manager.SetComponentData(node.Member, due);
                if (node.DueOrdinal > step.Ordinal)
                    Link(manager, session, node.Member, ref due, node.DueOrdinal);
                else if (node.DueOrdinal < step.Ordinal)
                    counters.CancelledOrInvalidDueCount++;
                else
                    ProcessDueMember(manager, session, node.Member, step, ref counters);

                nodeIndex = nextNode;
            }

            if (dueThisStep > counters.PeakDuePerStep) counters.PeakDuePerStep = dueThisStep;
            manager.SetComponentData(session, counters);
        }

        private static void ProcessDueMember(EntityManager manager, Entity session, Entity member, SimulationStepContext step,
            ref MissileCommandSchedulingCounters counters)
        {
            if (!MissileSalvoModelValidation.TryResolveActiveMemberSalvo(manager, member, step.Generation, out var salvo)
                || !manager.HasComponent<MissileMemberLaunchState>(member) || !manager.HasComponent<MissileMemberCommandState>(member)
                || !manager.HasComponent<MissileMemberCommandRequest>(member))
            {
                counters.CancelledOrInvalidDueCount++;
                return;
            }

            var launch = manager.GetComponentData<MissileMemberLaunchState>(member);
            var command = manager.GetComponentData<MissileMemberCommandState>(member);
            if (launch.Cancelled == 1) return;

            if (launch.Pending == 1)
            {
                counters.LaunchDueCount++;
                bool emitterDestroyed = manager.HasComponent<MissileSalvoLaunchControl>(salvo)
                    && manager.GetComponentData<MissileSalvoLaunchControl>(salvo).EmitterDestroyed == 1;
                if (emitterDestroyed || !MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, step.Generation,
                    out _, out _, out _))
                {
                    launch.Pending = 0;
                    launch.Cancelled = 1;
                    manager.SetComponentData(member, launch);
                    WriteRequest(manager, member, MissileCommandRequestKind.Cancel, step.Ordinal, 0, default);
                    counters.CancelledOrInvalidDueCount++;
                    return;
                }

                launch.Pending = 0;
                launch.LaunchRequested = 1;
                manager.SetComponentData(member, launch);
                WriteRequest(manager, member, MissileCommandRequestKind.Launch, step.Ordinal, 0, default);
                return;
            }

            if (command.Active != 1)
            {
                counters.CancelledOrInvalidDueCount++;
                return;
            }

            bool expired = command.HasCurrentSegment == 1 && command.CurrentSegment.EndOrdinal <= step.Ordinal;
            bool firstAcquire = false;
            if (manager.HasComponent<MissileMemberLaunchHoldState>(member))
            {
                var hold = manager.GetComponentData<MissileMemberLaunchHoldState>(member);
                if (hold.Active == 1)
                {
                    if (step.Ordinal < hold.GuidanceEnableOrdinal)
                    {
                        if (!expired && command.HasCurrentSegment == 1 && command.ReplanRequested != 1)
                        {
                            RescheduleSegmentEnd(manager, session, member, ref command);
                            manager.SetComponentData(member, command);
                            return;
                        }

                        if (!MissileSalvoApproachPlanning.TryResolveProfileForMember(manager, member, step.Generation, out var holdProfile)
                            || !manager.HasComponent<MissileMemberSpawnAcknowledgement>(member))
                        {
                            HandleUnavailablePlan(manager, session, member, step, ref command, expired, ref counters);
                            return;
                        }

                        var acknowledgement = manager.GetComponentData<MissileMemberSpawnAcknowledgement>(member);
                        command.ReplanRequested = 0;
                        if (TryPublishLaunchHoldSegment(manager, session, member, acknowledgement, holdProfile, step.Ordinal,
                            ref command, out _))
                        {
                            counters.CommandGeneratedCount++;
                            return;
                        }

                        HandleUnavailablePlan(manager, session, member, step, ref command, expired, ref counters);
                        return;
                    }

                    hold.Active = 0;
                    manager.SetComponentData(member, hold);
                    firstAcquire = true;
                }
            }

            if (command.HasCurrentSegment == 1 && !expired && command.ReplanRequested != 1)
            {
                RescheduleSegmentEnd(manager, session, member, ref command);
                manager.SetComponentData(member, command);
                return;
            }

            counters.CommandDueCount++;
            command.ReplanRequested = 0;
            if (!manager.HasComponent<MissileMemberTrajectoryVariation>(member)
                || !manager.HasComponent<MissileMemberGuidanceState>(member)
                || !manager.HasComponent<MissileMemberMotionState>(member)
                || !MissileSalvoApproachPlanning.TryResolveCurrentPlanForMember(manager, member, step.Generation,
                    out _, out var plan, out var profile))
            {
                HandleUnavailablePlan(manager, session, member, step, ref command, expired, ref counters);
                return;
            }

            var variation = manager.GetComponentData<MissileMemberTrajectoryVariation>(member);
            var guidance = manager.GetComponentData<MissileMemberGuidanceState>(member);
            var motion = manager.GetComponentData<MissileMemberMotionState>(member);
            uint sequence = checked(command.NextCommandSequence + 1);
            var request = new MissileGuidanceRequest
            {
                Motion = new MissileGuidanceMotion { Position = motion.Position, Forward = motion.Forward, Speed = motion.Speed },
                // The first Acquire request is evaluated with Separation's zero snap
                // allowance. Subsequent commands use the recorded Acquire state.
                State = firstAcquire ? MissileGuidanceState.Separation : guidance.Value,
                DeltaTime = step.DeltaTime,
                StartOrdinal = step.Ordinal,
                CommandSequence = sequence
            };
            var nextGuidanceState = NextGuidanceState(guidance.Value, firstAcquire, plan, profile, motion.Position);
            counters.GuidanceSolverCallCount++;
            if (!MissileGuidanceSolver.TryCreateSegment(plan, variation, profile, request, out var segment, out var decision))
            {
                HandleUnavailablePlan(manager, session, member, step, ref command, expired, ref counters);
                return;
            }

            command.NextCommandSequence = sequence;
            command.LastRequestOrdinal = step.Ordinal;
            command.CurrentSegment = segment;
            command.HasCurrentSegment = 1;
            manager.SetComponentData(member, command);
            if (firstAcquire)
                manager.SetComponentData(member, new MissileMemberGuidanceState { Value = MissileGuidanceState.Acquire });
            WriteRequest(manager, member, decision == MissileGuidanceDecision.StraightFallback
                ? MissileCommandRequestKind.StraightFallback : MissileCommandRequestKind.FlightSegment,
                step.Ordinal, sequence, segment, nextGuidanceState);
            counters.CommandGeneratedCount++;
            if (decision == MissileGuidanceDecision.StraightFallback) counters.StraightFallbackCount++;
            RescheduleSegmentEnd(manager, session, member, ref command);
        }

        private static void HandleUnavailablePlan(EntityManager manager, Entity session, Entity member, SimulationStepContext step,
            ref MissileMemberCommandState command, bool expired, ref MissileCommandSchedulingCounters counters)
        {
            if (!expired && command.HasCurrentSegment == 1)
            {
                manager.SetComponentData(member, command);
                RescheduleSegmentEnd(manager, session, member, ref command);
                return;
            }

            if (command.HasCurrentSegment == 1 && manager.HasComponent<MissileMemberMotionState>(member))
            {
                var motion = manager.GetComponentData<MissileMemberMotionState>(member);
                uint sequence = checked(command.NextCommandSequence + 1);
                if (MissileGuidanceSolver.TryCreateStraightFallback(command.CurrentSegment, motion.Position, motion.Forward,
                    step.Ordinal, sequence, step.DeltaTime, out var fallback))
                {
                    var preservedState = manager.HasComponent<MissileMemberGuidanceState>(member)
                        ? manager.GetComponentData<MissileMemberGuidanceState>(member).Value
                        : MissileGuidanceState.LaunchBurst;
                    command.NextCommandSequence = sequence;
                    command.LastRequestOrdinal = step.Ordinal;
                    command.CurrentSegment = fallback;
                    command.HasCurrentSegment = 1;
                    manager.SetComponentData(member, command);
                    WriteRequest(manager, member, MissileCommandRequestKind.StraightFallback, step.Ordinal, sequence, fallback,
                        preservedState);
                    counters.CommandGeneratedCount++;
                    counters.StraightFallbackCount++;
                    return;
                }
            }

            command.LastRequestOrdinal = step.Ordinal;
            command.HasCurrentSegment = 0;
            manager.SetComponentData(member, command);
            WriteRequest(manager, member, MissileCommandRequestKind.Expire, step.Ordinal, command.NextCommandSequence, default);
            counters.CancelledOrInvalidDueCount++;
        }

        private static bool TryPublishLaunchHoldSegment(EntityManager manager, Entity session, Entity member,
            MissileMemberSpawnAcknowledgement acknowledgement, MissileApproachProfile profile, ulong startOrdinal,
            ref MissileMemberCommandState command, out ulong endOrdinal)
        {
            endOrdinal = 0;
            if (!manager.HasComponent<MissileMemberLaunchHoldState>(member)
                || !manager.HasComponent<MissileMemberMotionState>(member))
                return false;

            var hold = manager.GetComponentData<MissileMemberLaunchHoldState>(member);
            if (hold.Active != 1 || startOrdinal >= hold.GuidanceEnableOrdinal) return false;
            var motion = manager.GetComponentData<MissileMemberMotionState>(member);
            int tickRate = manager.GetComponentData<MissileMemberCommandCalendar>(session).TickRate;
            if (!TryCalculateLaunchHoldSegmentEnd(startOrdinal, hold.GuidanceEnableOrdinal, profile.SegmentDuration,
                tickRate, out endOrdinal))
                return false;

            uint sequence = checked(command.NextCommandSequence + 1);
            if (!MissileGuidanceSolver.TryCreateStraightSegment(motion.Position, acknowledgement.LaunchForward, profile.Speed,
                1f / tickRate, startOrdinal, endOrdinal,
                sequence, out var segment))
                return false;

            command.NextCommandSequence = sequence;
            command.LastRequestOrdinal = startOrdinal;
            command.CurrentSegment = segment;
            command.HasCurrentSegment = 1;
            manager.SetComponentData(member, command);
            WriteRequest(manager, member, MissileCommandRequestKind.FlightSegment, startOrdinal, sequence, segment);
            RescheduleSegmentEnd(manager, session, member, ref command);
            return true;
        }

        private static bool TryCalculateLaunchHoldSegmentEnd(ulong startOrdinal, ulong guidanceEnableOrdinal,
            float maximumSegmentDuration, int tickRate, out ulong endOrdinal)
        {
            endOrdinal = 0;
            if (startOrdinal == 0 || guidanceEnableOrdinal <= startOrdinal || !math.isfinite(maximumSegmentDuration)
                || maximumSegmentDuration <= 0f || tickRate <= 0)
                return false;

            // This calendar only supports the session tick rate; a launch hold is split
            // at the profile segment duration so it never creates an unbounded command.
            double ticks = Math.Ceiling(maximumSegmentDuration * tickRate - TickRoundingTolerance);
            if (double.IsNaN(ticks) || double.IsInfinity(ticks) || ticks < 1d || ticks > ulong.MaxValue - startOrdinal)
                return false;
            ulong limitedEnd = checked(startOrdinal + (ulong)ticks);
            endOrdinal = limitedEnd < guidanceEnableOrdinal ? limitedEnd : guidanceEnableOrdinal;
            return endOrdinal > startOrdinal;
        }

        private static void RescheduleSegmentEnd(EntityManager manager, Entity session, Entity member, ref MissileMemberCommandState command)
        {
            if (command.HasCurrentSegment != 1 || command.CurrentSegment.EndOrdinal == ulong.MaxValue) return;
            var due = manager.GetComponentData<MissileMemberDueState>(member);
            Link(manager, session, member, ref due, command.CurrentSegment.EndOrdinal);
        }

        private static void WriteRequest(EntityManager manager, Entity member, MissileCommandRequestKind kind,
            ulong ordinal, uint sequence, MissileFlightSegment segment,
            MissileGuidanceState nextGuidanceState = MissileGuidanceState.LaunchBurst)
        {
            manager.SetComponentData(member, new MissileMemberCommandRequest
            {
                Kind = kind,
                Ordinal = ordinal,
                CommandSequence = sequence,
                Segment = segment,
                NextGuidanceState = nextGuidanceState,
                Pending = 1
            });
        }

        private static MissileGuidanceState NextGuidanceState(MissileGuidanceState current, bool firstAcquire,
            SalvoAttackPlan plan, MissileApproachProfile profile, float3 position)
        {
            if (firstAcquire || current == MissileGuidanceState.LaunchBurst)
                return MissileGuidanceState.Acquire;
            if (current == MissileGuidanceState.Acquire || current == MissileGuidanceState.Separation
                || current == MissileGuidanceState.CruiseDrift)
                return MissileGuidanceState.HomingArc;
            if (current == MissileGuidanceState.HomingArc && profile.AttackCommitDistance > 0f
                && math.distance(position, plan.PredictedTarget) <= profile.AttackCommitDistance)
                return MissileGuidanceState.AttackCommit;
            return current;
        }

        private static void Link(EntityManager manager, Entity session, Entity member, ref MissileMemberDueState state, ulong dueOrdinal)
        {
            if (dueOrdinal == 0) throw new ArgumentOutOfRangeException(nameof(dueOrdinal));
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            var buckets = manager.GetBuffer<MissileMemberCommandBucket>(session);
            var nodes = manager.GetBuffer<MissileMemberDueNode>(session);
            int nodeIndex = AcquireNode(ref calendar, nodes);
            int bucketIndex = (int)(dueOrdinal % (ulong)calendar.TickRate);
            nodes[nodeIndex] = new MissileMemberDueNode { Member = member, DueOrdinal = dueOrdinal, NextNode = buckets[bucketIndex].HeadNode };
            buckets[bucketIndex] = new MissileMemberCommandBucket { HeadNode = nodeIndex };
            state.NextDueOrdinal = dueOrdinal;
            state.NodeIndex = nodeIndex;
            state.IsScheduled = 1;
            manager.SetComponentData(member, state);
            manager.SetComponentData(session, calendar);
        }

        /// <summary>Reuses the existing calendar after M2 rejects a same-step segment contract.</summary>
        public static bool TryScheduleReplanNextStep(EntityManager manager, Entity session, Entity member,
            SimulationStepContext step)
        {
            if (step.Ordinal == ulong.MaxValue || !HasCalendar(manager, session)
                || !manager.Exists(member) || !manager.HasComponent<MissileMemberDueState>(member)
                || !manager.HasComponent<MissileMemberCommandState>(member))
                return false;

            var command = manager.GetComponentData<MissileMemberCommandState>(member);
            if (command.Active != 1) return false;
            var due = manager.GetComponentData<MissileMemberDueState>(member);
            // The old node stays linked until its bucket is reached and then self-retires as stale.
            // This avoids a second queue or a full Member scan while making the recovery due authoritative.
            due.IsScheduled = 0;
            due.NodeIndex = -1;
            command.ReplanRequested = 1;
            manager.SetComponentData(member, command);
            Link(manager, session, member, ref due, step.Ordinal + 1);
            return true;
        }

        private static int AcquireNode(ref MissileMemberCommandCalendar calendar, DynamicBuffer<MissileMemberDueNode> nodes)
        {
            if (calendar.FreeNodeHead >= 0)
            {
                int nodeIndex = calendar.FreeNodeHead;
                calendar.FreeNodeHead = nodes[nodeIndex].NextNode;
                return nodeIndex;
            }

            int next = nodes.Length;
            nodes.Add(default);
            return next;
        }

        private static void ReleaseNode(EntityManager manager, Entity session, DynamicBuffer<MissileMemberDueNode> nodes, int nodeIndex)
        {
            var calendar = manager.GetComponentData<MissileMemberCommandCalendar>(session);
            nodes[nodeIndex] = new MissileMemberDueNode { NextNode = calendar.FreeNodeHead };
            calendar.FreeNodeHead = nodeIndex;
            manager.SetComponentData(session, calendar);
        }
    }
}
