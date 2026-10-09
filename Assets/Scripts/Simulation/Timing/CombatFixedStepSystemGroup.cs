using System;
using Unity.Entities;

namespace OverTheHorizon.Simulation
{
    [DisableAutoCreation]
    public partial class CombatFixedStepSystemGroup : ComponentSystemGroup
    {
        internal CombatStepExecutor Executor;
        internal CombatStepSystem[] Stages;
        internal bool Completed;
        private bool running;
        protected override void OnUpdate()
        {
            if (running || Executor == null || !Executor.IsExecuting || Stages == null)
                throw new InvalidOperationException("Combat runner has no active execution.");
            running = true;
            try
            {
                // Do not call the standard group's exception-swallowing update loop.
                foreach (var stage in Stages)
                {
                    Executor.RequireExecuting();
                    stage.Run(Executor.CurrentStep, Executor.SessionEntity, Executor.Jobs);
                    Executor.RequireExecuting();
                }
                Completed = true;
            }
            finally { running = false; }
        }
    }
}
