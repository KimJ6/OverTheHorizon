using UnityEngine;

namespace OverTheHorizon.Player.Input
{
    /// <summary>
    /// 한 축의 이전/현재 입력 관계.
    /// Retargeted는 A->D, W->S처럼 0을 거치지 않는 부호 반전을 하나의 사건으로 표현한다.
    /// </summary>
    public enum AxisTransition
    {
        None,
        Pressed,
        Held,
        Released,
        Retargeted
    }

    /// <summary>
    /// ControlIntent 두 스냅샷으로부터 계산되는 순수 값 타입.
    /// Flight 수학과 Input System API를 분리한다.
    /// </summary>
    public readonly struct InputTransition
    {
        public readonly Vector3 PreviousMove;
        public readonly Vector3 CurrentMove;

        public readonly AxisTransition Axial;
        public readonly AxisTransition Lateral;
        public readonly AxisTransition Vertical;

        public readonly bool HadMoveInput;
        public readonly bool HasMoveInput;
        public readonly bool AllReleased;

        /// <summary>
        /// 새 Press 또는 Sign Flip이 발생한 축만 -1/0/+1로 기록한다.
        /// 새 Press 또는 Sign Flip 축만 추출한다.
        /// </summary>
        public readonly Vector3 NewlyActivated;

        private InputTransition(
            Vector3 previousMove,
            Vector3 currentMove,
            AxisTransition axial,
            AxisTransition lateral,
            AxisTransition vertical,
            bool hadMoveInput,
            bool hasMoveInput,
            bool allReleased,
            Vector3 newlyActivated)
        {
            PreviousMove = previousMove;
            CurrentMove = currentMove;
            Axial = axial;
            Lateral = lateral;
            Vertical = vertical;
            HadMoveInput = hadMoveInput;
            HasMoveInput = hasMoveInput;
            AllReleased = allReleased;
            NewlyActivated = newlyActivated;
        }

        public static InputTransition Resolve(
            ControlIntent previous,
            ControlIntent current,
            float deadZone)
        {
            float dz = Mathf.Max(0.0001f, deadZone);
            Vector3 previousMove = Quantize(previous.MoveInput, dz);
            Vector3 currentMove = Quantize(current.MoveInput, dz);

            AxisTransition lateral = ResolveAxis(previousMove.x, currentMove.x, dz);
            AxisTransition vertical = ResolveAxis(previousMove.y, currentMove.y, dz);
            AxisTransition axial = ResolveAxis(previousMove.z, currentMove.z, dz);

            bool hadMoveInput = HasMove(previousMove, dz);
            bool hasMoveInput = HasMove(currentMove, dz);

            Vector3 newlyActivated = new Vector3(
                NewlyActivatedSign(lateral, currentMove.x, dz),
                NewlyActivatedSign(vertical, currentMove.y, dz),
                NewlyActivatedSign(axial, currentMove.z, dz));

            return new InputTransition(
                previousMove,
                currentMove,
                axial,
                lateral,
                vertical,
                hadMoveInput,
                hasMoveInput,
                hadMoveInput && !hasMoveInput,
                newlyActivated);
        }

        private static AxisTransition ResolveAxis(float previous, float current, float deadZone)
        {
            int previousSign = Sign(previous, deadZone);
            int currentSign = Sign(current, deadZone);

            if (previousSign == 0 && currentSign == 0)
                return AxisTransition.None;
            if (previousSign == 0)
                return AxisTransition.Pressed;
            if (currentSign == 0)
                return AxisTransition.Released;
            if (previousSign != currentSign)
                return AxisTransition.Retargeted;

            return AxisTransition.Held;
        }

        private static float NewlyActivatedSign(
            AxisTransition transition,
            float current,
            float deadZone)
        {
            if (transition != AxisTransition.Pressed
                && transition != AxisTransition.Retargeted)
            {
                return 0f;
            }

            return Sign(current, deadZone);
        }

        private static Vector3 Quantize(Vector3 raw, float deadZone)
        {
            return new Vector3(
                Mathf.Abs(raw.x) > deadZone ? Mathf.Clamp(raw.x, -1f, 1f) : 0f,
                Mathf.Abs(raw.y) > deadZone ? Mathf.Clamp(raw.y, -1f, 1f) : 0f,
                Mathf.Abs(raw.z) > deadZone ? Mathf.Clamp(raw.z, -1f, 1f) : 0f);
        }

        private static int Sign(float value, float deadZone)
        {
            if (value > deadZone)
                return 1;
            if (value < -deadZone)
                return -1;
            return 0;
        }

        private static bool HasMove(Vector3 move, float deadZone)
            => move.sqrMagnitude > deadZone * deadZone;
    }
}
