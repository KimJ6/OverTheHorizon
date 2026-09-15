using UnityEngine;

namespace OverTheHorizon.Player.Flight
{
    public readonly struct AttitudeResult
    {
        public readonly Quaternion Rotation;
        public readonly Vector3 Forward;

        /// <summary>
        /// 이번 Solve가 적용된 뒤의 Body-Steering 각도 오차.
        /// Telemetry의 BodyForward와 같은 post-step 시점을 사용한다.
        /// </summary>
        public readonly float SeparationDegrees;

        public AttitudeResult(
            Quaternion rotation,
            Vector3 forward,
            float separationDegrees)
        {
            Rotation = rotation;
            Forward = forward;
            SeparationDegrees = separationDegrees;
        }
    }

    /// <summary>
    /// SteeringReference를 실제 Body rotation으로 변환하는 단순 비오버슈트 추종.
    /// 프로토타입에서는 Body-Steering 간극이 커져도 catch-up multiplier를 추가하지 않는다.
    /// Rigidbody를 직접 수정하지 않는다.
    /// </summary>
    public static class AttitudeSolver
    {
        public static AttitudeResult Solve(
            Quaternion current,
            Quaternion desired,
            AttitudeSettings settings,
            float deltaTime)
        {
            float maxDegrees = settings.TurnSpeedDegPerSec * Mathf.Max(0f, deltaTime);
            Quaternion next = Quaternion.RotateTowards(current, desired, maxDegrees);
            Vector3 forward = (next * Vector3.forward).normalized;

            // Diagnostics는 실제로 이번 step에 적용할 next rotation 기준으로 기록한다.
            // 기존 구현은 pre-step angle과 post-step BodyForward를 섞어 HUD가 한 step 어긋났다.
            float postStepSeparation = Quaternion.Angle(next, desired);

            return new AttitudeResult(next, forward, postStepSeparation);
        }
    }
}
