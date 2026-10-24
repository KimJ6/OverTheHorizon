using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace OverTheHorizon.Player.Input
{
    /// <summary>
    /// Unity Input System raw input을 View/Flight가 소비하는 현재 상태로 변환한다.
    ///
    /// Flight Core의 일반 이동은 Press/Release 이벤트를 사용하지 않는다.
    /// Dynamic Update 이후 한 번 수집한 캐시를 View/Flight가 공유하므로,
    /// A -> W 같은 입력 전환에서 중간 이벤트 순서가 운동방향을 오염시키지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Optional Input Action Asset")]
        [Tooltip("비워두면 기본 WASDRF/Mouse/Alt/Shift 입력을 런타임 생성합니다.")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Mecha";

        [Header("Required Action Names")]
        [SerializeField] private string lookActionName = "Look";
        [SerializeField] private string axialActionName = "Axial";
        [SerializeField] private string lateralActionName = "Lateral";
        [SerializeField] private string verticalActionName = "Vertical";

        [FormerlySerializedAs("brakeActionName")]
        [Tooltip("Freelook 전용 Action 이름입니다. 새 InputActionAsset에서는 'FreeLook' 사용을 권장합니다. 기존 'Brake' Action은 하위 호환용으로만 자동 탐색합니다.")]
        [SerializeField] private string freeLookActionName = "FreeLook";

        [Header("Optional / Reserved Action Names")]
        [FormerlySerializedAs("highGActionName")]
        [Tooltip("향후 회피/특수기동용 예약 입력입니다. 현재 기본 Flight Core에서는 없어도 정상 동작합니다.")]
        [SerializeField] private string maneuverActionName = "HighG";

        private const string CanonicalFreeLookActionName = "FreeLook";
        private const string LegacyFreeLookActionName = "Brake";

        private InputActionMap actionMap;
        private InputActionMap runtimeFallbackMap;
        private InputAction lookAction;
        private InputAction axialAction;
        private InputAction lateralAction;
        private InputAction verticalAction;
        private InputAction freeLookAction;
        private InputAction maneuverAction;

        public bool IsReady { get; private set; }
        public bool UsingRuntimeFallback => runtimeFallbackMap != null;
        public bool HasManeuverAction => maneuverAction != null;
        public bool GameplayInputEnabled => gameplayInputEnabled;

        private ulong rawSequence;
        private bool gameplayInputEnabled = true;
        private bool discardNextLook;
        public RawControlSnapshot RawSnapshot { get; private set; }
        public string LastInputError { get; private set; }
        public InputEdgeBuffer Edges { get; } = new InputEdgeBuffer(32);

        private void Update()
        {
            if (!IsReady) { RawSnapshot = default; return; }
            try
            {
                float axial = axialAction.ReadValue<float>();
                float lateral = lateralAction.ReadValue<float>();
                float vertical = verticalAction.ReadValue<float>();
                Vector2 look = lookAction.ReadValue<Vector2>();
                bool valid = PlayerValueValidation.Finite(axial) && PlayerValueValidation.Finite(lateral)
                    && PlayerValueValidation.Finite(vertical) && PlayerValueValidation.Finite(look.x) && PlayerValueValidation.Finite(look.y);
                ControlIntent intent = gameplayInputEnabled
                    ? new ControlIntent(axial, lateral, vertical, freeLookAction.IsPressed(), maneuverAction != null && maneuverAction.IsPressed())
                    : default;
                if (!gameplayInputEnabled || discardNextLook) look = Vector2.zero;
                discardNextLook = false;
                RawSnapshot = new RawControlSnapshot(checked(++rawSequence), intent, look, valid);
                LastInputError = valid ? null : "Non-finite input.";
            }
            catch (Exception ex) { RawSnapshot = default; LastInputError = ex.Message; }
        }
        public void SetGameplayInputEnabled(bool enabled)
        { gameplayInputEnabled = enabled; discardNextLook = true; RawSnapshot = default; }
        // Session boundaries cancel events; pause/fault only invalidate continuous input.
        public void ResetInputCache() { RawSnapshot = default; Edges.Clear(); }
        public void EnqueueGameplayEdge(uint kind) { if (gameplayInputEnabled) Edges.Enqueue(kind); }
        private void OnDestroy() { runtimeFallbackMap?.Dispose(); runtimeFallbackMap = null; }

        private void Awake()
        {
            ResolveActions();
        }

        private void OnEnable()
        {
            if (!IsReady)
                ResolveActions();

            actionMap?.Enable();
        }

        private void OnDisable()
        {
            actionMap?.Disable();
            RawSnapshot = default;
            discardNextLook = true;
        }

        private void ResolveActions()
        {
            IsReady = false;
            runtimeFallbackMap?.Dispose();
            runtimeFallbackMap = null;

            if (inputActions != null)
            {
                actionMap = inputActions.FindActionMap(actionMapName, false);
                if (actionMap == null)
                {
                    Debug.LogWarning(
                        $"PlayerInputReader: Action Map '{actionMapName}'을 찾지 못해 기본 런타임 입력으로 전환합니다.",
                        this);
                    BuildRuntimeFallback();
                    return;
                }

                lookAction = FindRequiredAction(lookActionName);
                axialAction = FindRequiredAction(axialActionName);
                lateralAction = FindRequiredAction(lateralActionName);
                verticalAction = FindRequiredAction(verticalActionName);
                freeLookAction = ResolveFreeLookAction();

                // Maneuver는 아직 Flight Core의 필수 입력이 아니다.
                // Action이 없어도 전체 InputActionAsset을 실패 처리하지 않는다.
                maneuverAction = FindOptionalAction(maneuverActionName);

                IsReady = lookAction != null
                          && axialAction != null
                          && lateralAction != null
                          && verticalAction != null
                          && freeLookAction != null;

                if (!IsReady)
                {
                    Debug.LogWarning(
                        "PlayerInputReader: 필수 InputAction 구성이 완전하지 않아 기본 런타임 입력으로 전환합니다.",
                        this);
                    BuildRuntimeFallback();
                }

                return;
            }

            BuildRuntimeFallback();
        }

        private InputAction ResolveFreeLookAction()
        {
            InputAction action = null;

            // 기존 컴포넌트에는 FormerlySerializedAs 때문에 freeLookActionName="Brake"가
            // 남아 있을 수 있다. Asset에 새 'FreeLook'이 존재하면 그것을 우선한다.
            bool configuredIsLegacy = string.Equals(
                freeLookActionName,
                LegacyFreeLookActionName,
                StringComparison.OrdinalIgnoreCase);

            if (!configuredIsLegacy)
                action = FindOptionalAction(freeLookActionName);

            action ??= FindOptionalAction(CanonicalFreeLookActionName);

            if (action == null && configuredIsLegacy)
                action = FindOptionalAction(freeLookActionName);

            action ??= FindOptionalAction(LegacyFreeLookActionName);

            if (action == null)
            {
                Debug.LogWarning(
                    $"PlayerInputReader: Freelook Action을 찾지 못했습니다. '{CanonicalFreeLookActionName}'을 권장하며 기존 '{LegacyFreeLookActionName}'도 호환됩니다.",
                    this);
            }

            return action;
        }

        private InputAction FindRequiredAction(string actionName)
        {
            InputAction action = FindOptionalAction(actionName);
            if (action == null)
                Debug.LogWarning($"PlayerInputReader: 필수 Action '{actionName}'을 찾지 못했습니다.", this);
            return action;
        }

        private InputAction FindOptionalAction(string actionName)
        {
            if (actionMap == null || string.IsNullOrWhiteSpace(actionName))
                return null;

            return actionMap.FindAction(actionName, false);
        }

        private void BuildRuntimeFallback()
        {
            runtimeFallbackMap = new InputActionMap("RuntimePlayerControl");
            actionMap = runtimeFallbackMap;

            lookAction = actionMap.AddAction("Look", InputActionType.Value);
            lookAction.AddBinding("<Mouse>/delta");

            axialAction = actionMap.AddAction("Axial", InputActionType.Value);
            axialAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/s")
                .With("Positive", "<Keyboard>/w");

            lateralAction = actionMap.AddAction("Lateral", InputActionType.Value);
            lateralAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");

            verticalAction = actionMap.AddAction("Vertical", InputActionType.Value);
            verticalAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/f")
                .With("Positive", "<Keyboard>/r");

            freeLookAction = actionMap.AddAction(CanonicalFreeLookActionName, InputActionType.Button);
            freeLookAction.AddBinding("<Keyboard>/leftAlt");

            maneuverAction = actionMap.AddAction("Maneuver", InputActionType.Button);
            maneuverAction.AddBinding("<Keyboard>/leftShift");

            IsReady = true;

            if (isActiveAndEnabled)
                actionMap.Enable();
        }

        public ControlIntent ReadControlIntent() => RawSnapshot.Valid ? RawSnapshot.Intent : default;
        public Vector2 ReadLookDelta() => RawSnapshot.Valid ? RawSnapshot.LookDelta : Vector2.zero;
        public bool ReadFreeLookHeld() => RawSnapshot.Valid && RawSnapshot.Intent.FreeLook;
    }
}
