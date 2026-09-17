---
id: E28/F03/US01/T01
type: task
story: us-01-citation-ids-backend
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-citation-ids-backend — Stamp real contract/document/page/span on citations (NW-83)

## Coding objective
In `backend/src/Raffa.Api/AskCopilotService.cs` (`BuildContractFactItem`,
`BuildClausePackAsync`, and the notice pack once NW-91 lands) and
`backend/src/Raffa.Chat/Application/Reply/CopilotReplyBuilder.cs`
(`BuildCitations`), stamp real `contractId` (from the pack item's contract),
`documentId` (evidence `SourceDocumentId` / clause `SourceDocumentId`), `page`
(`SourcePage`) and span/clause on each citation — replacing the current
`DocumentId = citation key` / `ContractId = null` / `Page`/`PreviewUrl = null`
stubs. Set `href` to the W18 viewer deep-link `/documents/{documentId}/viewer?page=<n>&clause=<clauseId>`
when a clause id exists, else viewer `?page=` + evidence highlight, else 360
`?clause=`/`?page=`; calc citations (Q1/Q3 strategy points) set
`href = /contracts/{id}` + `contractId`. No new reply `kind`.

## Parent story AC covered
- AC-1 real contract/document/page/span ids.
- AC-2 viewer `href` when clause/page exists.
- AC-3 calc items `href` = `/contracts/{id}`.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Api/AskCopilotService.cs | stamp ids on `PackItem`/citation |
| backend/src/Raffa.Chat/Application/Reply/CopilotReplyBuilder.cs | map `contractId`/`documentId`/`page`/span into `ReplyCitation` |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 17); ADR-018 (viewer route).
- **Do not touch**: the viewer route (w18); `ReplyCitation` shape beyond filling real values.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exits 0, with a test that a notice/clause citation carries a non-null `contractId`+`documentId`+`page` and a viewer `href`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| integration | citation ids + viewer href populated | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E28/F03/US01/T01
  prompt: reports/workitems/epic-28-ask-retrieval-citations/feature-03-citation-ids/us-01-citation-ids-backend/tasks/task-01-citation-ids-backend.md
  produces: [citation-ids]
  depends_on: [rag-contract-filter]
  effort: M
  layer: backend
  status: live
```
