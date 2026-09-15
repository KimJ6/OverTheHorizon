using System.Text;
using UnityEngine;
using OverTheHorizon.Player.Flight;

namespace OverTheHorizon.Diagnostics
{
    /// <summary>
    /// 새 FlightTelemetry를 표시하는 개발용 HUD.
    /// A->W 관성 오염, Gravity channel, Steering-Body 간극을 즉시 확인하기 위한 계측 중심 구성.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightDebugHUD : MonoBehaviour
    {
        [SerializeField] private PlayerFlightController controller;
        [SerializeField] private Vector2 screenOffset = new Vector2(12f, 12f);
        [SerializeField] private int fontSize = 14;

        private GUIStyle style;
        private readonly StringBuilder builder = new StringBuilder(1800);

        private void Reset()
        {
            controller = GetComponent<PlayerFlightController>();
        }

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<PlayerFlightController>();
        }

        private void OnGUI()
        {
            if (controller == null)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = fontSize,
                    richText = false,
                    padding = new RectOffset(8, 8, 6, 6)
                };
            }

            FlightTelemetry t = controller.Telemetry;

            builder.Clear();
            builder.Append("Flight - Direction / Speed / Gravity\n");
            builder.Append("Phase                     : ").Append(t.Phase).Append('\n');
            builder.Append("Command Active            : ").Append(t.Motion.CommandActive ? "YES" : "NO").Append('\n');
            builder.Append("Raw Command Magnitude     : ").Append(t.Motion.RawCommandMagnitude.ToString("0.000")).Append('\n');
            builder.Append("Controlled Speed          : ").Append(t.Motion.ControlledSpeed.ToString("0.0")).Append(" / ")
                .Append(t.MaxControlledSpeed.ToString("0.0")).Append(" m/s\n");
            builder.Append("Actual Speed              : ").Append(t.ActualSpeed.ToString("0.0")).Append(" m/s\n");
            builder.Append("Propulsion Authority      : ").Append(t.Motion.PropulsionAuthority.ToString("0.000")).Append('\n');
            builder.Append("Gravity Support           : ").Append(t.GravitySupport.ToString("0.000")).Append('\n');
            builder.Append("FreeLook                  : ").Append(t.FreeLookActive ? "HELD" : "OFF").Append('\n');
            builder.Append("Camera Recentering        : ").Append(t.CameraRecentering ? "YES" : "NO").Append('\n');
            builder.Append("Shift/Maneuver (Reserved) : ").Append(t.ManeuverInputHeld ? "HELD" : "OFF").Append('\n');
            builder.Append('\n');

            builder.Append("Camera Forward             : ").AppendLine(Format(t.CameraForward));
            builder.Append("Steering Forward           : ").AppendLine(Format(t.SteeringForward));
            builder.Append("Steering Right             : ").AppendLine(Format(t.SteeringRight));
            builder.Append("Body Forward               : ").AppendLine(Format(t.BodyForward));
            builder.Append("Raw Command                : ").AppendLine(Format(t.Motion.RawCommand));
            builder.Append("Command Direction          : ").AppendLine(Format(t.Motion.CommandDirection));
            builder.Append("Controlled Direction       : ").AppendLine(Format(t.Motion.ControlledDirection));
            builder.Append('\n');

            builder.Append("Controlled Velocity        : ").AppendLine(Format(t.Motion.ControlledVelocity));
            builder.Append("Gravity Velocity           : ").AppendLine(Format(t.Motion.GravityVelocity));
            builder.Append("Expected Result Velocity   : ").AppendLine(Format(t.Motion.ResultVelocity));
            builder.Append("Rigidbody Velocity         : ").AppendLine(Format(t.RigidbodyVelocity));
            builder.Append("Pre-Step Residual          : ").AppendLine(Format(t.PreStepVelocityResidual));
            builder.Append('\n');

            builder.Append("Controlled dV              : ").AppendLine(Format(t.ControlledDeltaV));
            builder.Append("Unopposed Gravity dV       : ").AppendLine(Format(t.UnopposedGravityDeltaV));
            builder.Append("Gravity Recovery dV        : ").AppendLine(Format(t.GravityRecoveryDeltaV));
            builder.Append('\n');

            builder.Append("Steering-Body Separation   : ")
                .Append(t.SteeringBodySeparationDegrees.ToString("0.0")).Append(" deg\n");
            builder.Append("Steering-Motion Separation : ")
                .Append(t.SteeringMotionSeparationDegrees.ToString("0.0")).Append(" deg\n");
            builder.Append("Body-Motion Separation     : ")
                .Append(t.BodyMotionSeparationDegrees.ToString("0.0")).Append(" deg\n");

            string text = builder.ToString();
            Vector2 size = style.CalcSize(new GUIContent(text));
            size.x = Mathf.Max(size.x, 610f);

            GUI.Box(
                new Rect(screenOffset.x, screenOffset.y, size.x + 16f, size.y + 12f),
                text,
                style);
        }

        private static string Format(Vector3 value)
            => $"({value.x,8:0.000}, {value.y,8:0.000}, {value.z,8:0.000})";
    }
}
