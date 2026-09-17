---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-notice-fallbacks — A scoped notice turn never asks "which supplier"

## Story
As the **Ask engine**, I want the five notice fallbacks decided server-side so a
scoped turn answers or recovers honestly, never asking which supplier.

## Acceptance criteria
- [ ] AC-1 deadline+evidence → answer+deep-link (happy path).
- [ ] AC-2 deadline, no span → answer the date + 360 Review CTA (no fake page).
- [ ] AC-3 no deadline, clause text → quote clause + Review; neither → abstain naming this contract + 360 Review; unscoped deictic → abstain + Portfolio.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 22)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-01 (notice-pack) | the fallbacks wrap the pack |

## Architecture decisions in force
- ADR-024 w19 (cl. 22) — five fallbacks, server-decided.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | notice-fallbacks | M | phase-3 |

## Council decisions carried into this story
- the client renders the server recovery verbatim, never synthesises one.

## Open questions
- none
