# OverTheHorizon - Prototype 0.2 Vector Command Flight Testbed

대상: Unity 6.3.23f1 LTS / Universal 3D / Input System

## 이번 버전의 목적

Prototype 0의 `AddForce` 중심 이동을 교체한다.

핵심 동작은 다음과 같다.

- `WASDRF`를 하나의 3차원 `MoveIntent`로 해석
- Velocity의 **방향(Direction)** 과 **속력(Speed)** 을 분리하여 제어
- 이동 입력 시 기존 관성보다 플레이어의 명시적 이동 의도를 우선
- 방향은 매우 빠르게 Desired Move Direction으로 회전
- 속력은 Burst → Sustain 응답으로 Directional Cruise Speed에 수렴
- 새로운 방향의 Cruise Speed가 낮아도 방향전환 순간 속도를 즉시 잘라내지 않음
- 입력 해제 시 현재 Velocity를 그대로 유지
- `S` = 후진 Vector Command
- `Left Alt` = Velocity Kill / Brake
- `Left Shift` = High Maneuver Authority
- Absolute Hard Speed만 실제 안전 상한으로 Clamp
- 3D Runtime World Lattice와 Debug HUD 제공

의도적으로 제외:

- Maneuver Resolver
- Pivot / Flip
- Maneuver Reserve
- 고도 제한
- Thruster Animation
- Combat / Cruise 최종 모드
- 특수기동 Camera Presentation

---

## 파일

```text
Assets/OverTheHorizon/Prototype0_2/
├─ Input/
│  └─ MechaPrototype02.inputactions
└─ Scripts/
   ├─ Input/
   │  ├─ MechaVectorIntent.cs
   │  └─ MechaVectorInputReader.cs
   ├─ Flight/
   │  └─ MechaVectorFlightController.cs
   ├─ Camera/
   │  └─ MechaPrototypeCameraController02.cs
   └─ Debug/
      ├─ RuntimeWorldLattice3D.cs
      └─ MechaVectorDebugHUD.cs
```

---

## 기존 Prototype 0와 함께 사용할 때

0.2는 클래스 이름을 별도로 두었으므로 기존 Prototype 0 스크립트와 컴파일 충돌하지 않는다.
다만 **한 Mecha에 기존 `MechaMotionController`와 새 `MechaVectorFlightController`를 동시에 활성화하면 안 된다.**
둘이 Rigidbody를 동시에 제어하게 된다.

기존 Controller는 Disable 또는 Remove한 뒤 0.2 Controller만 사용한다.

---

## Unity 적용

### 1. Mecha

Mecha 루트에 다음을 둔다.

- Collider
- Rigidbody
- `MechaVectorInputReader`
- `MechaVectorFlightController`
- `MechaVectorDebugHUD` (권장)

`MechaVectorInputReader > Input Actions`에 `MechaPrototype02.inputactions`를 지정한다.

권장 Rigidbody 상태:

- Use Gravity: Controller 기본 설정이 Play 시 Off로 설정
- Linear Damping: Controller 기본 설정이 0으로 설정
- Interpolate: Controller 기본 설정이 Interpolate로 설정
- Rotation Constraints: Freeze하지 않음

### 2. Camera

Main Camera를 Mecha의 Child로 두지 않는다.

Main Camera에:

- `MechaPrototypeCameraController02`

를 추가하고 `Flight Controller`에 플레이어 Mecha의 `MechaVectorFlightController`를 지정한다.

### 3. 3D Debug Lattice

빈 GameObject를 만든다.

```text
DebugWorldLattice
└─ RuntimeWorldLattice3D
```

`Target`에 Mecha Transform을 지정한다.

기본값:

- Cell Size: 5m
- Half Cell Count: 8
- 표시 Cube: 약 80m × 80m × 80m
- Major Line Every: 5

격자는 Mecha를 부드럽게 따라가지 않고 5m Cell 단위로 Snap한다.
따라서 Mecha가 격자를 통과하는 상대속도를 수평/수직 모두 확인할 수 있다.

---

## 입력

| Input | Prototype 0.2 의미 |
|---|---|
| Mouse | Desired Facing |
| W | 기수축 전방 Move Vector |
| S | 기수축 후방 Move Vector |
| A / D | Yaw Control Frame 기준 좌/우 Move Vector |
| R / F | World Up 기준 상승/하강 Move Vector |
| Left Alt | Velocity Kill |
| Left Shift | High Maneuver Authority |

### 핵심

`W -> S` 같은 반대 방향 전환에서 목표는 다음이다.

```text
Forward Velocity
      ↓
Velocity Vector 자체가 빠르게 회전
      ↓
Backward Velocity
```

아래처럼 속도 0을 거치는 signed forward-speed 방식이 아니다.

```text
+Speed -> 0 -> -Speed   (X)
```

---

## 기본 튜닝값의 의미

모든 값은 최종 밸런스가 아니라 체감 탐색용 시작값이다.

### Directional Cruise Envelope

- Forward: 65 m/s
- Reverse: 40 m/s
- Lateral: 50 m/s
- Vertical: 45 m/s

대각선 입력은 타원체 Speed Envelope로 계산하므로 `W+A+R`이 단일 W보다 빨라지는 대각선 보너스를 만들지 않는다.

### Speed Response

- Burst Acceleration: 260 m/s²
- Sustain Acceleration: 32 m/s²
- Over-Cruise Convergence: 35 m/s²
- Brake: 320 m/s²

Burst는 Velocity에 임펄스를 더하는 Dash가 아니다.
새 입력/큰 방향전환 직후 **제어 응답이 잠시 강해지는 상태**이다.

### Direction Response

- Base: 1440 deg/s
- Burst Multiplier: ×1.75
- High-G Multiplier: ×1.5

즉 방향변환은 속력 변화보다 훨씬 빠르게 일어난다.

### Hard Speed

- Absolute Hard Speed: 90 m/s

Cruise Speed는 즉시 Clamp가 아니라 수렴 목표이고, Hard Speed만 실제 절대 상한이다.

---

## 첫 플레이 테스트 순서

1. `W` 입력 → 놓기
   - 빠르게 전방 Cruise로 접근하는지
   - 놓은 뒤 관성이 유지되는지

2. 전진 중 `A`
   - 감속 후 좌회전이 아니라 Velocity Vector 자체가 빠르게 좌측으로 재지향되는지

3. 전진 중 `S`
   - 0 m/s를 거치는 브레이크가 아니라 후방 Move Vector로 빠르게 전환되는지

4. 전진 중 `R`, `A+R`, `W+A+R`
   - 3D Lattice에서 수직/대각선 Vector Change가 읽히는지

5. 이동 중 `Alt`
   - 입력 해제와 달리 실제 Velocity Kill로 동작하는지

6. 같은 방향전환을 `Shift` 유/무로 비교
   - Shift가 단순 상시 Sprint가 아니라 Direction/Burst Authority 증가로 느껴지는지

---

## Debug HUD

기본 표시:

- Current Speed
- Directional Cruise Target
- Absolute Hard Limit
- Direction Error
- Current Velocity Direction
- Desired Move Direction
- Move Input
- Burst
- High-G
- Brake

F1로 HUD를 숨기거나 다시 표시할 수 있다.

---

## 다음 판단

이번 버전에서 먼저 확인할 것은 다음 3가지다.

1. **명시적 이동 입력이 관성을 충분히 압도하는가**
2. **방향은 즉각적이지만 속력 변화에는 적절한 연속성이 남는가**
3. **높은 순간 반응과 낮은 지속 Cruise가 전술기다운 기동감을 만드는가**

이 세 가지가 확보된 뒤에만 Maneuver Resolver / Pivot / Flip / Maneuver Reserve를 설계·추가한다.
