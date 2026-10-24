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
    public sealed class MissileCombatM0Tests
    {
        private static PlayerStepElement Player(uint id, SimulationStepContext context)
            => new PlayerStepElement
            {
                Valid = 1,
                PlayerId = id,
                SlotGeneration = 1,
                Generation = context.Generation,
                ConsumedOrdinal = context.Ordinal,
                InputSequence = 1,
                StartStateTick = unchecked(context.Tick - 1),
                EndStateTick = context.Tick,
                StartRotation = quaternion.identity,
                EndRotation = quaternion.identity,
                SteeringRotation = quaternion.identity,
                SteeringForward = new float3(0, 0, 1),
                SteeringRight = new float3(1, 0, 0),
                ViewForward = new float3(0, 0, 1)
            };

        private static void RegisterM0Stages(CombatStepExecutor executor)
        {
            executor.RegisterStage<MissilePlanAndCommandStage>();
            executor.RegisterStage<MissileSpawnAndCommandCommitStage>();
            executor.RegisterStage<MissileMovementStage>();
            executor.RegisterStage<MissileContactGatherStage>();
            executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
        }

        [TestCase(50)]
        [TestCase(60)]
        [TestCase(120)]
        public void NoOpStagesRunInOrderForAllFourPlayerSlots(int tickRate)
        {
            using (var world = new World("Missile M0 no-op"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(tickRate)))
            using (var players = new NativeArray<PlayerStepElement>(4, Allocator.TempJob))
            {
                RegisterM0Stages(executor);
                executor.Seal();

                var group = world.GetExistingSystemManaged<CombatFixedStepSystemGroup>();
                Assert.AreEqual(7, group.ManagedSystems.Count);
                Assert.IsInstanceOf<SimulationEntrySystem>(group.ManagedSystems[0]);
                Assert.IsInstanceOf<MissilePlanAndCommandStage>(group.ManagedSystems[1]);
                Assert.IsInstanceOf<MissileSpawnAndCommandCommitStage>(group.ManagedSystems[2]);
                Assert.IsInstanceOf<MissileMovementStage>(group.ManagedSystems[3]);
                Assert.IsInstanceOf<MissileContactGatherStage>(group.ManagedSystems[4]);
                Assert.IsInstanceOf<MissileOutcomeAndLifecycleStage>(group.ManagedSystems[5]);
                Assert.IsInstanceOf<SimulationCompleteSystem>(group.ManagedSystems[6]);

                var progress = new SimulationProgress();
                progress.Reset(1);
                var context = progress.Begin(new SimulationTimingConfig(tickRate));
                var writable = players;
                for (uint i = 0; i < 4; ++i) writable[(int)i] = Player(i + 1, context);

                executor.Execute(context, players, progress);
                executor.ValidatePending(context, progress);
                progress.Complete(context);
                executor.AcknowledgeAuthoritativeCompletion(context, progress);

                Assert.AreEqual(1ul, progress.CompletedOrdinal);
                Assert.AreEqual(CombatExecutionState.Ready, executor.State);
            }
        }

        [Test]
        public void MissileStageRegistrationIsRejectedAfterSeal()
        {
            using (var world = new World("Missile M0 late stage"))
            using (var executor = new CombatStepExecutor(world, 1, new SimulationTimingConfig(50)))
            {
                RegisterM0Stages(executor);
                executor.Seal();
                Assert.Throws<InvalidOperationException>(() => executor.RegisterStage<MissilePlanAndCommandStage>());
            }
        }

        [Test]
        public void LogicalKeysKeepSessionAndRegistryIdentitySeparate()
        {
            Assert.IsFalse(default(SalvoLogicalKey).IsValid);
            var salvo = new SalvoLogicalKey(42, 0);
            var member = new MemberLogicalKey(salvo, 0);
            var player = new SessionPlayerKey(7, 1, 3);

            Assert.IsTrue(salvo.IsValid);
            Assert.IsTrue(member.IsValid);
            Assert.IsTrue(player.IsValid);
            Assert.AreNotEqual(player, new SessionPlayerKey(8, 1, 3));
            Assert.AreNotEqual(MissileSeedDomain.MemberVariation, MissileSeedDomain.Scheduling);
            Assert.IsFalse(default(SessionPlayerKey).IsValid);
        }
    }
}
