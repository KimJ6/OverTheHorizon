using System;
using UnityEngine;
using OverTheHorizon.Timing;
using OverTheHorizon.Player.Input;
using OverTheHorizon.Player.View;

namespace OverTheHorizon.Player.Flight
{
    /// <summary>
    /// Player Flight의 Unity/Rigidbody 진입점.
    /// - Input/View를 읽고 순수 Solver를 호출한다.
    /// - Player Rigidbody의 rotation/linearVelocity를 쓰는 유일한 Flight component다.
    /// - 과거 입력별 velocity ownership을 보존하지 않는다.
    /// - ControlledVelocity와 GravityVelocity를 분리한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerFlightController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private PlayerViewState viewState;

        [Header("Rigidbody Setup")]
        [Tooltip("Gravity와 감속은 Flight Core가 명시적으로 계산합니다. useGravity/linearDamping은 런타임에서 강제로 비활성화됩니다.")]
        [SerializeField] private bool useInterpolation = true;

        [Header("Body Attitude")]
        [Tooltip("Body는 SteeringReference를 이 일정 최대 각속도로 추종합니다. Gap 기반 catch-up 보정은 프로토타입에서 사용하지 않습니다.")]
        [SerializeField] private float baseBodyTurnSpeedDegPerSec = 860f;
        [SerializeField] private bool clearAngularVelocityWhileControlled = true;

        [Header("Controlled Motion - WASDRF")]
        [SerializeField] private float maxControlledSpeed = 68f;
        [SerializeField] private float moveAcceleration = 120f;
        [Tooltip("모든 유효 이동 입력 해제 후 ControlledSpeed가 0으로 접근하는 감속도.")]
        [SerializeField] private float coastDeceleration = 22f;
        [SerializeField] private float inputAxisDeadZone = 0.01f;
        [Tooltip("상쇄 후 Raw Command가 이 크기 이상일 때 비활성->활성 전환.")]
        [SerializeField] private float commandActivateThreshold = 0.08f;
        [Tooltip("활성 상태에서는 이 값 미만으로 떨어질 때 비활성화. Activate보다 작게 두어 hysteresis를 만듭니다.")]
        [SerializeField] private float commandDeactivateThreshold = 0.04f;

        [Header("Propulsion / Gravity Support")]
        [Tooltip("이동 입력 해제 후 PropulsionAuthority가 1->0으로 감소하는 초당 비율.")]
        [SerializeField] private float propulsionAuthorityReleaseRate = 0.75f;
        [SerializeField] private float gravityScale = 1f;
        [Tooltip("추력 여유가 회복됐을 때 이미 쌓인 GravityVelocity를 0으로 되돌리는 최대 가속도.")]
        [SerializeField] private float gravityRecoveryAcceleration = 60f;
        [Tooltip("X=PropulsionAuthority, Y=Gravity Support. 1이면 중력 완전 보정, 0이면 완전 낙하.")]
        [SerializeField] private AnimationCurve gravitySupportByAuthority = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.35f, 0.10f),
            new Keyframe(0.70f, 0.72f),
            new Keyframe(1f, 1f));

        private readonly MotionState motionState = new MotionState();
        private readonly FlightMotionSolver motionSolver = new FlightMotionSolver();

        private ControlIntent latestIntent;
        private FlightTelemetry telemetry;
        private bool runtimeStateInitialized;

        private bool managed, managedEnabled, stepPrepared;
        private Rigidbody managedBody;
        private PlayerInputReader managedReader;
        private PlayerViewState managedView;
        private uint managedPlayerId;
        private Action<uint, string> failureSink;
        private SimulationStepContext preparedContext;
        private PlayerControlSnapshot preparedControl;
        private Vector3 preparedGravity;
        private FlightSettings managedSettings;
        private int settingsVersion;
        public ulong AppliedGeneration { get; private set; }
        public ulong AppliedOrdinal { get; private set; }
        public Rigidbody Body => body;
        public PlayerInputReader InputReader => inputReader;
        public PlayerViewState ViewState => viewState;
        public bool IsManaged => managed;
        public FlightTelemetry Telemetry => telemetry;
        public Vector3 CurrentVelocity => body != null ? body.linearVelocity : Vector3.zero;
        public float CurrentSpeed => CurrentVelocity.magnitude;
        public float MaxControlledSpeed => maxControlledSpeed;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
            inputReader = GetComponent<PlayerInputReader>();
            viewState = GetComponent<PlayerViewState>();
        }

        private void Awake()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (inputReader == null)
                inputReader = GetComponent<PlayerInputReader>();
            if (viewState == null)
                viewState = GetComponent<PlayerViewState>();

            ConfigureRigidbody();

            // 최초 scene 진입 시의 Rigidbody velocity만 ControlledVelocity로 간주한다.
            // 이후 disable -> enable은 state ownership을 다시 분류하지 않는다.
            InitializeRuntimeState(body != null ? body.linearVelocity : Vector3.zero, Vector3.zero);
        }

        private void OnEnable()
        {
            if (managed) { SetManagedExecutionEnabled(false); return; }
            if (body == null)
                return;

            ConfigureRigidbody();

            // Awake 이후 재활성화에서는 기존 Controlled/Gravity ownership을 보존한다.
            // Respawn/teleport처럼 의도적인 재초기화가 필요하면 ResetFlightState()를 호출한다.
            if (!runtimeStateInitialized)
                InitializeRuntimeState(body.linearVelocity, Vector3.zero);
        }

        private void ConfigureRigidbody()
        {
            if (body == null)
                return;

            body.useGravity = false;
            body.linearDamping = 0f;
            body.interpolation = useInterpolation
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
        }

        /// <summary>
        /// Respawn/teleport처럼 Flight ownership을 명시적으로 다시 설정해야 할 때 사용한다.
        /// 실제 Rigidbody velocity 전체를 자동 분류하지 않고 Controlled/Gravity 성분을 호출자가 분리해 전달한다.
        /// 현재 프로토타입은 ExternalVelocity 채널이 없으므로 두 성분의 합을 Rigidbody에 즉시 반영한다.
        /// </summary>
        public void ResetFlightState(Vector3 controlledVelocity, Vector3 gravityVelocity)
        {
            if (managedEnabled || stepPrepared) throw new InvalidOperationException("Suspend managed execution before resetting Flight.");
            if (managed) ValidateManagedReferences();
            if (!PlayerValueValidation.Finite(controlledVelocity) || !PlayerValueValidation.Finite(gravityVelocity))
                throw new InvalidOperationException("Flight initial velocity must be finite.");
            if (body == null)
                return;

            ConfigureRigidbody();
            InitializeRuntimeState(controlledVelocity, gravityVelocity);
            if (!body.isKinematic) body.linearVelocity = motionState.Result;
        }

        private void InitializeRuntimeState(
            Vector3 initialControlledVelocity,
            Vector3 initialGravityVelocity)
        {
            if (body == null)
                return;

            Vector3 steeringForward = ResolveSteeringForward();
            motionState.Initialize(
                initialControlledVelocity,
                initialGravityVelocity,
                steeringForward);
            latestIntent = default;
            runtimeStateInitialized = true;

            Vector3 bodyForward = ResolveBodyForward();
            Vector3 cameraForward = ResolveCameraForward();
            Vector3 steeringRight = ResolveSteeringRight();

            telemetry = new FlightTelemetry(
                FlightPhase.Coast,
                new MotionSnapshot(motionState),
                cameraForward,
                steeringForward,
                steeringRight,
                bodyForward,
                motionState.Result,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                maxControlledSpeed,
                0f,
                Vector3.Angle(bodyForward, steeringForward),
                viewState != null && viewState.IsFreeLook,
                viewState != null && viewState.IsCameraRecentering,
                false);
        }

        public void BindManaged(uint playerId, Action<uint, string> onFailure)
        {
            ValidateManagedBinding(playerId);
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            int nextVersion = checked(settingsVersion + 1);
            FlightSettings original = BuildSettings();
            AnimationCurve curve = original.Gravity.SupportByPropulsionAuthority;
            AnimationCurve owned = curve == null ? null : new AnimationCurve(curve.keys)
                { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
            var settings = new FlightSettings(original.Attitude, original.Motion,
                new GravitySupportSettings(original.Gravity.GravityScale, original.Gravity.GravityRecoveryAcceleration, owned));
            // All validation/allocation precedes the ownership change.
            managed = true; managedEnabled = stepPrepared = false; managedPlayerId = playerId;
            failureSink = onFailure; AppliedGeneration = AppliedOrdinal = 0;
            managedBody = body; managedReader = inputReader; managedView = viewState;
            settingsVersion = nextVersion; managedSettings = settings;
        }
        public void ValidateManagedBinding(uint playerId)
        {
            if (managed || managedEnabled || stepPrepared || playerId == 0 || body == null || inputReader == null || viewState == null)
                throw new InvalidOperationException("An unbound Flight with prepared references is required.");
            ValidateManagedSettings();
        }
        private void ValidateManagedSettings()
        {
            if (!PlayerValueValidation.Finite(baseBodyTurnSpeedDegPerSec) || !PlayerValueValidation.Finite(maxControlledSpeed)
                || !PlayerValueValidation.Finite(moveAcceleration) || !PlayerValueValidation.Finite(coastDeceleration)
                || !PlayerValueValidation.Finite(inputAxisDeadZone) || !PlayerValueValidation.Finite(commandActivateThreshold)
                || !PlayerValueValidation.Finite(commandDeactivateThreshold) || !PlayerValueValidation.Finite(propulsionAuthorityReleaseRate)
                || !PlayerValueValidation.Finite(gravityScale) || !PlayerValueValidation.Finite(gravityRecoveryAcceleration))
                throw new InvalidOperationException("Flight settings must be finite.");
            if (gravitySupportByAuthority != null) foreach (var key in gravitySupportByAuthority.keys)
                if (!PlayerValueValidation.Finite(key.time) || !PlayerValueValidation.Finite(key.value)
                    || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent)
                    || !PlayerValueValidation.Finite(key.inWeight) || !PlayerValueValidation.Finite(key.outWeight))
                    throw new InvalidOperationException("Gravity support curve contains invalid keys.");
        }
        public void SetManagedExecutionEnabled(bool enabled)
        { managedEnabled = enabled; if (!enabled) stepPrepared = false; }
        public void UnbindManaged()
        { managed = managedEnabled = stepPrepared = false; failureSink = null; managedBody = null; managedReader = null; managedView = null; }
        public void DetachManagedFailureSink()
        { SetManagedExecutionEnabled(false); failureSink = null; }
        public void ValidateManagedReferences()
        {
            if (!managed || managedBody == null || managedReader == null || managedView == null
                || body != managedBody || inputReader != managedReader || viewState != managedView
                || !isActiveAndEnabled || !managedReader.isActiveAndEnabled || !managedView.isActiveAndEnabled
                || managedView.InputReader != managedReader)
                throw new InvalidOperationException("Registered Flight references were disabled, destroyed or replaced.");
        }
        public void PrepareStep(SimulationStepContext context, PlayerControlSnapshot control, Vector3 gravity)
        {
            ValidateManagedReferences();
            if (!managed || !managedEnabled || stepPrepared || !control.Valid || control.PlayerId != managedPlayerId
                || context.Generation != control.Generation || !PlayerValueValidation.Finite(gravity)
                || (AppliedGeneration == context.Generation && AppliedOrdinal >= context.Ordinal))
                throw new InvalidOperationException("Invalid or duplicate Flight step preparation.");
            preparedContext = context; preparedControl = control; preparedGravity = gravity; stepPrepared = true;
        }
        public FlightRuntimeSnapshot CaptureFlightState() => new FlightRuntimeSnapshot(motionState, settingsVersion);
        public void RestoreFlightState(FlightRuntimeSnapshot snapshot)
        {
            if (managedEnabled || stepPrepared || snapshot.SettingsVersion != settingsVersion)
                throw new InvalidOperationException("Flight restore requires suspension and matching settings.");
            snapshot.RestoreTo(motionState);
            runtimeStateInitialized = true;
        }
        public readonly struct PreparationState
        {
            internal readonly FlightRuntimeSnapshot Motion;
            internal readonly FlightTelemetry Telemetry;
            internal readonly ControlIntent Intent;
            internal readonly bool Initialized;
            internal readonly ulong Generation, Ordinal;
            internal PreparationState(PlayerFlightController flight)
            {
                Motion = flight.CaptureFlightState(); Telemetry = flight.telemetry; Intent = flight.latestIntent;
                Initialized = flight.runtimeStateInitialized; Generation = flight.AppliedGeneration; Ordinal = flight.AppliedOrdinal;
            }
        }
        public PreparationState CapturePreparationState() => new PreparationState(this);
        public void RestorePreparationState(PreparationState state)
        {
            if (managedEnabled || stepPrepared || (managed && state.Motion.SettingsVersion != settingsVersion))
                throw new InvalidOperationException("Preparation rollback requires suspended execution and unchanged managed settings.");
            state.Motion.RestoreTo(motionState); settingsVersion = state.Motion.SettingsVersion;
            telemetry = state.Telemetry; latestIntent = state.Intent; runtimeStateInitialized = state.Initialized;
            AppliedGeneration = state.Generation; AppliedOrdinal = state.Ordinal;
        }
        private void FixedUpdate()
        {
            if (managed)
            {
                if (!managedEnabled) return;
                if (!stepPrepared) { failureSink?.Invoke(managedPlayerId, "Flight has no reserved input."); return; }
                stepPrepared = false;
                try
                {
                    ValidateManagedReferences();
                    ExecuteFlightStep(managedBody, preparedContext.DeltaTime, managedSettings, preparedControl.Intent,
                        preparedControl.SteeringForward, preparedControl.SteeringRight, preparedControl.SteeringRotation,
                        preparedControl.ViewForward, preparedControl.Intent.FreeLook, preparedControl.Recentering, preparedGravity);
                    if (!PlayerValueValidation.Finite(managedBody.linearVelocity) || !PlayerValueValidation.Finite(managedBody.rotation))
                        throw new InvalidOperationException("Non-finite Flight result.");
                    AppliedGeneration = preparedContext.Generation; AppliedOrdinal = preparedContext.Ordinal;
                }
                catch (Exception ex) { failureSink?.Invoke(managedPlayerId, ex.Message); }
                return;
            }
            if (body == null || inputReader == null) return;
            ExecuteFlightStep(body, Time.fixedDeltaTime, BuildSettings(), inputReader.ReadControlIntent(), ResolveSteeringForward(),
                ResolveSteeringRight(), viewState != null ? viewState.DesiredBodyFacing : body.rotation,
                ResolveCameraForward(), viewState != null && viewState.IsFreeLook,
                viewState != null && viewState.IsCameraRecentering, Physics.gravity);
        }
        private void ExecuteFlightStep(Rigidbody stepBody, float dt, FlightSettings settings, ControlIntent intent,
            Vector3 steeringForward, Vector3 steeringRight, Quaternion desiredBodyFacing,
            Vector3 cameraForward, bool freeLook, bool recentering, Vector3 gravity)
        {
            if (stepBody == null)
                return;

            latestIntent = intent;
            Vector3 worldUp = Vector3.up;

            AttitudeResult attitude = AttitudeSolver.Solve(
                stepBody.rotation,
                desiredBodyFacing,
                settings.Attitude,
                dt);

            if (settings.Attitude.ClearAngularVelocityWhileControlled)
                stepBody.angularVelocity = Vector3.zero;

            // During a running step Flight owns rotation; lifecycle suspension may restore/freeze the pose.
            stepBody.MoveRotation(attitude.Rotation);

            Vector3 expectedBeforeStep = motionState.Result;
            Vector3 actualBeforeStep = stepBody.linearVelocity;

            // 충돌/넉백/다른 물리 영향으로 Flight Core 예상값과 실제 Rigidbody가 달라졌는지 관측한다.
            // 현재 baseline에서는 이 residual을 Telemetry에만 남기고 다음 줄의 system velocity로 덮어쓴다.
            // 전투/충돌 확장 시 이 지점이 ExternalVelocity ownership을 도입할 경계다.
            Vector3 preStepResidual = actualBeforeStep - expectedBeforeStep;

            FlightMotionStepResult motionStep = motionSolver.Step(
                motionState,
                latestIntent,
                steeringForward,
                steeringRight,
                worldUp,
                settings.Motion,
                dt);

            GravitySupportStepResult gravityStep = GravitySupportSolver.Step(
                motionState,
                gravity,
                settings.Gravity,
                dt);
            // Prototype contract: collision/knockback ownership은 아직 별도 채널로 모델링하지 않는다.
            // 따라서 현재 Flight Core가 소유한 Controlled + Gravity 상태를 그대로 Rigidbody에 쓴다.
            stepBody.linearVelocity = motionState.Result;

            Vector3 bodyForward = attitude.Forward;

            telemetry = new FlightTelemetry(
                ResolvePhase(motionState, gravityStep.Support),
                new MotionSnapshot(motionState),
                cameraForward,
                steeringForward,
                steeringRight,
                bodyForward,
                body.linearVelocity,
                preStepResidual,
                motionStep.ControlledDeltaV,
                gravityStep.UnopposedGravityDeltaV,
                gravityStep.GravityRecoveryDeltaV,
                settings.Motion.MaxControlledSpeed,
                gravityStep.Support,
                attitude.SeparationDegrees,
                freeLook,
                recentering,
                latestIntent.Maneuver);
        }

        private FlightSettings BuildSettings()
        {
            return new FlightSettings(
                new AttitudeSettings(
                    baseBodyTurnSpeedDegPerSec,
                    clearAngularVelocityWhileControlled),
                new MotionSettings(
                    maxControlledSpeed,
                    moveAcceleration,
                    coastDeceleration,
                    inputAxisDeadZone,
                    commandActivateThreshold,
                    commandDeactivateThreshold,
                    propulsionAuthorityReleaseRate),
                new GravitySupportSettings(
                    gravityScale,
                    gravityRecoveryAcceleration,
                    gravitySupportByAuthority));
        }

        private static FlightPhase ResolvePhase(MotionState state, float gravitySupport)
        {
            if (state != null && state.CommandActive)
                return FlightPhase.Powered;

            if (gravitySupport < 0.95f)
                return FlightPhase.Glide;

            return FlightPhase.Coast;
        }

        private Vector3 ResolveSteeringForward()
        {
            Vector3 forward = viewState != null
                ? viewState.SteeringForward
                : ResolveBodyForward();
            return SafeNormalized(forward, Vector3.forward);
        }

        private Vector3 ResolveSteeringRight()
        {
            Vector3 right = viewState != null
                ? viewState.SteeringRight
                : (body != null ? body.rotation * Vector3.right : Vector3.right);
            return SafeNormalized(right, Vector3.right);
        }

        private Vector3 ResolveCameraForward()
        {
            Vector3 forward = viewState != null
                ? viewState.ViewForward
                : ResolveSteeringForward();
            return SafeNormalized(forward, Vector3.forward);
        }

        private Vector3 ResolveBodyForward()
        {
            if (body != null)
            {
                Vector3 forward = body.rotation * Vector3.forward;
                return SafeNormalized(forward, Vector3.forward);
            }

            return SafeNormalized(transform.forward, Vector3.forward);
        }

        private static Vector3 SafeNormalized(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude > 0.000001f)
                return value.normalized;
            return fallback.sqrMagnitude > 0.000001f
                ? fallback.normalized
                : Vector3.forward;
        }
    }
}
