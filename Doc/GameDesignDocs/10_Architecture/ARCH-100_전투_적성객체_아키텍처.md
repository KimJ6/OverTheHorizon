---
document_id: ARCH-100
title: 전투·적성 객체 아키텍처
document_type: architecture
status: canonical
revision: 1.1
last_updated: '2026-10-18'
scope:
- combat runtime
- encounter activation
- hostile shared context
- target infrastructure
- attack execution
- hit damage death pipeline
- authority determinism replication
- lifecycle memory visual separation
- performance budget profiling
depends_on:
- PRJ-000
interfaces:
- ARCH-110
- SYS-120
- SYS-130
- SYS-160
source_documents:
- 전투_적성객체_아키텍처_설계_v0.1.md
---

# ARCH-100 전투·적성 객체 아키텍처

> **한 문장 정의:** 플레이어 상태, Encounter 활성화, Group/Salvo 공통 판단, Target 인프라, 공격 실행, Gameplay 결과 확정, 시각 표현을 분리하여 대량 적성 객체를 낮은 연산비용과 예측 가능한 부하로 운용하고 Host 기반 멀티플레이에서 필요한 최소 결정론을 유지한다.

## 0. 문서 계약

### 0.1 이 문서가 결정하는 것

본 문서는 여러 전투 시스템이 공통으로 따라야 하는 **Runtime 구조와 책임 경계**를 결정한다. Encounter 활성화, Group·Salvo 공유 판단, Target·공격·피해 Pipeline, 생명주기와 메모리, Host 권위·Replication, Gameplay·Visual 분리, 성능 Budget과 검증 기준을 소유한다.

### 0.2 이 문서가 결정하지 않는 것

플레이어 운동, 무기별 Target Selection, Missile Guidance, 개별 적의 전술·무장·수치는 각 System이 결정한다. Group·Budget·Pool·Simulation LOD의 최종값과 Network·Rendering 구현 경로는 프로파일링과 구현 단계에서 결정한다.

로컬 고정 스텝의 PlayerLoop·Physics 연결, ECS Stage의 실제 실행 순서 검증, Job 결과 소비 경계, `SimulationProgress`의 완료 기록과 정리 복구는 [ARCH-110](ARCH-110_공용_타이밍_실행_계약.md)이 소유한다. 본 문서는 그 실행 기반 위에서 어떤 Gameplay 결과를 어떤 논리 Phase와 권위 정책으로 확정할지를 결정한다.

### 0.3 관련 Owner

- 프로젝트 목표와 공통 기술 방향: [PRJ-000](../00_Project/PRJ-000_프로젝트_통합기획_기준서.md)
- 공용 고정 스텝 실행·완료 기록·ECS 실행 수명주기: [ARCH-110](ARCH-110_공용_타이밍_실행_계약.md)
- 플레이어 무기의 후보 평가와 Target 선택: [SYS-120](../20_Systems/SYS-120_자동타게팅_직사화기.md)
- Missile 행동·Guidance·요격·연출: [SYS-130](../20_Systems/SYS-130_미사일_이타노서커스.md)
- 군도 구획의 Gameplay 의미와 보상·작전 상태: [SYS-160](../20_Systems/SYS-160_군도작전_전방지원.md)

이 문서의 `Zone`은 Encounter Activation·Spawn 단위다. 군도 작전 구획의 의미를 소유하지 않으며, 하위 System은 공통 Registry·Authority·Damage Pipeline·Visual Buffer를 별도로 재정의하지 않는다.

---

## 1. 아키텍처 목표

### 1.1 최상위 성능 목표

대량 객체 환경에서는 개체 하나의 연산을 미세하게 빠르게 만드는 것보다 **개체가 수행해야 하는 연산의 종류와 횟수 자체를 줄이는 것**을 우선한다.

```text
Player
→ 상태를 Tick당 한 번 수집해 공유

Zone / Spawn
→ 낮은 빈도로 활성화 판단

Group / Salvo
→ 낮은 빈도로 공통 Target 판단

Active Entity
→ 개체별로 필요한 이동·유도만 수행

Attack Ready
→ Budget으로 고비용 공격 실행량 제한

Gameplay Result
→ Host가 권위적으로 확정

Visual
→ Gameplay에서 분리하고 반복 작업은 GPU 우선 검토
```

### 1.2 구조적 목표

| 항목 | 목표 |
|---|---|
| 개별 적의 Player GameObject/Transform 탐색 | 0 |
| Player 원본 상태 수집 | Player당 Simulation Tick 1회 |
| 개별 적의 Player 후보 순회 | 0 |
| 개별 적의 독립 Target Selection | 기본 금지 |
| Dormant Encounter AI | 0 |
| Dormant SpawnSource 지속 Polling | 0 |
| Target 재평가 | Movement보다 낮은 빈도 |
| Spatial Index 갱신 | 필요 시 또는 rebuild tick |
| 실제 Projectile Entity | 공간적 존재가 Gameplay에 필요한 공격만 |
| 고비용 Attack/Hit 처리 | 최대 처리량을 Budget으로 제한 |
| Combat Hot Path managed allocation | 0 B 목표 |
| Gameplay Entity : VFX Object | 1:1 대응 금지 방향 |
| Client 독립 Hit 판정 | 0 |
| Query 반환 순서에 의존하는 Gameplay 결과 | 0 |

ECS, Burst, Jobs를 적용하기 전에 불필요한 판단을 제거하고 대상·빈도를 줄이며 공통 결과를 공유한다. 병렬화는 남은 Hot Data 처리에 사용하고, 반복 시각 작업은 Gameplay와 분리해 GPU 이전을 검토한다.

### 1.3 Architecture Invariants

다음 구조가 나타나면 Architecture를 재검토한다.

- 모든 적끼리 거리 비교하여 Group 생성
- 모든 Missile끼리 유폭 검사
- 모든 Entity 간 수동 Collision 검사
- 모든 Group끼리 split/merge 비교
- Player마다 전체 Target 순회
- 매 Activation Tick 전체 Zone 순회
- 모든 저빈도 대상을 매 Tick Polling한 뒤 due 여부만 검사

대안은 Spatial Broadphase, Spawn/Group relation, Event, Stable Index, Timing Bucket을 우선한다.

---

## 2. 전체 Runtime 구조

### 2.1 책임 흐름

```text
                         Player 1 ... 4
                               │
                               ▼
                    Host Canonical PlayerState
                               │
         ┌─────────────────────┼─────────────────────┐
         ▼                     ▼                     ▼
 Encounter Activation    Group / Salvo         Player Weapon
  Zone / SpawnSource     Target Context        Targeting SYS
         │                     │                     │
         ▼                     ▼                     │
    Spawn Request           Target Plan               │
         └──────────────┬──────┴──────────────────────┘
                        ▼
                 Active Gameplay Entities
             ┌──────────┼───────────┐
             ▼          ▼           ▼
           Drone      Missile     Ship / etc.
             │          │
             └──────┬───┘
                    ▼
               Attack Ready
                    ▼
             Ready Attack Queue
                    ▼
          Combat Execution Budget
                    ▼
               Attack Permit
                    ▼
               Attack Resolve
                    ▼
             Authoritative Hit
                    ▼
       Damage / Detonation Reduction
                    ▼
          Death / State Commit
             ┌──────┴───────┐
             ▼              ▼
       Network State   Visual State Buffer
                            ▼
                            GPU
```

각 계층은 자신의 책임만 수행한다. 개별 Entity가 Player 탐색부터 Target 선택, 공격 결과 확정, VFX 생성까지 모두 담당하는 구조는 피한다.

### 2.2 Host Fixed-Tick Phase

Gameplay 결과에 영향을 주는 Phase 순서를 명시한다.

이 순서는 Gameplay 결과의 논리적 Phase 모델이다. 로컬 Unity PlayerLoop에서 이 Phase를 언제 실행하고 완료로 기록하는지는 [ARCH-110](ARCH-110_공용_타이밍_실행_계약.md)의 실행 계약을 따른다.

```text
──────────── Simulation Tick N ────────────

1. Network Input Accept
2. Canonical PlayerState Snapshot

3. Due Activation / Dirty Encounter Resolve
4. Spawn Request Resolve

5. Due Group Target Evaluation
6. Due Salvo Target Sample

7. Movement / Guidance
   ├─ Drone
   ├─ Missile
   ├─ Ship
   └─ World Projectile

8. Spatial Membership Update / Rebuild

9. Attack Ready transition
10. Ready Queue / Combat Budget
11. Attack Permit Resolve

12. Attack Resolve
13. Hit Event Gather

14. Damage / Detonation Reduction
15. Damage Commit
16. Death / Despawn Commit

17. Group / Encounter State Commit

18. Authoritative Gameplay Event Build
19. Network Snapshot Build
20. Visual State Buffer Build

──────────── Tick N End ────────────
```

이 순서는 System ordering 버그를 줄이고, 결정론 검증과 Profiling 구간, Network Capture 시점을 명확하게 한다.

---

## 3. 시간 모델과 Scheduling

### 3.1 연산 빈도 분리

```text
Movement Tick
≠ AI Tick
≠ Target Tick
≠ Activation Tick
≠ Network Snapshot Tick
```

모든 시스템을 60 Hz로 실행하지 않는다. Movement가 고빈도라고 해서 Target Selection이나 Encounter Activation까지 동일 빈도로 실행할 이유는 없다.

### 3.2 Prototype 시작값

다음 값은 **최종값이 아니라 프로파일링용 시작점**이다.

| 시스템 | 시작 기준 |
|---|---:|
| Host Simulation Tick | 60 Hz |
| PlayerState Snapshot | 60 Hz |
| 근접 Drone 위치 적분 | 60 Hz |
| Drone Steering | 20~30 Hz |
| Group 전술 판단 | 약 10 Hz |
| Group Target 재평가 | 5~10 Hz |
| Missile 위치 적분 | 60 Hz |
| Missile Guidance | 60 Hz 또는 상태별 차등 |
| Salvo Target Sample | 15~20 Hz |
| Zone Activation | 약 5 Hz |
| Player Weapon Targeting | 10~20 Hz 후보 |
| Static Weapon Target 평가 | 5~10 Hz |
| SpawnSource | Event / Due Tick |
| Spatial membership | Cell 변화 또는 rebuild tick |
| Network Snapshot | 별도 저빈도 |

이 표의 60Hz는 대량 전투 Profiling을 위한 시작 후보이며, 현재 로컬 실행기의 기본값이나 지원 TickRate 계약은 ARCH-110을 따른다.

### 3.3 Timing Bucket / Timing Wheel

저빈도 시스템은 전체 대상을 매 Tick 순회하며 `if due`를 검사하지 않는다.

예를 들어 60 Hz Simulation에서 5 Hz 평가라면 12개 Phase로 분산할 수 있다.

```text
TargetBucket[0]
TargetBucket[1]
...
TargetBucket[11]

bucketIndex = SimulationTick % 12
```

적용 대상:

- Hostile Group Target Update
- Salvo Target Sample
- Static Weapon Evaluation
- AI Think
- SpawnSource Due Evaluation
- 일부 Zone 재평가

Stable ID 기반으로 Bucket을 배정하여 부하 분산과 재현성을 함께 확보한다.

### 3.4 Shared Sampling과 개체별 Guidance

공유 Target Sample과 개체별 Movement/Guidance의 빈도를 분리한다.

```text
Canonical PlayerState
        ↓
Shared Target Sample
        ↓
Shared Context
        ↓
Guidance / Movement × N
```

Missile Salvo는 Target Sample을 공유할 수 있으나 Homing, Snap Turn, Overshoot, Reacquire 같은 개별 Guidance 규칙은 `SYS-130`이 결정한다.

---

## 4. Canonical Player State

### 4.1 Host Canonical PlayerState

Host는 Simulation Tick 시작 시 최대 4명의 Player 상태를 한 번 수집한다.

```text
PlayerState
{
    StablePlayerId
    SimulationTick
    Position
    Velocity
    Forward
    Alive
    Targetable
    MovementMode
}

CanonicalPlayerState[4]
```

해당 Tick 동안 읽기 전용 canonical data로 취급한다.

### 4.2 탐색과 직접 참조의 구분

개별 적성 객체가 금지되는 것은 다음과 같다.

```text
Find Player
GetComponent<Player>()
Transform 검색
모든 Player를 순회해 최근접 Target 계산
```

반면 Group 또는 Salvo가 이미 `TargetPlayerIndex`를 결정했다면 개별 Entity가 다음처럼 현재 상태를 읽는 것은 허용한다.

```text
CanonicalPlayerState[TargetPlayerIndex]
```

Player 후보를 다시 검색하는 것과 작은 고정 배열을 indexed read하는 것은 다른 비용 모델로 취급한다.

### 4.3 Derived Player Context

예측 위치나 특정 전투 파생값을 무조건 PlayerState에 넣지 않는다.

```text
PredictedPosition
ThreatVector
RelativeClosingSpeed
```

여러 시스템이 같은 파생값을 반복 계산하는 것이 실제로 확인될 때만 공유 Context를 만든다.

```text
PlayerDerivedContext
{
    PredictedShort
    PredictedMid
    ...
}
```

목표는 사용되지 않는 사전 계산을 피하고 작은 Player data를 유지하는 것이다.

---

## 5. Encounter Activation

### 5.1 Zone의 역할

`Zone`은 적성 객체의 소유자가 아니라 **Encounter의 공간적 활성화 단위**다.

```text
Zone
{
    ZoneId
    ActivationBounds
    SpawnSourceRefs
    EncounterState
    PersistentState
}
```

Zone이 담당하는 것:

- Player 접근 여부 판단
- Encounter Prewarm / Activate
- SpawnSource 활성화
- Encounter 상태 저장
- 월드 콘텐츠·환경 상태 연결

Zone이 담당하지 않는 것:

- 개별 Enemy Target Selection
- Enemy Movement
- Hit / Damage
- 전역 Target Registry
- Enemy의 절대 Lifetime ownership
- Zone 밖으로 나간 적의 강제 제거

이 경계를 통해 Encounter 활성화와 실제 전투 Simulation을 분리한다.

### 5.2 HomeZone / CurrentZone / ManagementScope

Spawn 위치와 실제 이동 가능 범위를 분리한다.

```text
HomeZone
= 생성·기획상의 원점

CurrentZone
= 현재 공간적으로 위치한 Zone

ManagementScope
= ZoneBound / CrossZone / WorldPursuit 등
```

Zone 경계를 넘는 Missile, Drone, 특수 적을 지원하더라도 Zone 자체를 전투 소유자로 확장하지 않는다.

### 5.3 Dormant Encounter Virtualization

비활성 Zone에서 실제 Enemy Entity 수백 개를 유지하지 않는다.

```text
Dormant Encounter
=
EncounterSeed
+ PersistentEncounterState
+ SpawnSourceState
```

Player가 접근하면:

```text
Dormant
→ Prewarm
→ Active
→ 실제 Gameplay Entity 생성
```

목표는 비활성 객체를 싸게 업데이트하는 것이 아니라 **Simulation World에서 제거**하는 것이다.

### 5.4 Activation Broadphase

모든 Zone을 주기적으로 전체 순회하지 않는다. 고속 이동을 고려해 Player의 이전 위치와 현재 위치를 이용한 Swept Region으로 후보를 줄인다.

```text
PreviousPosition
        +
CurrentPosition
        +
High-speed margin
        ↓
Swept Activation Region
        ↓
Activation Spatial Index
        ↓
Candidate Zones
```

전체 Zone 수가 `Z`라고 해도 실제 판단 대상은 Swept Region과 교차하는 `Kz`에 가깝도록 한다.

Activation용 공간 구조와 전투 Target용 공간 구조는 책임을 분리한다.

### 5.5 SpawnSource

SpawnSource는 비활성 상태에서 지속 Polling하지 않는다.

```text
SpawnSource
{
    SpawnProfile
    SpawnTransform / Bounds
    PersistentState
    ActivationCondition
}
```

권장 흐름:

```text
Encounter Activate
→ SpawnSource 활성화
→ SpawnRequest 생성
→ Commit Phase에서 실제 Spawn
```

SpawnSource가 매 Tick Player 거리, Zone 상태, Spawn 조건을 반복 평가하는 구조는 피한다.

### 5.6 Simulation Relevance / Simulation LOD

활성 객체가 매우 많아질 경우 모든 Entity를 같은 빈도로 시뮬레이션하지 않는다.

| Tier | 의미 | 처리 방향 |
|---|---|---|
| T0 | Player와 직접 상호작용 | 60 Hz 개별 Simulation |
| T1 | 근거리, 곧 상호작용 가능 | 30 Hz 수준 + 보간 후보 |
| T2 | 먼 활성 Group | 10~15 Hz coarse simulation 후보 |
| T3 | 현재 상호작용 불가능 | Group / Salvo Proxy 중심 |
| Dormant | Encounter 비활성 | Simulation 없음 |

Simulation LOD는 Gameplay를 훼손하지 않는 범위에서만 사용한다. 전환 거리와 세부 Tier 수치는 프로파일링 후 결정한다.

---

## 6. Hostile Group과 Missile Salvo Context

### 6.1 Hostile Group

Hostile Group은 Entity를 대신 움직이는 Controller가 아니라 **공통 의사결정 데이터 단위**다.

```text
HostileGroupContext
{
    GroupStableId
    PolicyId
    RelevantPlayerMask
    TargetPlan
    TargetUpdatePhase
    AttackReadyState
    StableMemberCursor
}
```

Group이 담당하는 것:

- 복수 Player Target 후보 평가
- Group 단위 Target Plan
- Player별 공격 분산
- Group 공통 전술 상태
- Attack 후보 선출
- Simulation LOD용 Proxy 상태

Group이 담당하지 않는 것:

- Member별 Position 적분
- Member별 Steering
- 개별 공격 Hit 판정
- 개별 HP 처리

평균 Group 크기를 `b1`, Drone 수를 `n1`이라 하면 Group 수는 대략 `G ≈ n1 / b1`이다. Player 대상 평가를 개별 Drone이 아니라 Group 단위로 수행해 `O(P × n1)`을 `O(P × G)`에 가깝게 줄이는 것이 목표다.

### 6.2 RelevantPlayerMask

최대 Player 수가 4명이므로 작은 고정 집합은 동적 List보다 bit mask를 우선한다.

```text
0001 = P1
0010 = P2
0100 = P3
1000 = P4
```

예:

```text
0101 = P1 + P3
```

작은 고정 데이터에 동적 Collection과 allocation을 도입하지 않는다.

### 6.3 Group Target Assignment

개별 Drone은 누구를 공격할지 검색하지 않는다.

```text
CanonicalPlayerState
        ↓
Group Target Evaluation
        ↓
TargetPlan
        ↓
Member
```

단일 Target Group은 `PrimaryTargetPlayerIndex`만 가질 수 있고, 복수 Target 분산이 필요하면 고정 크기 Slot을 사용할 수 있다.

```text
TargetSlots[0] = P1
TargetSlots[1] = P2

memberStableOrdinal % 2
→ Target Slot
```

Group Target이 바뀌더라도 모든 Member의 TargetHandle을 다시 쓰지 않고 Group Context의 소수 필드만 수정하는 방향을 우선한다.

### 6.4 Target Retention과 재평가

매 Frame Target을 다시 선택하지 않는다.

재평가 조건 예:

- Target Evaluation Phase 도달
- 현재 Target 사망
- Targetable 해제
- 전투 정책 변화
- Group split / merge
- 명시적인 강제 Target Event

그 외에는 기존 TargetPlan을 유지한다.

### 6.5 Group 생성과 재구성

모든 적끼리 거리 비교하여 Group을 만드는 `O(N²)` 구조는 금지한다.

기본 Group 생성 기준:

- Spawn batch
- Formation
- 역할
- SpawnSource
- Encounter phase

필요한 경우에만 Spatial Cell 기반 저빈도 split/merge를 검토한다.

### 6.6 Missile Salvo Context

Missile Salvo 역시 거대한 중앙 Controller가 아니라 공유 Context다.

```text
SalvoContext
{
    SalvoStableId
    EmitterStableId
    TargetPlayerIndex
    TargetSample
    TargetSamplePhase
    SpawnTick
    SalvoSeed
    MemberCount
}
```

개별 Missile은 다음과 같은 자신의 상태를 가진다.

```text
Missile
{
    SalvoIndex
    GuidanceState
    IndividualSeed
    Position
    Velocity
    ...
}
```

Salvo는 공통 Target Sample과 Seed를 제공하고, 개별 Missile은 자신의 Guidance와 Movement를 수행한다.

### 6.7 Missile Target Sampling 계약

Player Target Sample을 Salvo 단위로 공유한다.

```text
Canonical PlayerState
        ↓
Salvo Target Sample
        ↓
Cached Salvo Context
        ↓
Missile Guidance × Members
```

Architecture는 Sample 공유와 갱신 빈도 분리 원칙까지만 소유한다. 개별 Missile의 Homing, Snap Turn, 제한 선회, Attack Commit, Overshoot/Reacquire는 `SYS-130`이 소유한다.

---

## 7. Combat Target Infrastructure

### 7.1 Combat Target Registry

Registry는 Entity 상태의 완전한 복사본이 아니라 **빠른 접근용 인덱스**다.

```text
StableHandle Registry
+
Combat Spatial Index
```

Registry와 Spatial Index에는 Target 후보 검색에 필요한 최소 정보만 유지한다. Gameplay 상태 전체를 중복 저장하지 않는다.

### 7.2 Stable Handle / Logical Stable ID

Pooling과 ECS Entity 재사용을 고려하여 다음 식별자를 구분한다.

```text
LogicalStableId
≠ ECS Entity Index
≠ Pool Slot
```

Gameplay 결정론, Network identity, Event 참조는 재사용 가능한 물리적 Slot이나 Entity Index에 직접 의존하지 않는다.

### 7.3 Spatial Broadphase

하나의 Spatial 구조에 모든 책임을 넣지 않는다.

```text
Activation Spatial Index
→ Zone / Encounter 후보

Combat Target Spatial Index
→ 공격 가능한 Entity 후보
```

필요하면 Missile 특화 구조를 별도로 검토하되 프로파일링 전에는 구조를 늘리지 않는다.

### 7.4 Combat Spatial Index 갱신 전략

두 전략을 프로토타입에서 비교한다.

**Incremental**

```text
newCell == oldCell
→ 변경 없음

newCell != oldCell
→ remove old
→ insert new
```

비용은 실제 Cell을 넘은 객체 수에 비례한다.

**Periodic Rebuild**

```text
전체 Target
→ CellKey 병렬 계산
→ Spatial Index 재구축
```

점근적으로 `O(Ntarget)`이더라도 연속 메모리 접근과 병렬화가 더 유리할 수 있으므로 Big-O만으로 전략을 확정하지 않는다.

### 7.5 Missile Spatial Strategy

고속 Missile은 Cell crossing이 많을 수 있으므로 다음을 A/B 테스트한다.

**A. Individual Missile Indexing**

- 후보 검색 정밀도 높음
- 고속 Missile의 index mutation 비용 증가 가능

**B. Salvo Proxy**

```text
SalvoBounds
→ Broadphase
→ Proxy 적중 시 Member Expansion
```

- index update 수 감소 가능
- Salvo가 크게 퍼지면 Proxy Bounds가 과도하게 커질 수 있음

**C. Far Salvo Virtualization**

상호작용 범위 밖에서는 Salvo 단위 trajectory만 유지하고 Interaction Envelope 진입 시 개별 Missile로 확장하는 고급 최적화 후보다.

현재 문서에서는 어느 전략도 최종 확정하지 않는다.

### 7.6 Player Weapon Targeting과의 경계

본 Architecture는 `Combat Target Registry`, `Stable Handle`, `Combat Spatial Index`, Broadphase 후보 공급을 소유한다.

플레이어 무기의 다음 항목은 `SYS-120`이 소유한다.

- Logical Targeting View
- Weapon별 Eligibility
- Shared Target Feature
- 최대 4개 무기의 Target Selection
- Target Retention / Hysteresis
- Fire-control solution

```text
ARCH-100
World Targets → Registry / Spatial → Candidate Set
                                  ↓
SYS-120
Eligibility → Weapon Selection → Fire / Aim
```

---

## 8. Attack Execution

### 8.1 Attack Ready와 Attack Intent 분리

공격 조건을 만족했다고 즉시 실제 공격을 실행하지 않는다.

```text
Weapon Ready
+ Target Valid
+ Range / Policy Valid
        ↓
Attack Ready
```

Ready 상태가 된 Entity 또는 Group만 실행 후보가 된다.

### 8.2 Ready Attack Queue

모든 Group이 매 Tick AttackIntent를 만들고 전체 정렬하는 구조를 피한다. Ready 상태가 **변한 순간** Queue에 등록한다.

```text
Not Ready
   ↓ state transition
Ready
   ↓ enqueue once
ReadyAttackQueue
```

Queue는 공격 유형이나 Priority class에 따라 고정된 소수 그룹으로 나눌 수 있다.

### 8.3 Combat Execution Budget / Attack Permit

```text
ReadyAttackQueue
       ↓
Combat Budget Scheduler
       ↓
AttackPermit
       ↓
Attack Resolve
```

Budget은 평균 부하뿐 아니라 최악 순간의 고비용 실행량을 제한하기 위한 구조다.

적용 후보:

- Heavy Attack
- Projectile Spawn
- Physics Query가 필요한 공격
- Chain Detonation
- 일부 Visual Event

Movement나 즉각적인 Hit 결과처럼 지연하면 Gameplay가 깨지는 작업을 무조건 Budget으로 미루지 않는다.

### 8.4 Deterministic Attack Permit

Permit을 Job 완료 순서나 Queue 삽입 순서에 의존해 배분하지 않는다.

예:

```text
PriorityClass
→ ReadyTick
→ GroupStableId
→ SourceStableId
```

또는 Priority별 Queue와 Stable Round-robin Cursor를 사용한다.

### 8.5 Attack Model

공격은 크게 다음 둘로 구분한다.

```text
Attack
├─ Instant Resolve
└─ World Projectile
```

**Instant Resolve**

탄의 비행 자체가 Gameplay 핵심이 아닌 고속 직사 연사 등은 실제 Projectile Entity를 만들지 않는다.

```text
Fire
→ Hit Query / Resolve
→ Hit Event
→ Visual Event
```

**World Projectile**

비행시간, 요격, 공간 점유, 접촉이 Gameplay에 중요하면 실제 World Projectile을 유지한다.

### 8.6 World Projectile 공통 속성

World Projectile은 다음 속성의 조합으로 정의할 수 있다.

```text
MovementModel
Interceptable?
Health?
PlayerContactResponse
DamageTrigger
Lifetime
AreaEffect
Penetration
```

구체적인 Missile 행동은 `SYS-130`이 결정한다.

---

## 9. Hit / Damage / Detonation / Death Pipeline

### 9.1 Authoritative Hit Resolve

Gameplay Hit 결과는 Host가 확정한다.

```text
AttackPermit
→ Attack Resolve
→ Host Physics / Segment / Custom Query
→ Authoritative HitResult
```

Client가 동일한 Physics Query를 다시 실행해 Gameplay 결과를 결정하지 않는다. Unity Physics의 cross-machine bit-identical 재현을 요구하지 않는다.

### 9.2 Damage / Detonation Reduction

같은 Tick에서 여러 공격이 동일 Target에 들어와도 System이 발견한 순서대로 HP를 즉시 수정하지 않는다.

```text
HitEvent
    ↓
DamageEvent
    ↓
Target별 Accumulation / Reduction
    ↓
Damage Commit
    ↓
Death / Detonation State
```

합산 가능한 값은 commutative reduction을 우선하여 병렬 실행 순서에 대한 의존성을 줄인다.

### 9.3 모든 Event를 정렬하지 않는다

결정론을 위해 모든 Event를 StableId 순으로 `O(E log E)` 정렬하는 구조는 피한다.

순서가 필요 없는 항목:

- Damage 합산
- 단순 Hit count
- 일부 Stack 누적

이 경우 reduction을 사용한다.

순서가 필요한 항목:

- 동일 자원에 대한 Permit 경쟁
- Stable Spawn Sequence
- 순서가 Gameplay 결과를 바꾸는 제한된 상태전이

이 경우에만 Stable ordering 또는 명시적인 tie-break를 사용한다.

### 9.4 Explosion / Chain Detonation

폭발 시 모든 Missile 또는 모든 Target을 순회하지 않는다.

금지:

```text
Explosion × all missiles
→ O(N²) 위험
```

권장:

```text
Explosion
→ Combat Spatial radius query
→ 후보만 Detonation / Damage Event 생성
→ Reduction / Commit
```

연쇄 폭발이 발생하더라도 Event와 Spatial Query를 통해 영향을 받는 후보만 처리한다.

### 9.5 시스템별 폭발 규칙과의 경계

본 문서는 폭발·연쇄 효과의 **공통 실행 경로**를 소유한다. 어떤 Missile이 어떤 조건에서 폭발·유폭 요청을 생성하고 유폭이 어떤 플레이 의미를 갖는지는 `SYS-130`이 소유한다.

---

## 10. Lifecycle과 데이터 구성

### 10.1 Death / Despawn / Structural Change

Entity를 System 실행 중 즉시 생성·파괴하지 않는다.

```text
SpawnRequest
DeathRequest
StructuralChangeRequest
        ↓
Commit Phase
        ↓
Batch Playback
```

Gameplay 결과 계산과 structural change를 분리하여 반복적인 sync point와 구조 변경 비용을 줄인다.

### 10.2 FSM과 Archetype 변경

Gameplay FSM 상태 변화마다 Component Add/Remove를 사용하지 않는다.

예를 들어 Missile의 다음 상태:

```text
Launch
Boost
Homing
Overshoot
Reacquire
```

를 각각 Archetype 변경으로 표현하기보다 상태 값과 공통 Component를 유지하는 방향을 우선한다.

Archetype 변경은 실제 데이터 구성이 달라질 때 사용한다.

### 10.3 Hot / Cold Data 분리

같은 Entity의 데이터라고 해서 모든 System이 모든 Component를 읽지 않도록 한다.

예:

```text
Movement Hot Data
- Position
- Velocity
- Steering
- GroupIndex

Combat Hot Data
- WeaponState
- Target reference
- Attack ready state

Cold / Profile Data
- 최대 속도
- 장갑 Profile
- Weapon Profile
- Visual Profile
```

System별 Hot Path가 실제로 필요한 데이터만 순차적으로 읽는 구조를 지향한다.

### 10.4 Immutable Profile 공유

동일 종류의 대량 Entity가 동일한 상수 데이터를 개별 복사하지 않는다.

```text
MaxSpeed
TurnRate
WeaponRange
ArmorProfile
...
```

는 Profile ID 또는 공유 immutable data로 참조하고 Entity에는 변하는 상태만 둔다.

### 10.5 Pooling 정책

Pooling은 기본 철학이 아니라 **Spawn/Despawn churn 최적화 수단**이다.

| 대상 | 초기 방향 |
|---|---|
| Missile | 적극 A/B 검증 |
| Drone | 실제 churn 측정 후 |
| Ship | 우선순위 낮음 |
| Salvo Context | ECS data 재사용 검토 |
| Tracer | CPU Pool보다 GPU VFX 우선 |
| Explosion VFX | GPU 또는 visual pool |
| Event Buffer | 재사용 Native Buffer |

Pool을 도입할 때는 inactive memory, reset 비용, structural change 감소 효과를 함께 측정한다.

---

## 11. Authority / Determinism / Replication

### 11.1 Host Gameplay Authority

초기 기준은 다음과 같다.

```text
Host = Gameplay Authority
```

Host가 최종 확정하는 것:

- Spawn
- Stable Logical ID
- Target Assignment
- Attack Permit
- Attack 발생
- Hit Result
- Damage / Detonation
- Death
- Encounter / Clear State
- Reward 등 중요 Gameplay State

### 11.2 최소 결정론 경계

본 프로젝트는 모든 Client가 동일 float 연산과 Physics 결과를 재현하는 완전 lockstep을 목표로 하지 않는다.

다음 상태전이는 실행 순서에 의해 흔들리지 않아야 한다.

```text
Canonical Authoritative State
+ SimulationTick
+ Accepted Input / Event
+ Stable Seed
        ↓

Spawn decision
Stable ID assignment
Target Assignment
Attack Permit
Attack logical occurrence
Damage accumulation
Detonation state
Death
Encounter / Clear state
Reward
```

Host 권위이지만 cross-machine 결정론이 불필요한 것:

- Enemy continuous movement
- Unity Physics 내부 계산
- 미세 Steering float 결과
- Transform interpolation

결정론이 필요 없는 것:

- Trail
- Tracer
- Particle
- Camera shake
- Animation phase
- Visual randomness
- Render interpolation

### 11.3 RNG 규칙

전역 순차 RNG 호출을 실행 순서에 의존하지 않는다.

```text
EntitySeed =
Hash(SessionSeed, StableEntityId)

SalvoSeed =
Hash(SessionSeed, EmitterStableId, SalvoSequence)

DecisionSeed =
Hash(EntitySeed, DecisionType, DecisionSequence)
```

Entity 추가·삭제나 Job 실행 순서가 다른 Entity의 Random 결과에 연쇄적으로 영향을 주지 않도록 한다.

### 11.4 Query 반환 순서를 신뢰하지 않는다

다음의 첫 번째 항목을 Gameplay 결정으로 그대로 사용하지 않는다.

- Spatial Hash iteration
- Physics Query hit array
- Native container iteration
- Parallel event insertion order

필요한 경우 명시적인 tie-break를 사용한다.

```text
Score
→ Distance
→ StableId
```

### 11.5 Parallel Calculate / Stable Commit

```text
Parallel Calculate
       ↓
Local / Concurrent Event Collection
       ↓
Reduction or Stable Resolve
       ↓
Commit
```

병렬화하기 좋은 것:

- Movement
- Guidance
- Cheap Eligibility
- Target Score calculation
- Visual State generation

Commit에서 확정할 것:

- Spawn
- Attack Permit
- Damage
- Death
- Reward
- Encounter State

### 11.6 Network Replication Relevance

Host가 모든 Enemy Transform을 모든 Client에 동일 빈도로 보내지 않는다.

```text
Host Simulation World
        ↓
Per-client Relevance Filter
        ↓
Relevant Snapshot
        ↓
Client
```

개별 State가 필요한 것:

- 근거리 교전 Entity
- Player가 공격할 수 있는 Entity
- Player에게 직접 위협인 Projectile

Group/Salvo 수준으로 축약 가능한 것:

- 먼 Hostile Group
- 먼 Missile Salvo
- 아직 상호작용 불가능한 Encounter

목표는 `O(N × ClientCount)`에 가까운 전송을 피하고 Client별 relevant state 중심으로 전송량을 제한하는 것이다.

### 11.7 Salvo 기반 Network 압축

Missile 다수를 개별 Spawn packet으로 보내기보다 상위 Event로 표현하는 방식을 검토한다.

```text
SalvoStart
{
    SalvoId
    EmitterId
    Count
    TargetPlayerId
    ProfileId
    Seed
    SpawnTick
}
```

Client는 시각 표현을 생성하고 Host는 중요 Gameplay State와 correction을 authoritative하게 전달한다.

이 방식은 최종 Network format이 아니라 검증할 Architecture 후보이다.

### 11.8 Continuous Snapshot

Client가 Enemy AI를 Host와 동일하게 재실행하는 것을 기본 전제로 하지 않는다.

```text
Host
→ Authoritative Movement State
→ Low-frequency Snapshot
→ Client interpolation / optional prediction
```

초기에는 저빈도 Snapshot과 interpolation 조합을 후보로 측정하며 최종 Hz는 고정하지 않는다.

### 11.9 현재 권위 기준: ZoneOwner 비사용

`ZoneOwner` 분산 권위는 현재 구현하지 않는다. Host CPU가 실제 병목이라는 프로파일링 근거가 확보될 때만 다시 검토한다.

현재 선제적으로 추가하지 않는 복잡도:

- Authority migration
- State handoff
- RNG state transfer
- Client verification
- Disconnect recovery
- Cheat trust model

과거 ZoneOwner 설계는 현행 요구사항이 아니며, 필요할 때 Git 이력에서 확인한다.

---

## 12. Gameplay / Visual 분리

### 12.1 Visual State Buffer

Gameplay Entity와 시각 객체를 1:1로 대응시키지 않는다.

```text
Gameplay Entity
       ↓
Compact Visual State
       ↓
GraphicsBuffer
       ↓
GPU Rendering / VFX
```

예:

```text
MissileVisualState
{
    Position
    Forward
    VisualType
    Alive
    Phase
}
```

Gameplay System은 전투 결과를 계산하고, Visual System은 해당 결과를 표현하기 위한 최소 상태만 소비한다.

### 12.2 Visual Event

Tracer, Impact, Explosion 등 일회성 표현은 논리 Entity를 지속적으로 만들기보다 Event 또는 compact visual command로 전달하는 방향을 우선한다.

Gameplay 판정과 Visual lifetime을 분리한다.

### 12.3 대량 Animation 원칙

```text
AI Entity
≠ Animator GameObject
```

대량 일반 Drone은 GPU animation / instancing 계열을 우선 검토하고, Boss / Hero Unit처럼 개체성이 중요한 객체에는 별도 고품질 경로를 허용한다.

이 문서는 VAT, Entities Graphics, BRG 중 하나를 확정하지 않는다. 핵심은 Animation Update가 CPU Gameplay 비용과 함께 개체 수에 비례해 증가하는 구조를 피하는 것이다.

---

## 13. Allocation / Warm-up

### 13.1 Hot Path Allocation

Combat Hot Path에서 managed allocation은 `0 B`를 목표로 한다.

대상:

- Target candidate buffer
- Physics query result
- Attack event
- Hit event
- Damage event
- Spawn request

목적은 평균 CPU 비용뿐 아니라 GC spike를 억제해 frame time 안정성을 확보하는 것이다.

### 13.2 Session Warm-up과 Encounter Prewarm

두 종류를 구분한다.

**Session Warm-up**

세션 진입 시 준비하는 항목:

- Shader / PSO
- VFX Graph
- 공통 Mesh / Material
- Graphics Buffer
- 필수 Native Container capacity
- 자주 사용하는 Effect path

**Encounter Prewarm**

특정 Encounter 접근 시 준비하는 항목:

- Spawn Profile
- 필요한 Missile Profile
- 필요한 Asset residency
- 필요한 Pool / Entity capacity
- Encounter-specific VFX

로딩 단계에서 해결할 수 있는 초기화 비용과 Encounter 접근 시점의 Gameplay 준비 비용을 분리한다.

---

## 14. 성능 복잡도와 Budget

### 14.1 시간 복잡도 모델

변수:

```text
P       = Player 수, 최대 4
n1      = Active Drone
n2      = Active Missile
n3      = Active Ship / Large Hostile
n4      = Active World Projectile
Nactive = 전체 Active Gameplay Entity
G       = Hostile Group 수
S       = Missile Salvo 수
Nhigh   = 이번 Tick 고빈도 Simulation 개체
Nmedium = 낮은 빈도로 처리 가능한 개체
r       = 빈도 감소 비율
Gdue    = 이번 Tick Target을 평가할 Hostile Group
Sdue    = 이번 Tick Target을 Sample할 Missile Salvo
Kz      = Activation 후보 Zone
Kt      = Target Broadphase 후보
B       = 이번 Tick 실제 Attack Permit
Chit    = Attack/Hit 1건 처리 비용
E       = Commit 대상 Event
```

목표 비용은 다음 형태에 가깝게 만든다.

```text
Ttick ≈ O(P)
       + O(Nhigh)
       + O(Nmedium / r)
       + O(Gdue)
       + O(Sdue)
       + O(Kz)
       + O(Kt)
       + O(B × Chit)
       + O(E)
```

`P ≤ 4`는 사실상 상수로 본다. 대량화 시 지배항은 실제로 움직이거나 상호작용하는 객체 수에 가깝게 유지하고 `O(P × Nactive)`, `O(Nactive²)`, 불필요한 반복 `O(Nactive)`을 피한다.

| 대상 | 목표 비용·처리 | 피해야 할 반복 |
|---|---|---|
| Drone | Target 평가 `O(P × n1)`을 Group 단위 `O(P × Gdue)`로 줄이고 개체는 Steering·Movement만 수행 | 개체별 Player·Target·Zone 탐색 |
| Missile | Target 평가 `O(P × n2)`를 Salvo 단위 `O(P × Sdue)`로 줄이고 개체는 필요한 Guidance·Movement만 수행 | 개체별 Player 예측, Missile 간 전수 검사 |
| Ship | `O(n3 + activeParts + activeWeapons)`로 Root·활성 Part·Weapon 비용을 분리 | 비활성 Part·Weapon의 상시 처리 |
| World Projectile | 공간적 존재가 필요한 공격만 `O(n4)` 비용을 지불 | Instant Resolve 가능한 공격의 Entity 생성 |
| Static Hostile | Movement 없이 due Target·Weapon 평가만 수행 | 매 Tick 전수 평가 |

Far Simulation LOD와 Salvo virtualization은 Gameplay 상호작용을 훼손하지 않고 실제 비용을 줄일 때만 적용한다.

### 14.2 메모리 복잡도

기본 메모리 구조는 다음에 비례한다.

```text
O(
    Nactive
  + G
  + S
  + SpatialIndex
  + EventBuffers
  + PoolCapacity
)
```

CPU 최적화를 이유로 지나치게 많은 inactive Entity를 항상 유지하지 않는다. Pool Capacity 역시 peak active 수보다 과도하게 크게 잡지 않는다.

### 14.3 성능 Budget

정확한 수치는 프로파일링 후 결정하지만 Architecture는 다음과 같은 상한을 지원해야 한다.

```text
MaxActiveEncounter
MaxHighFrequencyEntities
MaxWorldProjectiles

MaxGroupTargetEvaluationsPerTick
MaxSalvoSamplesPerTick

MaxAttackPermitPerTick
MaxProjectileSpawnPerTick
MaxHeavyAttackPerTick
MaxDetonationResolvePerTick

MaxPhysicsQueriesPerTick
```

Budget은 주로 다음에 적용한다.

- 저빈도 판단
- 공격 실행 경쟁
- 생성량
- 연쇄 효과
- 고비용 Query
- 시각 효과

Movement나 즉시 반영되어야 하는 Gameplay 결과를 무작정 지연시키는 수단으로 사용하지 않는다.

---

## 15. Profiling과 Validation

### 15.1 Profiling 지표

첫 Prototype부터 다음 Counter를 수집한다.

**Entity / Activation**

- Loaded Encounter 수
- Active Encounter 수
- Active Drone
- Active Missile
- Active World Projectile
- Simulation Tier별 Entity 수

**Player / Target**

- Player Transform 원본 read 횟수
- Canonical PlayerState indexed read 횟수
- Group Target Evaluation 수
- Salvo Target Sample 수
- Target 후보 Broadphase 전/후 수

**Spatial**

- Cell change 수
- Incremental insert/remove 수
- Spatial rebuild 비용
- Missile index update 수
- Salvo Proxy expansion 수

**Combat**

- Ready Queue enqueue 수
- Attack Permit 수
- Attack type별 Permit
- Physics / First-hit Query 수
- Hit Event 수
- Damage Event 수
- Detonation query 수

**ECS / Memory**

- System별 CPU time
- Job scheduling time
- Structural change 수
- ECB playback 시간
- Managed allocation
- Native buffer resize 횟수
- Chunk utilization

**Network**

- Client별 relevant entity 수
- Event bytes/sec
- Snapshot bytes/sec
- Salvo 압축 전/후 크기
- Correction 횟수

### 15.2 결정론 검증

같은 입력으로 Host Simulation을 반복한다.

```text
SessionSeed
Player Input Recording
Encounter Configuration
```

Gameplay Log 예:

```text
SpawnLog
TargetAssignmentLog
AttackPermitLog
HitResultLog
DamageCommitLog
DeathLog
EncounterStateLog
```

필요하면 Tick별 `GameplayTickChecksum`을 생성한다.

검증 대상은 Visual이나 Transform이 완전히 동일한지가 아니라 **결정론 경계 내부의 Gameplay 상태전이가 안정적인가**이다.

### 15.3 Architecture Prototype 범위

초기 Prototype은 다음 정도로 제한한다.

```text
Local Canonical PlayerState × 2
Combat Zone × 1~수개
Drone SpawnSource × 1
Hostile Group 여러 개
Drone 20~100

Missile Emitter × 1
Salvo 여러 개
Missile 50~300

Instant Direct-fire Weapon
Target Registry / Spatial Index

Attack Ready Queue
Attack Budget / Permit

Host Authoritative Hit
Damage / Death Commit
```

실제 Online Network보다 먼저 로컬에서 2개의 PlayerState를 움직여 Multi-target 상황을 재현할 수 있다.

우선 검증 항목:

1. Group Targeting이 개별 Targeting보다 실제로 싸고 자연스러운가
2. Drone Steering과 Target Update 빈도를 분리해도 반응성이 충분한가
3. Salvo Target Sample 공유의 실제 이득은 얼마인가
4. Attack Budget이 CPU peak와 Player 압박을 동시에 제어하는가
5. Missile Spatial은 Individual / Proxy 중 무엇이 더 유리한가
6. Incremental Spatial Update와 Periodic Rebuild 중 무엇이 더 유리한가
7. Pooling이 실제 Create/Destroy보다 유리한 대상은 무엇인가
8. Simulation LOD가 Gameplay를 해치지 않고 효과가 있는가

---

## 16. Open Decisions

다음 항목은 Architecture의 방향은 정해져 있으나 최종 구현값 또는 전략을 프로파일링으로 결정해야 한다.

현재 문서의 Hz, Budget, Pool Capacity, Simulation LOD 전환값은 프로파일링을 위한 시작값 또는 후보값이다. 명시적으로 확정되지 않은 수치를 최종 Gameplay 요구사항이나 성능 계약으로 취급하지 않는다.

| 항목 | 현재 후보 / 상태 | 검증 기준 |
|---|---|---|
| Combat Spatial Index 갱신 | Incremental vs Periodic Rebuild | ECS 메모리 패턴, Cell crossing, rebuild 비용 |
| Missile Spatial Strategy | Individual Indexing vs Salvo Proxy | 후보 정밀도, index mutation, proxy expansion 비용 |
| Far Missile 처리 | 필요 시 Salvo virtualization | Gameplay 상호작용 손실과 실제 성능 이득 |
| Pooling 적용 대상 | Missile 적극 A/B, Drone은 churn 측정 후 | Create/Destroy, structural change, reset 비용 |
| Simulation LOD | T0~T3 구조, 전환 기준 미정 | 반응성·Gameplay 손실 없이 얻는 CPU 절감 |
| Attack / Physics Budget 수치 | 구조만 확정 | 최악 순간 부하와 플레이 압박 |
| Group 최대 크기 | 미정 | Targeting 비용과 행동 자연스러움 |
| Snapshot 빈도 | 저빈도 Snapshot + interpolation 후보 | 전송량, correction, 시각 오차 |
| Rendering 경로 | GPU 이전 원칙만 확정 | 대상 수·플랫폼·프로파일링 결과 |
