# P1-A/P1-B Unity 최종 검증 기록

- Tested Source SHA: `58d003c774daee4baf39f5ba478fb45d7a4b1515`
- Evidence Commit SHA: 없음
- Tracked working-tree patch ID (2026-10-15 종합 검증 시점): `10140e9c23485bfc7719d440a169d6b9d4f4f7ce`
- 작업 트리: `CombatStepExecutor`의 봉인된 stage 조회 API와 `CombatStepSystem` 보호 헬퍼를 추가하고, Producer가 이를 사용하도록 수정했다. 검증 중 EditMode/PlayMode 최소 회귀 테스트도 추가했다.
- Unity Editor: `6000.3.23f1`
- Entities: `1.5.0`
- Unity Pipeline: `0.8.0-exp.1`, 실행 중인 Editor Pipeline으로 실행

| 항목 | 결과 | 근거 |
| --- | --- | --- |
| Unity 재컴파일 | 통과 | `recompile_status: completed`, 오류 0 |
| P1-B EditMode | 통과 | 7/7. ViewDot, DistanceSq, StableId tie-break, 생성 순서, No Target, 오염 결과를 포함 |
| P1-B Bootstrap 정상 | 통과 | 실제 `ConfigureExecution`에서 Producer/Resolver Job 결과를 commit하고 `CompletedOrdinal` 증가 |
| P1-B Bootstrap 오염 결과 | 통과 | 오염된 Generation/Tick/Ordinal이 Fault되고 완료 전 차단 |
| Resolver 그룹 멤버십 | 통과 | 수정 전 단독 재현은 0/1 실패(`Running`); 수정 후 World 전용 Resolver는 1/1 통과, 완료 전 Fault |
| Resolver 비활성화 | 통과 | 봉인 뒤 Resolver가 비활성화되면 stage 실행 순서 검증이 Fault시키고 완료를 차단 |
| 전체 EditMode | 통과 | 49/49, skipped 0 |
| P1-B Bootstrap 및 P1-A lifecycle | 통과 | `LocalTimingLifecycleTests` 48/48, skipped 0 |
| 전체 PlayMode | 통과 | 51/51, skipped 0 |
| P1-A Job 수명 및 Stop/Destroy | 통과 | 전체 PlayMode의 기존 lifecycle 회귀 포함 |
| TimingCost 50/60/120Hz | 통과 | 4-player 100-step headless Editor 측정 3/3 통과. 각 측정은 fixed-step managed GC allocation 0을 assert |

## 결론

`PASS` (P1-B 필수 Resolver 등록 계약 수정 범위). `Seal()`이 실제 combat group에서 구성하고 검증한 `orderedStages` 스냅샷만 필수 Resolver 존재의 근거로 사용한다. World에 존재하지만 해당 Executor가 소유·봉인하지 않은 Resolver는 Producer가 Job을 예약하기 전에 기존 Fail-stop 경로로 Fault된다.

## 2026-10-15 종합 안정화 재검증

이 결과는 위 SHA와 미커밋 패치 ID를 함께 기준으로 한다. Base HEAD만으로 현재 테스트 소스를 재현할 수는 없다.

| 항목 | 실제 완료 결과 |
| --- | --- |
| Unity 재컴파일 | `completed`, 오류 0 |
| 공용 Executor EditMode | 24/24, skipped 0 |
| P1-B AutoTargeting EditMode | 7/7, skipped 0 |
| P1-A 수명 및 P1-B Bootstrap PlayMode | `LocalTimingLifecycleTests` 48/48, skipped 0 |
| 전체 EditMode | 49/49, skipped 0 |
| 전체 PlayMode | 51/51, skipped 0 |
| TimingCost | 50/60/120Hz 각각 통과; 4-player 100-step fixed-step managed GC allocation 0 assert |

마지막 전체 PlayMode Pipeline 결과는 `Temp/pipeline_test_status.json`에 남아 있다. Player build 성능과 자동 타게팅 활성 세션의 별도 CPU/GC 측정은 이 검증에 포함하지 않았다.

## 미검증

- 자동 타게팅이 실제로 등록된 세션의 별도 CPU/GC 비용
- Player build 성능
- 대상 제거/재사용과 중복 Stable ID 정책
