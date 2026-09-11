# AGENTS.md

Behavioral and engineering guidelines for Codex.
Merge these rules with repository-specific instructions.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Inspect Before Coding

Before implementing:

- Inspect the relevant code, tests, documentation, and logs first.
- State assumptions that materially affect the implementation.
- If multiple interpretations produce meaningfully different results,
  explain the options and ask only when repository evidence cannot resolve them.
- For minor ambiguity, choose the simplest reversible interpretation and report it.
- Do not ask questions that can be answered by inspecting the repository.
## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. No Closing Colons (Korean Output)

**End Korean sentences with a period, not a colon.**

When the user writes in Korean, your output is also Korean:
- Don't end sentences with `:` even if the next line is a list or example.
- LLMs trained on English docs leak the colon habit into Korean. Catch it.
- The test: every Korean sentence terminator should be `.`, `?`, or `!` — not `:`.
- Colons are fine inside code, key-value pairs, or labels. Not as sentence enders.
This rule applies to conversational Korean responses only.

Do not rewrite:
- source code
- structured data
- existing documentation style
- command output
- logs

## 6. File Header Comments in Korean

**First line of every new source file: a one-line Korean comment stating its role.**

When creating a new file:
- TypeScript/JavaScript: `// 사용자 인증 상태를 관리하는 Context Provider`
- Python: `# KIS API 호출을 비동기로 래핑하는 클라이언트`
- SQL: `-- 일별 집계 결과를 저장하는 머티리얼라이즈드 뷰`
- Place it directly under required directives (`'use client'`, `'use server'`, shebang).
- Skip config files (`*.config.ts`, `package.json`, etc.).

Why: agents read files selectively, not whole codebases. A one-line Korean header gives instant context so the next session (human or agent) can navigate without re-reading the entire file.

File Role Comments

For newly created application source files, add a one-line Korean role
comment only when it improves navigation and matches the surrounding style.

Do not add it to:
- generated or vendored files
- configuration files
- migrations and fixtures
- files whose framework requires a specific first line
- directories that consistently use another documentation style

## 7. Planning and Persistent Context

For non-trivial tasks, state a brief execution plan before editing.

Do not create planning or context files for routine tasks.

Create a persistent execution plan only when:
- the task is expected to span multiple sessions
- multiple agents or worktrees must coordinate
- the change has several independently verifiable milestones
- the user explicitly requests documentation

When persistent planning is needed:
- use the repository's existing planning location
- otherwise use `docs/exec-plans/active/<task-name>.md`
- record decisions, progress, verification results, and unresolved risks
- move completed plans to `docs/exec-plans/completed/`

## 8. Verify Before Completion

If code was changed, verify it before reporting completion.

Use the narrowest reliable verification first:

1. Run the test that reproduces or covers the change.
2. Run tests for the affected package or module.
3. Run lint, type checking, or compilation as applicable.
4. Run the full test suite when practical or required by the repository.

If verification cannot run:
- report the exact command attempted
- include the relevant error output
- distinguish code failure from environment failure
- do not claim the task is fully complete

## 9. Git and Semantic Commits

Do not create commits unless:
- the user explicitly requests commits
- the task is running in a workflow that explicitly requires commits
- repository instructions require a commit before handoff

Before editing:
- inspect `git status`
- preserve unrelated user changes
- do not reset, stash, discard, or overwrite existing changes without permission

When commits are requested:
- create one logical change per commit
- do not mix unrelated changes
- use concise semantic commit messages
- run the relevant verification before committing
- report the resulting commit hash

## 10. Read Errors, Don't Guess

**Read the actual error/log line. Don't pattern-match from memory.**

When something fails:
- Read the full error message and stack trace.
- Check the actual log output, not what you assume it should say.
- Don't apply a "common fix" before confirming the cause.
- If unclear, add a print/log to verify state — then fix.

This is the step LLMs skip most often after "run tests". They guess from error keywords and apply the most-recent-pattern fix. That's how a one-line bug becomes a three-file refactor.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

## 11. 프로젝트 전용 규칙 — 한국 전통놀이 VR

### 11.1 사양의 우선순위

- 사용자의 명시적인 변경 요청을 우선한다.
- 사방치기 관련 작업 전 같은 폴더의 `사방치기규칙_구체화.txt`를 읽고 세부 동작의 기준으로 삼는다.
- `Assets/한국 전통놀이 VR 체험 기획서.pdf`는 프로젝트 전체 방향을 참고하는 문서로 사용한다.
- 위 기준과 기존 대화로 해결되지 않는 사양 충돌은 사용자에게 알리고 확인한다.

### 11.2 현재 구현 범위

- 우선 1인용 사방치기 8칸의 기본 플레이를 완성한다.
- 장구 모드는 사방치기와 독립적으로 유지한다.
- AI 물리, 전신 트래킹, 4칸 모드, 장애물, BPM 연동은 별도 요청 없이 추가하지 않는다.

### 11.3 Unity 작업 분담

- 씬 배치, 컴포넌트 추가, UI 구성과 Inspector 연결은 사용자가 담당한다.
- 스크립트는 필요한 참조를 Inspector에 노출하고 연결 방법을 제공한다.
- 오브젝트와 컴포넌트의 자동 생성은 요청받았을 때 사용한다.

### 11.4 VR 입력과 이동

- 실제 점프, 숙이기, 빠른 신체 회전을 요구하지 않는다.
- Trigger는 선택, 투척, QTE에 사용하고 Grip은 망 줍기에 사용한다.
- 플레이 중 이동은 QTE 결과로 제어한다.

### 11.5 게임 판정 원칙

- 입력 접수, 투척 연출, 착지 판정, 게임 진행을 구분한다.
- 비행 중에는 투척 결과를 확정하지 않는다.
- 결과 이벤트와 실패 횟수는 시도당 한 번만 처리한다.

### 11.6 설정값과 역할 구분

- 파워, 허용 오차, 게이지 속도, 비행 시간 등은 Inspector에서 조정 가능하게 한다.
- 구역 판정은 `Map`, 투척은 `SabangStone`, 이동과 라운드 진행은 해당 담당 코드에서 처리한다.

### 11.7 검증과 완료 보고

- 컴파일 통과와 Unity 실행 검증을 구분하여 보고한다.
- 사용자 연결이 필요한 항목, 미검증 동작, 프로토타입의 제한을 명시한다.
- 기존 씬과 에셋의 사용자 변경을 보존한다.

## 12. 스크립트 최적화, 간소화 및 스파게티 코드 방지

### 12.1 기본 원칙과 변경 범위

- 기존 동작을 유지하면서 불필요한 복잡성과 반복 실행 비용을 줄인다. 코드 줄 수 자체를 목표로 삼지 않는다.
- 간소화는 중복과 불필요한 분기 제거, 성능 최적화는 실행 시간과 메모리 등 비용 감소로 구분한다. 측정하지 않은 구조 개선을 성능 향상으로 보고하지 않는다.
- 작업 대상과 호출 관계를 먼저 확인하고, 근거가 있는 부분만 최소한으로 변경한다. 관련 없는 파일 정리나 기존 자동 생성 방식의 전환을 함께 수행하지 않는다.
- 읽기 쉬운 흐름을 우선하고, 긴 조건을 한 줄로 압축하거나 설정값을 코드 내부 상수로 숨기지 않는다.

### 12.2 Unity 동작과 연결 보존

- 게임 규칙, 입력 타이밍, 상태 전환, 이벤트 호출 횟수를 보존한다.
- 직렬화 필드명과 타입, 공개 메서드 및 UnityEvent 연결 대상을 변경하기 전에 씬·프리팹·호출부의 사용 여부를 확인한다. 코드 참조가 없다는 이유만으로 삭제하지 않는다.
- 변경이 필요하면 기존 연결을 유지할 수 있는 마이그레이션을 적용하고, 사용자가 재연결해야 하는 항목을 명시한다.
- 트리거 중복 입력 방지, 결과·실패 횟수의 중복 처리 방지, 경계선 접촉 기록, 재시도 초기화, 이벤트 해제와 리소스 정리를 단순히 코드가 길다는 이유로 제거하지 않는다.
- 실제 설정 누락을 설명하는 오류 메시지는 유지한다. 대체 구현은 기존 보장 사항을 동일하게 만족해야 한다.

### 12.3 반복 실행 비용 점검

- Update와 FixedUpdate에서는 현재 상태에 필요한 계산만 수행한다.
- 반복적인 오브젝트·컴포넌트 검색은 Inspector 참조나 수명이 명확한 캐시로 대체한다.
- 물리 조회는 필요한 단계와 Layer 범위로 제한한다. 조회 빈도를 줄일 때는 입력 반응성과 충돌 정확도를 함께 검증한다.
- 정적인 UI는 값이나 상태가 바뀔 때 갱신하고, 움직이는 게이지는 필요한 주기로 갱신한다.
- 반복 루프의 임시 컬렉션과 문자열 생성 등 실제 메모리 할당 지점을 확인한다. 할당 여부를 확인하지 않고 일괄 재작성하지 않는다.
- 이벤트 중복 등록, 해제 누락 및 하나의 입력이 여러 단계에서 처리되는 문제를 점검한다.
- 풀링, 전역 캐시, 새 패키지는 실제 필요와 이점이 확인될 때만 도입한다.

### 12.4 책임, 상태와 의존 관계

- 구역 판정, 투척, UI 표시, 라운드 진행처럼 변경 이유가 다른 기능을 한 클래스에 계속 추가하지 않는다. 짧고 응집된 기능을 불필요하게 여러 파일로 나누지도 않는다.
- 진행 단계는 enum 등 명시적인 상태로 표현한다. 여러 bool의 조합으로 단계를 암묵적으로 표현하지 않는다. 경계선 접촉 여부처럼 독립적인 사실을 나타내는 bool은 허용한다.
- 상태 변경 경로를 모으고 명확한 메서드를 통해 전환한다. 다른 스크립트의 내부 상태를 여러 곳에서 직접 수정하지 않는다.
- 게임 진행 코드가 구역 판정·투척 기능을 호출하고 결과를 받도록 의존 방향을 유지한다. 순환 의존과 추적하기 어려운 이벤트 연결을 피한다.
- 입력 처리, 게임 판정, 연출의 경계를 유지한다. UI 표시 함수가 성공 여부를 결정하거나 충돌 콜백이 다음 라운드 전체를 진행하지 않도록 한다.
- 동일한 판정·초기화 로직은 한곳에서 관리한다. 조건이 깊게 중첩되면 조기 반환이나 의미 있는 메서드 분리를 검토한다.
- 단순히 파일이 길다는 이유로 분리하지 않는다. 한 번만 쓰는 기능을 위해 불필요한 관리자, 상속 구조 또는 이벤트 체계를 추가하지 않는다.
- SabangStone에 이동 QTE와 라운드 전체 진행을 계속 추가하지 않는다. 투척 결과를 전달하고 게임 진행 코드가 다음 단계를 결정하게 한다.

### 12.5 검증과 보고

- 대상과 문제 근거 확인, 최소 변경, 동작 검증 순서로 진행한다.
- 변경에 맞는 검증으로 Inspector 연결, 입력 중복 방지, 결과 이벤트 호출 횟수, 실패 후 초기화가 유지되는지 확인한다.
- 무엇을 간소화하거나 최적화했는지, 기존 동작과 연결을 어떻게 보존했는지 보고한다.
- 컴파일, Unity 실행 검증, 성능 측정 결과를 구분한다. 성능 개선 수치는 측정 조건과 근거가 있을 때만 제시한다.
- 확인하지 못한 부분과 사용자 재연결 필요 여부를 명시한다.
