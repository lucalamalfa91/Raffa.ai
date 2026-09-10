---
id: E01/F06/US01/T01
type: task
story: us-01-document-upload
wave: R0
status: live
target_repo: raffa-backend
---

# task-01-document-upload — 01 Document Upload

## Coding objective
Implement POST /api/documents to tenant-scoped blob + processing job.

## Parent story AC covered
- See parent story `us-01-document-upload` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `document-upload` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-009, ADR-011.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `document-upload`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | document-upload behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F06/US01/T01
  prompt: reports/workitems/epic-01-platform/feature-06-document-ingestion/us-01-document-upload/tasks/task-01-document-upload.md
  produces: [document-upload]
  depends_on: [workspace-roles, deployable-api]
  effort: M
  layer: backend
  status: live
```
