---
id: us-02
type: user-story
parent: feature-03
wave: w18
status: active
---

# us-02-scoped-ask-web — The Ask screen briefs the scoped contract

## Story
As a **user** who clicked "Ask about it", I want the Ask screen to show which
contract is in scope (supplier kicker + scope line) and scoped suggestion
chips, so I know my question is about that contract.

## Acceptance criteria
- [ ] AC-1 on `/ask?scope=<contractId>` the new-chat heading shows the supplier kicker + scope line for that contract.
- [ ] AC-2 the scoped entry shows supplier-templated suggestion chips (c360Chips), not the generic Ask chips.
- [ ] AC-3 the `?scope=` greeting does not land on the generic hello; it briefs the contract.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024/ADR-020 (heading copy)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (scoped-ask-backend) | the scope id is already consumed; the web renders the brief |

## Architecture decisions in force
- ADR-020 — heading copy is supplier kicker + scope line; ADR-024 — scoped entry.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | scoped-ask-web | S | phase-3 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Heading copy: supplier kicker + scope line; scoped chips = `buildScopedSuggestions`; never the generic gate.

## Open questions
- none
