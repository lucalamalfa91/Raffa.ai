---
id: us-02
type: user-story
parent: feature-05
wave: w18
status: active
---

# us-02-abstain-recovery-web — Abstain renders the recovery action as secondary

## Story
As a **user** who got an abstain, I want a visible (secondary) next step, so
Ask never dead-ends on a bare "Cannot determine reliably."

## Acceptance criteria
- [ ] AC-1 an abstain reply renders its recovery action as a secondary action (never primary).
- [ ] AC-2 the action reuses the existing `ActionRow` (native link, ADR-019), not a new control.
- [ ] AC-3 an abstain without an action (defensive) still renders the abstain block, never an empty screen.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024 (reply kinds) and ADR-019
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (abstain-recovery-backend) | the action the web renders is the server-selected one |

## Architecture decisions in force
- ADR-024 — abstain carries a recovery action; ADR-019 — native link.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | abstain-recovery-web | S | phase-4 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Recovery actions are secondary, never primary (product-owner/ux).

## Open questions
- none
