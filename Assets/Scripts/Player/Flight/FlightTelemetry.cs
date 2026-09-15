using UnityEngine;

namespace OverTheHorizon.Player.Flight
{
    public enum FlightPhase
    {
        Powered,
        Coast,
        Glide
    }

    /// <summary>
    /// Mutable MotionState를 Diagnostics에 직접 노출하지 않기 위한 읽기 전용 Snapshot.
    /// </summary>
    public readonly struct MotionSnapshot
    {
        public readonly Vector3 ControlledDirection;
        public readonly float ControlledSpeed;
        public readonly Vector3 ControlledVelocity;
        public readonly Vector3 GravityVelocity;
        public readonly Vector3 ResultVelocity;
        public readonly float PropulsionAuthority;
        public readonly bool CommandActive;
        public readonly float RawCommandMagnitude;
        public readonly Vector3 RawCommand;
        public readonly Vector3 CommandDirection;

        public MotionSnapshot(MotionState state)
        {
            ControlledDirection = state != null ? state.ControlledDirection : Vector3.forward;
            ControlledSpeed = state != null ? state.ControlledSpeed : 0f;
            ControlledVelocity = state != null ? state.ControlledVelocity : Vector3.zero;
            GravityVelocity = state != null ? state.GravityVelocity : Vector3.zero;
            ResultVelocity = state != null ? state.Result : Vector3.zero;
            PropulsionAuthority = state != null ? state.PropulsionAuthority : 0f;
            CommandActive = state != null && state.CommandActive;
            RawCommandMagnitude = state != null ? state.RawCommandMagnitude : 0f;
            RawCommand = state != null ? state.RawCommand : Vector3.zero;
            CommandDirection = state != null ? state.CommandDirection : Vector3.forward;
        }
    }

    /// <summary>
    /// Flight runtime의 관측 전용 Snapshot.
    /// HUD/Visualizer는 MotionState/Solver를 직접 참조하지 않고 이 값만 읽는다.
    /// </summary>
    public readonly struct FlightTelemetry
    {
        public readonly FlightPhase Phase;
        public readonly MotionSnapshot Motion;

        public readonly Vector3 CameraForward;
        public readonly Vector3 SteeringForward;
        public readonly Vector3 SteeringRight;
        public readonly Vector3 BodyForward;
        public readonly Vector3 RigidbodyVelocity;
        public readonly Vector3 PreStepVelocityResidual;

        public readonly Vector3 ControlledDeltaV;
        public readonly Vector3 UnopposedGravityDeltaV;
        public readonly Vector3 GravityRecoveryDeltaV;

        public readonly float ActualSpeed;
        public readonly float MaxControlledSpeed;
        public readonly float GravitySupport;
        public readonly float SteeringBodySeparationDegrees;
        public readonly float SteeringMotionSeparationDegrees;
        public readonly float BodyMotionSeparationDegrees;
        public readonly bool FreeLookActive;
        public readonly bool CameraRecentering;
        public readonly bool ManeuverInputHeld;

        public FlightTelemetry(
            FlightPhase phase,
            MotionSnapshot motion,
            Vector3 cameraForward,
            Vector3 steeringForward,
            Vector3 steeringRight,
            Vector3 bodyForward,
            Vector3 rigidbodyVelocity,
            Vector3 preStepVelocityResidual,
            Vector3 controlledDeltaV,
            Vector3 unopposedGravityDeltaV,
            Vector3 gravityRecoveryDeltaV,
            float maxControlledSpeed,
            float gravitySupport,
            float steeringBodySeparationDegrees,
            bool freeLookActive,
            bool cameraRecentering,
            bool maneuverInputHeld)
        {
            Phase = phase;
            Motion = motion;
            CameraForward = cameraForward;
            SteeringForward = steeringForward;
            SteeringRight = steeringRight;
            BodyForward = bodyForward;
            RigidbodyVelocity = rigidbodyVelocity;
            PreStepVelocityResidual = preStepVelocityResidual;
            ControlledDeltaV = controlledDeltaV;
            UnopposedGravityDeltaV = unopposedGravityDeltaV;
            GravityRecoveryDeltaV = gravityRecoveryDeltaV;
            ActualSpeed = rigidbodyVelocity.magnitude;
            MaxControlledSpeed = maxControlledSpeed;
            GravitySupport = gravitySupport;
            SteeringBodySeparationDegrees = steeringBodySeparationDegrees;
            SteeringMotionSeparationDegrees = SafeAngle(steeringForward, motion.ControlledDirection);
            BodyMotionSeparationDegrees = SafeAngle(bodyForward, motion.ControlledDirection);
            FreeLookActive = freeLookActive;
            CameraRecentering = cameraRecentering;
            ManeuverInputHeld = maneuverInputHeld;
        }

        private static float SafeAngle(Vector3 a, Vector3 b)
        {
            if (a.sqrMagnitude < 0.000001f || b.sqrMagnitude < 0.000001f)
                return 0f;
            return Vector3.Angle(a, b);
        }
    }
}
