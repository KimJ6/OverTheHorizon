using UnityEngine;

namespace OverTheHorizon.Player.Flight
{
    public readonly struct GravitySupportStepResult
    {
        public readonly float Support;
        public readonly Vector3 UnopposedGravityDeltaV;
        public readonly Vector3 GravityRecoveryDeltaV;

        public GravitySupportStepResult(
            float support,
            Vector3 unopposedGravityDeltaV,
            Vector3 gravityRecoveryDeltaV)
        {
            Support = support;
            UnopposedGravityDeltaV = unopposedGravityDeltaV;
            GravityRecoveryDeltaV = gravityRecoveryDeltaV;
        }
    }

    /// <summary>
    /// Controller가 소유하는 GravityVelocity만 관리한다.
    /// ControlledVelocity나 충돌/넉백 성분을 수정하지 않는다.
    /// </summary>
    public static class GravitySupportSolver
    {
        public static GravitySupportStepResult Step(
            MotionState state,
            Vector3 gravityAcceleration,
            GravitySupportSettings settings,
            float deltaTime)
        {
            if (state == null)
                return default;

            float dt = Mathf.Max(0f, deltaTime);
            float authority = Mathf.Clamp01(state.PropulsionAuthority);
            float support = EvaluateSupport(authority, settings.SupportByPropulsionAuthority);

            Vector3 scaledGravity = gravityAcceleration * settings.GravityScale;
            Vector3 gravityDelta = scaledGravity * (1f - support) * dt;
            state.GravityVelocity += gravityDelta;

            Vector3 beforeRecovery = state.GravityVelocity;

            // 기존 낙하속도 복구는 새 추진 명령이 실제로 활성화된 경우에만 수행한다.
            // 무입력 coast 중에는 Support가 남아 있더라도 낙하속도를 되감지 않고,
            // (1-Support)만큼의 중력을 점진적으로 누적하여 glide/sink로 전환한다.
            if (state.CommandActive && support > 0f)
            {
                float recoveryStep = settings.GravityRecoveryAcceleration * support * dt;
                state.GravityVelocity = Vector3.MoveTowards(
                    state.GravityVelocity,
                    Vector3.zero,
                    recoveryStep);
            }

            Vector3 recoveryDelta = state.GravityVelocity - beforeRecovery;

            return new GravitySupportStepResult(
                support,
                gravityDelta,
                recoveryDelta);
        }

        private static float EvaluateSupport(float authority, AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
                return authority;

            return Mathf.Clamp01(curve.Evaluate(authority));
        }
    }
}
