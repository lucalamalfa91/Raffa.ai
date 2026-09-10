---
id: E01/F06/US01/T02
type: task
story: us-01-document-upload
wave: R0
status: live
target_repo: raffa-backend
---

# task-02-document-metadata — 02 Document Metadata

## Coding objective
Persist document metadata/status; GET /api/documents/{id}.

## Parent story AC covered
- See parent story `us-01-document-upload` acceptance criteria (traced by this task objective).

## Files to create or modify
| Path | Change |
|------|--------|
| workspace/raffa-backend/src/ | implementation for `document-metadata` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-003.
- **Do not touch**: unrelated wave artifacts and provider SDKs in domain code.

## Definition of done
- [ ] Applicable build (e.g. `dotnet build`) exits 0 and a named test proves the produced artifact `document-metadata`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | document-metadata behaviour | workspace/raffa-backend/tests |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E01/F06/US01/T02
  prompt: reports/workitems/epic-01-platform/feature-06-document-ingestion/us-01-document-upload/tasks/task-02-document-metadata.md
  produces: [document-metadata]
  depends_on: [document-upload]
  effort: S
  layer: backend
  status: live
```
