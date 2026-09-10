---
id: E01/F01/US01/T02
type: task
story: us-01-github-org-repo-protection
wave: R0
status: live
target_repo: raffa-infra
---

# task-02-monorepo-secret-scan — 02 Monorepo Secret Scan

## Coding objective
Scan for committed secrets and verify five-folder layout + no-secret state.

## Parent story AC covered
- See parent story `us-01-github-org-repo-protection` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-infra/src/ | implementation for `repo-secret-scan` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-014.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `repo-secret-scan`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | repo-secret-scan behaviour | workspace/raffa-infra/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F01/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-01-platform-bootstrap/us-01-github-org-repo-protection/tasks/task-02-monorepo-secret-scan.md
  produces: [repo-secret-scan]
  depends_on: [github-org-repos]
  effort: S
  layer: backend
  status: live
```
