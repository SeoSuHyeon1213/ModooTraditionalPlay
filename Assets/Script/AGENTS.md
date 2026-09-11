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
