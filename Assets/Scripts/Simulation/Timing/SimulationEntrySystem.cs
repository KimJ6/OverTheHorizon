using System;
using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    public partial class SimulationEntrySystem : CombatStepSystem
    {
        protected override void OnStep()
        {
            if (!EntityManager.Exists(SessionEntity)) throw new InvalidOperationException("Timing entity is missing.");
            var header = EntityManager.GetComponentData<SimulationStepHeader>(SessionEntity);
            if (header.Generation == 0 || header.Ordinal == 0 || header.Ordinal != header.CompletedOrdinal + 1)
                throw new InvalidOperationException("Invalid entry progress.");
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Entry);
        }
    }
}
