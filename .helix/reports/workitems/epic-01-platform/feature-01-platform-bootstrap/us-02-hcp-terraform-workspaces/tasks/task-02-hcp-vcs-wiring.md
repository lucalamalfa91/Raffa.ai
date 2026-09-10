---
id: E01/F01/US02/T02
type: task
story: us-02-hcp-terraform-workspaces
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-hcp-vcs-wiring — 02 Hcp Vcs Wiring

## Coding objective
Wire the two HCP workspaces to the raffa repo VCS and assert remote state only.

## Parent story AC covered
- See parent story `us-02-hcp-terraform-workspaces` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `hcp-vcs-wiring` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-007, ADR-014.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `hcp-vcs-wiring`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | hcp-vcs-wiring behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F01/US02/T02
  prompt: reports/workitems/epic-01-platform/feature-01-platform-bootstrap/us-02-hcp-terraform-workspaces/tasks/task-02-hcp-vcs-wiring.md
  produces: [hcp-vcs-wiring]
  depends_on: [hcp-terraform-workspaces]
  effort: S
  layer: backend
  status: live
```
