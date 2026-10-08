using System;

namespace OverTheHorizon.Timing
{
    /// <summary>Single-writer progress. Reservation is not completion; faults never advance completion.</summary>
    public sealed class SimulationProgress
    {
        public ulong Generation { get; private set; }
        public uint ReservedTick { get; private set; }
        public uint CompletedTick { get; private set; }
        public ulong CompletedOrdinal { get; private set; }
        public bool StepOpen { get; private set; }
        public bool Faulted { get; private set; }
        private SimulationStepContext openStep;

        public void Reset(ulong generation)
        {
            if (generation == 0 || generation <= Generation || StepOpen)
                throw new InvalidOperationException("Reset requires a closed step and a strictly newer generation.");
            Generation = generation; ReservedTick = CompletedTick = 0; CompletedOrdinal = 0;
            Faulted = false; openStep = default;
        }
        public SimulationStepContext Begin(SimulationTimingConfig config)
        {
            if (config.Version < 1 || config.FixedDeltaTime <= 0) throw new ArgumentException("A constructed timing configuration is required.");
            if (Generation == 0 || StepOpen || Faulted) throw new InvalidOperationException("Cannot reserve this step.");
            ulong ordinal = checked(CompletedOrdinal + 1);
            ReservedTick = unchecked(CompletedTick + 1);
            openStep = new SimulationStepContext(Generation, ReservedTick, ordinal, config);
            StepOpen = true;
            return openStep;
        }
        public void Complete(SimulationStepContext context)
        {
            if (!StepOpen || Faulted || !context.Matches(openStep)) throw new InvalidOperationException("Completion does not match the reserved step.");
            CompletedTick = context.Tick; CompletedOrdinal = context.Ordinal; StepOpen = false;
        }
        public void Abort()
        {
            StepOpen = false; Faulted = true;
        }
    }
}
