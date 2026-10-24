using NUnit.Framework;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.LocalCombatMissiles;
using OverTheHorizon.Timing;
using Unity.Mathematics;

namespace OverTheHorizon.Tests
{
    public sealed class MissileFlightPatternDiagnosticsTests
    {
        [TearDown]
        public void TearDown() => MissileFlightPatternDiagnostics.Clear();

        [Test]
        public void CurrentFrameKeepsAllConfiguredMembersWhileHistoryUsesItsIndependentSamplingBound()
        {
            MissileFlightPatternDiagnostics.ConfigureSampling(50, 1f, 0.04f, 3, 2);
            MissileFlightPatternDiagnostics.BeginFrame(1, 10);
            Record(0, 10);
            Record(1, 10);
            Record(2, 10);
            Assert.AreEqual(3, MissileFlightPatternDiagnostics.CurrentFrame.Count);
            Assert.AreEqual(3, Count(MissileFlightPatternDiagnostics.History));

            MissileFlightPatternDiagnostics.BeginFrame(1, 11);
            Record(0, 11);
            Record(1, 11);
            Record(2, 11);
            Assert.AreEqual(3, MissileFlightPatternDiagnostics.CurrentFrame.Count);
            Assert.AreEqual(3, Count(MissileFlightPatternDiagnostics.History));

            MissileFlightPatternDiagnostics.BeginFrame(1, 12);
            Record(0, 12);
            Record(1, 12);
            Record(2, 12);
            Assert.AreEqual(3, MissileFlightPatternDiagnostics.CurrentFrame.Count);
            Assert.AreEqual(3, Count(MissileFlightPatternDiagnostics.History));
        }

        [Test]
        public void CurrentFrameSupportsTheThreeHundredMemberDiagnosticCeiling()
        {
            MissileFlightPatternDiagnostics.ConfigureSampling(50, 6f, 0.04f, 60000, 300);
            MissileFlightPatternDiagnostics.BeginFrame(1, 10);
            for (uint i = 0; i < 300; ++i) Record(i, 10);

            Assert.AreEqual(300, MissileFlightPatternDiagnostics.CurrentFrame.Count);
        }

        [Test]
        public void ApproachSpaceMetricsUseTheSharedPlanBasisInsteadOfWorldAxes()
        {
            var first = CreateRecord(0, 10, new float3(2f, 3f, 4f));
            var second = CreateRecord(1, 10, new float3(4f, 3f, 4f));
            Assert.IsTrue(MissileFlightPatternDiagnostics.TryGetApproachSpacePosition(first, out var local));
            Assert.AreEqual(new float3(2f, 3f, 4f), local);

            var metrics = MissileFlightPatternDiagnostics.CalculateMetrics(new[] { first, second }, true);
            Assert.AreEqual(2, metrics.Count);
            Assert.AreEqual(new float3(3f, 3f, 4f), metrics.Center);
            Assert.That(metrics.CovarianceXX, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(metrics.CovarianceYY, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(metrics.CovarianceZZ, Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void TerminalOutcomesRemainBoundedAfterRuntimeMemberDespawn()
        {
            MissileFlightPatternDiagnostics.ConfigureSampling(50, 1f, 0.04f, 10, 1);
            MissileFlightPatternDiagnostics.BeginFrame(1, 10);
            MissileFlightPatternDiagnostics.RecordTerminal(new MissileOutcomeResult
            {
                SessionGeneration = 1,
                Ordinal = 10,
                Member = Key(0),
                Reason = MissileTerminalReason.Contact
            });
            MissileFlightPatternDiagnostics.RecordTerminal(new MissileOutcomeResult
            {
                SessionGeneration = 1,
                Ordinal = 10,
                Member = Key(1),
                Reason = MissileTerminalReason.Expire
            });

            Assert.AreEqual(1, Count(MissileFlightPatternDiagnostics.Terminals));
            foreach (var terminal in MissileFlightPatternDiagnostics.Terminals)
                Assert.AreEqual(MissileTerminalReason.Expire, terminal.Reason);
        }

        [Test]
        public void PauseRetainsDiagnosticsWhileStopClearsTheSessionBoundedStore()
        {
            MissileFlightPatternDiagnostics.ConfigureSampling(50, 1f, 0.04f, 10, 10);
            MissileFlightPatternDiagnostics.BeginFrame(1, 10);
            Record(0, 10);
            MissileFlightPatternDiagnostics.ObserveLifecycle(1, SimulationLifecycle.Paused);
            Assert.AreEqual(1, MissileFlightPatternDiagnostics.CurrentFrame.Count);

            MissileFlightPatternDiagnostics.ObserveLifecycle(1, SimulationLifecycle.Stopped);
            Assert.AreEqual(0, MissileFlightPatternDiagnostics.CurrentFrame.Count);
            Assert.AreEqual(0, Count(MissileFlightPatternDiagnostics.History));
        }

        private static void Record(uint ordinal, ulong step)
            => MissileFlightPatternDiagnostics.Record(CreateRecord(ordinal, step, new float3(ordinal, 0f, step)));

        private static MissileFlightDiagnosticRecord CreateRecord(uint ordinal, ulong step, float3 position)
        {
            var plan = new SalvoAttackPlan
            {
                Valid = 1,
                Right = new float3(1f, 0f, 0f),
                Up = new float3(0f, 1f, 0f),
                Forward = new float3(0f, 0f, 1f)
            };
            return new MissileFlightDiagnosticRecord(1, step, Key(ordinal), new MissileLaunchCellKey(700, ordinal % 4),
                0x1000UL + ordinal, new MissileMemberTrajectoryVariation { Motif = MissileTrajectoryMotif.CurvedWideApproach, Valid = 1 },
                1, 4, default, new float3(0f, 0f, 1f), position, new float3(0f, 0f, 1f), 40f,
                MissileGuidanceState.HomingArc, MissileFlightPrimitive.ConstantCurvatureArc, 0.02f,
                new float3(0f, 1f, 0f), step, step, plan);
        }

        private static MemberLogicalKey Key(uint ordinal) => new MemberLogicalKey(new SalvoLogicalKey(700, 1), ordinal);

        private static int Count<T>(System.Collections.Generic.IEnumerable<T> values)
        {
            int count = 0;
            foreach (var _ in values) ++count;
            return count;
        }
    }
}
