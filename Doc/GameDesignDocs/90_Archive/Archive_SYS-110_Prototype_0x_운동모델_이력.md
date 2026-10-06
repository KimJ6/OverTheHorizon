---
document_id: ARCHIVE-SYS-110-PROTOTYPE-0X
title: 플레이어 기동 Prototype 0.x 운동모델 이력
document_type: archive
status: archive
last_updated: '2026-09-25'
canonical_replacement: SYS-110
source_documents:
- 전술기형_플레이어_운동모델_설계정리_v0.6
---

# Archive — 플레이어 기동 Prototype 0.x 운동모델 이력

> **Archive:** 현재 구현 요구사항이 아니다. Prototype 0~0.5의 시도·관찰·폐기 이유와 이후 재평가를 보존하며, 현재 기동 규칙은 [SYS-110 플레이어 기동](../20_Systems/SYS-110_플레이어_기동.md)을 따른다.

## 0. 보존 범위

이 문서는 과거 Prototype의 폐기 이유, 구조적 문제의 재발 방지, Control Law 변경 근거와 현재 Canon의 차이를 확인할 때만 사용한다.

---

## 1. 당시 Prototype 진행과 관찰

### 1.1 Prototype 0 — Assisted Rigidbody / AddForce

기본 AddForce 기반 assisted Rigidbody 방식을 사용했다.

확인된 문제:

- 관성이 지나치게 강함
- 기체가 현재 플레이어 입력보다 기존 운동에 끌려다님
- 고속 방향 전환이 둔함

이 단계에서 단순 물리 가속만으로는 목표한 전술기형 고권한 기동을 만들기 어렵다는 문제가 드러났다.

### 1.2 Prototype 0.2 — Vector Command

WASDRF 입력으로 Desired Direction을 만들고 전체 Velocity 방향을 적극적으로 회전하는 구조를 사용했다.

개념:

```text
Vector3.RotateTowards(
    CurrentVelocityDirection,
    DesiredDirection
)
```

장점:

- 기민한 방향 전환
- 입력 직후 화면상 반응이 명확함
- A/D/R/F의 권한이 강하게 느껴짐

문제:

- 주추력축의 의미가 약해짐
- W와 A가 사실상 동일한 Vector 입력이 됨
- Main Propulsion의 존재감이 사라짐
- 전체 Velocity를 직접 지배해 캐릭터 컨트롤러에 가까워짐

### 1.3 Prototype 0.3 — Main + Maneuver 초기 분리

Main과 Maneuver를 분리하려 했지만 실제 계산에서는 다시 하나의 Desired Velocity로 합쳐졌다.

문제:

- Main과 Maneuver의 역할이 충분히 분리되지 않음
- Redirect 단계가 전체 `linearVelocity`를 직접 수정
- 관성과 추진의 역할이 모호함

### 1.4 Prototype 0.4 — Transient / Sustain

Transient 단계에서 전체 Velocity를 빠르게 재지향하고 이후 Sustain 단계에서 ΔV를 적용하는 구조였다.

문제:

- Transient 결과가 새로운 관성으로 사실상 확정됨
- 기동 이벤트가 운동 상태를 영구적으로 바꿈
- KeyDown 이벤트 중심의 운동 설계가 됨

### 1.5 Prototype 0.5 — Persistent + FCS-owned Controlled Motion

다음 상태를 분리했다.

```text
ActualVelocity =
PersistentVelocity
+ LateralContribution
+ VerticalContribution
+ RevectorCorrection
```

의도:

- 기존 관성 유지
- A/D/R/F contribution은 입력 해제 시 회수
- Transient는 차이 보정만 담당
- All Release 시 Actual을 새 Persistent로 commit

그러나 실제 테스트에서 여러 구조적 문제가 확인되었다.

---

## 2. Prototype 0.5에서 관찰한 실패 원인

### 2.1 Revector가 사실상 Velocity Lerp

Revector가 다음 형태이면:

```text
Revector =
(DesiredActualVelocity - BaseVelocity)
× Authority
```

최종 결과는 사실상:

```text
Actual = Base + (Desired - Base) × Authority
```

즉 `Lerp(Base, Desired, Authority)`와 유사해진다.

Authority가 시간에 따라 감소하면:

```text
초기에는 Command 방향
→ 시간 경과
→ 다시 Persistent 방향
```

으로 끌려간다.

### 2.2 Hold와 연타의 불일치

Revector가 Press/Retarget 이벤트에서 재시작되면:

```text
D Hold
→ Revector 1회
→ 이후 Sustain
```

반면:

```text
D 연타
→ KeyDown마다 Revector 재시작
```

되어 입력 빈도가 사실상 추가 추진력처럼 작동한다.

### 2.3 All Release가 Transient 결과를 Persistent로 승격

모든 이동 입력을 놓을 때:

```text
Persistent = Current Actual
```

로 Rebase하면 Revector가 강하게 작동한 순간의 임시 결과가 영구적인 Persistent 운동으로 저장된다.

연타와 결합하면:

```text
강한 Revector
→ Release
→ Persistent 갱신
→ Revector 재시작
```

이라는 exploit이 가능해진다.

### 2.4 Main Propulsion이 기존 횡관성을 회수하지 못함

다음 상태에서:

```text
Facing ↑
Persistent ←
W Hold
```

주추력이 단순히 Forward 속도만 더하면 결과는 장기간 대각선으로 남을 수 있다.

```text
Velocity ↖
```

즉 Main Propulsion이 장기 운동을 지배한다는 감각을 만들지 못한다.

### 2.5 Persistent 우선 Hard Cap

속도 상한에서 Persistent를 먼저 보존하고 Controlled Motion을 줄이면:

```text
과거 관성
>
현재 플레이어 Command
```

라는 우선순위가 된다.

고속일수록 현재 방향 전환이 어려워지는 문제가 발생했다.

### 2.6 Newly Activated Axis 중심 Revector

W를 누른 상태에서 A를 추가할 때 전체 현재 Command가 아니라 새로 활성화된 A만 Revector 목표로 쓰면:

```text
W → A
```

와:

```text
A → W
```

의 결과가 크게 달라진다.

운동 결과가 현재 입력보다 과거 입력 순서에 의존하게 된다.

---

## 3. 당시 폐기한 제어 패턴

다음 패턴은 현재 Canon에서 사용하지 않는다.

### 3.1 KeyDown 기반 Burst Authority

```text
KeyDown
→ 일정 시간 강한 Revector
```

폐기 이유:

- 입력 빈도가 추진력을 만듦
- Hold와 연타가 비정상적으로 달라짐

### 3.2 고정 시간 Transient / Sustain

```text
입력 후 일정 시간 강한 제어
→ 이후 자동으로 약화
```

폐기 이유:

- 실제 Motion Error와 무관하게 Authority가 감소
- Command를 계속 유지해도 기존 관성으로 되끌릴 수 있음

### 3.3 장기 Hidden Velocity Contribution

```text
LateralContribution
VerticalContribution
RevectorCorrection
```

등 입력별 Velocity 성분을 별도 보관하는 구조.

폐기 이유:

- Release 시 어떤 상태를 제거/보존할지 복잡해짐
- 실제 Rigidbody Velocity와 내부 상태를 계속 동기화해야 함
- 관성 표현을 위해 실제 Velocity 외에 중복 상태를 유지하게 됨

### 3.4 Input Order Dependency

동일한 Current State와 Current Command에서 과거 입력 순서에 따라 결과가 달라지는 구조.

폐기 이유:

- 조작 예측 가능성 저하
- 신규 축 Press가 추가 에너지처럼 작동할 가능성

---

## 4. 당시 정리한 교훈과 현재 재평가

Prototype 0.5 폐기 시점에는 다음 대응 방향을 승계 대상으로 정리했다. 이 표는 당시 판단을 보존한 것이며 현재 규칙은 §4.1과 `SYS-110`을 따른다.

| 과거 문제 | 당시 대응 방향 |
|---|---|
| Body 회전이 운동을 암묵적으로 끌고 감 | Facing / Velocity 독립 |
| AddForce만으로 반대 관성 탈출이 느림 | High-authority Maneuver |
| Vector Command가 주추력 의미 제거 | W/S Axial과 A/D/R/F Maneuver 분리 |
| KeyDown Burst | Error-driven Authority |
| Hold vs 연타 exploit | No Press Bonus |
| Contribution 제거 시 discontinuity | Single Physical Velocity + Partial Release 재계산 |
| All Release Rebase exploit | Release 순간 Velocity 유지 |
| Persistent 우선 Hard Cap | Hard Cap에서도 Steering Authority 보존 |
| 입력 순서 의존 | Command Order Independence |

이 표는 과거 설계를 현재 구현에 다시 도입하기 위한 것이 아니라, 어떤 실패를 방지하려 했는지 추적하기 위한 기록이다.

### 4.1 2026-09-25 Baseline 재평가

2026-09-25 후속 직접 조작 검증에서 통합 3D Command를 사용하는 Flight Prototype의 전체 조작감이 승인되었다.

해당 Baseline은 외형상 Prototype 0.2의 Vector Command와 일부 유사하지만 다음 조건이 다르다.

- Press 횟수에 따른 Burst Authority를 사용하지 않는다.
- 과거 입력 순서나 Newly Activated Axis를 운동 에너지로 사용하지 않는다.
- WASDRF 전체 현재 상태를 매 FixedStep 하나의 Command로 합성한다.
- 복합 입력을 정규화해 축 개수에 따른 속력 증가를 방지한다.
- 방향 재지정과 ControlledSpeed 가속·감속을 서로 다른 규칙으로 관리한다.
- ControlledVelocity와 GravityVelocity의 소유권을 분리한다.
- SteeringReference, CameraView, BodyFacing을 분리한다.
- 입력 해제 시 마지막 방향을 유지하되 ControlledSpeed와 PropulsionAuthority를 감쇠한다.

따라서 과거의 “Vector Command가 주추력 의미를 약화한다”는 평가는 당시 Prototype의 관찰로 보존하되, 통합 방향 Command 자체를 금지하는 근거로 사용하지 않는다.

당시 승인된 규칙과 검증 항목은 `SYS-110 revision 2.0`에 반영되었다. 이 재평가는 과거 테스트 결과를 삭제하거나 Prototype 0.2를 그대로 복원한다는 의미가 아니다.

---

## 5. 현재 사용 범위

PersistentVelocity 폐기, KeyDown Revector, Hold·연타 exploit, Prototype 0.2와 현행 구조의 차이를 추적할 때 이 문서를 사용한다. 현재 입력·Authority·Combat/Cruise·카메라·추진익 규칙은 `SYS-110`을 우선한다.
