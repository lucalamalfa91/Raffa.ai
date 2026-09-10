---
id: E01/F09/US01/T01
type: task
story: us-01-final-integration
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-r0-integration — 01 R0 Integration

## Coding objective
Prove R0 end-to-end: workspace->upload->storage->audit on dev/demo.

## Parent story AC covered
- See parent story `us-01-final-integration` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `r0-integration` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-001, ADR-002, ADR-009, ADR-011, ADR-016.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `r0-integration`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | r0-integration behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F09/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-09-r0-integration/us-01-final-integration/tasks/task-01-r0-integration.md
  produces: [r0-integration]
  depends_on: [document-upload, document-metadata, audit-query, web-client, web-api-client, mobile-scaffold, mobile-oidc, deployable-worker]
  effort: L
  layer: backend
  status: live
```
