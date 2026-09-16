---
id: us-01
type: user-story
parent: feature-05
wave: w18
status: active
---

# us-01-w18-final-integration — Wave w18 is green end to end with a dev acceptance runbook

## Story
As the **operator**, I want one final-integration task that proves the whole
wave builds, tests and type-checks green, and a `docs/waves/w18-acceptance.md`
that turns every item's acceptance into a manual `dev` check, so the wave is
"immediately working" on merge.

## Acceptance criteria
- [ ] AC-1 `dotnet build backend/Raffa.slnx` and per-project `dotnet test` exit 0.
- [ ] AC-2 `npm run typecheck`, `npm run lint`, `npm test` exit 0.
- [ ] AC-3 `docs/waves/w18-acceptance.md` lists, per item, the manual `dev` check (what to click, what to expect), including N17r, A18-p, A18-s, A17-S3, A18-o, A18-ops, A18-e2e, N10, N11, N13, N14.
- [ ] AC-4 the two ops walks (NW-40, NW-41) are recorded as runbook steps in that doc.

## Definition of done
- [ ] every AC above is verified by the named commands with exit 0
- [ ] honours ADR-016 (promotion is a HITL gate, not a task)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| every leaf artifact of the wave | the integration depends on the whole wave's leaves |

## Architecture decisions in force
- ADR-016 — the acceptance walk is recorded, never dispatched; promotion is the operator's HITL gate.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | w18-final-integration | L | phase-5 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- NW-40/NW-41 are runbook walks (no code, no `depends_on`), folded into this task's acceptance doc; NW-30's sweep and NW-50's re-conciliation are real tasks upstream.

## Open questions
- none
