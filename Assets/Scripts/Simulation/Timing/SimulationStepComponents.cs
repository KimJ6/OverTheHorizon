using Unity.Entities;
using Unity.Mathematics;
using OverTheHorizon.Timing;

namespace OverTheHorizon.Simulation
{
    public struct SimulationStepHeader : IComponentData
    {
        // This is the current ECS execution context, not a second
        // completion ledger. SimulationProgress is the sole completion owner.
        public ulong Generation, Ordinal;
        public uint Tick;
        public float DeltaTime;
        public int TickRate;
    }
    public struct SimulationStepReceipt : IComponentData
    {
        public const uint Entry = 1, Scheduled = 2, Exit = 4;
        public uint Bits;
    }
    [InternalBufferCapacity(4)]
    public struct PlayerStepElement : IBufferElementData
    {
        public byte Valid;
        public uint PlayerId, SlotGeneration, StartStateTick, EndStateTick;
        public ulong Generation, ConsumedOrdinal, InputSequence, EdgeHighWatermark;
        public float3 StartPosition, EndPosition, StartVelocity, EndVelocity, EndAngularVelocity;
        public quaternion StartRotation, EndRotation, SteeringRotation;
        public float3 SteeringForward, SteeringRight, ViewForward, Axes, Gravity;
        public float LogicalYaw, LogicalPitch;
        public byte FreeLook, Maneuver;
    }
    public static class StepComponentUtility
    {
        public static void Mark(EntityManager manager, Entity entity, uint bit)
        {
            var receipt = manager.GetComponentData<SimulationStepReceipt>(entity);
            receipt.Bits |= bit; manager.SetComponentData(entity, receipt);
        }
    }
}
