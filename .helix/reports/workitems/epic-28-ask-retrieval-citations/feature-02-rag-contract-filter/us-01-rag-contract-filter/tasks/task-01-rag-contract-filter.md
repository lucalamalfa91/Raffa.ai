---
id: E28/F02/US01/T01
type: task
story: us-01-rag-contract-filter
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-rag-contract-filter — Tenant RAG filters to a contract (NW-81)

## Coding objective
Add a contract-scoped search to `backend/src/Raffa.Documents.Contracts/Application/EmbeddingRetrievalService.cs`:
an `EmbeddingSearchQuery` (or overload) carrying `contractId`/source ids, so a
"this contract" slice returns only that contract's embedding rows (matched via
`SourceId`/`SourceType`), and a separate labelled "similar types" peer slice
returns other validated contracts of the same `ContractDocumentType`/category at
lower top-K. In `AskCopilotService.BuildClausePackAsync`, build the pack from the
scoped + peer slices rather than tenant-wide `SearchAsync`, and keep market notes
on `IMarketKnowledgeRetrieval` (never mixed into tenant pgvector).

## Parent story AC covered
- AC-1 `EmbeddingSearchQuery` + this-contract slice.
- AC-2 labelled peer slice.
- AC-3 market stays on `IMarketKnowledgeRetrieval`.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Documents.Contracts/Application/EmbeddingRetrievalService.cs | contract-scoped + peer-slice search |
| backend/src/Raffa.Api/AskCopilotService.cs | `BuildClausePackAsync` uses scoped + peer slices |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 15); ADR-011 (market isolation).
- **Do not touch**: the `embedding` entity/RLS; `IMarketKnowledgeRetrieval`.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests` exits 0, with a test that this-contract search does not return another supplier's MSA unless in the labelled peer slice

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | this-contract slice excludes other suppliers' chunks | `backend/tests/Raffa.Documents.Contracts.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E28/F02/US01/T01
  prompt: reports/workitems/epic-28-ask-retrieval-citations/feature-02-rag-contract-filter/us-01-rag-contract-filter/tasks/task-01-rag-contract-filter.md
  produces: [rag-contract-filter]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
