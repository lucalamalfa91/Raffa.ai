---
id: us-01
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-01-citation-ids-backend — Citations carry real contract/document/page/span ids

## Story
As the **Ask engine**, I want every notice/clause/evidence citation to carry
real `contractId`/`documentId`/`page`/span, so the viewer opens at the span and
no citation lacks a pack source.

## Acceptance criteria
- [ ] AC-1 `CopilotReplyBuilder`/`PackItem` stamp real `contractId` + `documentId` (evidence `SourceDocumentId`/clause) + `page` + span/clause.
- [ ] AC-2 notice/clause citations' `href` is the W18 viewer `?page` + clause when a clause id exists; else viewer `?page=` + highlight; else 360 `?clause=`/`?page=`.
- [ ] AC-3 calc Q1 citations set `href` = `/contracts/{id}` + `contractId`.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 17); lock 5
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (rag-contract-filter) | clause pack is contract-scoped now |

## Architecture decisions in force
- ADR-024 w19 (cl. 17) — no citation without a pack source.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | citation-ids-backend | M | phase-2 |

## Council decisions carried into this story
- `href` = viewer with page+clause when clause id exists; calc items `href` = `/contracts/{id}`.

## Open questions
- none
