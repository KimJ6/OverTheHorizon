using UnityEngine;

namespace OverTheHorizon.Player.View
{
    /// <summary>
    /// View-first TPS Camera.
    /// - Rotation = PlayerViewState 즉시 반영. SmoothDampAngle / LookAt 사용 금지.
    /// - Translation = Target Position을 느리게 따라가는 Anchor.
    /// - Screen catch-up은 Anchor translation 속도만 바꾼다.
    /// - FOV는 Flight 구현이 아니라 Rigidbody의 실제 속도를 읽는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerViewState viewState;
        [SerializeField] private Transform target;
        [SerializeField] private Rigidbody velocitySource;

        [Header("TPS Composition")]
        [SerializeField] private float followDistance = 9f;
        [SerializeField] private float targetHeight = 1.9f;
        [SerializeField] private float shoulderOffset = 0.35f;
        [SerializeField] private float viewVerticalOffset = 0.8f;

        [Header("Slow Translation")]
        [SerializeField] private float anchorFollowTime = 0.32f;
        [SerializeField] private float catchUpAnchorFollowTime = 0.075f;

        [Header("Screen-Space Translation Catch-Up")]
        [Range(0.05f, 0.6f)]
        [SerializeField] private float softDeadZoneRadius = 0.20f;
        [Range(0.1f, 0.8f)]
        [SerializeField] private float catchUpRadius = 0.43f;

        [Header("FOV")]
        [SerializeField] private float baseFov = 78f;
        [SerializeField] private float maxSpeedFovBonus = 12f;
        [SerializeField] private float speedForMaxFovBonus = 96f;
        [SerializeField] private float fovFollowTime = 0.15f;

        [Header("Safety")]
        [SerializeField] private float teleportSnapDistance = 45f;

        private Camera cameraComponent;
        private Vector3 smoothedAnchor;
        private Vector3 anchorVelocity;
        private float fovVelocity;
        private bool initialized;

        public Vector3 SmoothedAnchor => smoothedAnchor;

        private void Reset()
        {
            cameraComponent = GetComponent<Camera>();
        }

        private void Awake()
        {
            cameraComponent = GetComponent<Camera>();
            ResolveReferences();
        }

        private void Start()
        {
            SnapNow();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (viewState == null || target == null)
                return;

            if (!initialized)
            {
                SnapNow();
                if (!initialized)
                    return;
            }

            // Contract: Camera Rotation은 ViewState만 사용한다.
            Quaternion viewRotation = viewState.ViewRotation;
            transform.rotation = viewRotation;

            Vector3 targetAnchor = target.position + Vector3.up * targetHeight;
            UpdateSmoothedAnchor(targetAnchor);

            Vector3 viewForward = viewRotation * Vector3.forward;
            Vector3 viewRight = viewRotation * Vector3.right;
            Vector3 viewUp = viewRotation * Vector3.up;

            Vector3 desiredPosition = smoothedAnchor
                                      - viewForward * followDistance
                                      + viewRight * shoulderOffset
                                      + viewUp * viewVerticalOffset;

            transform.SetPositionAndRotation(desiredPosition, viewRotation);
            UpdateFov();
        }

        private void ResolveReferences()
        {
            if (target == null && velocitySource != null)
                target = velocitySource.transform;

            // 기존 Camera04가 FlightController에서 target을 간접 획득하던 경우를 위한 안전한 대체 경로.
            if (target == null && viewState != null)
                target = viewState.transform;

            if (velocitySource == null && target != null)
                velocitySource = target.GetComponent<Rigidbody>();
        }

        private void UpdateSmoothedAnchor(Vector3 targetAnchor)
        {
            float snapDistanceSq = teleportSnapDistance * teleportSnapDistance;
            if ((smoothedAnchor - targetAnchor).sqrMagnitude > snapDistanceSq)
            {
                smoothedAnchor = targetAnchor;
                anchorVelocity = Vector3.zero;
                return;
            }

            float catchUp01 = EvaluateScreenCatchUp(targetAnchor);
            float smoothTime = Mathf.Lerp(
                Mathf.Max(0.001f, anchorFollowTime),
                Mathf.Max(0.001f, catchUpAnchorFollowTime),
                catchUp01);

            smoothedAnchor = Vector3.SmoothDamp(
                smoothedAnchor,
                targetAnchor,
                ref anchorVelocity,
                smoothTime);
        }

        private float EvaluateScreenCatchUp(Vector3 targetPoint)
        {
            if (cameraComponent == null)
                return 0f;

            // Rotation은 이미 ViewState로 설정되어 있다.
            // 이 결과는 Anchor follow gain에만 사용한다.
            Vector3 viewport = cameraComponent.WorldToViewportPoint(targetPoint);
            if (viewport.z <= 0f)
                return 1f;

            Vector2 offset = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            float distance = offset.magnitude;
            float start = Mathf.Min(softDeadZoneRadius, catchUpRadius - 0.001f);

            return Mathf.InverseLerp(
                start,
                Mathf.Max(start + 0.001f, catchUpRadius),
                distance);
        }

        private void UpdateFov()
        {
            if (cameraComponent == null)
                return;

            float speed = velocitySource != null ? velocitySource.linearVelocity.magnitude : 0f;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, speedForMaxFovBonus));
            float targetFov = baseFov + speed01 * maxSpeedFovBonus;

            cameraComponent.fieldOfView = Mathf.SmoothDamp(
                cameraComponent.fieldOfView,
                targetFov,
                ref fovVelocity,
                Mathf.Max(0.001f, fovFollowTime));
        }

        public void SnapNow()
        {
            ResolveReferences();
            if (viewState == null || target == null)
                return;

            smoothedAnchor = target.position + Vector3.up * targetHeight;
            Quaternion viewRotation = viewState.ViewRotation;
            Vector3 desiredPosition = smoothedAnchor
                                      - (viewRotation * Vector3.forward) * followDistance
                                      + (viewRotation * Vector3.right) * shoulderOffset
                                      + (viewRotation * Vector3.up) * viewVerticalOffset;

            transform.SetPositionAndRotation(desiredPosition, viewRotation);

            if (cameraComponent != null)
                cameraComponent.fieldOfView = baseFov;

            anchorVelocity = Vector3.zero;
            fovVelocity = 0f;
            initialized = true;
        }
    }
}
