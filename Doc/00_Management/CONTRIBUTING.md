# Contributing Guide

이 문서는 프로젝트의 일관된 Git 이력과 안정적인 협업을 위해 브랜치, 커밋, Pull Request 관리 규칙을 정의합니다.

모든 기여자는 작업을 시작하기 전에 이 문서를 확인해 주세요.

---

## 1. 기본 원칙

프로젝트의 변경 사항은 다음 흐름을 따릅니다.

```text
Issue
  ↓
Branch
  ↓
Commit
  ↓
Pull Request
  ↓
Review / CI
  ↓
Merge
  ↓
main
```

다음 원칙을 기본으로 합니다.

- `main` 브랜치는 항상 실행 가능한 상태를 유지합니다.
- `main`에서 직접 작업하지 않습니다.
- 하나의 브랜치는 하나의 작업 목적만 가집니다.
- 하나의 커밋은 하나의 논리적인 변경을 담습니다.
- 모든 변경 사항은 Pull Request를 통해 병합합니다.
- 병합 전 코드 리뷰와 자동 검증을 수행합니다.
- 의미 없는 대규모 변경이나 서로 관련 없는 수정은 하나의 PR에 포함하지 않습니다.

---

## 2. Branch Strategy

### 2.1 `main`

`main`은 프로젝트의 기준 브랜치입니다.

다음 조건을 만족하는 코드만 포함합니다.

- 빌드 가능한 코드
- 기본 테스트를 통과한 코드
- 리뷰가 완료된 코드
- 병합이 승인된 코드

`main`에 직접 `commit` 또는 `push`하지 않습니다.

### 2.2 작업 브랜치

모든 작업은 `main`에서 새로운 브랜치를 생성하여 진행합니다.

```bash
git switch main
git pull origin main
git switch -c feat/player-movement
```

브랜치는 작업이 완료되면 Pull Request를 통해 `main`으로 병합하고 삭제합니다.

가능하면 작업 브랜치는 짧게 유지합니다.

---

## 3. Branch Naming Convention

브랜치 이름은 다음 형식을 사용합니다.

```text
<type>/<issue-number>-<description>
```

Issue가 없는 경우 번호는 생략할 수 있습니다.

```text
<type>/<description>
```

### Type

| Type | 용도 |
|---|---|
| `feat` | 새로운 기능 |
| `fix` | 버그 수정 |
| `refactor` | 기능 변화 없는 구조 개선 |
| `perf` | 성능 개선 |
| `docs` | 문서 수정 |
| `test` | 테스트 추가 또는 수정 |
| `build` | 빌드 시스템 및 의존성 변경 |
| `ci` | CI/CD 설정 변경 |
| `chore` | 기타 유지보수 작업 |
| `hotfix` | 긴급 수정 |

좋은 예:

```text
feat/42-player-movement
feat/enemy-targeting
fix/73-camera-shake
refactor/rendering-system
perf/entity-query
docs/update-readme
test/player-controller
ci/github-actions
```

피해야 할 예:

```text
new-feature
test
work
final
final-final
fix2
branch1
```

브랜치 이름만 보고도 무엇을 위한 작업인지 알 수 있도록 작성합니다.

---

## 4. Commit Convention

커밋 메시지는 다음 형식을 권장합니다.

```text
<type>(<scope>): <subject>
```

`scope`가 필요하지 않은 경우 생략할 수 있습니다.

```text
<type>: <subject>
```

예:

```text
feat(player): add boost movement
fix(camera): prevent rotation jitter
refactor(targeting): separate target selection logic
perf(rendering): reduce entity query allocation
docs: update installation guide
test(player): add movement tests
```

### 4.1 Commit Type

| Type | 의미 |
|---|---|
| `feat` | 새로운 기능 |
| `fix` | 버그 수정 |
| `refactor` | 기능 변화 없는 코드 구조 변경 |
| `perf` | 성능 개선 |
| `docs` | 문서 변경 |
| `test` | 테스트 코드 변경 |
| `build` | 빌드 환경 또는 의존성 변경 |
| `ci` | CI 설정 변경 |
| `chore` | 기타 유지보수 |
| `style` | 포맷팅 등 동작에 영향을 주지 않는 변경 |
| `revert` | 기존 변경 되돌리기 |

### 4.2 좋은 커밋

하나의 커밋에는 하나의 논리적인 변경만 포함합니다.

```text
feat(targeting): add nearest target search
fix(player): prevent boost while energy is empty
refactor(camera): separate rotation from follow logic
```

서로 다른 작업은 가능하면 분리합니다.

```text
feat(enemy): add enemy spawn system
docs: document enemy spawn configuration
test(enemy): add spawn system tests
```

### 4.3 피해야 할 커밋

변경 내용을 알 수 없는 메시지는 사용하지 않습니다.

```text
update
fix
수정
작업
asdf
temp
final
final2
버그수정
코드 수정
```

다음처럼 여러 작업을 하나의 커밋으로 묶는 것도 피합니다.

```text
feat: add player movement and fix camera and update docs
```

대신 다음처럼 분리합니다.

```text
feat(player): add movement system
fix(camera): correct follow rotation
docs: update control guide
```

---

## 5. Commit 작성 원칙

커밋을 만들기 전에 다음 사항을 확인합니다.

1. 하나의 목적을 가진 변경인가?
2. 현재 상태에서 빌드가 가능한가?
3. 디버그 코드나 임시 파일이 포함되어 있지 않은가?
4. 불필요한 포맷 변경이 포함되어 있지 않은가?
5. 커밋 메시지만 읽어도 변경 이유를 파악할 수 있는가?

가능하면 다음과 같은 커밋은 피합니다.

```text
WIP
temp
test
backup
```

완성되지 않은 작업을 원격 저장소에 공유해야 한다면 커밋 메시지보다 Pull Request의 `Draft` 기능을 사용하는 것을 권장합니다.

---

## 6. Commit Scope

프로젝트 규모가 커지면 `scope`를 사용하여 변경 영역을 표시합니다.

예:

```text
feat(player): ...
feat(enemy): ...
feat(ui): ...
feat(audio): ...

fix(camera): ...
fix(input): ...

perf(rendering): ...
perf(ecs): ...
```

Scope는 프로젝트 구조에 맞게 정의합니다.

Unity 프로젝트라면 다음과 같이 사용할 수 있습니다.

```text
player
enemy
camera
input
ui
audio
rendering
ecs
physics
network
editor
```

Scope 종류를 지나치게 세분화하지 않고 프로젝트 전체에서 일관되게 사용합니다.

---

## 7. Pull Request

작업이 완료되면 작업 브랜치에서 `main`을 대상으로 Pull Request를 생성합니다.

PR은 가능한 한 하나의 목적만 가져야 합니다.

좋은 PR:

```text
feat: 플레이어 부스트 이동 구현
```

좋지 않은 PR:

```text
플레이어 수정 + 적 추가 + UI 변경 + README 수정
```

PR이 너무 커지면 리뷰가 어려워지고 문제 발생 시 원인을 추적하기도 어려워집니다.

---

## 8. Pull Request 작성 형식

PR 본문에는 최소한 다음 내용을 포함합니다.

```markdown
## 변경 내용

- 플레이어 부스트 이동 구현
- 에너지 소비 로직 추가
- 최대 속도 제한 적용

## 변경 이유

기존 이동 시스템에서는 순간적인 고속 이동을 지원하지 않아
부스트 이동 기능을 추가했습니다.

## 테스트

- [x] 일반 이동
- [x] 부스트 이동
- [x] 에너지 부족 상태
- [x] 최대 속도 제한

## 관련 Issue

Closes #42
```

복잡한 변경이라면 다음 내용도 추가할 수 있습니다.

```markdown
## 설계 결정

## 성능 영향

## Breaking Changes

## 참고 자료
```

---

## 9. Pull Request 크기

PR은 리뷰어가 변경 목적을 쉽게 이해할 수 있는 수준으로 유지합니다.

큰 기능은 다음처럼 단계적으로 나누는 것을 권장합니다.

```text
1. 데이터 구조 추가
2. 핵심 로직 구현
3. 시스템 연결
4. 테스트 추가
5. 문서 업데이트
```

다음과 같은 PR은 가급적 피합니다.

```text
+5,000 lines
-3,000 lines
40 files changed
```

단, 자동 생성 코드나 대규모 리팩터링처럼 합리적인 이유가 있다면 PR 설명에 해당 이유를 명시합니다.

---

## 10. Code Review

리뷰에서는 단순히 코드 스타일만 확인하지 않습니다.

다음 항목을 확인합니다.

- 요구사항을 충족하는가?
- 기존 기능을 손상시키지 않는가?
- 구현 구조가 적절한가?
- 불필요하게 복잡하지 않은가?
- 성능 또는 메모리 문제는 없는가?
- 테스트가 필요한 변경인가?
- 이름과 책임이 명확한가?
- 문서 수정이 필요한가?
- 코드만 보고 알기 어려운 설계 결정이 기록되어 있는가?

리뷰 의견은 사람이 아니라 변경 사항 자체를 대상으로 작성합니다.

---

## 11. Merge Policy

기본 병합 방식은 다음을 권장합니다.

```text
Squash and Merge
```

작업 브랜치의 여러 커밋을 하나의 논리적인 변경으로 정리하여 `main`의 이력을 단순하게 유지할 수 있습니다.

예를 들어 작업 브랜치에 다음 커밋이 있다면,

```text
feat: add target search
fix: correct target distance
refactor: simplify target query
test: add targeting test
```

병합 후 `main`에는 다음과 같이 남길 수 있습니다.

```text
feat(targeting): implement target search system (#42)
```

프로젝트에서 개별 커밋 이력을 보존해야 하는 경우에는 `Rebase and Merge`를 사용할 수 있습니다.

팀에서 특별한 이유가 없다면 하나의 병합 정책을 정하고 일관되게 사용하는 것을 권장합니다.

---

## 12. Merge 전 확인

PR을 병합하기 전에 다음 조건을 만족해야 합니다.

- [ ] 코드 리뷰가 완료되었다.
- [ ] 리뷰 의견이 모두 해결되었다.
- [ ] 빌드가 성공한다.
- [ ] 테스트가 통과한다.
- [ ] CI 검증이 통과한다.
- [ ] 디버그 코드가 제거되었다.
- [ ] 불필요한 파일이 포함되지 않았다.
- [ ] 필요한 문서가 업데이트되었다.
- [ ] Breaking Change가 있다면 명시했다.

---

## 13. 최신 `main` 반영

장기간 작업한 브랜치는 병합 전에 최신 `main` 상태를 반영합니다.

```bash
git switch main
git pull origin main

git switch feat/player-movement
git rebase main
```

또는 프로젝트 정책에 따라 다음 방식을 사용할 수 있습니다.

```bash
git merge main
```

팀에서는 `rebase` 또는 `merge` 중 하나의 방식을 정하고 일관되게 사용하는 것을 권장합니다.

이미 여러 사람이 공유하고 있는 브랜치의 히스토리는 불필요하게 `rebase`하거나 강제 변경하지 않습니다.

---

## 14. Force Push

공유 브랜치와 `main`에서는 다음 명령을 사용하지 않습니다.

```bash
git push --force
```

개인 작업 브랜치에서 rebase 후 강제 push가 필요한 경우에는 가능하면 다음 명령을 사용합니다.

```bash
git push --force-with-lease
```

`--force-with-lease`는 원격 브랜치의 예상하지 못한 변경을 덮어쓰는 위험을 줄여줍니다.

---

## 15. Conflict Resolution

Merge Conflict가 발생하면 충돌 내용을 이해한 뒤 직접 해결합니다.

충돌 해결 후에는 반드시 다음 항목을 다시 확인합니다.

```text
Build
Test
Runtime behavior
```

충돌을 없애기 위해 상대방의 변경 사항을 임의로 삭제하지 않습니다.

변경 의도를 알 수 없다면 해당 코드를 작성한 기여자와 확인합니다.

---

## 16. Issue 연결

가능하면 작업은 Issue와 연결합니다.

브랜치:

```text
feat/42-player-boost
```

커밋:

```text
feat(player): add boost movement
```

PR:

```text
feat: 플레이어 부스트 이동 구현

Closes #42
```

이렇게 하면 다음 관계를 추적하기 쉬워집니다.

```text
Issue
 └─ Branch
     └─ Commit
         └─ Pull Request
             └─ Merge
```

---

## 17. Repository History

Git 이력은 단순한 작업 기록이 아니라 프로젝트의 변경 이유를 추적하기 위한 문서입니다.

좋은 Git 이력은 다음 질문에 답할 수 있어야 합니다.

```text
무엇이 변경되었는가?
왜 변경되었는가?
언제 변경되었는가?
어떤 Issue와 관련되어 있는가?
누가 검토했는가?
```

따라서 커밋 수 자체를 늘리는 것보다 변경의 의미가 명확하게 기록되도록 관리하는 것을 우선합니다.

---

## 18. Recommended Workflow

일반적인 작업 흐름은 다음과 같습니다.

```bash
# 1. main 최신화
git switch main
git pull origin main

# 2. 작업 브랜치 생성
git switch -c feat/42-player-boost

# 3. 작업

# 4. 변경 확인
git status
git diff

# 5. 필요한 파일만 stage
git add <files>

# 6. commit
git commit -m "feat(player): add boost movement"

# 7. 원격 저장소에 push
git push -u origin feat/42-player-boost
```

이후 GitHub에서 Pull Request를 생성합니다.

```text
Review
 ↓
CI
 ↓
Approval
 ↓
Squash and Merge
 ↓
Branch Delete
```

---

## 19. Quick Reference

| 작업 | 규칙 | 예시 |
|---|---|---|
| 기능 브랜치 | `feat/<issue>-<name>` | `feat/42-player-boost` |
| 버그 브랜치 | `fix/<issue>-<name>` | `fix/73-camera-jitter` |
| 문서 브랜치 | `docs/<name>` | `docs/update-readme` |
| 기능 커밋 | `feat(scope): subject` | `feat(player): add boost` |
| 버그 커밋 | `fix(scope): subject` | `fix(camera): prevent jitter` |
| 리팩터링 | `refactor(scope): subject` | `refactor(input): simplify handler` |
| 성능 개선 | `perf(scope): subject` | `perf(ecs): reduce allocations` |
| 병합 | Squash and Merge | `feat(player): implement boost (#42)` |

---

## 20. Summary

이 저장소의 Git 관리 원칙은 다음과 같습니다.

```text
main은 항상 안정적으로 유지한다.

작업마다 별도의 브랜치를 사용한다.

하나의 브랜치는 하나의 목적을 가진다.

하나의 커밋은 하나의 논리적 변경을 가진다.

커밋 메시지만 보고 변경 내용을 이해할 수 있게 한다.

모든 변경은 Pull Request를 통해 검토한다.

CI와 리뷰를 통과한 변경만 main에 병합한다.

Git 이력을 프로젝트의 변경 기록이자 문서로 관리한다.
```

---

## 참고

이 문서는 GitHub 기반 협업에서 `Issue → Branch → PR → Review → CI → Merge`로 이어지는 검토·배포 흐름과, 기술 문서의 명확성·간결성·일관성을 유지하는 원칙을 참고하여 작성했습니다.

작성일: 2026-09-15
