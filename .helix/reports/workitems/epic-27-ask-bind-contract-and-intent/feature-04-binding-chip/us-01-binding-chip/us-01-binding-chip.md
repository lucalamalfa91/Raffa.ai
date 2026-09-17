---
id: us-01
type: user-story
parent: feature-04
wave: w19
status: active
---

# us-01-binding-chip — The thread names the bound contract, clickable

## Story
As a **user** in a scoped Ask thread, I want to see which contract is bound
(supplier · type, clickable to 360), so the thread does not look unscoped after
the first turn.

## Acceptance criteria
- [ ] AC-1 while `scopeContractId` is set, a chip renders `{supplierName} · {type}` linking `/contracts/{id}`.
- [ ] AC-2 it survives resume (rebuilt from conversation detail + 360 header), never the transient `?scope=`.
- [ ] AC-3 the chip is unrendered while the contract is not yet resolvable.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-012 (cl. 49) / ADR-020 (37.2)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-03 (bar-scope) | the scope id now reaches the conversation |

## Architecture decisions in force
- ADR-012 (cl. 49) / ADR-020 (37.2) — chip from persisted `scopeContractId`.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | binding-chip | S | phase-3 |

## Council decisions carried into this story
- chip reads the conversation's persisted `scopeContractId`, never the transient query.

## Open questions
- none
