---
id: us-01
type: user-story
parent: feature-04
wave: w19
status: active
---

# us-01-w19-final-integration — Wave w19 is green with a three-question acceptance runbook

## Story
As the **operator**, I want one integration task proving the wave green and a
`docs/waves/w19-acceptance.md` turning the three wow questions into manual `dev`
checks.

## Acceptance criteria
- [ ] AC-1 `dotnet build backend/Raffa.slnx` + per-project `dotnet test` exit 0.
- [ ] AC-2 `npm run typecheck` / `npm run lint` / `npm test` exit 0.
- [ ] AC-3 `docs/waves/w19-acceptance.md` lists the three wow questions (Q1/Q2/Q3) with their manual `dev` checks and the golden `kind=answer`/`answer`-not-`abstain` assertions.

## Definition of done
- [ ] every AC verified by the named commands (exit 0)
- [ ] honours ADR-016 (recorded, never dispatched)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| every leaf artifact of the wave | the integration depends on the whole wave's leaves |

## Architecture decisions in force
- ADR-016 — promotion is the operator's HITL gate.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | w19-final-integration | L | phase-5 |

## Council decisions carried into this story
- Q1 overflows to W20 (epic-32, queued) — not part of this wave's golden walks; Q2 + Q3 are.

## Open questions
- none
