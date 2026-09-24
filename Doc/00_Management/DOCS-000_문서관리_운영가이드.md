---
document_id: DOCS-000
title: 게임 설계 문서 관리·운영 가이드
document_type: management
status: active
revision: 1.0
last_updated: 2026-09-24
scope:
  - document ownership
  - folder organization
  - file naming
  - metadata properties
  - markdown links and backlinks
  - canon and archive policy
  - revision and update workflow
  - obsidian usage
  - git version control
---

# DOCS-000 게임 설계 문서 관리·운영 가이드

## 0. 문서 목적

본 문서는 `Docs/GameDesignDocs`에 있는 게임 설계 문서 집합을 장기간 일관되게 관리하기 위한 운영 기준이다.

이 문서는 **게임 설계 내용을 결정하지 않는다.**  
게임의 실제 규칙은 `PRJ`, `ARCH`, `SYS`, `WORLD` Canon 문서가 결정한다.

본 문서가 결정하는 것은 다음과 같다.

- 어떤 종류의 문서를 어디에 둘 것인가
- 각 Canon 문서가 어떤 책임을 가져야 하는가
- 새로운 규칙을 어느 문서에 기록할 것인가
- 파일명과 `document_id`를 어떻게 정할 것인가
- Obsidian Properties/YAML을 어떤 형식으로 사용할 것인가
- 문서 간 링크와 Backlink를 어떻게 구성할 것인가
- Canon, Draft, Archive를 어떻게 구분할 것인가
- Open Decision을 어떻게 관리할 것인가
- 문서 변경 시 어떤 순서로 최신화할 것인가
- Git을 이용해 버전 이력을 어떻게 보존할 것인가
- LLM에게 어떤 문서를 기준으로 제공할 것인가

---

# 1. 최상위 원칙

## 1.1 Markdown 파일이 원본이다

문서의 실제 원본은 로컬의 `.md` 파일이다.

```text
Markdown = 문서 원본
Obsidian = 문서 탐색·편집·관계 확인
Git = 변경 이력·복구
GitHub = 원격 백업·동기화
```

Obsidian 자체가 문서의 별도 원본을 만드는 것이 아니다.

---

## 1.2 한 규칙에는 한 Owner만 둔다

같은 규칙을 여러 Canon 문서가 각각 정의하지 않는다.

예:

```text
플레이어 운동 규칙
→ SYS-110

Target Registry / Spatial Broadphase
→ ARCH-100

무기별 Target Selection
→ SYS-120

Missile Guidance
→ SYS-130

복귀 시 장비 손실 규칙
→ SYS-140

군도 안전권·작전 항로
→ SYS-160

리마니움의 세계관적 정의
→ WORLD-100
```

다른 문서는 해당 규칙이 필요할 때 Owner 문서를 참조한다.

### 원칙

> **One Rule, One Owner.**

---

## 1.3 문서의 최신성보다 책임 범위가 우선한다

문서의 작성일이나 Revision이 더 높다고 해서 다른 문서의 Owner 영역을 덮어쓰지 않는다.

예를 들어 `SYS-130`이 더 최근에 수정되었더라도 공통 Combat Target Registry 구조는 `ARCH-100`이 우선한다.

충돌이 발견되면 둘 중 하나를 임의로 선택하지 않는다.

```text
충돌 발견
→ 실제 Owner 확인
→ Owner 문서에서 규칙 확정
→ 비Owner 문서를 참조 형태로 수정
```

---

## 1.4 현재 Canon과 과거 이력을 분리한다

현재 설계와 과거 실험·폐기안을 같은 문서 안에 계속 누적하지 않는다.

```text
현재 규칙
→ Canon

아직 결정되지 않은 규칙
→ Open Decisions

과거 Prototype / 폐기안 / 설정 연구
→ 90_Archive

동일 Canon의 과거 버전
→ Git History
```

Archive는 버전 백업 폴더가 아니다.

---

## 1.5 파일 수를 필요 이상으로 늘리지 않는다

새 문서는 다음 세 조건을 모두 만족할 때만 만든다.

1. 독립적으로 설명할 수 있다.
2. 독립적으로 설계·검증할 수 있다.
3. 다른 문서에서 하나의 책임 단위로 참조할 가치가 있다.

그렇지 않으면 기존 Owner 문서의 하위 섹션으로 관리한다.

---

# 2. Vault와 폴더 구조

## 2.1 권장 최상위 구조

Obsidian Vault는 `Docs`로 둔다.

```text
Docs/
├─ 00_Management/
│  └─ DOCS-000_문서관리_운영가이드.md
│
├─ GameDesignDocs/
│  ├─ 00_Project/
│  ├─ 10_Architecture/
│  ├─ 20_Systems/
│  ├─ 30_World/
│  └─ 90_Archive/
│
└─ 90_Inbox/
```

---

## 2.2 폴더별 책임

| 폴더 | 역할 |
|---|---|
| `00_Management` | 문서·Git·Obsidian 운영 규칙 |
| `GameDesignDocs/00_Project` | 프로젝트 전체 기준 |
| `GameDesignDocs/10_Architecture` | 여러 시스템이 공유하는 기술·Runtime 규칙 |
| `GameDesignDocs/20_Systems` | 개별 Gameplay System |
| `GameDesignDocs/30_World` | 세계관의 현재 사실 |
| `GameDesignDocs/90_Archive` | 폐기·대체·과거 실험·설정 연구 |
| `90_Inbox` | 아직 Owner가 결정되지 않은 아이디어·메모 |

`90_Inbox`의 내용은 Canon이 아니다.

---

# 3. 문서 종류와 책임

## 3.1 PRJ — Project Canon

형식:

```text
PRJ-000_프로젝트_통합기획_기준서.md
```

소유 범위:

- 프로젝트 정체성
- 장르 정의
- 디자인 필러
- Core Loop
- 목표 플레이 경험
- 시스템 지도
- 프로젝트 수준 성능 목표
- MVP 우선순위
- 프로젝트 수준 리스크
- Canonical Document Index

PRJ는 상세 알고리즘이나 시스템 내부 규칙을 소유하지 않는다.

---

## 3.2 ARCH — Architecture Canon

형식:

```text
ARCH-100_전투_적성객체_아키텍처.md
```

소유 범위:

- 여러 Gameplay System이 공유하는 Runtime
- 공통 데이터 흐름
- Authority
- Determinism
- Registry / Spatial Broadphase
- 공통 Hit / Damage / Death Pipeline
- Lifecycle
- Gameplay / Visual 분리
- 공통 성능 Budget
- Profiling 원칙

ARCH는 개별 시스템의 플레이 규칙을 소유하지 않는다.

---

## 3.3 SYS — System Canon

형식:

```text
SYS-110_플레이어_기동.md
SYS-120_자동타게팅_직사화기.md
SYS-130_미사일_이타노서커스.md
...
```

각 SYS 문서는 하나의 Gameplay System을 소유한다.

각 시스템 문서는 별도 문서로 Gameplay / Presentation / Performance를 분리하지 않는다.

하나의 System Canon 안에서 다음을 함께 관리한다.

```text
목표 경험
Gameplay 규칙
Presentation 요구
Performance 요구
Validation
Open Decisions
```

---

## 3.4 WORLD — World Canon

형식:

```text
WORLD-100_세계관_Canon.md
```

소유 범위:

- 현재 세계 안에서 사실인 것
- 물리·우주론·역사·세력의 Canon
- Gameplay와 설정의 허구층위 연결
- 플레이어 공개 용어
- 작가측 설정 용어
- 미확정 세계관 문제

외부 작품 비교, 창작 모티프 연구, 폐기된 설정은 WORLD Canon에 두지 않는다.

---

## 3.5 Archive

Archive는 현재 Source of Truth가 아니다.

주요 용도:

- 폐기된 설계안
- 대체된 구형 문서
- Prototype 이력
- 실패 원인
- 설정 연구 과정
- 외부 모티프 검토

Archive 문서에는 반드시 현재 Canon을 명시한다.

예:

```yaml
canonical_replacement: SYS-110
```

또는 본문:

```md
> 이 문서는 현재 Canon이 아니다.
> 현재 규칙은 SYS-110을 참조한다.
```

---

## 3.6 Management 문서

`00_Management` 문서는 문서를 관리하는 방법만 정의한다.

Management 문서가 Gameplay Rule을 결정해서는 안 된다.

게임 설계와 문서관리 규칙이 충돌하는 경우:

```text
게임 규칙
→ 해당 Canon

문서 운영 방식
→ Management
```

---

# 4. 현재 Canon 책임 지도

현재 기준 Canon은 다음 8개다.

| ID | Owner 범위 |
|---|---|
| `PRJ-000` | 프로젝트 통합 기준 |
| `ARCH-100` | 전투·적성 객체 공통 Runtime |
| `SYS-110` | 플레이어 기동 |
| `SYS-120` | 자동 타게팅·직사 화기 |
| `SYS-130` | 미사일 위협·Guidance·연출 |
| `SYS-140` | 세션·장비·빌드·익스트랙션 |
| `SYS-160` | 군도 작전·전방지원 |
| `WORLD-100` | 세계관 Canon |

현재 문서 구조에서 새 규칙을 작성하기 전에 반드시 이 표를 먼저 확인한다.

---

# 5. 파일명 규칙

## 5.1 Canon 파일명

기본 형식:

```text
<ID>_<짧고_명확한_한글명>.md
```

예:

```text
PRJ-000_프로젝트_통합기획_기준서.md
ARCH-100_전투_적성객체_아키텍처.md
SYS-110_플레이어_기동.md
WORLD-100_세계관_Canon.md
```

---

## 5.2 Canon 파일명에는 버전을 붙이지 않는다

금지:

```text
SYS-130_미사일_v1.md
SYS-130_미사일_v1.1.md
SYS-130_미사일_최종.md
SYS-130_미사일_최종진짜.md
```

권장:

```text
SYS-130_미사일_이타노서커스.md
```

현재 파일이 항상 최신 Canon이다.

과거 버전은 Git이 관리한다.

---

## 5.3 ID는 변경하지 않는다

`document_id`는 문서의 영구 식별자다.

제목을 바꾸더라도 ID는 가급적 유지한다.

예:

```text
SYS-130
```

의 제목이 향후 달라져도 동일 시스템의 연속이라면 `SYS-130`을 유지한다.

책임 자체가 완전히 분리되거나 다른 시스템으로 재정의될 때만 새 ID를 검토한다.

---

## 5.4 번호 대역

현재 권장 범위:

```text
PRJ-000          프로젝트
ARCH-100~199     공통 아키텍처
SYS-100~199      핵심 Gameplay System
WORLD-100~199    세계관
DOCS-000~099     문서 운영
```

새 번호는 기존 의미를 유지하면서 충돌하지 않게 부여한다.

---

# 6. Properties / YAML 규칙

## 6.1 표준 필드

Canon 문서는 아래 스키마를 기본으로 한다.

```yaml
---
document_id: SYS-130
title: 미사일 시스템 요구사항 및 설계 — 이타노서커스형 군집
document_type: system
status: canonical
revision: 1.0
last_updated: 2026-09-24

scope:
  - missile gameplay threat
  - missile lifecycle and FSM
  - missile guidance and salvo behavior

depends_on:
  - ARCH-100

interfaces:
  - SYS-110
  - SYS-120
  - SYS-140
  - SYS-160
  - WORLD-100

supersedes:
  - missile_design_itano_circus_v0.2.md

source_documents:
  - missile_design_itano_circus_v0.2.md
---
```

---

## 6.2 필드 의미

| 필드 | 의미 | 필수 여부 |
|---|---|---|
| `document_id` | 영구 문서 ID | 필수 |
| `title` | 사람이 읽는 제목 | 필수 |
| `document_type` | 문서 종류 | 필수 |
| `status` | 현재 효력 상태 | 필수 |
| `revision` | 의미 있는 설계 Revision | Canon 권장 |
| `last_updated` | 마지막 의미 변경일 | 필수 |
| `scope` | 이 문서가 소유하는 범위 | Canon 필수 |
| `depends_on` | 규칙을 받아오는 상위 Owner | 필요 시 |
| `interfaces` | 직접 상호작용하는 다른 Canon | 필요 시 |
| `supersedes` | 이 문서가 대체한 이전 문서 | 필요 시 |
| `source_documents` | 작성 근거가 된 원문 | 필요 시 |
| `canonical_replacement` | Archive의 현행 대체 문서 | Archive 필수 |
| `canonical_documents` | PRJ의 Canon Index용 ID 목록 | PRJ만 사용 |

---

## 6.3 `document_type` 값

권장 값:

```text
project
architecture
system
world
archive
management
```

기존 문서에서 사용 중인 `type`, `organization`은 향후 정리 시 `document_type`으로 통일한다.

---

## 6.4 `status` 값

게임 설계 문서는 다음 상태를 사용한다.

```text
canonical
draft
superseded
archive
```

의미:

| 상태 | 의미 |
|---|---|
| `canonical` | 현재 Source of Truth |
| `draft` | 검토 중이며 현재 Canon을 대체하지 않음 |
| `superseded` | 다른 문서에 의해 대체됨 |
| `archive` | 역사·연구·Prototype 보존용 |

Management 문서는 필요하면 `active`, `draft`, `retired`를 사용할 수 있다.

---

## 6.5 `depends_on`과 `interfaces`의 차이

### depends_on

이 문서가 규칙을 받아오는 Owner다.

예:

```yaml
depends_on:
  - ARCH-100
```

SYS-130이 공통 Runtime 구조를 ARCH-100에 의존한다는 뜻이다.

### interfaces

서로 데이터를 주고받거나 직접적인 시스템 경계를 가진 문서다.

예:

```yaml
interfaces:
  - SYS-120
  - SYS-140
```

`interfaces`는 우선순위를 의미하지 않는다.

---

## 6.6 YAML에는 링크를 넣지 않는다

권장:

```yaml
depends_on:
  - ARCH-100
```

금지:

```yaml
depends_on:
  - "[ARCH-100](../10_Architecture/...)"
```

YAML은 ID 기반 구조 정보로 유지한다.

클릭 가능한 내비게이션은 본문의 `관련 Canon`에서 제공한다.

---

## 6.7 Metadata 필드 이름을 중복하지 않는다

앞으로 다음은 사용하지 않는다.

```text
type + organization
related + interfaces
source_document + source_documents
version + revision
```

통일 기준:

```text
document_type
interfaces
source_documents
revision
```

기존 문서는 추후 일괄 정리할 수 있다.

---

# 7. Canon 문서 기본 구조

Canon 문서는 가능한 한 다음 구조를 따른다.

```md
---
YAML
---

# 문서 제목

## 0. 문서 계약

### 0.1 이 문서가 결정하는 것

### 0.2 이 문서가 결정하지 않는 것

### 0.3 Canon 해석 원칙
필요 시

### 0.4 관련 Canon

---

## 1. 시스템/영역 요약

...

## N. 검증

...

## N+1. Open Decisions

...

## N+2. Canonical Summary
필요 시
```

`0. 문서 계약`은 LLM과 사람이 문서의 책임 범위를 빠르게 판단하기 위한 핵심 섹션이다.

---

# 8. 링크 규칙

## 8.1 표준 Markdown 링크를 사용한다

권장:

```md
[ARCH-100 전투·적성 객체 아키텍처](../10_Architecture/ARCH-100_전투_적성객체_아키텍처.md)
```

기본 규칙으로 Obsidian 전용 Wiki Link는 사용하지 않는다.

```text
[[ARCH-100]]
```

를 기본 형식으로 사용하지 않는 이유:

- GitHub에서도 동일하게 동작하는 표준 Markdown 유지
- 다른 Markdown Editor와 호환
- LLM 입력 시 명시적 경로 유지
- Vault 밖으로 옮겨도 구조 이해가 쉬움

---

## 8.2 링크는 상대경로를 사용한다

절대경로 금지:

```text
C:\Users\...\Docs\GameDesignDocs\...
```

권장:

```text
../10_Architecture/ARCH-100_전투_적성객체_아키텍처.md
```

이렇게 해야 PC나 저장 위치가 바뀌어도 링크 구조를 유지할 수 있다.

---

## 8.3 관련 Canon 섹션에서 실제 링크를 제공한다

각 Canon의 `0. 문서 계약` 안에 `관련 Canon` 섹션을 둔다.

예:

```md
### 0.4 관련 Canon

- [PRJ-000 프로젝트 통합 기획 기준서](../00_Project/PRJ-000_프로젝트_통합기획_기준서.md)
- [ARCH-100 전투·적성 객체 아키텍처](../10_Architecture/ARCH-100_전투_적성객체_아키텍처.md)
- [SYS-120 자동 타게팅·직사 화기](SYS-120_자동타게팅_직사화기.md)
```

본문에서 문서 ID가 반복될 때마다 모두 링크로 만들 필요는 없다.

본문에서는 다음처럼 간결하게 사용해도 된다.

```md
공통 Runtime은 `ARCH-100`을 따른다.
```

---

## 8.4 링크와 Metadata의 관계

권장 구조:

```text
YAML
→ 구조적 관계를 ID로 표현

관련 Canon
→ 사람이 클릭할 수 있는 실제 Markdown 링크

본문
→ 필요한 곳에서 문서 ID를 간단히 참조
```

---

## 8.5 Backlink의 의미

Obsidian Backlink는 해당 문서를 참조하는 다른 문서를 찾는 데 사용한다.

예:

```text
ARCH-100 수정
→ ARCH-100 Backlinks 확인
→ SYS-120 / SYS-130 / SYS-160 등 영향 문서 확인
```

Backlink는 **변경 영향 범위 확인 도구**로 사용한다.

그래프 모양 자체를 예쁘게 만드는 것이 목적이 아니다.

---

## 8.6 Archive 링크 규칙

Canon에서 Archive는 과거 근거를 확인해야 할 때만 링크한다.

Archive에서 Canonical replacement로 가는 링크는 반드시 둔다.

즉 방향은 다음을 권장한다.

```text
Archive
→ 현재 Canon : 필수

현재 Canon
→ Archive : 역사 추적이 필요한 경우만
```

---

# 9. Open Decisions 규칙

## 9.1 Open Decision은 Canon이 아니다

`Open Decisions`에 있는 내용은 아직 확정된 규칙으로 사용하지 않는다.

예:

```md
## Open Decisions

- Combat Mode의 최종 FOV
- Boost 자원 비용
- Missile ECM 장비 채택 여부
```

이 상태에서 LLM이나 구현 작업자가 임의 수치를 확정해서는 안 된다.

---

## 9.2 결정되면 본문으로 이동한다

결정 과정:

```text
Open Decision
↓
Prototype / 검증 / 설계 결정
↓
Owner 문서 본문에 Canon 규칙으로 기록
↓
Open Decisions에서 제거
↓
revision / last_updated 갱신
↓
관련 Canon 영향 확인
↓
Git Commit
```

체크박스만 완료 처리하고 Open Decisions에 계속 두지 않는다.

---

## 9.3 세계관은 `Open Canon`을 사용할 수 있다

WORLD 문서에서는 세계관적 미확정 사항을 `Open Canon`으로 부를 수 있다.

의미는 일반 `Open Decisions`와 동일하다.

> 아직 세계 내부의 확정 사실이 아니다.

---

# 10. Revision과 날짜 관리

## 10.1 Git과 Revision의 역할을 구분한다

```text
Git Commit
→ 모든 실제 변경 이력

revision
→ 사람이 보는 의미 있는 설계 단계
```

오탈자 하나마다 Revision을 올리지 않는다.

---

## 10.2 Revision 증가 기준

권장 기준:

| 변경 | 처리 |
|---|---|
| 오탈자·띄어쓰기 | Revision 유지 |
| 문장 표현 개선, 의미 동일 | Revision 유지 |
| 설명 추가, 기존 규칙 불변 | 보통 유지 |
| 실제 Gameplay Rule 변경 | Minor 증가 |
| 시스템 책임 또는 구조의 큰 변경 | Major 증가 |

예:

```text
1.0 → 1.1
```

같은 책임 안에서 의미 있는 규칙 변경.

```text
1.4 → 2.0
```

문서 역할이나 핵심 구조가 크게 변경됨.

---

## 10.3 `last_updated`

`last_updated`는 의미 있는 내용 변경 때 갱신한다.

단순 포맷팅이나 오탈자 수정은 반드시 갱신할 필요가 없다.

형식:

```yaml
last_updated: 2026-09-24
```

---

# 11. Archive 정책

## 11.1 Git으로 해결할 변경

같은 Canon의 과거 버전은 Archive 복사본을 만들지 않는다.

예:

```text
SYS-130 revision 1.0
SYS-130 revision 1.1
SYS-130 revision 1.2
```

모두 파일 하나:

```text
SYS-130_미사일_이타노서커스.md
```

로 유지한다.

이전 상태는 Git History에서 확인한다.

---

## 11.2 Archive로 이동할 문서

다음은 Archive 대상이다.

- 구형 종합 설계서가 새 Canon에 완전히 대체됨
- 실패한 Prototype의 분석 가치가 있음
- 설정 연구 과정이 현행 세계관과 분리되어야 함
- 폐기된 접근을 재발 방지용으로 보존할 가치가 있음

---

## 11.3 Archive 문서의 필수 경고

Archive 상단에 다음 의미가 분명해야 한다.

```md
> 이 문서는 현재 Canon이 아니다.
> 현재 규칙은 `SYS-xxx`를 참조한다.
```

그리고 YAML:

```yaml
document_type: archive
status: archive
canonical_replacement: SYS-xxx
```

---

# 12. 문서 변경 절차

## 12.1 가장 먼저 Owner를 찾는다

새로운 아이디어가 생기면 바로 아무 문서에 적지 않는다.

먼저 질문한다.

> **이 규칙을 최종적으로 결정할 책임은 어느 문서에 있는가?**

예:

```text
Missile 선회 방식
→ SYS-130

Salvo 공통 Target Sample 인프라
→ ARCH-100

미사일 장비의 세션 획득·내구도
→ SYS-140
```

---

## 12.2 Owner-first 변경 순서

기본 절차:

```text
1. Owner 확인
2. Owner Canon 수정
3. Backlinks / interfaces 확인
4. 영향받는 Canon 검토
5. 중복 규칙을 참조 형태로 정리
6. Open Decision 상태 정리
7. revision / last_updated 갱신
8. Git Diff 확인
9. Commit
10. Push
```

---

## 12.3 비Owner 문서에 같은 규칙을 복사하지 않는다

금지 예:

```text
ARCH-100에 Target Registry 규칙 정의
SYS-120에도 동일 내용 복사
SYS-130에도 동일 내용 복사
```

권장:

```text
ARCH-100
→ 실제 규칙 정의

SYS-120
→ "후보 공급은 ARCH-100을 따른다."

SYS-130
→ "Spatial Broadphase는 ARCH-100을 따른다."
```

---

# 13. 변경 영향 확인

문서를 수정한 뒤 최소한 다음을 확인한다.

| 수정 문서 | 우선 영향 확인 |
|---|---|
| `PRJ-000` | 전체 Canon |
| `ARCH-100` | SYS-120, SYS-130, SYS-160 |
| `SYS-110` | SYS-120, SYS-140, SYS-160 |
| `SYS-120` | ARCH-100, SYS-110, SYS-130, SYS-140 |
| `SYS-130` | ARCH-100, SYS-110, SYS-120, SYS-140, SYS-160 |
| `SYS-140` | SYS-110, SYS-120, SYS-130, SYS-160, WORLD-100 |
| `SYS-160` | ARCH-100, SYS-110, SYS-130, SYS-140, WORLD-100 |
| `WORLD-100` | PRJ-000 및 설정 근거를 사용하는 SYS |

이 표는 절대 규칙이 아니라 첫 번째 확인 범위다.

실제 영향 범위는 `interfaces`, `depends_on`, Obsidian Backlinks로 확인한다.

---

# 14. Obsidian 운영 규칙

## 14.1 PRJ-000을 게임 설계의 진입점으로 사용한다

게임 설계를 읽을 때:

```text
PRJ-000
→ 필요한 Owner Canon
→ 필요한 경우 Archive
```

순서로 이동한다.

Archive부터 현재 설계를 해석하지 않는다.

---

## 14.2 검색을 적극 사용한다

전체 검색:

```text
Ctrl + Shift + F
```

유용한 검색 예:

```text
Open Decisions
FOV
Zone
Encounter
Target
Authority
Pooling
Boost
```

동일 개념이 여러 문서에서 중복 정의되는지 확인하는 데 사용한다.

---

## 14.3 Backlinks를 영향 분석에 사용한다

문서를 수정하기 전이나 후에 해당 문서의 Backlinks를 본다.

특히 `ARCH-100`처럼 여러 시스템이 참조하는 문서는 중요하다.

---

## 14.4 Graph View는 보조 수단이다

Graph View는 문서 관계를 시각적으로 확인하는 보조 도구다.

문서 구조의 정확성은 다음이 우선한다.

```text
Owner
YAML
관련 Canon 링크
Backlinks
```

Graph가 보기 좋게 나오는 것을 목표로 문서를 과도하게 링크하지 않는다.

---

# 15. Git 운영 규칙

## 15.1 초기에는 `main` 하나만 사용한다

Git 초보 단계에서는 다음 흐름으로 충분하다.

```text
Obsidian 수정
↓
GitHub Desktop
↓
Changes / Diff 확인
↓
Commit to main
↓
Push origin
```

---

## 15.2 Commit은 의미 단위로 한다

파일 저장마다 Commit하지 않는다.

하나의 설계 변경이 끝났을 때 Commit한다.

예:

```text
docs(sys-130): revise missile guidance rules
docs(sys-140): clarify extraction loss rules
docs(arch): revise combat target infrastructure
docs: align missile ownership across canon
```

---

## 15.3 Commit 전에 Diff를 확인한다

확인 사항:

- 의도하지 않은 파일이 수정되지 않았는가
- 문서가 삭제되지 않았는가
- 대량의 공백/인코딩 변화가 생기지 않았는가
- Archive와 Canon을 잘못 이동하지 않았는가

---

## 15.4 여러 PC를 사용할 때

작업 시작:

```text
Fetch / Pull
```

작업 종료:

```text
Commit
Push
```

원칙:

> **작업 전 Pull, 작업 후 Push.**

---

## 15.5 Branch는 큰 실험부터 사용한다

다음과 같은 경우 Branch를 고려한다.

```text
현재 Canon을 유지한 채
아키텍처를 크게 재설계하고 싶다.
```

예:

```text
docs/rework-missile-architecture
```

작은 문장 수정이나 일반적인 Canon 갱신에 Branch를 의무화하지 않는다.

---

# 16. LLM과 문서를 사용할 때의 규칙

## 16.1 현재 설계를 질문할 때

우선 제공:

```text
PRJ-000
+
질문 Owner Canon
```

필요 시:

```text
ARCH-100
또는
직접 Interface가 있는 다른 SYS
```

까지 추가한다.

항상 8개 문서를 전부 넣을 필요는 없다.

---

## 16.2 Archive는 기본 입력에서 제외한다

현재 구현이나 설계를 물을 때 Archive를 기본 근거로 사용하지 않는다.

Archive가 필요한 질문:

- 왜 이 방식을 폐기했는가
- 이전 Prototype에서 어떤 문제가 있었는가
- 세계관 설정이 어떻게 도출되었는가
- 과거 안과 현재 Canon을 비교해달라

---

## 16.3 LLM에게 임의 통합을 요구하지 않는다

서로 다른 Canon이 충돌할 경우:

```text
"두 내용을 적당히 합쳐줘"
```

보다:

```text
"Owner를 판별하고 충돌을 보고한 뒤,
어느 문서를 수정해야 하는지 제안해줘."
```

라고 요청한다.

---

## 16.4 Open Decision을 사실처럼 사용하지 않는다

LLM에게 문서를 줄 때도:

```text
Open Decisions / Open Canon은 미확정이며
본문의 확정 규칙과 동일하게 취급하지 않는다.
```

는 원칙을 유지한다.

---

# 17. 새 문서 생성 기준

새 Canon을 만들기 전에 다음을 확인한다.

```text
[ ] 기존 Owner 문서에 넣으면 서로 다른 두 핵심 책임이 충돌하는가?
[ ] 이 주제를 독립적으로 설명할 수 있는가?
[ ] 독립적인 요구사항과 검증 기준이 있는가?
[ ] 다른 문서에서 독립 책임 단위로 참조할 가능성이 높은가?
[ ] 단순히 내용이 길다는 이유로 분리하려는 것은 아닌가?
```

앞의 네 항목이 대부분 Yes이고 마지막이 No일 때만 새 Canon을 고려한다.

---

# 18. 문서 삭제 규칙

Canon을 바로 삭제하지 않는다.

다음 순서를 따른다.

```text
1. 대체 Owner 결정
2. 필요한 내용 이관
3. 링크 갱신
4. 대체 문서에서 supersedes 기록
5. 기존 문서를 Archive 또는 superseded 처리
6. Git Commit
```

완전히 불필요한 파일도 Git History가 있기 때문에 삭제 가능하지만, 설계 의사결정 근거가 있는 문서는 Archive를 우선한다.

---

# 19. 표준 변경 체크리스트

Canon을 수정한 뒤 다음만 확인하면 된다.

```text
[ ] 이 규칙의 Owner가 맞는가?
[ ] 다른 Canon의 책임을 침범하지 않았는가?
[ ] 같은 규칙을 여러 문서에 복사하지 않았는가?
[ ] Open Decision이 확정된 경우 본문으로 이동했는가?
[ ] depends_on / interfaces 변경이 필요한가?
[ ] 관련 Canon 링크가 유효한가?
[ ] Revision 변경이 필요한가?
[ ] last_updated 변경이 필요한가?
[ ] Archive에 잘못된 최신 규칙이 남아 있지 않은가?
[ ] Git Diff를 확인했는가?
[ ] 의미 있는 Commit 메시지를 작성했는가?
[ ] Push했는가?
```

---

# 20. 현재 Metadata 정규화 목표

현재 Canon들은 과도기적으로 다음 표현이 혼재할 수 있다.

```text
type
organization

related
interfaces

source_document
source_documents

version
revision
```

향후 일괄 정리 시 다음으로 통일한다.

```text
document_type
interfaces
source_documents
revision
```

정리 전까지는 기존 필드가 있다고 해서 문서 내용이 무효가 되는 것은 아니다.

이 가이드는 **향후 작성·수정 시 적용할 목표 표준**이다.

---

# 21. 가장 간단한 일상 운영 절차

새 아이디어가 생겼을 때:

```text
아이디어 발생
↓
Owner가 바로 보이면 해당 Canon 확인
Owner가 불분명하면 90_Inbox에 임시 기록
↓
Owner 결정
↓
Open Decision 또는 Canon 본문에 반영
↓
관련 Canon / Backlinks 확인
↓
필요한 문서만 동기화
↓
Git Diff
↓
Commit
↓
Push
```

현재 설계를 확인할 때:

```text
PRJ-000
↓
Owner Canon
↓
필요한 Interface Canon
↓
과거 이유가 필요할 때만 Archive
```

---

# 22. 최종 운영 원칙 요약

본 프로젝트의 문서 관리는 다음 문장으로 요약한다.

> **현재 설계는 적은 수의 Canon 문서에 책임별로 집중하고, 하나의 규칙에는 하나의 Owner만 둔다. 문서 간 관계는 ID 기반 Metadata와 표준 Markdown 링크로 표현하며, 미정 사항은 Open Decisions에 격리하고, 과거 버전은 Git에, 폐기된 별도 설계와 Prototype 이력은 Archive에 보존한다. Obsidian은 현재 Canon을 탐색하고 변경 영향을 확인하는 인터페이스로 사용한다.**

---

**기준일: 2026-09-24**
