using UnityEngine;

namespace OverTheHorizon.Player.Flight
{
    /// <summary>
    /// Flight Core의 canonical runtime state.
    ///
    /// 과거 입력별 velocity ownership(P/L/V/R)을 보존하지 않는다.
    /// ControlledVelocity와 GravityVelocity를 명시적으로 분리하며,
    /// 현재 프로토타입에서 Rigidbody에 쓰는 system velocity는 두 성분의 합이다.
    ///
    /// 중요: 런타임 재초기화 시 실제 Rigidbody velocity 전체를 자동으로
    /// ControlledVelocity로 재분류하지 않는다. 속도 ownership은 호출자가 명시한다.
    /// </summary>
    public sealed class MotionState
    {
        public Vector3 ControlledDirection { get; internal set; } = Vector3.forward;
        public float ControlledSpeed { get; internal set; }
        public Vector3 ControlledVelocity { get; internal set; }

        public Vector3 GravityVelocity { get; internal set; }
        public float PropulsionAuthority { get; internal set; }

        public bool CommandActive { get; internal set; }
        public float RawCommandMagnitude { get; internal set; }
        public Vector3 RawCommand { get; internal set; }
        public Vector3 CommandDirection { get; internal set; }

        public Vector3 Result => ControlledVelocity + GravityVelocity;

        /// <summary>
        /// Flight state를 명시적인 ownership으로 초기화한다.
        /// initialControlledVelocity와 initialGravityVelocity를 호출자가 분리해서 전달해야 한다.
        /// </summary>
        public void Initialize(
            Vector3 initialControlledVelocity,
            Vector3 initialGravityVelocity,
            Vector3 fallbackDirection)
        {
            Vector3 fallback = fallbackDirection.sqrMagnitude > 0.000001f
                ? fallbackDirection.normalized
                : Vector3.forward;

            if (initialControlledVelocity.sqrMagnitude > 0.000001f)
            {
                ControlledDirection = initialControlledVelocity.normalized;
                ControlledSpeed = initialControlledVelocity.magnitude;
                ControlledVelocity = initialControlledVelocity;
            }
            else
            {
                ControlledDirection = fallback;
                ControlledSpeed = 0f;
                ControlledVelocity = Vector3.zero;
            }

            GravityVelocity = initialGravityVelocity;
            PropulsionAuthority = 0f;
            CommandActive = false;
            RawCommandMagnitude = 0f;
            RawCommand = Vector3.zero;
            CommandDirection = fallback;
        }
    }
}
