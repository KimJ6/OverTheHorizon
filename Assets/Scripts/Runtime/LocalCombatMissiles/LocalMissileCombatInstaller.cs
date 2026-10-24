using System;
using OverTheHorizon.Combat.Missiles;
using OverTheHorizon.LocalSimulation;
using OverTheHorizon.Simulation;
using UnityEngine;

namespace OverTheHorizon.LocalCombatMissiles
{
    /// <summary>
    /// Local composition root for the M0 missile stages. The bootstrap remains the
    /// sole owner of session preparation, completion, faults, and executor disposal.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalSimulationBootstrap))]
    public sealed class LocalMissileCombatInstaller : MonoBehaviour
    {
        private LocalSimulationBootstrap bootstrap;
        private bool subscribed;

        private void Awake()
        {
            bootstrap = GetComponent<LocalSimulationBootstrap>();
            if (bootstrap == null)
                throw new InvalidOperationException("Local missile stages require a LocalSimulationBootstrap.");
        }

        private void OnEnable()
        {
            if (subscribed) return;
            if (bootstrap == null)
                throw new InvalidOperationException("Local missile stages have no LocalSimulationBootstrap.");
            bootstrap.ConfigureExecution += ConfigureExecution;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed || bootstrap == null) return;
            bootstrap.ConfigureExecution -= ConfigureExecution;
            subscribed = false;
        }

        private static void ConfigureExecution(CombatStepExecutor executor)
        {
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            var manager = executor.World.EntityManager;
            if (!manager.HasBuffer<MissileContactResult>(executor.SessionEntity))
                manager.AddBuffer<MissileContactResult>(executor.SessionEntity);
            if (!manager.HasComponent<MissileContactDiagnostics>(executor.SessionEntity))
                manager.AddComponentData(executor.SessionEntity, new MissileContactDiagnostics { MinimumSeparation = float.PositiveInfinity });
            if (!manager.HasBuffer<MissileOutcomeResult>(executor.SessionEntity))
                manager.AddBuffer<MissileOutcomeResult>(executor.SessionEntity);
            if (!manager.HasComponent<MissileLifecycleDiagnostics>(executor.SessionEntity))
                manager.AddComponentData(executor.SessionEntity, default(MissileLifecycleDiagnostics));
            executor.RegisterStage<MissilePlanAndCommandStage>();
            executor.RegisterStage<MissileSpawnAndCommandCommitStage>();
            executor.RegisterStage<MissileMovementStage>();
            executor.RegisterStage<MissileContactGatherStage>();
            executor.RegisterStage<MissileOutcomeAndLifecycleStage>();
        }
    }
}
