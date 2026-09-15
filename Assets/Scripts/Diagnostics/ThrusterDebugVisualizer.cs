using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using OverTheHorizon.Player.Flight;

namespace OverTheHorizon.Diagnostics
{
    /// <summary>
    /// 새 Flight Core의 방향 상태를 시각화하는 개발용 도구.
    /// Steering / Body / Command / Controlled / Gravity / Actual 벡터를 분리해서 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThrusterDebugVisualizer : MonoBehaviour
    {
        [SerializeField] private PlayerFlightController controller;

        [Header("Debug Thruster Geometry")]
        [SerializeField] private bool autoCreateGeometry = true;
        [SerializeField] private Vector3 leftLocalPosition = new Vector3(-0.7f, -0.25f, -0.55f);
        [SerializeField] private Vector3 rightLocalPosition = new Vector3(0.7f, -0.25f, -0.55f);
        [SerializeField] private float nozzleRadius = 0.14f;
        [SerializeField] private float nozzleLength = 0.55f;
        [SerializeField] private float nozzleRotationResponse = 18f;

        [Header("Vector Lines")]
        [SerializeField] private bool drawVectorLines = true;
        [SerializeField] private float basisVectorLength = 4f;
        [SerializeField] private float commandVectorLength = 5f;
        [SerializeField] private float velocityScale = 0.10f;
        [SerializeField] private float deltaVScale = 4f;
        [SerializeField] private float lineWidth = 0.045f;

        [Header("Colors")]
        [SerializeField] private Color cameraColor = new Color(0.95f, 0.95f, 1f, 1f);
        [SerializeField] private Color steeringColor = new Color(0.72f, 0.35f, 1f, 1f);
        [SerializeField] private Color bodyColor = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField] private Color rawCommandColor = new Color(1f, 0.25f, 0.85f, 1f);
        [SerializeField] private Color controlledColor = new Color(0.1f, 0.9f, 1f, 1f);
        [SerializeField] private Color gravityColor = new Color(0.35f, 0.55f, 1f, 1f);
        [SerializeField] private Color actualColor = new Color(0.3f, 1f, 0.35f, 1f);
        [SerializeField] private Color controlledDeltaColor = new Color(1f, 0.3f, 0.12f, 1f);

        private Transform leftPivot;
        private Transform rightPivot;
        private Material debugMaterial;
        private readonly List<Material> ownedMaterials = new List<Material>(2);

        private LineRenderer cameraLine;
        private LineRenderer steeringLine;
        private LineRenderer bodyLine;
        private LineRenderer rawCommandLine;
        private LineRenderer controlledLine;
        private LineRenderer gravityLine;
        private LineRenderer actualLine;
        private LineRenderer controlledDeltaLine;

        private void Reset()
        {
            controller = GetComponent<PlayerFlightController>();
        }

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<PlayerFlightController>();

            CreateSharedMaterial();

            if (autoCreateGeometry)
                EnsureManipulatorGeometry();
            if (drawVectorLines)
                EnsureVectorLines();
        }

        private void LateUpdate()
        {
            if (controller == null)
                return;

            FlightTelemetry telemetry = controller.Telemetry;
            UpdateNozzles(telemetry);
            if (drawVectorLines)
                UpdateVectorLines(telemetry);
        }

        private void CreateSharedMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return;

            debugMaterial = new Material(shader)
            {
                name = "Flight Debug Vector Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void EnsureManipulatorGeometry()
        {
            if (leftPivot == null)
                leftPivot = CreateNozzle("LeftThruster", leftLocalPosition);
            if (rightPivot == null)
                rightPivot = CreateNozzle("RightThruster", rightLocalPosition);
        }

        private Transform CreateNozzle(string objectName, Vector3 localPosition)
        {
            GameObject pivot = new GameObject(objectName + "_Pivot");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = localPosition;

            GameObject nozzle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nozzle.name = objectName + "_Nozzle";
            nozzle.transform.SetParent(pivot.transform, false);
            nozzle.transform.localPosition = Vector3.zero;
            nozzle.transform.localScale = new Vector3(
                nozzleRadius * 2f,
                nozzleLength * 0.5f,
                nozzleRadius * 2f);

            Collider collider = nozzle.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = nozzle.GetComponent<Renderer>();
            if (renderer != null && debugMaterial != null)
            {
                Material material = new Material(debugMaterial)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                SetMaterialColor(material, new Color(0.65f, 0.68f, 0.72f, 1f));
                renderer.material = material;
                ownedMaterials.Add(material);
            }

            return pivot.transform;
        }

        private void UpdateNozzles(FlightTelemetry telemetry)
        {
            if (leftPivot == null || rightPivot == null)
                return;

            Vector3 supportDirection = Physics.gravity.sqrMagnitude > 0.000001f
                ? -Physics.gravity.normalized * telemetry.GravitySupport
                : Vector3.zero;
            Vector3 commandDirection = telemetry.Motion.CommandActive
                ? telemetry.Motion.CommandDirection
                : Vector3.zero;

            Vector3 conceptualThrust = commandDirection + supportDirection;
            Vector3 thrustDirection = conceptualThrust.sqrMagnitude > 0.000001f
                ? conceptualThrust.normalized
                : telemetry.SteeringForward;

            Vector3 exhaustLocal = transform.InverseTransformDirection(-thrustDirection);
            if (exhaustLocal.sqrMagnitude < 0.000001f)
                return;

            Quaternion targetLocalRotation = Quaternion.FromToRotation(
                Vector3.up,
                exhaustLocal.normalized);
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, nozzleRotationResponse) * Time.deltaTime);

            leftPivot.localRotation = Quaternion.Slerp(
                leftPivot.localRotation,
                targetLocalRotation,
                t);
            rightPivot.localRotation = Quaternion.Slerp(
                rightPivot.localRotation,
                targetLocalRotation,
                t);
        }

        private void EnsureVectorLines()
        {
            cameraLine = CreateLine("CameraForward", cameraColor);
            steeringLine = CreateLine("SteeringForward", steeringColor);
            bodyLine = CreateLine("BodyForward", bodyColor);
            rawCommandLine = CreateLine("RawCommand", rawCommandColor);
            controlledLine = CreateLine("ControlledVelocity", controlledColor);
            gravityLine = CreateLine("GravityVelocity", gravityColor);
            actualLine = CreateLine("ActualVelocity", actualColor);
            controlledDeltaLine = CreateLine("ControlledDeltaV", controlledDeltaColor);
        }

        private LineRenderer CreateLine(string objectName, Color color)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (debugMaterial != null)
                line.material = debugMaterial;
            return line;
        }

        private void UpdateVectorLines(FlightTelemetry telemetry)
        {
            Vector3 origin = transform.position;

            SetLine(cameraLine, origin, telemetry.CameraForward * basisVectorLength);
            SetLine(steeringLine, origin, telemetry.SteeringForward * basisVectorLength);
            SetLine(bodyLine, origin, telemetry.BodyForward * basisVectorLength);
            SetLine(rawCommandLine, origin, telemetry.Motion.RawCommand * commandVectorLength);
            SetLine(controlledLine, origin, telemetry.Motion.ControlledVelocity * velocityScale);
            SetLine(gravityLine, origin, telemetry.Motion.GravityVelocity * velocityScale);
            SetLine(actualLine, origin, telemetry.RigidbodyVelocity * velocityScale);
            SetLine(controlledDeltaLine, origin, telemetry.ControlledDeltaV * deltaVScale);
        }

        private static void SetLine(LineRenderer line, Vector3 origin, Vector3 vector)
        {
            if (line == null)
                return;

            bool visible = vector.sqrMagnitude > 0.00001f;
            line.enabled = visible;
            if (!visible)
                return;

            line.SetPosition(0, origin);
            line.SetPosition(1, origin + vector);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < ownedMaterials.Count; i++)
            {
                if (ownedMaterials[i] != null)
                    Destroy(ownedMaterials[i]);
            }
            ownedMaterials.Clear();

            if (debugMaterial != null)
                Destroy(debugMaterial);
        }
    }
}
