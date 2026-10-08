using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    [UpdateAfter(typeof(SimulationEntrySystem))]
    [UpdateBefore(typeof(SimulationCompleteSystem))]
    public partial class SimulationInputValidationSystem : SystemBase
    {
        public Entity SessionEntity;
        public StepJobFence Fence;
        private NativeArray<int> result;
        public bool ValidationPassed => result.IsCreated && result[0] == 1;
        protected override void OnCreate() { result = new NativeArray<int>(1, Allocator.Persistent); }
        protected override void OnDestroy() { Dependency.Complete(); if (result.IsCreated) result.Dispose(); }
        protected override void OnUpdate()
        {
            result[0] = 0;
            var header = EntityManager.GetComponentData<SimulationStepHeader>(SessionEntity);
            var input = EntityManager.GetBuffer<PlayerStepElement>(SessionEntity, true).AsNativeArray();
            Dependency = new ValidateInputJob { Header = header, Input = input, Result = result }.Schedule(Dependency);
            Fence.Register(Dependency);
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Scheduled);
        }
        private struct ValidateInputJob : IJob
        {
            public SimulationStepHeader Header;
            [ReadOnly] public NativeArray<PlayerStepElement> Input;
            public NativeArray<int> Result;
            public void Execute()
            {
                int active = 0;
                for (int i = 0; i < Input.Length; ++i)
                {
                    var p = Input[i]; if (p.Valid == 0) continue; ++active;
                    if (p.Valid != 1 || p.PlayerId == 0 || p.SlotGeneration == 0 || p.InputSequence == 0
                        || p.FreeLook > 1 || p.Maneuver > 1 || p.Generation != Header.Generation || p.ConsumedOrdinal != Header.Ordinal
                        || p.StartStateTick != unchecked(Header.Tick - 1) || p.EndStateTick != Header.Tick
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
                        || math.abs(math.lengthsq(p.SteeringRotation.value) - 1f) > 0.01f) return;
                    for (int j = 0; j < i; ++j) if (Input[j].Valid != 0 && Input[j].PlayerId == p.PlayerId) return;
                }
                Result[0] = active > 0 && Input.Length == 4 ? 1 : 0;
            }
        }
    }
}
