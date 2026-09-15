using UnityEngine;

namespace OverTheHorizon.Player.Flight
{
    /// <summary>
    /// PlayerFlightController의 직렬화 튜닝값을 순수 Solver에 전달하는 런타임 Snapshot.
    /// </summary>
    public readonly struct FlightSettings
    {
        public readonly AttitudeSettings Attitude;
        public readonly MotionSettings Motion;
        public readonly GravitySupportSettings Gravity;

        public FlightSettings(
            AttitudeSettings attitude,
            MotionSettings motion,
            GravitySupportSettings gravity)
        {
            Attitude = attitude;
            Motion = motion;
            Gravity = gravity;
        }
    }

    public readonly struct AttitudeSettings
    {
        public readonly float TurnSpeedDegPerSec;
        public readonly bool ClearAngularVelocityWhileControlled;

        public AttitudeSettings(
            float turnSpeedDegPerSec,
            bool clearAngularVelocityWhileControlled)
        {
            TurnSpeedDegPerSec = Mathf.Max(0f, turnSpeedDegPerSec);
            ClearAngularVelocityWhileControlled = clearAngularVelocityWhileControlled;
        }
    }

    public readonly struct MotionSettings
    {
        public readonly float MaxControlledSpeed;
        public readonly float MoveAcceleration;
        public readonly float CoastDeceleration;
        public readonly float InputAxisDeadZone;
        public readonly float CommandActivateThreshold;
        public readonly float CommandDeactivateThreshold;
        public readonly float PropulsionAuthorityReleaseRate;

        public MotionSettings(
            float maxControlledSpeed,
            float moveAcceleration,
            float coastDeceleration,
            float inputAxisDeadZone,
            float commandActivateThreshold,
            float commandDeactivateThreshold,
            float propulsionAuthorityReleaseRate)
        {
            MaxControlledSpeed = Mathf.Max(0f, maxControlledSpeed);
            MoveAcceleration = Mathf.Max(0f, moveAcceleration);
            CoastDeceleration = Mathf.Max(0f, coastDeceleration);
            InputAxisDeadZone = Mathf.Max(0f, inputAxisDeadZone);

            float activate = Mathf.Max(0.0001f, commandActivateThreshold);
            float deactivate = Mathf.Clamp(commandDeactivateThreshold, 0f, activate);
            CommandActivateThreshold = activate;
            CommandDeactivateThreshold = deactivate;

            PropulsionAuthorityReleaseRate = Mathf.Max(0f, propulsionAuthorityReleaseRate);
        }
    }

    public readonly struct GravitySupportSettings
    {
        public readonly float GravityScale;
        public readonly float GravityRecoveryAcceleration;
        public readonly AnimationCurve SupportByPropulsionAuthority;

        public GravitySupportSettings(
            float gravityScale,
            float gravityRecoveryAcceleration,
            AnimationCurve supportByPropulsionAuthority)
        {
            GravityScale = Mathf.Max(0f, gravityScale);
            GravityRecoveryAcceleration = Mathf.Max(0f, gravityRecoveryAcceleration);
            SupportByPropulsionAuthority = supportByPropulsionAuthority;
        }
    }
}
