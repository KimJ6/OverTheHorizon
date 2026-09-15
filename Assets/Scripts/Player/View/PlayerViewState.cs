using UnityEngine;
using OverTheHorizon.Player.Input;

namespace OverTheHorizon.Player.View
{
    /// <summary>
    /// SteeringReference와 CameraView를 분리한다.
    /// - 평상시: Mouse가 SteeringReference를 즉시 갱신하고 CameraView는 동일 방향을 사용한다.
    /// - Alt Freelook: SteeringReference는 고정되고 Mouse는 CameraView만 갱신한다.
    /// - Alt Release: SteeringReference는 유지되고 CameraView만 빠르게 복귀한다.
    /// Body는 Flight 계층에서 SteeringReference를 유한한 속도로 추종한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerViewState : MonoBehaviour
    {
        private const float YawWrapThreshold = 3600f;

        [Header("Input")]
        [SerializeField] private PlayerInputReader inputReader;

        [Header("Mouse Steering")]
        [SerializeField] private float mouseDegreesPerPixel = 0.085f;
        [SerializeField] private bool invertMouseY = false;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        [Header("Alt Freelook")]
        [Tooltip("Alt 해제 후 CameraView가 SteeringReference로 복귀하는 최대 각속도.")]
        [SerializeField] private float cameraRecenterDegPerSec = 1080f;
        [SerializeField] private float recenterSnapEpsilonDegrees = 0.05f;

        private float steeringYaw;
        private float steeringPitch;
        private float cameraYaw;
        private float cameraPitch;
        private bool freeLookHeld;
        private bool recenteringCamera;
        private bool initialized;

        public float SteeringYaw => steeringYaw;
        public float SteeringPitch => steeringPitch;
        public float ViewYaw => cameraYaw;
        public float ViewPitch => cameraPitch;

        public Quaternion SteeringRotation => Quaternion.Euler(steeringPitch, steeringYaw, 0f);
        public Quaternion ViewRotation => Quaternion.Euler(cameraPitch, cameraYaw, 0f);

        public Vector3 SteeringForward => (SteeringRotation * Vector3.forward).normalized;

        /// <summary>
        /// A/D의 기준축. Pitch 극한에서도 안정적으로 수평 좌우를 유지하기 위해 Yaw만 사용한다.
        /// </summary>
        public Vector3 SteeringRight
            => (Quaternion.Euler(0f, steeringYaw, 0f) * Vector3.right).normalized;

        public Vector3 ViewForward => (ViewRotation * Vector3.forward).normalized;
        public Vector3 ViewRight => (ViewRotation * Vector3.right).normalized;

        // 기존 Camera/외부 코드 호환용 별칭.
        public Vector3 ControlRight => SteeringRight;
        public Quaternion DesiredBodyFacing => SteeringRotation;

        public bool IsFreeLook => freeLookHeld;
        public bool IsCameraRecentering => recenteringCamera;
        public bool IsInitialized => initialized;

        private void Reset()
        {
            inputReader = GetComponent<PlayerInputReader>();
        }

        private void Awake()
        {
            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();

            InitializeFromTransform(transform);
        }

        private void Update()
        {
            if (inputReader == null)
                return;

            Vector2 look = inputReader.ReadLookDelta();
            bool freeLookNow = inputReader.ReadFreeLookHeld();

            float yawDelta = look.x * mouseDegreesPerPixel;
            float pitchSign = invertMouseY ? 1f : -1f;
            float pitchDelta = look.y * mouseDegreesPerPixel * pitchSign;

            if (freeLookNow)
            {
                cameraYaw += yawDelta;
                cameraPitch = ClampPitch(cameraPitch + pitchDelta);
                recenteringCamera = false;
            }
            else
            {
                if (freeLookHeld)
                    recenteringCamera = true;

                steeringYaw += yawDelta;
                steeringPitch = ClampPitch(steeringPitch + pitchDelta);

                if (recenteringCamera)
                {
                    float maxStep = Mathf.Max(0f, cameraRecenterDegPerSec) * Time.deltaTime;
                    cameraYaw = Mathf.MoveTowardsAngle(cameraYaw, steeringYaw, maxStep);
                    cameraPitch = Mathf.MoveTowards(cameraPitch, steeringPitch, maxStep);

                    float yawError = Mathf.Abs(Mathf.DeltaAngle(cameraYaw, steeringYaw));
                    float pitchError = Mathf.Abs(cameraPitch - steeringPitch);
                    float epsilon = Mathf.Max(0.001f, recenterSnapEpsilonDegrees);

                    if (yawError <= epsilon && pitchError <= epsilon)
                    {
                        cameraYaw = steeringYaw;
                        cameraPitch = steeringPitch;
                        recenteringCamera = false;
                    }
                }
                else
                {
                    // 일반 조향에서는 Camera가 SteeringReference를 즉시 표현한다.
                    cameraYaw = steeringYaw;
                    cameraPitch = steeringPitch;
                }
            }

            // Yaw를 무한 누적하면 장시간 세션에서 float 정밀도가 서서히 떨어질 수 있다.
            // 매 frame 강제 wrap하지 않고 충분히 큰 값에서만 정규화해 디버그 값의 불필요한 점프를 줄인다.
            NormalizeYawIfNeeded();

            freeLookHeld = freeLookNow;
        }

        public void InitializeFromTransform(Transform source)
        {
            if (source == null)
                return;

            Vector3 forward = source.forward.normalized;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            steeringYaw = yaw;
            steeringPitch = ClampPitch(pitch);
            cameraYaw = steeringYaw;
            cameraPitch = steeringPitch;
            freeLookHeld = false;
            recenteringCamera = false;
            initialized = true;
        }

        /// <summary>
        /// SteeringReference와 CameraView를 동시에 재설정한다.
        /// </summary>
        public void SetView(float yawDegrees, float pitchDegrees)
        {
            steeringYaw = WrapAngle180(yawDegrees);
            steeringPitch = ClampPitch(pitchDegrees);
            cameraYaw = steeringYaw;
            cameraPitch = steeringPitch;
            freeLookHeld = false;
            recenteringCamera = false;
            initialized = true;
        }

        private void NormalizeYawIfNeeded()
        {
            if (Mathf.Abs(steeringYaw) > YawWrapThreshold)
                steeringYaw = WrapAngle180(steeringYaw);

            if (Mathf.Abs(cameraYaw) > YawWrapThreshold)
                cameraYaw = WrapAngle180(cameraYaw);
        }

        private static float WrapAngle180(float angle)
            => Mathf.Repeat(angle + 180f, 360f) - 180f;

        private float ClampPitch(float pitch)
        {
            float min = Mathf.Min(minPitch, maxPitch);
            float max = Mathf.Max(minPitch, maxPitch);
            return Mathf.Clamp(pitch, min, max);
        }
    }
}
