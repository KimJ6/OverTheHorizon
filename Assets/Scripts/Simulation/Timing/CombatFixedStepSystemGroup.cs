using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    public partial class CombatFixedStepSystemGroup : ComponentSystemGroup
    {
        // R1: Unity owns member sorting and iteration. This type must not recreate
        // a custom foreach scheduler; CombatStepExecutor selects the one legal Update call.
    }
}
