using System;

namespace OverTheHorizon.Timing
{
    public enum SimulationLifecycle { Uninitialized, Preparing, Ready, Running, Paused, Stopping, Faulted, Stopped }
    public enum SimulationFaultCode { None, Configuration, Ownership, HookOrder, Input, Flight, Snapshot, Ecs, WorldLost, Capacity, Shutdown }

    public readonly struct SimulationTimingConfig
    {
        public readonly int TickRate;
        public readonly float FixedDeltaTime;
        public readonly int Version;
        public SimulationTimingConfig(int tickRate, int version = 1)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            if (tickRate != 50 && tickRate != 60 && tickRate != 120)
                throw new ArgumentOutOfRangeException(nameof(tickRate), "Supported trial rates: 50, 60, 120.");
            TickRate = tickRate;
            FixedDeltaTime = (float)(1.0 / tickRate);
            Version = version;
        }
    }

    public readonly struct SimulationStepContext
    {
        public readonly ulong Generation;
        public readonly uint Tick;
        public readonly int TickRate, ConfigVersion;
        public readonly ulong Ordinal;
        public readonly float DeltaTime;
        public readonly double StartTime;
        public readonly double EndTime;
        public SimulationStepContext(ulong generation, uint tick, ulong ordinal, SimulationTimingConfig config)
        {
            if (generation == 0 || ordinal == 0 || config.Version < 1 || config.FixedDeltaTime <= 0)
                throw new ArgumentException("A step requires a prepared generation and a positive ordinal.");
            Generation = generation;
            Tick = tick;
            TickRate = config.TickRate; ConfigVersion = config.Version;
            Ordinal = ordinal;
            DeltaTime = config.FixedDeltaTime;
            StartTime = (ordinal - 1) / (double)config.TickRate;
            EndTime = ordinal / (double)config.TickRate;
        }
        public bool Matches(SimulationStepContext other)
            => Generation == other.Generation && Tick == other.Tick && Ordinal == other.Ordinal
                && TickRate == other.TickRate && ConfigVersion == other.ConfigVersion;
    }

    public readonly struct SimulationFault
    {
        public readonly SimulationFaultCode Code;
        public readonly ulong Generation;
        public readonly uint ReservedTick;
        public readonly uint CompletedTick;
        public readonly uint PlayerId;
        public readonly string Stage;
        public readonly string Message;
        public SimulationFault(SimulationFaultCode code, ulong generation, uint reservedTick,
            uint completedTick, string stage, string message, uint playerId = 0)
        {
            Code = code; Generation = generation; ReservedTick = reservedTick;
            CompletedTick = completedTick; Stage = stage; Message = message; PlayerId = playerId;
        }
    }
}
