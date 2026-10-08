using System;
using NUnit.Framework;
using OverTheHorizon.Timing;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.Tests
{
    public sealed class TimingCoreTests
    {
        [TestCase(50)] [TestCase(60)] [TestCase(120)]
        public void ClockUsesIntegerOrdinal(int rate)
        {
            var p = new SimulationProgress(); p.Reset(1); var config = new SimulationTimingConfig(rate);
            for (int i = 0; i < 1000; ++i) { var step = p.Begin(config); Assert.AreEqual((i + 1) / (double)rate, step.EndTime); p.Complete(step); }
            Assert.AreEqual(1000ul, p.CompletedOrdinal);
        }
        [Test] public void AbortDoesNotCompleteReservedTick()
        {
            var p = new SimulationProgress(); p.Reset(1); var config = new SimulationTimingConfig(50);
            p.Complete(p.Begin(config)); p.Begin(config); p.Abort();
            Assert.AreEqual(1u, p.CompletedTick); Assert.AreEqual(2u, p.ReservedTick);
            Assert.Throws<InvalidOperationException>(() => p.Begin(config)); p.Reset(2); Assert.AreEqual(0u, p.CompletedTick);
            Assert.Throws<InvalidOperationException>(() => p.Reset(2));
        }
        [Test] public void DoubleBeginAndMismatchedCompletionAreRejected()
        {
            var p = new SimulationProgress(); p.Reset(1); var c = new SimulationTimingConfig(50); var step = p.Begin(c);
            Assert.Throws<InvalidOperationException>(() => p.Begin(c));
            Assert.Throws<InvalidOperationException>(() => p.Complete(new SimulationStepContext(2, 1, 1, c)));
            Assert.Throws<InvalidOperationException>(() => p.Complete(new SimulationStepContext(1, 1, 1, new SimulationTimingConfig(60))));
            p.Complete(step); Assert.Throws<InvalidOperationException>(() => p.Complete(step));
        }
        [Test] public void TickDeadlineWorksAcrossWrap()
        {
            Assert.IsTrue(TickMath.IsDue(1, uint.MaxValue)); Assert.IsFalse(TickMath.IsDue(uint.MaxValue, 1));
            Assert.AreEqual(2u, TickMath.Elapsed(1, uint.MaxValue));
            Assert.AreEqual(3u, TickMath.NextFutureDue(1, uint.MaxValue, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => TickMath.Elapsed(0x80000000u, 0));
        }
        [Test] public void DurationRoundingDoesNotAddSpuriousTick()
        {
            Assert.AreEqual(1u, TickMath.DurationToTicks((float)0.02, 50));
            Assert.AreEqual(2u, TickMath.DurationToTicks(0.0200001, 50));
            Assert.AreEqual(0u, TickMath.DurationToTicks(0, 50)); Assert.AreEqual(1u, TickMath.PeriodToTicks(0, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => TickMath.DurationToTicks(double.NaN, 50));
        }
        [Test] public void EdgeReadsAreNonDestructiveAndPartialAckKeepsNewEdges()
        {
            var q = new InputEdgeBuffer(2); q.Enqueue(10); ulong accepted = q.HighWatermark; q.Enqueue(20);
            Assert.AreEqual(10u, q.Read(0).Kind); Assert.AreEqual(2, q.Count);
            Assert.Throws<InvalidOperationException>(() => q.Enqueue(30)); q.AckThrough(accepted);
            Assert.AreEqual(20u, q.Read(0).Kind); ulong previous = q.HighWatermark; q.Clear(); q.Enqueue(40);
            Assert.Greater(q.HighWatermark, previous);
        }
    }
}
