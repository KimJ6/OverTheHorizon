using System;
using UnityEngine;
using OverTheHorizon.Timing;

namespace OverTheHorizon.LocalSimulation
{
    [CreateAssetMenu(menuName = "OverTheHorizon/Simulation Timing Settings")]
    public sealed class SimulationTimingSettings : ScriptableObject
    {
        [SerializeField] private int tickRate = 50;
        [SerializeField] private float maximumDeltaTime = 0.33333334f;
        [SerializeField] private int traceCapacity = 512;
        public SimulationTimingConfig CreateConfig() => new SimulationTimingConfig(tickRate);
        public float MaximumDeltaTime => maximumDeltaTime;
        public int TraceCapacity => traceCapacity;
        public void Validate()
        {
            var config = CreateConfig();
            if (float.IsNaN(maximumDeltaTime) || float.IsInfinity(maximumDeltaTime)
                || maximumDeltaTime < config.FixedDeltaTime || traceCapacity < 1 || traceCapacity > 65536)
                throw new InvalidOperationException("Invalid local timing settings.");
        }
    }
}
