using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>
    /// Editor/development-only observation input source. It queues ordinary keyboard state into
    /// the Input System; PlayerInputReader, PlayerViewState, Flight, physics and canonical-frame
    /// publication remain the only runtime path that consumes it.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class PlayerObservationScenarioDriver : MonoBehaviour
    {
        public enum Scenario { Manual, Baseline, Horizontal, DirectionChange, Vertical, Composite }

        private static readonly Key[] None = Array.Empty<Key>();
        private static readonly Key[] Forward = { Key.W };
        private static readonly Key[] ForwardRight = { Key.W, Key.D };
        private static readonly Key[] ForwardLeft = { Key.W, Key.A };
        private static readonly Key[] ForwardUp = { Key.W, Key.R };
        private static readonly Key[] ForwardDown = { Key.W, Key.F };
        private static readonly Key[] ForwardRightUp = { Key.W, Key.D, Key.R };
        private static readonly Key[] ForwardLeftDown = { Key.W, Key.A, Key.F };

        [SerializeField] private Scenario scenario = Scenario.Manual;
        [SerializeField, Min(0.1f)] private float phaseDurationSeconds = 2f;
        [SerializeField, Min(0.1f)] private float observationDurationSeconds = 10f;

        private float startedAt;
        private bool started;

        public Scenario SelectedScenario => scenario;
        public float ElapsedSeconds => started ? Mathf.Max(0f, Time.time - startedAt) : 0f;
        public bool IsDriving => Application.isPlaying && scenario != Scenario.Manual && ElapsedSeconds < observationDurationSeconds;

        private void OnEnable() => started = false;

        private void Update()
        {
            if (!Application.isPlaying || (!Application.isEditor && !Debug.isDebugBuild)) return;
            if (!started) { startedAt = Time.time; started = true; }
            var keyboard = Keyboard.current;
            if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState(ResolveKeys(ElapsedSeconds)));
        }

        private Key[] ResolveKeys(float elapsed)
        {
            if (scenario == Scenario.Manual || elapsed >= observationDurationSeconds) return None;
            float phase = Mathf.Max(0.1f, phaseDurationSeconds);
            switch (scenario)
            {
                case Scenario.Baseline: return None;
                case Scenario.Horizontal: return Forward;
                case Scenario.DirectionChange: return elapsed < phase ? ForwardRight : elapsed < phase * 2f ? ForwardLeft : Forward;
                case Scenario.Vertical: return elapsed < phase ? ForwardUp : elapsed < phase * 2f ? ForwardDown : Forward;
                case Scenario.Composite:
                    if (elapsed < phase) return ForwardRightUp;
                    if (elapsed < phase * 2f) return ForwardRight;
                    if (elapsed < phase * 3f) return ForwardLeftDown;
                    if (elapsed < phase * 4f) return ForwardLeft;
                    return Forward;
                default: return None;
            }
        }
    }
}
