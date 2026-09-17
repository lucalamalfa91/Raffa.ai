---
id: feature-02
type: feature
parent: epic-28
wave: w19
status: active
---

# feature-02-rag-contract-filter — Tenant RAG filters to a contract (NW-81)

## Slice
`EmbeddingSearchQuery` carries `contractId` (or source ids) so the clause pack
is "this contract" + a labelled "similar types" peer slice (same
`ContractDocumentType`/category, lower K), never cosine over all tenant
embeddings; market stays on `IMarketKnowledgeRetrieval`.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | rag-contract-filter | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 15) — scoped retrieval; market not mixed into tenant pgvector.

## Target repo
`raffa-backend`
