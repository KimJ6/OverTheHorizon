using System;
using NUnit.Framework;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.Simulation;
using OverTheHorizon.Timing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace OverTheHorizon.Tests
{
    public sealed class MissileSalvoTargetSamplingTests
    {
        private readonly struct PlayerFrame
        {
            public readonly uint Id, SlotGeneration;
            public readonly float3 Position, Velocity;

            public PlayerFrame(uint id, uint slotGeneration, float3 position, float3 velocity)
            {
                Id = id;
                SlotGeneration = slotGeneration;
                Position = position;
                Velocity = velocity;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly SimulationTimingConfig config;
            private readonly NativeArray<PlayerStepElement> players;
            private readonly SimulationProgress progress = new SimulationProgress();

            public readonly World World = new World("Missile M1-B target sampling");
            public readonly CombatStepExecutor Executor;
            public EntityManager Manager => World.EntityManager;

            public Fixture(int tickRate = 50)
            {
                config = new SimulationTimingConfig(tickRate);
                Executor = new CombatStepExecutor(World, 1, config);
                Executor.RegisterStage<MissilePlanAndCommandStage>();
                Executor.RegisterStage<MissileSpawnAndCommandCommitStage>();
                Executor.RegisterStage<MissileMovementStage>();
                Executor.RegisterStage<MissileContactGatherStage>();
                Executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
                Executor.Seal();
                MissileSalvoTargetSampling.EnsureCalendar(Manager, Executor.SessionEntity);
                players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob);
                progress.Reset(1);
            }

            public Entity CreateSalvo(SalvoLogicalKey key, SessionPlayerKey target)
            {
                var salvo = Manager.CreateEntity(typeof(SalvoIdentity), typeof(SalvoTargetContext), typeof(SalvoTargetSamplingState));
                Manager.SetComponentData(salvo, new SalvoIdentity { Key = key, SessionGeneration = 1 });
                Manager.SetComponentData(salvo, new SalvoTargetContext { Target = target, HasTarget = 1 });
                MissileSalvoTargetSampling.Register(Manager, Executor.SessionEntity, salvo, 1);
                return salvo;
            }

            public SimulationStepContext Run(params PlayerFrame[] active)
            {
                var context = progress.Begin(config);
                var writable = players;
                for (int i = 0; i < writable.Length; ++i) writable[i] = default;
                for (int i = 0; i < active.Length; ++i)
                    writable[i] = Player(active[i], context);
                if (active.Length == 0)
                    writable[0] = Player(new PlayerFrame(1, 1, default, default), context);

                Executor.Execute(context, players, progress);
                Executor.ValidatePending(context, progress);
                progress.Complete(context);
                Executor.AcknowledgeAuthoritativeCompletion(context, progress);
                return context;
            }

            public void Dispose()
            {
                if (players.IsCreated) players.Dispose();
                Executor.Dispose();
                World.Dispose();
            }

            private static PlayerStepElement Player(PlayerFrame frame, SimulationStepContext context)
                => new PlayerStepElement
                {
                    Valid = 1,
                    PlayerId = frame.Id,
                    SlotGeneration = frame.SlotGeneration,
                    Generation = context.Generation,
                    ConsumedOrdinal = context.Ordinal,
                    InputSequence = 1,
                    StartStateTick = unchecked(context.Tick - 1),
                    EndStateTick = context.Tick,
                    StartPosition = frame.Position,
                    EndPosition = frame.Position,
                    StartVelocity = frame.Velocity,
                    EndVelocity = frame.Velocity,
                    StartRotation = quaternion.identity,
                    EndRotation = quaternion.identity,
                    SteeringRotation = quaternion.identity,
                    SteeringForward = new float3(0, 0, 1),
                    SteeringRight = new float3(1, 0, 0),
                    ViewForward = new float3(0, 0, 1)
                };
        }

        [Test]
        public void SamplesTheAssignedPlayerEndStateForEachSalvoOnlyOnce()
        {
            using (var fixture = new Fixture())
            {
                var target = new SessionPlayerKey(1, 1, 1);
                Entity first = fixture.CreateSalvo(new SalvoLogicalKey(10, 0), target);
                Entity second = fixture.CreateSalvo(new SalvoLogicalKey(10, 1), target);
                var position = new float3(2, 3, 5);
                var velocity = new float3(-1, 4, 0.5f);

                fixture.Run(new PlayerFrame(1, 1, position, velocity));

                var firstContext = fixture.Manager.GetComponentData<SalvoTargetContext>(first);
                var secondContext = fixture.Manager.GetComponentData<SalvoTargetContext>(second);
                Assert.AreEqual(target, firstContext.SampledTarget);
                Assert.AreEqual(position, firstContext.Position);
                Assert.AreEqual(velocity, firstContext.Velocity);
                Assert.AreEqual(1ul, firstContext.SampleOrdinal);
                Assert.AreEqual(1u, firstContext.Revision);
                Assert.AreEqual(1, firstContext.SampleValid);
                Assert.AreEqual(1u, secondContext.Revision);
                Assert.AreEqual(position, secondContext.Position);
            }
        }

        [Test]
        public void MembersDoNotCauseAdditionalSalvoSamples()
        {
            using (var fixture = new Fixture())
            {
                var key = new SalvoLogicalKey(11, 0);
                Entity salvo = fixture.CreateSalvo(key, new SessionPlayerKey(1, 1, 1));
                for (uint i = 0; i < 64; ++i)
                {
                    Entity member = fixture.Manager.CreateEntity(typeof(MissileMemberIdentity), typeof(MissileMemberSalvoReference));
                    fixture.Manager.SetComponentData(member, new MissileMemberIdentity { Key = new MemberLogicalKey(key, i), SessionGeneration = 1 });
                    fixture.Manager.SetComponentData(member, new MissileMemberSalvoReference { Salvo = salvo, SessionGeneration = 1, Active = 1 });
                }

                fixture.Run(new PlayerFrame(1, 1, new float3(1, 0, 0), default));

                Assert.AreEqual(1u, fixture.Manager.GetComponentData<SalvoTargetContext>(salvo).Revision);
            }
        }

        [Test]
        public void InitialSampleIsImmediateAndNonDueOrdinalDoesNotRewriteIt()
        {
            using (var fixture = new Fixture())
            {
                var key = FindKeyWithPhase(50, 0);
                Entity salvo = fixture.CreateSalvo(key, new SessionPlayerKey(1, 1, 1));
                fixture.Run(new PlayerFrame(1, 1, new float3(1, 0, 0), default));
                var first = fixture.Manager.GetComponentData<SalvoTargetContext>(salvo);
                fixture.Run(new PlayerFrame(1, 1, new float3(2, 0, 0), default));
                var second = fixture.Manager.GetComponentData<SalvoTargetContext>(salvo);

                Assert.AreEqual(1ul, first.SampleOrdinal);
                Assert.AreEqual(1u, first.Revision);
                Assert.AreEqual(first.Revision, second.Revision);
                Assert.AreEqual(first.Position, second.Position);
            }
        }

        [TestCase(50)]
        [TestCase(60)]
        [TestCase(120)]
        public void DueScheduleMaintainsTwentySamplesPerSecondWithoutRoundedTickDrift(int tickRate)
        {
            var key = new SalvoLogicalKey(50, 7);
            uint phase = MissileSalvoTargetSampling.CalculatePhase(key, tickRate);
            ulong current = 0;
            int count = 0;
            for (int second = 0; second < 10; ++second)
            {
                ulong limit = (ulong)(second + 1) * (ulong)tickRate;
                while (true)
                {
                    ulong next = MissileSalvoTargetSampling.NextDueAfter(current, phase, tickRate);
                    if (next > limit) break;
                    current = next;
                    ++count;
                }
                Assert.AreEqual((second + 1) * MissileSalvoTargetSampling.SamplesPerSecond, count);
            }
        }

        [Test]
        public void StableKeyProducesStableAndDistributedSchedulePhases()
        {
            var key = new SalvoLogicalKey(91, 4);
            uint first = MissileSalvoTargetSampling.CalculatePhase(key, 60);
            uint same = MissileSalvoTargetSampling.CalculatePhase(key, 60);
            uint different = MissileSalvoTargetSampling.CalculatePhase(FindKeyWithDifferentPhase(key, 60), 60);

            Assert.AreEqual(first, same);
            Assert.AreNotEqual(first, different);
        }

        [Test]
        public void MissingTargetInvalidatesTheSharedSample()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreateSalvo(new SalvoLogicalKey(12, 0), new SessionPlayerKey(1, 1, 1));
                fixture.Run(new PlayerFrame(1, 1, new float3(1, 0, 0), default));
                RunUntil(fixture, salvo, context => context.SampleValid == 0,
                    new PlayerFrame(2, 1, new float3(2, 0, 0), default));

                var context = fixture.Manager.GetComponentData<SalvoTargetContext>(salvo);
                Assert.AreEqual(0, context.SampleValid);
                Assert.AreEqual(default(SessionPlayerKey), context.SampledTarget);
                Assert.AreEqual(2u, context.Revision);
            }
        }

        [Test]
        public void ChangedSlotGenerationIsNotAcceptedAsThePreviousTarget()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreateSalvo(new SalvoLogicalKey(12, 1), new SessionPlayerKey(1, 1, 1));
                fixture.Run(new PlayerFrame(1, 1, default, default));
                RunUntil(fixture, salvo, context => context.SampleValid == 0,
                    new PlayerFrame(1, 2, default, default));

                Assert.AreEqual(2u, fixture.Manager.GetComponentData<SalvoTargetContext>(salvo).Revision);
            }
        }

        [Test]
        public void ExplicitTargetChangeInvalidatesThenPublishesTheNewIdentity()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreateSalvo(new SalvoLogicalKey(13, 0), new SessionPlayerKey(1, 1, 1));
                fixture.Run(new PlayerFrame(1, 1, new float3(1, 0, 0), default), new PlayerFrame(2, 1, new float3(2, 0, 0), default));
                MissileSalvoTargetSampling.AssignTarget(fixture.Manager, salvo, new SessionPlayerKey(1, 2, 1));

                var invalidated = fixture.Manager.GetComponentData<SalvoTargetContext>(salvo);
                Assert.AreEqual(0, invalidated.SampleValid);
                Assert.AreEqual(2u, invalidated.Revision);

                RunUntil(fixture, salvo, context => context.SampleValid == 1 && context.SampledTarget.PlayerId == 2,
                    new PlayerFrame(1, 1, new float3(3, 0, 0), default), new PlayerFrame(2, 1, new float3(4, 0, 0), default));
                var refreshed = fixture.Manager.GetComponentData<SalvoTargetContext>(salvo);
                Assert.AreEqual(new float3(4, 0, 0), refreshed.Position);
                Assert.AreEqual(3u, refreshed.Revision);
            }
        }

        [Test]
        public void ChangedSalvoSessionGenerationInvalidatesAndStopsTheOldSchedule()
        {
            using (var fixture = new Fixture())
            {
                Entity salvo = fixture.CreateSalvo(new SalvoLogicalKey(14, 0), new SessionPlayerKey(1, 1, 1));
                fixture.Run(new PlayerFrame(1, 1, default, default));
                var identity = fixture.Manager.GetComponentData<SalvoIdentity>(salvo);
                identity.SessionGeneration = 2;
                fixture.Manager.SetComponentData(salvo, identity);

                RunUntil(fixture, salvo, context => context.SampleValid == 0,
                    new PlayerFrame(1, 1, default, default));
                Assert.AreEqual(2u, fixture.Manager.GetComponentData<SalvoTargetContext>(salvo).Revision);
                Assert.AreEqual(0, fixture.Manager.GetComponentData<SalvoTargetSamplingState>(salvo).IsScheduled);
            }
        }

        [Test]
        public void EmptySalvoSetIsANormalNoOp()
        {
            using (var fixture = new Fixture())
            {
                fixture.Run(new PlayerFrame(1, 1, default, default));
                Assert.AreEqual(CombatExecutionState.Ready, fixture.Executor.State);
            }
        }

        private static void RunUntil(Fixture fixture, Entity salvo, Func<SalvoTargetContext, bool> condition, params PlayerFrame[] players)
        {
            for (int i = 0; i < 121; ++i)
            {
                fixture.Run(players);
                if (condition(fixture.Manager.GetComponentData<SalvoTargetContext>(salvo))) return;
            }
            Assert.Fail("Expected the Salvo sampling schedule to reach the requested state.");
        }

        private static SalvoLogicalKey FindKeyWithPhase(int tickRate, uint phase)
        {
            for (uint sequence = 0; sequence < 4096; ++sequence)
            {
                var key = new SalvoLogicalKey(100, sequence);
                if (MissileSalvoTargetSampling.CalculatePhase(key, tickRate) == phase) return key;
            }
            throw new AssertionException("No stable phase key was found.");
        }

        private static SalvoLogicalKey FindKeyWithDifferentPhase(SalvoLogicalKey source, int tickRate)
        {
            uint phase = MissileSalvoTargetSampling.CalculatePhase(source, tickRate);
            for (uint sequence = 0; sequence < 4096; ++sequence)
            {
                var candidate = new SalvoLogicalKey(source.EmitterStableId, sequence);
                if (MissileSalvoTargetSampling.CalculatePhase(candidate, tickRate) != phase) return candidate;
            }
            throw new AssertionException("No differing stable phase key was found.");
        }
    }
}
