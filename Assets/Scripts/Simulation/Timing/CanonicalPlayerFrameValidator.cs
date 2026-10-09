using System;
using Unity.Collections;
using Unity.Mathematics;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    /// <summary>Validate all four slots before any ECS state or gameplay effect is changed.</summary>
    public static class CanonicalPlayerFrameValidator
    {
        public static void Validate(SimulationStepContext context, NativeArray<PlayerStepElement> input)
        {
            if (!input.IsCreated || input.Length != 4) throw new InvalidOperationException("Exactly four canonical slots are required.");
            int active = 0;
            for (int i = 0; i < input.Length; ++i)
            {
                var p = input[i]; if (p.Valid == 0) continue; ++active;
                if (p.Valid != 1 || p.PlayerId == 0 || p.SlotGeneration == 0 || p.InputSequence == 0
                    || p.FreeLook > 1 || p.Maneuver > 1 || p.Generation != context.Generation || p.ConsumedOrdinal != context.Ordinal
                    || p.StartStateTick != unchecked(context.Tick - 1) || p.EndStateTick != context.Tick
                    || !math.all(math.isfinite(p.StartPosition)) || !math.all(math.isfinite(p.EndPosition))
                    || !math.all(math.isfinite(p.StartVelocity)) || !math.all(math.isfinite(p.EndVelocity))
                    || !math.all(math.isfinite(p.EndAngularVelocity)) || !math.all(math.isfinite(p.StartRotation.value))
                    || !math.all(math.isfinite(p.EndRotation.value)) || !math.all(math.isfinite(p.SteeringRotation.value))
                    || !math.all(math.isfinite(p.SteeringForward)) || !math.all(math.isfinite(p.SteeringRight))
                    || !math.all(math.isfinite(p.ViewForward)) || !math.isfinite(p.LogicalYaw) || !math.isfinite(p.LogicalPitch)
                    || !math.all(math.isfinite(p.Axes)) || !math.all(math.isfinite(p.Gravity))
                    || math.any(math.abs(p.Axes) > 1f)
                    || math.abs(math.lengthsq(p.StartRotation.value) - 1f) > 0.01f
                    || math.abs(math.lengthsq(p.EndRotation.value) - 1f) > 0.01f
                    || math.abs(math.lengthsq(p.SteeringRotation.value) - 1f) > 0.01f) throw new InvalidOperationException("Invalid canonical player frame.");
                for (int j = 0; j < i; ++j) if (input[j].Valid != 0 && input[j].PlayerId == p.PlayerId) throw new InvalidOperationException("Invalid canonical player frame.");
            }
            if (active == 0) throw new InvalidOperationException("At least one active player is required.");
        }
    }
}
