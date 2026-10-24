using System;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Entities;

namespace OverTheHorizon.Combat.Missiles
{
    /// <summary>
    /// Session-owned, Salvo-only due buckets. This is deliberately not a reusable
    /// scheduler: one bucket table serves only M1-B target samples and is destroyed
    /// with the CombatStepExecutor session entity.
    /// </summary>
    public struct SalvoTargetSamplingCalendar : IComponentData
    {
        public ulong SessionGeneration;
        public int TickRate;
    }

    [InternalBufferCapacity(0)]
    public struct SalvoTargetSamplingBucket : IBufferElementData
    {
        public Entity Head;
    }

    /// <summary>One intrusive calendar link per Salvo; it never belongs to a Member.</summary>
    public struct SalvoTargetSamplingState : IComponentData
    {
        public ulong NextDueOrdinal;
        public Entity NextInBucket;
        public uint Phase;
        public byte IsScheduled;
    }

    public static class MissileSalvoTargetSampling
    {
        public const int SamplesPerSecond = 20;

        public static void EnsureCalendar(EntityManager manager, Entity session)
        {
            if (!manager.Exists(session) || !manager.HasComponent<SimulationStepHeader>(session))
                throw new ArgumentException("A CombatStepExecutor session is required.", nameof(session));

            var header = manager.GetComponentData<SimulationStepHeader>(session);
            if (header.Generation == 0 || header.TickRate <= 0)
                throw new InvalidOperationException("The session timing header is not initialized.");

            if (!manager.HasComponent<SalvoTargetSamplingCalendar>(session))
            {
                manager.AddComponentData(session, new SalvoTargetSamplingCalendar
                {
                    SessionGeneration = header.Generation,
                    TickRate = header.TickRate
                });
                var buckets = manager.AddBuffer<SalvoTargetSamplingBucket>(session);
                buckets.ResizeUninitialized(header.TickRate);
                for (int i = 0; i < buckets.Length; ++i) buckets[i] = default;
                return;
            }

            var calendar = manager.GetComponentData<SalvoTargetSamplingCalendar>(session);
            if (calendar.SessionGeneration != header.Generation || calendar.TickRate != header.TickRate
                || !manager.HasBuffer<SalvoTargetSamplingBucket>(session)
                || manager.GetBuffer<SalvoTargetSamplingBucket>(session).Length != header.TickRate)
                throw new InvalidOperationException("Salvo sampling calendar does not match the current session.");
        }

        /// <summary>
        /// Registers a fully-created Salvo for its first sample. M1-E spawning will call
        /// this after it has created identity, assignment, context, and sampling state.
        /// The first due ordinal is immediate so the Salvo never begins with an implicit
        /// stale target; later samples use the stable phase.
        /// </summary>
        public static void Register(EntityManager manager, Entity session, Entity salvo, ulong firstDueOrdinal)
        {
            EnsureCalendar(manager, session);
            if (firstDueOrdinal == 0 || !manager.Exists(salvo)
                || !manager.HasComponent<SalvoIdentity>(salvo)
                || !manager.HasComponent<SalvoTargetContext>(salvo)
                || !manager.HasComponent<SalvoTargetSamplingState>(salvo))
                throw new ArgumentException("A complete Salvo and a non-zero first due ordinal are required.", nameof(salvo));

            var calendar = manager.GetComponentData<SalvoTargetSamplingCalendar>(session);
            var identity = manager.GetComponentData<SalvoIdentity>(salvo);
            var state = manager.GetComponentData<SalvoTargetSamplingState>(salvo);
            if (!identity.Key.IsValid || identity.SessionGeneration != calendar.SessionGeneration || state.IsScheduled != 0)
                throw new InvalidOperationException("Only an unscheduled Salvo in this session may be registered.");

            state.Phase = CalculatePhase(identity.Key, calendar.TickRate);
            Link(manager, session, salvo, ref state, firstDueOrdinal);
        }

        /// <summary>Target assignment is an explicit operation: it invalidates the old
        /// sample synchronously, while the next registered due slot obtains new data.</summary>
        public static void AssignTarget(EntityManager manager, Entity salvo, SessionPlayerKey target)
        {
            if (!manager.Exists(salvo) || !manager.HasComponent<SalvoTargetContext>(salvo))
                throw new ArgumentException("A Salvo target context is required.", nameof(salvo));

            var context = manager.GetComponentData<SalvoTargetContext>(salvo);
            byte hasTarget = target.IsValid ? (byte)1 : (byte)0;
            if (context.Target.Equals(target) && context.HasTarget == hasTarget) return;
            context.Target = target;
            context.HasTarget = hasTarget;
            Invalidate(ref context);
            manager.SetComponentData(salvo, context);
            MissileSalvoApproachPlanning.InvalidateIfPresent(manager, salvo);
        }

        public static uint CalculatePhase(SalvoLogicalKey key, int tickRate)
        {
            if (!key.IsValid || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(key));
            ulong value = key.EmitterStableId ^ ((ulong)key.SalvoSequence << 32) ^ 0x53414D504C455048ul;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9ul;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBul;
            value ^= value >> 31;
            return (uint)(value % (uint)tickRate);
        }

        /// <summary>
        /// Returns the next exact rational 20 Hz due ordinal after <paramref name="ordinal"/>.
        /// The event pattern repeats at TickRate, so this has no accumulated rounded-tick
        /// error at 50, 60, or 120 Hz.
        /// </summary>
        public static ulong NextDueAfter(ulong ordinal, uint phase, int tickRate)
        {
            if (tickRate <= 0 || phase >= (uint)tickRate) throw new ArgumentOutOfRangeException(nameof(tickRate));
            for (uint offset = 1; offset <= (uint)tickRate; ++offset)
            {
                ulong candidate = checked(ordinal + offset);
                uint local = (uint)((candidate + phase) % (uint)tickRate);
                if (local == 0 || (local * SamplesPerSecond) / (uint)tickRate
                    > ((local - 1) * SamplesPerSecond) / (uint)tickRate)
                    return candidate;
            }
            throw new InvalidOperationException("No Salvo sampling due slot was found.");
        }

        internal static void ProcessDue(EntityManager manager, Entity session, SimulationStepContext step)
        {
            EnsureCalendar(manager, session);
            var calendar = manager.GetComponentData<SalvoTargetSamplingCalendar>(session);
            var buckets = manager.GetBuffer<SalvoTargetSamplingBucket>(session);
            int bucketIndex = (int)(step.Ordinal % (ulong)calendar.TickRate);
            Entity salvo = buckets[bucketIndex].Head;
            buckets[bucketIndex] = default;

            while (salvo != Entity.Null)
            {
                if (!manager.Exists(salvo) || !manager.HasComponent<SalvoTargetSamplingState>(salvo))
                    break;

                var state = manager.GetComponentData<SalvoTargetSamplingState>(salvo);
                Entity next = state.NextInBucket;
                state.NextInBucket = Entity.Null;
                state.IsScheduled = 0;
                manager.SetComponentData(salvo, state);

                if (state.NextDueOrdinal != step.Ordinal)
                {
                    if (state.NextDueOrdinal > step.Ordinal)
                        Link(manager, session, salvo, ref state, state.NextDueOrdinal);
                    else if (manager.HasComponent<SalvoTargetContext>(salvo))
                    {
                        var stale = manager.GetComponentData<SalvoTargetContext>(salvo);
                        Invalidate(ref stale);
                        manager.SetComponentData(salvo, stale);
                    }
                    salvo = next;
                    continue;
                }

                if (manager.HasComponent<SalvoIdentity>(salvo))
                {
                    var identity = manager.GetComponentData<SalvoIdentity>(salvo);
                    if (identity.SessionGeneration == step.Generation && identity.Key.IsValid)
                    {
                        RefreshTargetSample(manager, session, salvo, step);
                        MissileSalvoApproachPlanning.RefreshIfPresent(manager, salvo);
                        Link(manager, session, salvo, ref state, NextDueAfter(step.Ordinal, state.Phase, calendar.TickRate));
                    }
                    else if (manager.HasComponent<SalvoTargetContext>(salvo))
                    {
                        var stale = manager.GetComponentData<SalvoTargetContext>(salvo);
                        Invalidate(ref stale);
                        manager.SetComponentData(salvo, stale);
                        MissileSalvoApproachPlanning.InvalidateIfPresent(manager, salvo);
                    }
                }
                salvo = next;
            }
        }

        private static void RefreshTargetSample(EntityManager manager, Entity session, Entity salvo, SimulationStepContext step)
        {
            var context = manager.GetComponentData<SalvoTargetContext>(salvo);
            if (context.HasTarget != 1 || !context.Target.IsValid || context.Target.SessionGeneration != step.Generation)
            {
                Invalidate(ref context);
                manager.SetComponentData(salvo, context);
                return;
            }

            var players = manager.GetBuffer<PlayerStepElement>(session);
            for (int i = 0; i < players.Length; ++i)
            {
                var player = players[i];
                if (player.Valid == 1 && player.Generation == step.Generation && player.PlayerId == context.Target.PlayerId
                    && player.SlotGeneration == context.Target.SlotGeneration)
                {
                    context.SampledTarget = context.Target;
                    context.Position = player.EndPosition;
                    context.Velocity = player.EndVelocity;
                    context.SampleOrdinal = step.Ordinal;
                    context.SampleValid = 1;
                    context.Revision = checked(context.Revision + 1);
                    manager.SetComponentData(salvo, context);
                    return;
                }
            }

            Invalidate(ref context);
            manager.SetComponentData(salvo, context);
        }

        private static void Link(EntityManager manager, Entity session, Entity salvo, ref SalvoTargetSamplingState state, ulong due)
        {
            var calendar = manager.GetComponentData<SalvoTargetSamplingCalendar>(session);
            var buckets = manager.GetBuffer<SalvoTargetSamplingBucket>(session);
            int index = (int)(due % (ulong)calendar.TickRate);
            state.NextDueOrdinal = due;
            state.NextInBucket = buckets[index].Head;
            state.IsScheduled = 1;
            buckets[index] = new SalvoTargetSamplingBucket { Head = salvo };
            manager.SetComponentData(salvo, state);
        }

        private static void Invalidate(ref SalvoTargetContext context)
        {
            if (context.SampleValid == 0 && !context.SampledTarget.IsValid && context.SampleOrdinal == 0) return;
            context.SampledTarget = default;
            context.Position = default;
            context.Velocity = default;
            context.SampleOrdinal = 0;
            context.SampleValid = 0;
            context.Revision = checked(context.Revision + 1);
        }
    }
}
