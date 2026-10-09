using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    public partial class SimulationCompleteSystem : CombatStepSystem
    {
        protected override void OnStep()
        {
            // Reaching the end is NOT authority completion. The executor must join and validate first.
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Exit);
        }
    }
}
