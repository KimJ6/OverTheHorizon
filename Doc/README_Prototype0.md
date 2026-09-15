# OverTheHorizon - Prototype 0 Flight Control Testbed

대상: Unity 6.3.23f1 LTS / Universal 3D / Input System

## 범위

Prototype 0는 다음만 구현한다.

- `Input Actions`
- `MechaInputReader`
- `MechaPlayerIntent`
- `MechaMotionController` (Assisted Rigidbody)
- 선택적 `MechaDebugHUD`

의도적으로 제외:

- Maneuver Resolver
- Pivot / Flip 자동 판단
- Maneuver Reserve
- Combat / Cruise 모드
- 최종 카메라
- 추진익/매니퓰레이터 애니메이션
- 멀티플레이

## 입력

| Input | Intent |
|---|---|
| Mouse | Desired Facing |
| W / S | 기수축 전진 / 후진 추력 |
| A / D | 수평 횡추력 |
| R / F | World Up 기준 상승 / 하강 보조추력 |
| Left Alt | Counter-thrust / Brake |
| Left Shift | High-G 출력 요청 |

## 설치

1. 압축파일의 `Assets/OverTheHorizon/Prototype0` 폴더를 Unity 프로젝트의 `Assets/OverTheHorizon/` 아래로 복사한다.
2. Unity가 임포트를 완료할 때까지 기다린다.
3. 테스트용 Mecha GameObject를 만든다. Capsule/Cube 또는 기존 프로토타입 소체 모두 가능하다.
4. Mecha 루트 GameObject에 다음 컴포넌트를 추가한다.
   - Collider
   - Rigidbody
   - `MechaInputReader`
   - `MechaMotionController`
   - `MechaDebugHUD` (선택)
5. `MechaInputReader > Input Actions`에 `MechaPrototype.inputactions`를 드래그한다.
6. Rigidbody의 Rotation Constraint를 Freeze하지 않는다.
7. 시작 위치를 지면보다 약간 위로 둔다. Prototype 0 기본값은 `Use Gravity = false`로, 체공 제한은 아직 구현하지 않는다.
8. 플레이한다.

## 중요한 프로토타입 전제

`MechaMotionController`의 Inspector 기본값은 **최종 수치가 아니라 테스트를 시작하기 위한 임시값**이다.

특히 아래 값을 직접 바꾸며 체감을 비교한다.

- Forward Acceleration
- Reverse Acceleration
- Lateral Acceleration
- Vertical Acceleration
- Brake Acceleration
- High G Multiplier
- Mouse Degrees Per Pixel
- Attitude Gain
- Angular Velocity Damping
- Max Angular Acceleration
- Soft Max Speed

## 현재 운동 모델

- `Facing Vector`와 `Velocity Vector`는 강제로 정렬하지 않는다.
- 마우스는 목표 자세(`DesiredFacing`)를 갱신한다.
- Rigidbody torque feedback가 목표 자세를 추종한다.
- W/S는 실제 기체의 현재 Forward를 따라 추력한다.
- A/D는 Pitch/Flip에 의해 좌우가 뒤집히지 않도록 yaw-only Control Frame을 사용한다.
- R/F는 World Up을 사용한다.
- Alt는 현재 Velocity 반대 방향으로 가속도를 만들어 속도를 감소시킨다.
- Shift는 translation/rotation 출력 상한을 같이 높인다.

## Prototype 0에서 확인할 것

1. 마우스에 기체가 너무 즉각적으로 붙는가 / 너무 늦는가
2. Facing과 Velocity가 분리된 상태가 읽히는가
3. W/S 추력과 A/D/R/F 보조추력의 역할이 구분되는가
4. Alt가 속도 감소가 아니라 역추진처럼 느껴지는가
5. Shift가 특정 회피기가 아니라 '출력 권한 상승'처럼 느껴지는가
6. 자연스럽게 Pivot이 필요하다고 느끼는 상황이 어떤 상태값에서 발생하는가

## 다음 단계

Prototype 1에서는 현재 Rigidbody 운동을 관찰하도록 별도의 관성형 TPV Camera Controller를 추가한다.
Maneuver Resolver는 아직 추가하지 않는다.
