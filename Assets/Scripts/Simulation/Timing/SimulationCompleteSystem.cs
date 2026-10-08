using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    [UpdateAfter(typeof(SimulationInputValidationSystem))]
    public partial class SimulationCompleteSystem : SystemBase
    {
        public Entity SessionEntity;
        protected override void OnUpdate()
        {
            // Reaching the end is NOT authority completion. The executor must join and validate first.
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Exit);
        }
    }
}
