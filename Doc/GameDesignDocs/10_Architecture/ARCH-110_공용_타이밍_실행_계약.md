---
document_id: ARCH-110
title: 공용 타이밍·시뮬레이션 실행 계약
document_type: architecture
status: canonical
revision: 1.0
last_updated: 2026-10-17
scope:
  - fixed-step simulation context and authoritative completion
  - Unity PlayerLoop and Physics handoff
  - canonical player frame boundary
  - ECS stage ownership ordering and fail-stop
  - ECS Job dependencies and result publication
  - local session lifecycle and cleanup recovery
  - timing validation and integration constraints
depends_on:
  - PRJ-000
  - ARCH-100
interfaces:
  - SYS-110
  - SYS-120
  - SYS-130
---

# ARCH-110 공용 타이밍·시뮬레이션 실행 계약

> **핵심 선택:** 게임플레이의 논리 틱과 권위 완료는 명시적인 `SimulationStepContext`와 `SimulationProgress`로 관리한다. 현재 로컬 실행에서는 Unity PlayerLoop의 Physics 전후 경계를 통해 플레이어 상태를 수집하고, Unity ECS `ComponentSystemGroup`이 전투 Stage를 실행하도록 유지한다. `CombatStepExecutor`는 필수 Stage의 소유·순서·결과 검증을 담당하지만 완료 틱은 기록하지 않는다.

이 문서는 **현재 구현된 공용 타이밍의 설계 이유, 실행 계약, 새로운 전투 시스템의 연결 방법과 보장 범위**를 정의한다. 로컬 고정 스텝 기반과 P1-A/P1-B 검증 범위를 설명하며, 향후 네트워크 예측·롤백 또는 `ARCH-100`의 전체 전투 Phase가 구현되었다고 주장하지 않는다.

## 0. 문서 계약

### 0.1 이 문서가 소유하는 것

- 논리 틱의 예약·검증·완료와 실행 세대 식별
- 로컬 Unity PlayerLoop·PhysX와 ECS 전투 실행의 연결
- 입력 및 플레이어 물리 결과를 Canonical Frame으로 확정하는 경계
- 전투 ECS Stage의 등록, 봉인, 순서·멤버십 검증과 Fail-stop
- ECS Job 의존성과 권위 결과를 읽고 확정하기 전의 동기화 조건
- 로컬 실행 세션의 시작·일시정지·재시작·실패·자원 정리 계약
- 공통 실행 기반의 검증 근거와 현재 적용 한계

### 0.2 다른 Owner와의 경계

[PRJ-000](../00_Project/PRJ-000_프로젝트_통합기획_기준서.md)은 프로젝트 목표·멀티플레이 범위를 정한다. [ARCH-100](ARCH-100_전투_적성객체_아키텍처.md)은 전투의 **논리적 Gameplay Phase**, Host 권위의 범위, Stable ID·결정론 정책, Target·공격·피해·전투 성능 목표를 소유한다. 본 문서는 그 Phase가 안전하게 실행·완료되기 위한 **기반 메커니즘**을 소유한다.

[SYS-110](../20_Systems/SYS-110_플레이어_기동.md)은 기동·조향 규칙을, [SYS-120](../20_Systems/SYS-120_자동타게팅_직사화기.md)은 타게팅 선택·직사 규칙을, [SYS-130](../20_Systems/SYS-130_미사일_이타노서커스.md)은 미사일 상태·Guidance·행동을 소유한다. 본 문서는 이 규칙이나 무기별 우선순위·미사일 FSM을 재정의하지 않는다. `SYS-140`의 출격·복귀·정산 세션 역시 여기서 설명하는 **로컬 시뮬레이션 실행 세션**과 구별한다.

### 0.3 설명 기준과 상태

현재 소스 기준은 `feat/shared-simulation-timing` 브랜치의 커밋 `72f3cebb24629e81c725bb16a17631e2d4458f51`이다. 클래스와 메서드명은 이 구현을 설명하는 진입점이며, 상위 계약을 유지하는 내부 코드 변경까지 금지하는 것은 아니다.

본문의 **현재 구현**은 해당 커밋의 코드를 뜻한다. **채택된 상위 원칙**은 관련 Canon의 규칙이며, **후속 과제**는 현재 완료·검증을 의미하지 않는다. 검증 수치는 §9의 실행 범위 안에서만 해석한다.

---

## 1. 해결할 문제와 주요 구조적 선택

### 1.1 서로 다른 실행 경계를 하나의 논리 틱으로 연결한다

플레이어의 입력·시점은 Unity `Update`에서 수집되지만 기동과 `Rigidbody`의 물리 상태는 고정 업데이트에서 변화한다. ECS 전투 시스템이 시점마다 임의로 `Transform`·`Rigidbody`를 읽으면 같은 판정 틱에서도 입력과 물리 결과의 대응이 흐려질 수 있다.

현재 로컬 구현은 **입력 수락 → 물리 이전 준비 → Unity 기동·PhysX → 물리 이후 Canonical Frame 생성 → ECS 전투**의 흐름을 채택한다. 이를 위해 기본 Unity 고정 스텝을 대체하지 않고 `LocalFixedStepDriver`가 PlayerLoop의 명시적 전후 경계에 콜백을 배치한다. 그 대가로 Unity PlayerLoop의 구조·실행 순서·훅 소유권을 감시해야 한다.

### 1.2 실행 성공을 권위 완료와 분리한다

ECS 그룹이 마지막 시스템까지 도달했다는 사실은 필수 전투 결과의 정상 확정이나 세션 틱의 권위 완료와 동일하지 않다. 필수 Stage가 빠지거나 Job의 잘못된 결과를 소비하더라도 완료 번호가 증가하면 다음 틱에서 결함을 추적하기 어렵고 잘못된 상태가 기준이 된다.

따라서 `CombatStepExecutor`는 **실행·검증**, `SimulationProgress`는 **완료 기록**을 각각 소유한다. `SimulationCompleteSystem`은 그룹의 끝에 도달했다는 영수증만 표시한다. 추가 검증 및 `PendingPublication` 상태를 관리하는 비용을 감수하는 대신, 완료 권위의 중복을 피한다.

### 1.3 ECS의 표준 실행 기능을 재구현하지 않는다

시스템 순서와 일반적인 ECS Job 의존성은 Unity의 `ComponentSystemGroup`, `UpdateBefore/After`, `SystemBase.Dependency`를 활용한다. 별도의 사용자 정의 전투 시스템 순회기나 전역 Job 대기기를 만들지 않는다.

Executor가 추가로 수행하는 일은 **필수 Stage를 정확히 등록했는지, 봉인된 순서대로 실행했는지, 실패한 뒤 후속 Gameplay를 막았는지** 확인하는 프로젝트 고유의 계약이다. 등록 목록 검증·그룹 정렬에 CPU 비용이 생기지만, 현재 이 비용이 병목이라는 측정 근거는 없다.

### 1.4 실패한 실행의 정리는 즉시 해제보다 소유권 안전을 우선한다

PlayerLoop 콜백 또는 Job이 실행 중일 때 World·NativeContainer·Stage를 파괴하면 해제된 자원에 접근할 수 있다. 또한 Unity 전역 시간 설정이나 참가자 물리 상태를 잘못 복원하면 다른 실행자의 상태를 훼손할 수 있다.

로컬 세션은 실행 중 해제를 금지하고, 중단 후 정리에 실패하면 해당 자원을 보유한 상태로 복구를 재시도한다. 이 선택은 `LocalSimulationBootstrap`, `LocalTimingOwner`, `LocalCleanupRecovery`의 관리 복잡성과 오류 시 세션 재시작 제약을 초래한다. 이는 안전한 실패를 위한 의도된 대가다.

---

## 2. Runtime 책임과 데이터 흐름

### 2.1 구성 요소

| 구성 요소 | 책임과 소유권 |
|---|---|
| `SimulationTimingConfig`, `SimulationStepContext`, `TickMath` | 논리 틱 설정·식별자·순환 틱 비교 및 시간 변환 |
| `SimulationProgress` | 틱 예약과 **유일한** `CompletedTick`·`CompletedOrdinal` 변경 |
| `LocalTimingOwner` | 로컬 `Time.fixedDeltaTime`, `Time.maximumDeltaTime`, `Time.timeScale`, Input System 갱신 모드의 배타적 소유·복원 |
| `LocalFixedStepDriver` | PlayerLoop의 Pre·Physics 완료 Fence·Post 훅 설치, 순서 감시, 콜백 분리 |
| `LocalSimulationBootstrap` | 로컬 세션·플레이어 바인딩·실행 요청·Fault·최종 완료 조정 |
| `LocalPlayerStatePublisher` 및 `PlayerStepElement` | 수락된 입력과 물리 시작·종료 상태를 ECS용 고정 크기 Canonical Frame으로 변환 |
| `CombatStepExecutor` | 호출자 소유 World에서 전투 Stage 등록·봉인·그룹 실행·결과 검증·자원 정리 |
| `CombatFixedStepSystemGroup`, `CombatStepSystem` | Unity 표준 ECS 그룹 실행과 개별 Stage의 공통 실행·오류·Job 계약 |
| `SimulationEntrySystem`, `SimulationCompleteSystem` | 입력 Header 확인과 그룹 진입·종료 영수증 기록 |
| `LocalCleanupRecovery`, `LocalCleanupRecoveryHost` | 실패한 정리 작업을 보존하고, 원래 Bootstrap이 파괴되어도 재시도 |
| `SimulationTraceBuffer`, `SimulationReplayStepSource` | Canonical Frame과 완료 여부의 진단 기록·직렬화·ECS 재실행 입력 |

`CombatStepExecutor`는 **전투 Stage와 세션 Entity를 소유**하지만 Unity `World` 자체는 생성·소유하지 않는다. 로컬 Bootstrap은 해당 실행기의 생성·폐기 시점을 결정하며 외부 World 소유권을 대신하지 않는다.

### 2.2 로컬 한 틱의 실제 순서

```text
Dynamic Update
  PlayerInputReader / PlayerViewState
    → PlayerControlSnapshotPublisher
          |
Unity FixedUpdate ───────────────────────────────────────────
  PreTimingHook  [LocalSimulationBootstrap.PrePhysics]
    → 세션·바인딩·훅·입력 상태 검증
    → 입력 수락(accepted snapshot)
    → SimulationProgress.Begin(context)
    → Flight.PrepareStep(context)
          |
  ScriptRunBehaviourFixedUpdate
    → PlayerFlightController.FixedUpdate
          |
  PhysicsFixedUpdate
    → Rigidbody / PhysX
          |
  PhysicsTimingFenceHook
          |
  PostTimingHook [LocalSimulationBootstrap.PostPhysics]
    → PlayerStepElement[4] 생성·검증
    → CombatStepExecutor.Execute(context, frame, progress)
        → SimulationEntrySystem
        → 봉인된 Gameplay CombatStepSystem들
        → SimulationCompleteSystem
    → CombatStepExecutor.ValidatePending
    → SimulationProgress.Complete                [단일 권위 기록]
    → CombatStepExecutor.AcknowledgeAuthoritativeCompletion
    → 로컬 완료 Snapshot 반영 및 입력 Edge ACK
    → Trace 기록
─────────────────────────────────────────────────────────────
```

이 순서는 **현재 로컬 어댑터의 실행 순서**다. `ARCH-100` §2.2에 나열된 Network Input, Encounter, Missile, Damage 등 전체 20개 Gameplay Phase가 현재 전부 등록·구현되어 있다는 뜻은 아니다. 향후 미사일·전투 시스템은 그 논리적 Phase에 맞춰 실제 Stage를 추가한다.

---

## 3. 시간 모델과 단일 완료 권위

### 3.1 식별자

| 필드 | 현행 의미 |
|---|---|
| `Generation` (`ulong`) | 준비·재시작을 통해 새 실행 세대를 구분한다. `SimulationProgress.Reset`은 더 큰 Generation만 허용한다 |
| `Tick` (`uint`) | 해당 Generation의 논리 틱. 최대값 이후 순환할 수 있다 |
| `Ordinal` (`ulong`) | 해당 Generation 안에서 단조 증가하는 틱 진행 순서. 증가 오버플로는 허용하지 않는다 |
| `TickRate`, `DeltaTime` | 실행 환경의 틱 주기와 계산 간격 |
| `ConfigVersion` | 동일 세션에서 설정 문맥을 검증하기 위한 버전 |
| `StartTime`, `EndTime` | Ordinal·TickRate로 계산하는 Generation 내부의 논리 시간 |

`TickMath`는 반구간(`2^31`틱 미만)을 전제로 순환 Tick의 기한 판정·경과 시간·주기 변환을 지원한다. 이것은 저빈도 스케줄링에 사용할 수 있는 계산 기반이며, 모든 시스템용 Timing Wheel이 이미 구현되었다는 뜻은 아니다.

현재 `SimulationTimingConfig`는 50·60·120Hz만 허용하며 기본 `SimulationTimingSettings`는 50Hz다. `ARCH-100`의 60Hz 수치는 프로파일링 시작 후보이므로 현행 기본 설정과 동일시하지 않는다.

### 3.2 완료 불변식

**`SimulationProgress`만 완료 Tick과 Ordinal을 기록한다.** `Begin()`은 새 문맥을 예약하고 `StepOpen`을 세울 뿐 완료를 기록하지 않는다. 완료 순서는 다음과 같다.

1. `SimulationProgress.Begin(config)` — 실행할 문맥 예약
2. `CombatStepExecutor.Execute(context, frame, progress)` — 문맥 검증과 ECS 그룹 실행
3. Executor의 `PendingPublication` — 그룹 실행 성공이나 아직 권위 완료 전
4. `ValidatePending(context, progress)` — **동일 Progress 인스턴스**, 열린 문맥과 Header를 확인
5. `SimulationProgress.Complete(context)` — 완료 Tick/Ordinal을 유일하게 기록
6. `AcknowledgeAuthoritativeCompletion(context, progress)` — Executor를 `Ready`로 되돌림
7. 완료된 로컬 Player Snapshot 게시·입력 ACK·진단 기록

Executor는 새 완료 번호를 계산하거나 별도로 저장하지 않는다. 다른 `SimulationProgress` 인스턴스가 우연히 같은 값을 가져도 미완료 실행의 소유권을 이전할 수 없다. 한 실행을 `PendingPublication`에서 두 번 실행하거나 두 번 승인하지 않는다.

예외·검증 실패는 `Fail` 경로에서 `SimulationProgress.Abort()`와 Executor 무효화로 이어진다. **완료 이전의 실패는 완료 번호를 증가시키지 않는다.** 다만 권위 완료가 이미 끝난 뒤 Trace와 같은 진단 기록에 실패가 발생한 경우 완료를 되돌리지는 않는다. 진단 실패로 세션이 Fault될 수 있다는 사실과, 물리 세계 전체를 롤백하는 기능은 아니라는 점을 구분한다.

### 3.3 권위 모델의 적용 범위

현재 구현은 **로컬 단일 실행 세션에서의 권위 진행 관리**다. `ARCH-100`이 정의한 Host-authoritative 정책과 연결할 수 있는 기반이지만, 네트워크 서버·클라이언트 World, 예측 재실행, 입력 지연 정책과 복제는 구현하지 않았다.

향후 네트워크 실행기가 논리 틱의 실행 시점을 제공할 수 있다. 현재의 Unity PlayerLoop·Time 설정을 네트워크 클라이언트 예측 World에 무조건 이식하거나 `SimulationProgress`를 예측 반복 실행기의 완료 장부로 재사용하는 것은 현행 계약에 포함되지 않는다.

---

## 4. PlayerLoop·물리·Canonical Frame

### 4.1 로컬 고정 스텝 어댑터

`LocalTimingOwner`는 3D Physics가 `SimulationMode.FixedUpdate`인 환경을 요구하고, 지정한 fixedDeltaTime·maximumDeltaTime·timeScale·Input 갱신 모드를 적용한다. Input System은 Dynamic Update에서 원본 입력을 수집한다. 준비·일시정지 시 `timeScale`을 0으로 두고 실행 시 1로 전환한다.

`LocalFixedStepDriver`는 하나의 `FixedUpdate` 분기 안에 Pre Hook과 Physics 완료 Fence, Post Hook을 삽입한다. 단순히 이름이 같은 훅이 있는지만 검사하지 않고 기존 Script·Physics 실행 함수와 상대 순서도 확인한다. 설치와 재실행 경계에서 PlayerLoop 구조를 검사하고, 정상적인 고정 스텝에서는 보유한 훅들의 실행 순서 상태를 확인한다. 따라서 매 틱 PlayerLoop 트리를 전체 순회하는 비용은 피한다.

이 구조는 Unity 표준 Physics 실행을 대체하지 않는다. 고정 틱이 PhysX보다 먼저 완료되거나, 교체된 필수 훅으로 조용히 진행되는 문제를 막기 위한 연결·감시층이다. PlayerLoop를 임의로 변경하는 다른 시스템과 충돌할 수 있으므로 훅 배타성과 복원을 계약으로 유지한다.

### 4.2 입력 확정과 물리 결과 전달

`PlayerInputReader`의 원본 입력, `PlayerViewState`가 소비한 원본 Sequence, `PlayerControlSnapshotPublisher`의 Snapshot이 일치할 때 해당 입력을 다음 틱에 사용할 수 있다. Bootstrap은 모든 참가자가 유효한 Snapshot을 제공했는지 확인한 뒤 수락한다.

Pre 단계에서는 `PlayerFlightController`에 해당 틱 문맥과 수락한 입력을 전달한다. Script FixedUpdate 동안 Flight가 이를 적용한 뒤, Post 단계에서 `Rigidbody`의 종료 위치·회전·속도·각속도를 캡처한다. `LocalPlayerStatePublisher`는 직전 완료 Snapshot, 수락된 입력, 현재 물리 결과를 `PlayerStepElement`로 변환한다.

- `PlayerStepElement[4]`는 고정 슬롯이며 **현재 로컬 참가자는 1~4명**이다.
- Bootstrap의 플레이어 배열 순서가 로컬 세션의 `PlayerId` 1~4를 정한다. 이 ID는 `ARCH-100`의 일반적인 전투 대상용 Stable Logical ID Registry 구현과 동일하지 않다.
- 각 유효 슬롯은 Generation·Ordinal·시작/종료 StateTick·입력 Sequence·위치·속도·조향 및 시점 데이터의 무결성을 검증한다.
- 입력 수락·물리 캡처는 완료를 확정하지 않는다. `CanonicalPlayerFrameValidator`가 검사한 Frame을 ECS가 소비하고, 최종 권위 완료 뒤에만 로컬 Snapshot과 입력 ACK를 전진시킨다.

현재 `InputEdgeBuffer`는 비파괴 읽기와 ACK를 지원하지만 **Gameplay 일회성 Edge를 실제로 소비하는 경로는 아직 없다.** 큐에 미소비 Edge가 남아 있으면 현재 Bootstrap은 틱 시작을 거부한다. 추후 발사·능력 버튼 등 일회성 이벤트가 필요한 기능에서는 수락 틱과 소비·ACK 계약을 명시적으로 연결해야 한다. 이를 지원하는 것처럼 설명하지 않는다.

---

## 5. ECS Stage의 소유권·봉인·실행

### 5.1 등록과 Seal

`CombatStepExecutor`는 호출자가 제공한 유효 World 안에서 `CombatFixedStepSystemGroup`, `SimulationEntrySystem`, `SimulationCompleteSystem`과 세션 Entity를 준비한다. 그룹 및 Stage에는 `DisableAutoCreation`을 사용하며, Executor가 등록한 그룹을 한 논리 틱에 **한 번 명시적으로 `Update()`**한다.

기능별 Stage는 준비 중 `RegisterStage<T>()`로 등록한다. Bootstrap은 `ConfigureExecution` 이벤트를 통해 이러한 등록을 허용한 다음 `Seal()`을 호출한다. `Seal()`은 Unity 그룹에 Entry → 기능별 Stage → Exit를 배치하고 `SortSystems()` 이후 실제 `ManagedSystems`의 소유권·순서를 검증해 `orderedStages`를 고정한다. 필수 의존 순서는 등록 호출 순서가 아니라 Unity의 `UpdateBefore/After` 선언으로 표현한다.

`SimulationEntrySystem`은 현재 Header와 문맥이 맞는지 검증하고 Entry Receipt를 표시한다. `SimulationCompleteSystem`은 Exit Receipt를 표시할 뿐 `SimulationProgress.Complete()`를 호출하지 않는다.

### 5.2 실행 직전 및 실행 중 검증

`Execute()`는 Progress의 열린 문맥과 Canonical Player Frame을 먼저 검증한다. 이후 ECS Header와 플레이어 버퍼를 설정하고, World의 현재 시간을 해당 논리 틱의 `EndTime`·`DeltaTime`으로 Push한다. 실행 후에는 이 시간 변경을 Pop하여 외부 World 시간을 복원한다.

그룹 실행 전 `ValidateGroupMembership()`은 실제 그룹을 정렬한 뒤 봉인된 배열과 멤버·순서가 같은지 검사한다. `CombatStepSystem.OnUpdate()`는 Executor의 `TryBeginStage()`를 통과해야 `OnStep()`을 실행한다. Stage가 실패하면 오류를 기록하고 뒤따르는 Stage의 Gameplay 실행을 차단한다. 최종적으로 필수 Stage가 모두 완료됐는지, Header가 그대로인지 확인한다.

한 번 Fault된 Executor를 같은 Generation의 정상 실행기로 되돌리지 않는다. 수리를 위해 필요한 자원을 보유·정리한 뒤 명시적 새 세션을 준비한다.

### 5.3 World 존재와 필수 Stage 등록은 다르다

다른 기능에 의존하는 Producer는 그 시스템이 World에 존재하는지만 검사해서는 안 된다. World에는 해당 Executor의 그룹에 등록되지 않은 관리 시스템도 존재할 수 있다.

필수 Resolve/Commit 같은 계약이 있다면 `CombatStepSystem.HasSealedStage<T>()`를 사용하여 **현재 Executor가 소유하고 봉인한 Stage**인지 확인한다. 이후 실제 실행·완료 여부는 Executor의 순서 검증이 책임진다.

현재 P1-B `AutoTargetingProducerSystem`은 Job과 Assignment 저장소를 준비하기 전에 `AutoTargetingResolveCommitSystem`의 봉인 멤버십을 요구한다. World에만 존재하거나 실행이 비활성화된 Resolver는 유효한 완료 틱을 만들 수 없다.

---

## 6. Job 의존성과 Gameplay 결과 확정

### 6.1 ECS Dependency와 외부 Job 구분

현재 `CombatStepSystem`은 `SystemBase` 기반이다. ECS 컴포넌트와 연결되는 비동기 Job의 선후관계는 일반적으로 `Dependency`에 맡긴다. **각 Stage 반환 시 모든 ECS Job을 전역적으로 강제 완료하지 않는다.** 이는 서로 독립된 계산의 병렬 실행 여지를 보존하기 위한 선택이다.

Stage가 외부 `NativeContainer`에 접근하는 Job을 예약하고 그 결과를 같은 Stage 안에서 읽어야 하면 `RegisterExternalStepJob(JobHandle)`로 명시적 Handle을 등록한다. 이 메서드는 외부 Handle을 추적하면서 ECS `Dependency`에도 연결한다. `OnStep()`의 끝에서는 등록된 외부 Job을 완료한 다음 `ValidateResult()`를 호출하고, Job 예외도 Stage 실패로 전달한다.

ECS 컴포넌트에 대한 Job의 결과를 다른 Stage가 읽는 경우에는 해당 컴포넌트의 의존성을 적절한 소비 지점에서 완료한다. P1-B `AutoTargetingResolveCommitSystem`은 `CombatTargetEvaluation`을 읽기 전에 `EntityManager.CompleteDependencyBeforeRO<CombatTargetEvaluation>()`를 호출한다.

### 6.2 Stage 완료와 권위 결과 준비의 차이

**Stage가 `OnStep()`을 반환하거나 그룹 전체 실행이 끝났다는 사실만으로 모든 ECS Job의 실제 실행·Gameplay 결과 유효성이 증명되지는 않는다.** 신규 전투 시스템은 다음 중 어떤 결과가 해당 Tick의 권위 완료에 필요한지 명시해야 한다.

- 그 틱에 확정해야 하는 Gameplay 결과: 소비 단계에서 필요한 Job 의존성 완료, Generation/Tick/Ordinal·데이터 유효성 검증 후 Commit
- 다음 Tick에 이어질 수 있는 계산: 소비 시점 및 자원 수명 계약을 명시하고 해당 Tick 완료와 충돌하지 않음을 증명
- 권위 결과와 분리된 시각 표현: 권위 완료 조건과 혼동하지 않으며 별도 소유권을 정함

`ARCH-100`의 **Parallel Calculate → Stable Resolve → Commit**은 Gameplay 결과의 논리·결정론 계약이고, ARCH-110은 이를 실행할 때 Job의 결과 소비·완료 경계를 제공한다. 어떤 게임플레이 사건을 어떤 우선순위로 확정하는지는 기능 Owner와 ARCH-100이 결정한다.

### 6.3 미사일 적용 시 요구

미사일 M1에서 Emitter·Salvo 공유 Target Sample·Guidance·Movement를 Stage로 구성할 수 있다. 하지만 이 문서는 실제 미사일 Stage 클래스나 Phase가 이미 존재한다고 가정하지 않는다.

미사일 개발에서 확인할 사항은 **해당 Tick의 Missile Gameplay 상태와 Hit/Detonation 요청 중 무엇이 권위 완료 전에 읽히고 확정되어야 하는지**이다. Job만 예약하고 후속 소비자 없이 완료 틱을 기록하도록 설계하지 않는다. 특정 의존성의 동기화는 결과 소비에 필요한 범위로 제한한다. Missile FSM·Salvo Target 정책·피격 규칙·처리 빈도는 SYS-130과 ARCH-100을 따른다.

---

## 7. 로컬 세션 생명주기·실패·복구

### 7.1 정상 전이

`SimulationLifecycle`은 `Uninitialized`, `Preparing`, `Ready`, `Running`, `Paused`, `Stopping`, `Faulted`, `Stopped`를 사용한다. 구성 준비와 참가자 바인딩, 입력 유효화가 끝나면 `Ready`가 되고, 명시적 시작 요청 또는 AutoStart 조건에서 `Running`으로 전환한다.

Pause·Stop·Restart 요청은 열린 스텝 도중 즉시 자원을 해제하지 않고 안전한 경계에서 처리한다. Pause 시 Flight·물리 몸체를 정지 상태로 유지하고 입력을 비활성화하며, Resume 시 유효한 새 입력을 요구한다. 재시작은 새 Generation과 초기 기준 물리·Flight 상태를 준비하는 로컬 세션 재준비다. 정상 Stop 이후에도 소유 객체의 수명에 따라 정리·재시작 단계가 구분된다.

`LocalTimingOwner`는 실제로 적용한 Unity 전역 설정을 추적한다. 외부에서 값이 변경되면 자신이 여전히 소유한다고 가정하여 덮어쓰지 않고 검증 실패 또는 조건부 복원을 수행한다. 현재 로컬 실행기는 전역 타이밍 소유자가 하나라는 전제를 갖는다.

### 7.2 Fault와 Quarantine

훅 순서·설정·입력·물리·Snapshot·ECS 실행·World 수명 검증에 실패하면 `LocalSimulationBootstrap.Fail()`이 최초 오류 정보를 보존하고 Progress를 Abort하며 Executor를 무효화한다. 플레이어의 관리 실행과 입력 게시를 중단하고 물리 몸체를 보존 상태로 전환한다. 실패한 틱의 CompletedOrdinal은 증가하지 않는다.

이 동작은 **실패한 ECS 결과의 권위 완료를 막고 추가 실행을 정지**시키기 위한 것이며, 이전 PhysX 충돌이나 모든 월드 상태를 역행시키는 원자적 롤백은 아니다.

### 7.3 Stop/Destroy와 정리 실패

`SessionResources`는 PlayerLoop 콜백 분리, Executor/Stage 해제, 외부 Frame 해제, 참가자 바인딩·물리 상태 복원, `LocalTimingOwner` 반환 순서를 관리한다. 현재 실행 중인 Driver/Executor는 즉시 해제할 수 없으며, 앞 단계의 해제 실패 시 뒤따르는 소유 자원도 무조건 버리지 않는다.

Bootstrap이 파괴되더라도 완료되지 않은 정리 작업은 `LocalCleanupRecovery`로 이전된다. `LocalCleanupRecoveryHost`는 `LateUpdate`에서 재시도할 수 있고, 미정리 자원이 남아 있으면 새로운 세션 생성을 막는다. `timeScale == 0`에서도 복구 경로가 유지되는 이유는 실패 상태가 일시정지로 인해 영구히 방치되지 않도록 하기 위함이다.

Recovery는 오류가 자동으로 반드시 해결된다는 약속이 아니다. 외부에서 훼손한 Unity 환경 또는 자원을 먼저 복구해야 할 수 있고, 재시도 성공 전에는 세션 재개를 승인하지 않는다.

---

## 8. 새로운 Gameplay Stage 통합 지침

새 기능은 전투 로직의 소유 문서를 먼저 확인한 뒤 기존 Executor 계약에 연결한다. 권위 완료 루틴이나 고유 PlayerLoop 스케줄러를 기능별로 새로 만들지 않는다.

### 8.1 구현·등록 절차

1. 기능 Owner(`SYS-120`, `SYS-130` 등)와 `ARCH-100`에서 필요한 입력·결과·논리 Phase를 결정한다.
2. ECS Stage가 필요하면 `CombatStepSystem`을 상속하고 `OnStep()`에 계산·예약 또는 Resolve 작업을 구현한다. 해당 Stage를 외부에서 자동 업데이트하지 않도록 현재 그룹 규칙을 유지한다.
3. 실제 필요한 선후관계를 `UpdateAfter` / `UpdateBefore`로 선언한다. **등록 순서 자체에 의존하지 않는다.**
4. 준비 중인 Bootstrap의 `ConfigureExecution`에서 `RegisterStage<T>()`를 호출한다. `Seal()` 이후 등록을 시도하지 않는다.
5. 필수 Producer/Resolver 관계가 있으면 `HasSealedStage<T>()`로 동일 Executor 소유·봉인 등록을 확인한다.
6. ECS Job은 `Dependency`로 연결하고, 외부 `NativeContainer` Job은 `RegisterExternalStepJob()` 등 현행 명시적 수명 계약을 따른다.
7. 해당 Tick에서 읽고 확정해야 하는 결과는 필요한 Job 완료 후 Generation/Tick/Ordinal 및 데이터 의미를 검증한다. 결과 판정 실패는 Exception을 통해 Fail-stop으로 전달한다.
8. Exit 도달은 완료 승인이 아니다. Bootstrap의 `ValidatePending` → `SimulationProgress.Complete` → Acknowledge 경계를 그대로 사용한다.
9. 정상·누락·비활성 Stage, 오래된/오염된 결과, Stop·Destroy 중 Job, 틱 완료 차단을 해당 기능의 EditMode/PlayMode로 검증한다.

현재 자동 타게팅의 **실제 등록 패턴**은 다음과 같다. 이 코드는 등록 위치의 예시이며, 현재 Stage가 다른 시스템에서 자동으로 등록되는 것을 의미하지 않는다.

```csharp
// LocalSimulationBootstrap.ConfigureExecution 구독자의 준비 단계
executor.RegisterStage<AutoTargetingProducerSystem>();
executor.RegisterStage<AutoTargetingResolveCommitSystem>();
// Bootstrap이 모든 구독자 등록 후 executor.Seal()을 호출한다.
```

필수 Stage 등록 검증과 결과 소비는 각각 다음 API로 확인할 수 있다.

```csharp
// Producer의 OnStep(), Job 예약 전에 검사
if (!HasSealedStage<AutoTargetingResolveCommitSystem>())
    throw new InvalidOperationException("Required Resolver is not sealed.");

// Resolver의 OnStep(), ECS 결과 읽기 전에 의존성 완료
EntityManager.CompleteDependencyBeforeRO<CombatTargetEvaluation>();
```

위 예시는 **시스템 통합 패턴**만 설명한다. 타게팅 후보 선정, 점수, Stable ID 동률 처리의 Owner는 SYS-120 및 ARCH-100이다. 미사일은 이 패턴의 권위·의존성 경계를 재사용하되 타게팅 P1-B의 매 틱 전체 후보 수집 방식을 그대로 일반화하지 않는다.

### 8.2 통합 시 금지되는 오해

- World에 시스템이 있다는 이유만으로 필수 Stage 등록 완료라고 판단하지 않는다.
- ECS 그룹이 마지막 Stage까지 실행되었다는 이유만으로 `SimulationProgress`를 우회하거나 별도의 CompletedTick을 기록하지 않는다.
- 같은 단계에서 필요하지 않은 모든 ECS Job을 강제로 완료하지 않는다.
- `SimulationCompleteSystem`을 Gameplay Commit의 권위 소유자로 만들지 않는다.
- 미구현된 전투 Phase·네트워크 예측·Target Registry를 이미 공용 타이밍의 기능인 것처럼 의존하지 않는다.
- 정리 실패를 무시하고 미완료 Job의 데이터나 World 소유 자원을 먼저 파괴하지 않는다.

---

## 9. 검증 근거, 비용과 현재 한계

### 9.1 확인된 범위

[공용 타이밍 및 P1-B 검증 기록](../../../Tools/Validation/P1B/verification-summary.md)은 2026-10-15의 **Base HEAD `58d003c` + 미커밋 패치**를 대상으로 수행한 Unity 실행 결과를 기록한다. 해당 변경은 이후 `72f3cebb` 안정화 커밋에 반영되었지만, 검증 기록에 쓰인 Base HEAD만으로 전체 테스트 소스를 재현할 수 있다고 표현하지 않는다. 본 문서를 작성하면서 Unity를 새로 실행한 것은 아니다.

| 대상 | 기존 검증 기록 |
|---|---|
| Unity Editor | 6000.3.23f1, 재컴파일 오류 0 |
| 공용 Executor EditMode | 24/24 통과 |
| P1-B 자동 타게팅 EditMode | 7/7 통과 |
| 전체 EditMode | 49/49 통과 |
| P1-A Job 수명 + P1-B Bootstrap PlayMode | 관련 `LocalTimingLifecycleTests` 48/48 통과 |
| 전체 PlayMode | 51/51 통과 |
| TimingCost | 50/60/120Hz 각각 4인·100 Fixed Step 기반 측정 통과, **해당 측정 구간의** managed GC allocation 0 |
| 실패 계약 | 필수 Resolver 누락·비활성·World에만 존재·오염된 결과에서 완료 전 Fault 경로 검증 |

이 결과는 **정해진 테스트와 로컬 Editor 환경에서의 안정화 근거**다. Player Build에서의 동일한 성능이나 전체 Gameplay의 결정론을 자동 보증하지 않는다.

### 9.2 선택에 따른 비용

- **CPU:** 매 전투 실행 전 그룹 정렬·멤버십 검증, Canonical Frame 검사, Stage 완료 검사가 발생한다. 실제 `SortSystems()`의 독립 비용은 대규모 Stage 구성에서 측정하지 않았다.
- **동기화:** 권위 결과를 읽는 Resolve/Commit 지점에는 해당 Job 의존성 완료 비용이 발생할 수 있다. 병렬성은 사용 데이터와 동기화 경계에 따라 달라진다.
- **메모리·할당:** 공용 타이밍 기존 4인 TimingCost 경로에서 고정 스텝 managed GC 0을 검증했다. 실제 자동 타게팅 활성 세션의 별도 CPU·GC 및 대량 미사일 실행 비용은 이 결과에 포함되지 않는다.
- **유지보수·검증:** PlayerLoop 훅 검증, Executor 소유권, 실패한 Cleanup을 보존하는 복구 코드와 관련 테스트를 유지해야 한다. 대신 실패를 조용히 권위 완료로 바꾸지 않도록 경계를 명시한다.

### 9.3 보장하지 않는 것과 후속 처리

| 항목 | 현행 상태와 다음 행동 |
|---|---|
| 완전한 네트워크 권위·예측·롤백 | **미구현.** Host/Client 실행 모델 확정 시 로컬 어댑터와 분리해 설계한다 |
| 서로 다른 기기의 완전 동일 PhysX 결과 | **비목표.** ARCH-100의 최소 Gameplay 결정론 경계를 따른다 |
| 전체 전투 Phase / Timing Wheel | **미구현·부분 설계.** ARCH-100의 Phase·주기 정책을 기능 구현 시 연결한다 |
| 미사일 대량 실행 성능 | **미검증.** SYS-130 구현 중 실제 Target Sampling, Guidance, Spawn/Despawn 부하를 측정한다 |
| 타게팅 대규모 후보 처리 | P1-B의 매 틱 전체 ECS 후보 `TempJob` 배열 평가를 최종 후보 인프라로 일반화하지 않는다. Broadphase·빈도 분리는 ARCH-100/SYS-120에 따른다 |
| 중복·재사용 Stable ID 및 대상 제거 | 일반 Target Registry와 공급자 정책을 확정·검증해야 한다. ARCH-100 Owner |
| Gameplay Input Edge 소비 | 현재는 미소비 Edge가 있으면 Tick 시작 거부. 소비자·ACK 계약을 해당 입력 기능 추가 시 구현한다 |
| 테스트 전용 Authority Stage의 World 존재 검사 | 일반화된 권위 Stage 검증에 재사용하기 전에 봉인 멤버십 검사와 회귀 테스트를 보강한다 |
| ECS Stage 증가 시 `SortSystems()` 비용 | 현재 측정 근거 없이 최적화하지 않는다 |
| Trace/Replay | Canonical Player Frame과 완료 여부를 기록·ECS 입력으로 복원한다. **물리·전투 월드 전체를 재시뮬레이션하거나 롤백하는 시스템이 아니다** |

미완성 기능을 보완하려고 공용 타이밍에 범용 Target Registry, 독자 전투 스케줄러, 전역 결과 장부 또는 네트워크 예측 시스템을 미리 추가하지 않는다. 요구가 실제 기능과 측정으로 확인되면 해당 Owner와 이 문서의 인터페이스를 함께 갱신한다.

## 10. 구현 및 검증 진입점

이 문서의 구현 설명은 다음 현행 소스를 기준으로 한다.

| 관심사 | 코드 |
|---|---|
| 시간 문맥·완료 권위 | [`SimulationTypes.cs`](../../../Assets/Scripts/Core/Timing/SimulationTypes.cs), [`SimulationProgress.cs`](../../../Assets/Scripts/Core/Timing/SimulationProgress.cs), [`TickMath.cs`](../../../Assets/Scripts/Core/Timing/TickMath.cs) |
| 로컬 설정·시간 소유 | [`SimulationTimingSettings.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/SimulationTimingSettings.cs), [`LocalTimingOwner.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/LocalTimingOwner.cs) |
| PlayerLoop·정리 복구 | [`LocalFixedStepDriver.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/LocalFixedStepDriver.cs), [`LocalCleanupRecoveryHost.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/LocalCleanupRecoveryHost.cs) |
| 세션·Canonical Frame | [`LocalSimulationBootstrap.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/LocalSimulationBootstrap.cs), [`LocalPlayerStatePublisher.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/LocalPlayerStatePublisher.cs) |
| ECS 실행기·Stage | [`CombatStepExecutor.cs`](../../../Assets/Scripts/Simulation/Timing/CombatStepExecutor.cs), [`CombatStepSystem.cs`](../../../Assets/Scripts/Simulation/Timing/CombatStepSystem.cs), [`CombatFixedStepSystemGroup.cs`](../../../Assets/Scripts/Simulation/Timing/CombatFixedStepSystemGroup.cs) |
| ECS 문맥·입출력 | [`SimulationStepComponents.cs`](../../../Assets/Scripts/Simulation/Timing/SimulationStepComponents.cs), [`CanonicalPlayerFrameValidator.cs`](../../../Assets/Scripts/Simulation/Timing/CanonicalPlayerFrameValidator.cs) |
| 기능별 통합 예시 | [`AutoTargetingSystems.cs`](../../../Assets/Scripts/Simulation/Timing/AutoTargetingSystems.cs) |
| Trace·Replay | [`SimulationTraceBuffer.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/SimulationTraceBuffer.cs), [`SimulationReplayStepSource.cs`](../../../Assets/Scripts/Runtime/LocalSimulation/SimulationReplayStepSource.cs) |
| 회귀 테스트 | [`TimingCoreTests.cs`](../../../Assets/Tests/EditMode/TimingCoreTests.cs), [`TimingHookTests.cs`](../../../Assets/Tests/EditMode/TimingHookTests.cs), [`CombatExecutorTests.cs`](../../../Assets/Tests/EditMode/CombatExecutorTests.cs), [`AutoTargetingTests.cs`](../../../Assets/Tests/EditMode/AutoTargetingTests.cs), [`LocalTimingLifecycleTests.cs`](../../../Assets/Tests/PlayMode/LocalTimingLifecycleTests.cs), [`TimingCostTests.cs`](../../../Assets/Tests/PlayMode/TimingCostTests.cs) |

문서 작성·Owner·Revision·근거 관리 원칙은 [DOCS-000](../../00_Management/DOCS-000_문서관리_운영가이드.md)을 따른다. Git·검증 절차는 [CONTRIBUTING](../../00_Management/CONTRIBUTING.md)을 따른다.
