using UnityEngine;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.Player.Flight
{
    public readonly struct FlightMotionStepResult
    {
        public readonly Vector3 RawCommand;
        public readonly Vector3 CommandDirection;
        public readonly bool CommandActive;
        public readonly Vector3 ControlledDeltaV;
        public readonly float PreviousControlledSpeed;
        public readonly float CurrentControlledSpeed;
        public readonly float PreviousPropulsionAuthority;
        public readonly float CurrentPropulsionAuthority;

        public FlightMotionStepResult(
            Vector3 rawCommand,
            Vector3 commandDirection,
            bool commandActive,
            Vector3 controlledDeltaV,
            float previousControlledSpeed,
            float currentControlledSpeed,
            float previousPropulsionAuthority,
            float currentPropulsionAuthority)
        {
            RawCommand = rawCommand;
            CommandDirection = commandDirection;
            CommandActive = commandActive;
            ControlledDeltaV = controlledDeltaV;
            PreviousControlledSpeed = previousControlledSpeed;
            CurrentControlledSpeed = currentControlledSpeed;
            PreviousPropulsionAuthority = previousPropulsionAuthority;
            CurrentPropulsionAuthority = currentPropulsionAuthority;
        }
    }

    /// <summary>
    /// WASDRF 현재 상태와 SteeringReference만으로 Controlled Motion을 계산한다.
    /// 핵심 불변조건:
    /// 1) CommandActive 동안 새 ControlledDirection 계산에 과거 MotionDirection을 사용하지 않는다.
    /// 2) BodyFacing을 방향 계산에 사용하지 않는다.
    /// 3) 복합 입력은 방향만 합성/정규화하고 가속/최대속력은 입력 개수와 무관하다.
    /// </summary>
    public sealed class FlightMotionSolver
    {
        public FlightMotionStepResult Step(
            MotionState state,
            ControlIntent intent,
            Vector3 steeringForward,
            Vector3 steeringRight,
            Vector3 worldUp,
            MotionSettings settings,
            float deltaTime)
        {
            if (state == null)
                return default;

            float dt = Mathf.Max(0f, deltaTime);

            Vector3 forward = SafeNormalized(steeringForward, Vector3.forward);
            Vector3 right = SafeNormalized(steeringRight, Vector3.right);
            Vector3 up = SafeNormalized(worldUp, Vector3.up);

            float axial = ApplyDeadZone(intent.Axial, settings.InputAxisDeadZone);
            float lateral = ApplyDeadZone(intent.Lateral, settings.InputAxisDeadZone);
            float vertical = ApplyDeadZone(intent.Vertical, settings.InputAxisDeadZone);

            Vector3 rawCommand = forward * axial
                                 + right * lateral
                                 + up * vertical;
            float rawMagnitude = rawCommand.magnitude;

            float commandThreshold = state.CommandActive
                ? settings.CommandDeactivateThreshold
                : settings.CommandActivateThreshold;

            bool anyAxisActive = Mathf.Abs(axial) > 0f
                                 || Mathf.Abs(lateral) > 0f
                                 || Mathf.Abs(vertical) > 0f;
            bool commandActive = anyAxisActive && rawMagnitude >= commandThreshold;

            Vector3 commandDirection = commandActive
                ? rawCommand / Mathf.Max(rawMagnitude, 0.000001f)
                : state.CommandDirection;

            Vector3 previousControlledVelocity = state.ControlledVelocity;
            float previousSpeed = state.ControlledSpeed;
            float previousAuthority = state.PropulsionAuthority;

            if (commandActive)
            {
                // 중요: 과거 방향을 보간하거나 합성하지 않는다.
                // A -> W라면 이 FixedStep부터 W 방향이 곧 ControlledDirection이다.
                state.ControlledDirection = commandDirection;
                state.ControlledSpeed = Mathf.MoveTowards(
                    state.ControlledSpeed,
                    settings.MaxControlledSpeed,
                    settings.MoveAcceleration * dt);
                state.PropulsionAuthority = 1f;
            }
            else
            {
                // 무입력에서는 방향 관성만 유지하고 속력/추력 여유를 자연 감쇠한다.
                state.ControlledSpeed = Mathf.MoveTowards(
                    state.ControlledSpeed,
                    0f,
                    settings.CoastDeceleration * dt);
                state.PropulsionAuthority = Mathf.MoveTowards(
                    state.PropulsionAuthority,
                    0f,
                    settings.PropulsionAuthorityReleaseRate * dt);
            }

            if (state.ControlledDirection.sqrMagnitude < 0.000001f)
                state.ControlledDirection = forward;
            else
                state.ControlledDirection.Normalize();

            state.ControlledVelocity = state.ControlledDirection * state.ControlledSpeed;
            state.CommandActive = commandActive;
            state.RawCommandMagnitude = rawMagnitude;
            state.RawCommand = rawCommand;
            state.CommandDirection = commandActive
                ? commandDirection
                : state.CommandDirection;

            return new FlightMotionStepResult(
                rawCommand,
                state.CommandDirection,
                commandActive,
                state.ControlledVelocity - previousControlledVelocity,
                previousSpeed,
                state.ControlledSpeed,
                previousAuthority,
                state.PropulsionAuthority);
        }

        private static float ApplyDeadZone(float value, float deadZone)
        {
            if (Mathf.Abs(value) <= deadZone)
                return 0f;

            return Mathf.Clamp(value, -1f, 1f);
        }

        private static Vector3 SafeNormalized(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude > 0.000001f)
                return value.normalized;

            if (fallback.sqrMagnitude > 0.000001f)
                return fallback.normalized;

            return Vector3.forward;
        }
    }
}
