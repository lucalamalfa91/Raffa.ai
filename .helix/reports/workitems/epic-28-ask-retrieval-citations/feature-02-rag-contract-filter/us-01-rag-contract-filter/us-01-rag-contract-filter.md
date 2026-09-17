---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-rag-contract-filter — Retrieval slices this contract, not the whole tenant

## Story
As the **Ask engine**, I want the tenant RAG to filter to a contract (plus a
labelled "similar types" peer slice), so Q2/Q3 do not mix 37 contracts.

## Acceptance criteria
- [ ] AC-1 `EmbeddingSearchQuery` accepts `contractId`/source ids; "this contract" slice is only that contract's chunks.
- [ ] AC-2 a labelled "similar types" peer slice (same `ContractDocumentType`/category, lower top-K) is returned separately.
- [ ] AC-3 market notes stay on `IMarketKnowledgeRetrieval` — never mixed into tenant pgvector.

## Definition of done
- [ ] every AC verified by a named unit test
- [ ] honours ADR-024 w19 (cl. 15); ADR-011
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `Embedding` already carries `SourceType`/`SourceId` (unused at query time) |

## Architecture decisions in force
- ADR-024 w19 (cl. 15) — scoped retrieval; ADR-011 two corpora isolated.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | rag-contract-filter | M | phase-1 |

## Council decisions carried into this story
- this-contract slice + labelled peer slice; market on `IMarketKnowledgeRetrieval`; RLS is the non-bypassable backstop.

## Open questions
- none
