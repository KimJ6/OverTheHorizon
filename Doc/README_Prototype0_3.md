# OverTheHorizon Prototype 0.3
## Propulsion-Aware Vector Control Testbed

Prototype 0.2 테스트 피드백을 반영한 기동/카메라/추력 가시화 버전입니다.

### 핵심 변경

- 0.2의 **속도 0을 거치지 않는 즉각적 Vector Redirect** 유지
- `Main Propulsion`과 `Maneuver Vectoring` 분리
- Redirect 중 주추력 권한을 일시적으로 낮추고, Redirect 종료 후 W/S 주추력이 다시 장기 궤적을 지배
- A/D/R/F를 놓았다고 Velocity를 즉시 기수 방향으로 정렬하지 않음
- Alt를 `Velocity Kill`에서 **Brake Pressure + Brake Vectoring**으로 변경
- Alt Tap: 작은 속력손실 + 강한 방향제어
- Alt Hold: Pressure 상승에 따라 실제 감속이 비선형 증가
- Alt Release: 실제 제동은 즉시 OFF, 내부 Pressure만 점진 감소하여 연타 exploit 완화
- Camera Rotation은 `Desired Facing`을 빠르게 추종
- Camera Translation은 느리게 추종하고 화면 외곽에서만 Catch-Up 강화
- Raw Velocity Look-Ahead 기본 제거
- Controller 명령을 직접 읽는 런타임 Prototype Thruster Manipulator + Vector Lines 추가
- InputActionAsset 미지정 시에도 WASDRF/Mouse/Alt/Shift 기본 입력을 자동 생성

---

## 기존 Prototype과 동시 사용 금지

플레이어 Mecha에서 기존 0 / 0.2 컨트롤 컴포넌트를 제거하거나 비활성화하세요.

제거 대상 예:

- `MechaInputReader`
- `MechaMotionController`
- `MechaVectorInputReader`
- `MechaVectorFlightController`
- 기존 Prototype Camera Controller

스크립트 파일 자체는 공존해도 되지만 **같은 Rigidbody를 두 Controller가 동시에 제어하면 안 됩니다.**

---

## Player Mecha 권장 구성

같은 루트 GameObject에 다음을 둡니다.

1. `Rigidbody`
2. Collider
3. `MechaInputReader03`
4. `MechaPropulsionFlightController03`
5. `PrototypeThrusterVisualizer03`
6. `MechaDebugHUD03` (선택)

### Input 설정

`MechaInputReader03`의 Input Actions는 **비워둬도 됩니다.**

비워두면 자동으로:

- Mouse = Look
- W/S = Axial
- A/D = Lateral
- R/F = Vertical
- Left Alt = Brake
- Left Shift = High-G

가 생성됩니다.

원하면 포함된 `MechaPrototype03.inputactions`를 직접 연결해도 됩니다.

---

## Main Camera

Main Camera에 `MechaPrototypeCameraController03`을 붙입니다.

- `Flight Controller` = Player Mecha의 `MechaPropulsionFlightController03`
- `Target`은 비워도 Flight Controller에서 자동 해결됩니다.
- Camera를 Mecha의 자식으로 두지 마세요.

0.3 카메라의 핵심은:

- Mouse / Desired Facing: 빠름
- Mecha 위치 변화: 느림
- 화면 외곽: Catch-Up 증가
- Velocity Look-Ahead: 기본 0

입니다.

---

## 3D Debug Lattice

빈 GameObject에 `RuntimeWorldLattice3D03`을 붙이고 Target을 Player Mecha로 지정합니다.

권장 시작값:

- Cell Size: 5
- Half Cell Count: 8
- Major Line Every: 5
- Follow Target On Y: On

격자는 Target을 부드럽게 따라가지 않고 Cell 단위로 Snap하므로 3차원 속도 변화를 읽을 수 있습니다.

---

## Prototype Thruster Visualizer

`PrototypeThrusterVisualizer03`을 Player Mecha에 추가하면 좌/우 단순 Cylinder 노즐이 런타임에 생성됩니다.

노즐 방향은 **Actual Velocity가 아니라 Controller의 Resultant Command**를 읽습니다.
따라서 제어 명령과 실제 운동의 불일치를 디버깅할 수 있습니다.

Game View 벡터선 기본 의미:

- Main Propulsion: 기수축 주/역추력 명령
- Maneuver: 횡/수직 지속 벡터링 명령
- Redirect: 순간 목표 운동벡터
- Velocity: 실제 Rigidbody Velocity
- Brake: 현재 감속 방향

노즐은 배기 방향을 표시하므로 실제 추력 방향과 반대쪽을 향합니다.

---

## Prototype 0.3 핵심 테스트

### 1. W -> A Tap
기대:
- A 입력 순간 Velocity가 빠르게 횡방향으로 Redirect
- A를 놓은 뒤 W가 계속 눌려 있으면 **즉시 전방 Snap이 아니라** Main Propulsion에 의해 점진적으로 전방 궤적으로 복귀

### 2. W + A Hold
기대:
- 초기에 강한 Redirect
- 이후 Forward Main > Lateral Sustain의 대각 지속기동

### 3. W -> S
기대:
- 0 m/s를 거치는 감속-후진이 아니라 Velocity Direction이 빠르게 후방으로 회전

### 4. W Release
기대:
- 자동 기수 정렬 없이 현재 Momentum 유지

### 5. Alt + A Tap
기대:
- Brake Steering은 즉시 작동
- Brake Pressure가 낮으므로 속력손실은 작음

### 6. Alt + A Hold
기대:
- 방향은 계속 조작 가능
- Hold 시간이 길어질수록 Brake Pressure와 실제 감속도 증가

### 7. Alt 연타
기대:
- Release 즉시 실제 Brake는 꺼짐
- Pressure는 남았다가 서서히 감소
- 빠르게 재입력하면 남은 Pressure에서 다시 상승하여 저비용 Steering 연타를 억제

### 8. Mouse 급회전
기대:
- Camera Rotation은 빠르게 Desired Facing을 따라감
- Mecha Position 변화에 Camera Translation은 늦게 반응

### 9. A/D 급기동
기대:
- Mecha가 화면 안에서 크게 이동하는 모습이 보임
- 화면 외곽 접근 시 Camera가 Catch-Up하여 Target 이탈을 방지

---

## 초기 튜닝 권장 순서

1. `Redirect Direction Response`
2. `Redirect Duration`
3. `Main Authority During Redirect`
4. `Sustain Direction Response`
5. `Lateral / Vertical Sustain Authority`
6. `Brake Pressure Rise Time`
7. `Brake Deceleration Curve`
8. `Brake Steering Response`
9. Camera `Position Follow Time`
10. Camera `Soft Dead Zone / Catch-Up Radius`

Maneuver Resolver, Pivot/Flip, Maneuver Reserve, 고도 제한은 아직 포함하지 않았습니다.

설계/구현 기준일: 2026-09-03
