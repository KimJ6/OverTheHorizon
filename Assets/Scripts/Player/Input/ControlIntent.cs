using UnityEngine;

namespace OverTheHorizon.Player.Input
{
    /// <summary>
    /// Flight가 소비하는 현재 입력 스냅샷.
    /// 일반 이동은 Press/Release 이벤트가 아니라 이 현재 상태만을 진실값으로 사용한다.
    /// x = Lateral(A/D), y = Vertical(R/F), z = Axial(W/S).
    /// </summary>
    public readonly struct ControlIntent
    {
        public readonly float Axial;
        public readonly float Lateral;
        public readonly float Vertical;
        public readonly bool FreeLook;
        public readonly bool Maneuver;

        public ControlIntent(
            float axial,
            float lateral,
            float vertical,
            bool freeLook,
            bool maneuver)
        {
            Axial = Mathf.Clamp(axial, -1f, 1f);
            Lateral = Mathf.Clamp(lateral, -1f, 1f);
            Vertical = Mathf.Clamp(vertical, -1f, 1f);
            FreeLook = freeLook;
            Maneuver = maneuver;
        }

        public Vector3 MoveInput => new Vector3(Lateral, Vertical, Axial);

        public bool HasMoveInput(float deadZone = 0.01f)
            => Mathf.Abs(Axial) > deadZone
               || Mathf.Abs(Lateral) > deadZone
               || Mathf.Abs(Vertical) > deadZone;
    }
}
