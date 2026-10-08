using System;

namespace OverTheHorizon.Timing
{
    public static class TickMath
    {
        public const uint HalfRange = 0x80000000u;
        public static bool IsDue(uint now, uint due) => unchecked((int)(now - due)) >= 0;
        public static uint Elapsed(uint newer, uint older)
        {
            uint elapsed = unchecked(newer - older);
            if (elapsed >= HalfRange) throw new ArgumentOutOfRangeException(nameof(older), "Tick distance must be below the half range.");
            return elapsed;
        }
        public static uint DurationToTicks(double seconds, int tickRate)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            double value = seconds * tickRate;
            double nearest = Math.Round(value);
            if (Math.Abs(value - nearest) <= 1e-6) value = nearest;
            value = Math.Ceiling(value);
            if (double.IsInfinity(value) || value >= HalfRange) throw new ArgumentOutOfRangeException(nameof(seconds));
            return checked((uint)value);
        }
        public static uint PeriodToTicks(double seconds, int tickRate) => Math.Max(1u, DurationToTicks(seconds, tickRate));
        public static uint NextFutureDue(uint now, uint due, uint interval)
        {
            if (interval == 0 || interval >= HalfRange) throw new ArgumentOutOfRangeException(nameof(interval));
            if (!IsDue(now, due)) return due;
            uint elapsed = Elapsed(now, due);
            ulong advance = ((ulong)elapsed / interval + 1) * interval;
            return unchecked(due + (uint)advance);
        }
    }
}
