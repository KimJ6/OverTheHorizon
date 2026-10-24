# OverTheHorizon

자동 사격 메카를 조종해 군도를 고속으로 돌파하고, 회수한 장비로 성장하며 더 깊이 진입할지 손실 전에 복귀할지 판단하는 **3인칭 로그라이트 익스트랙션 메카 액션**을 개발한다.

직접 조준보다 **기동·거리·각도·장비 선택**에 집중하는 전투와, 강해진 빌드를 활용하면서도 **추가 보상과 손실 위험을 비교해 복귀 시점을 판단하는 경험**을 목표로 한다.

## 프로젝트 소개

본 저장소는 개인 게임 프로젝트의 기획·설계 문서와 Unity 구현을 함께 관리하는 공간이다. 플레이어에게 제공하려는 경험을 먼저 정의하고, 이를 실현하는 시스템의 동작 규칙과 설계 판단의 근거, 검증 기준을 문서화하고 있다.

### 핵심 기획 의도

**1. 강해진 빌드를 더 오래 활용하는 로그라이트 익스트랙션**

로그라이트에서 빌드가 완성되어 기존 위협의 부담이 줄어들면, 플레이어가 고려해야 할 요소도 감소할 수 있다. 이에 더 위험한 구역으로 진입해 높은 보상을 노리는 익스트랙션 구조를 결합했다.

강해진 빌드로 심층에 도전할 유인을 제공하는 동시에, 소모품과 장비 내구도, 확보한 회수품의 가치를 비교해 복귀 시점을 선택하도록 설계한다.

**2. 직접 조준보다 기동과 포지셔닝에 집중하는 전투**

자동 타게팅을 도입해 직접 조준의 부담을 줄이되, 플레이어의 판단까지 자동화하지는 않는다.

플레이어가 기체의 이동 방향과 시야, 적과의 거리·각도, 장비 구성을 조절해 공격 효율을 결정하도록 설계한다. 기동과 위치 선정 자체가 전투 숙련도로 이어지는 것을 목표로 한다.

### 주요 기획·설계 문서

| 문서 | 주요 내용 |
|---|---|
| [PRJ-000 프로젝트 통합 기획 기준서](Doc/GameDesignDocs/00_Project/PRJ-000_프로젝트_통합기획_기준서.md) | 프로젝트 목표, 디자인 필러, 핵심 플레이 루프, 목표 경험과 검증 우선순위 |
| [SYS-140 세션·장비·빌드·익스트랙션](Doc/GameDesignDocs/20_Systems/SYS-140_세션_장비_빌드_익스트랙션.md) | 로그라이트 성장과 장비 운용, 위험·보상에 따른 복귀 판단 |
| [SYS-120 자동 타게팅·직사 화기](Doc/GameDesignDocs/20_Systems/SYS-120_자동타게팅_직사화기.md) | 플레이어의 기동·시야·무기 선택을 공격 결과에 연결하는 전투 규칙 |
| [SYS-110 플레이어 기동](Doc/GameDesignDocs/20_Systems/SYS-110_플레이어_기동.md) | 고속 3D 기동의 조작 경험과 운동 규칙 |
| [SYS-160 군도 작전·전방지원](Doc/GameDesignDocs/20_Systems/SYS-160_군도작전_전방지원.md) | 전투 구획, 항로 확보, 분견대 및 전방지원의 연계 |

기획 관점에서는 **PRJ-000 → SYS-140 → SYS-120** 순서로 읽는 것을 권장한다. 프로젝트가 목표로 하는 경험과 그 경험을 실현하기 위한 세션·전투 규칙을 확인할 수 있다.

### 개발 및 검증 현황

본 프로젝트는 개발 중이며, 기획·설계가 완료된 범위와 실제 구현·검증된 범위를 구분한다.

| 영역 | 진행 현황 |
|---|---|
| 플레이어 기동 | 기본 기동 기능 구현 |
| 공용 시뮬레이션 | 실행 기반 구현 |
| 자동 타게팅 | 일부 통합 기능 구현 및 검증 |
| 세션·장비·익스트랙션 | 설계 단계 |

*현황 기준: 2026-10-25. 이후 세부 구현·검증 상태는 관련 코드와 담당 시스템 문서를 기준으로 확인한다.*

### 기획 및 개발 방식

게임 기획서에는 프로젝트의 목표와 핵심 플레이 흐름을, 시스템별 설계 문서에는 동작 규칙과 설계 판단의 근거, 다른 시스템과의 연동 조건 및 검증 기준을 정리한다.

문서와 코드를 GitHub에서 함께 관리하며, ChatGPT와 Codex를 구현·수정 작업에 활용한다. 이후 Unity에서 게임을 직접 실행해 조작감과 실제 동작을 확인하고, 설계 의도에 비추어 수정 방향을 판단한다.

아래부터는 설계·구현 작업을 위한 문서 및 코드 탐색 안내다.

---

## 1. 읽기 안내

| 하고 싶은 일 | 시작점 |
|---|---|
| 기능 설계·코드 수정하기 | [작업별 문서](#2-작업별-문서)와 [설계·코딩 절차](#3-설계코딩-절차) |
| 문서 작성·갱신하기 | [DOCS-000 문서관리 가이드](Doc/00_Management/DOCS-000_문서관리_운영가이드.md) |
| 프로젝트 목표·범위 확인하기 | [PRJ-000 프로젝트 통합 기획 기준서](Doc/GameDesignDocs/00_Project/PRJ-000_프로젝트_통합기획_기준서.md) |
| 변경 검증·커밋·브랜치 작업하기 | [CONTRIBUTING 저장소 작업 가이드](Doc/00_Management/CONTRIBUTING.md) |
| 개발 환경·확인용 씬 확인하기 | [개발 환경](#4-개발-환경) |

## 2. 작업별 문서

Canon은 현재 채택된 설계 기준이고, Owner는 해당 규칙을 관리하는 담당 문서다. **Canon 채택은 구현 완료나 검증 성공을 뜻하지 않는다.**

프로젝트 목표와 범위는 PRJ-000, 개별 시스템 규칙은 해당 Owner, 공통 런타임과 전투 판정은 ARCH-100에서 확인한다. 전체 Canon 목록과 책임 지도는 PRJ-000에서 관리한다.

| 작업 | 먼저 읽을 문서 |
|---|---|
| 기동·조향·카메라·운동 피드백 | [SYS-110 플레이어 기동](Doc/GameDesignDocs/20_Systems/SYS-110_플레이어_기동.md) |
| 자동 타게팅·직사 화기 | [SYS-120 자동 타게팅·직사 화기](Doc/GameDesignDocs/20_Systems/SYS-120_자동타게팅_직사화기.md) |
| 미사일 유도·회피·요격·군집 표현 | [SYS-130 미사일](Doc/GameDesignDocs/20_Systems/SYS-130_미사일_이타노서커스.md) |
| 세션·장비·빌드·복귀·정산 | [SYS-140 세션·장비·빌드·익스트랙션](Doc/GameDesignDocs/20_Systems/SYS-140_세션_장비_빌드_익스트랙션.md) |
| 구획·항로·분견대·전방지원 | [SYS-160 군도 작전·전방지원](Doc/GameDesignDocs/20_Systems/SYS-160_군도작전_전방지원.md) |
| 공통 런타임·전투 판정·성능 | [ARCH-100 전투·적성 객체 아키텍처](Doc/GameDesignDocs/10_Architecture/ARCH-100_전투_적성객체_아키텍처.md) |

문서의 `depends_on`은 규칙을 받아오는 Owner, `interfaces`는 직접 상호작용하는 문서를 나타낸다. 변경과 관련된 규칙과 계약을 추가로 확인할 때 사용한다.

과거 시도와 선택 이유가 필요하면 [Archive](Doc/GameDesignDocs/90_Archive)를 확인한다.

## 3. 설계·코딩 절차

게임 설계나 동작을 변경할 때 사람과 AI 에이전트는 다음 순서로 필요한 범위를 확인한다. 작은 수정은 해당 부분과 직접 영향을 받는 범위만 확인한다.

1. **요구 확인:** 프로젝트 수준 판단은 PRJ-000에서, 구체적인 규칙과 구현·검증 상태는 담당 Owner에서 확인한다.
2. **영향 확인:** 변경과 관련된 `depends_on`·`interfaces`의 규칙과 계약을 확인한다.
3. **구현 대조:** 관련 코드·씬·직렬화 설정을 읽어 요구와 실제 구현을 비교한다. 차이가 있으면 해당 규칙과 구현을 명시하고, 설계 변경이 필요한지 구현 수정이 필요한지 판단한다.
4. **변경·검증·갱신:** 필요한 범위를 수정하고 담당 Owner의 관련 검증 조건과 CONTRIBUTING에 따라 변경 범위에 맞는 검증을 수행한다. 규칙·상태·근거가 바뀌면 담당 문서를 갱신한다.

예를 들어 기동 감속을 수정한다면 SYS-110의 제어 속도·추진 권한·중력 보정·회귀 조건을 확인한다. [PlayerFlightController](Assets/Scripts/Player/Flight/PlayerFlightController.cs), [FlightMotionSolver](Assets/Scripts/Player/Flight/FlightMotionSolver.cs), [GravitySupportSolver](Assets/Scripts/Player/Flight/GravitySupportSolver.cs)와 씬 설정을 대조하고, 변경과 관련된 SYS-110의 회귀 조건으로 결과를 검증한다.

문서 효력·충돌 해결·근거 작성·갱신 기준은 [DOCS-000](Doc/00_Management/DOCS-000_문서관리_운영가이드.md), 저장소 반영과 검증 절차는 [CONTRIBUTING](Doc/00_Management/CONTRIBUTING.md)을 따른다. README는 소개·개발 환경·문서와 코드 탐색에 영향이 있을 때 갱신한다.

## 4. 개발 환경

| 항목 | 기준 |
|---|---|
| Unity Editor | [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)에 기록된 버전 |
| 패키지 의존성 | [manifest.json](Packages/manifest.json) |
| 기동 확인용 씬 | [SampleScene](Assets/Scenes/SampleScene.unity) |
| 입력 정의 | [PlayerInputReader](Assets/Scripts/Player/Input/PlayerInputReader.cs)와 SYS-110 |

개발 환경 정보는 저장소 설정과 코드 기준이다. 기동 동작의 채택·검증 상태는 SYS-110에서 확인한다. 이 README는 실행·빌드 검증 기록을 제공하지 않는다.

## 5. 저장소 구조

| 경로 | 용도 |
|---|---|
| [Assets/Scripts/Player](Assets/Scripts/Player) | 입력·기동·시점 코드 |
| [Assets/Scripts/Diagnostics](Assets/Scripts/Diagnostics) | 기동 상태와 공간을 확인하는 진단 도구 |
| [Assets/Scenes](Assets/Scenes) | Unity 씬 |
| [Doc/GameDesignDocs](Doc/GameDesignDocs) | 프로젝트·아키텍처·시스템·세계관 설계와 Archive |
| [Doc/00_Management](Doc/00_Management) | 문서와 저장소 운영 기준 |
| [Packages](Packages) | 패키지 의존성 |
| [ProjectSettings](ProjectSettings) | Unity 프로젝트 설정 |
