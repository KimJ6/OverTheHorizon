using System;
using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    [UpdateBefore(typeof(SimulationInputValidationSystem))]
    public partial class SimulationEntrySystem : SystemBase
    {
        public Entity SessionEntity;
        protected override void OnUpdate()
        {
            if (!EntityManager.Exists(SessionEntity)) throw new InvalidOperationException("Timing entity is missing.");
            var header = EntityManager.GetComponentData<SimulationStepHeader>(SessionEntity);
            if (header.Generation == 0 || header.Ordinal == 0 || header.Ordinal != header.CompletedOrdinal + 1)
                throw new InvalidOperationException("Invalid entry progress.");
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Entry);
        }
    }
}
