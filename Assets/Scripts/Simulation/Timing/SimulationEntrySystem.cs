using System;
using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    // Ordering anchor: every sealed combat step validates its input header first.
    [UpdateInGroup(typeof(CombatFixedStepSystemGroup), OrderFirst = true)]
    public partial class SimulationEntrySystem : CombatStepSystem
    {
        protected override void OnStep()
        {
            if (!EntityManager.Exists(SessionEntity)) throw new InvalidOperationException("Timing entity is missing.");
            var header = EntityManager.GetComponentData<SimulationStepHeader>(SessionEntity);
            // The caller's open SimulationProgress already establishes
            // sequence. ECS only verifies the immutable context for this run.
            if (header.Generation != Step.Generation || header.Tick != Step.Tick || header.Ordinal != Step.Ordinal
                || header.TickRate != Step.TickRate || header.DeltaTime != Step.DeltaTime)
                throw new InvalidOperationException("Invalid entry progress.");
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Entry);
        }
    }
}
