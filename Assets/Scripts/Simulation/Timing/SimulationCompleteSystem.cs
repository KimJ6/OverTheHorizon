using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    // R1 ordering anchor: this records pipeline reachability, not authority completion.
    [UpdateInGroup(typeof(CombatFixedStepSystemGroup), OrderLast = true)]
    public partial class SimulationCompleteSystem : CombatStepSystem
    {
        protected override void OnStep()
        {
            // Reaching the end is NOT authority completion. The executor must join and validate first.
            StepComponentUtility.Mark(EntityManager, SessionEntity, SimulationStepReceipt.Exit);
        }
    }
}
