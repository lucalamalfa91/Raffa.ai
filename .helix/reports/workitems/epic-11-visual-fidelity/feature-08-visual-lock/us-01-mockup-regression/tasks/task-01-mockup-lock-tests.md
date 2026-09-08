---
id: E11/F08/US01/T01
type: task
story: us-01-mockup-regression
wave: 11
status: live
target_repo: contigo-web
---

# task-01-mockup-lock-tests — Lock mockup measurements in vitest

## Coding objective

Add `web/tests/styles/mockup-fidelity.test.ts` (extend
`web/tests/styles/layout.test.ts` if that file already covers 40rem). The
file must read CSS source or computed class strings and fail on:

1. `main,` / `main {` with `max-width: 40rem` in `web/src/index.css`
2. `.signin-north-star` without `clamp(30px, 3.8vw, 52px)`
3. `.signin-statement` without `--color-accent-100`
4. `.shell-layout` without `224px`
5. Health status visible (import App test helper or query
   `api-health-status` and expect not visible)

Do not add Playwright unless the repo already has it. Vitest + file reads
are enough.

## Parent story AC covered
- AC-1, AC-2, AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `web/tests/styles/mockup-fidelity.test.ts` | new lock tests |
| `web/tests/styles/layout.test.ts` | keep; do not weaken |

## Context the implementer needs
- Gap report OPEN rows F01–F03 are the lock set. Later screens' tests live
  in their own features; this file is the anti-regression net for chrome.
- **Do not** restyle production CSS here unless a prior e11 task missed a
  lock and the test is red — then fix in the owning file, not with a new look.

## Definition of done
- [ ] `npm test` in `web/` includes `mockup-fidelity` and passes on the
      post-F01–F07 tree.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | prototype measurements still in CSS | `web/tests/styles/mockup-fidelity.test.ts` |

## Open questions blocking this task
- none
