---
id: us-02
type: user-story
parent: feature-02
wave: R1
status: active
---

# us-02-embedding-search-index — Embedding + search index (pgvector)

## Story

As a **backend engineer**, I want embeddings stored in pgvector and a tenant-scoped
similarity search, so that semantic retrieval for Ask Raffa works without leaking
data across tenants.

## Acceptance criteria

- [ ] AC-1 Embedding table with `vector` column and fixed dimension (ADR-003, ADR-004).
- [ ] AC-2 Similarity search is tenant-filtered (return only authorized tenant rows).
- [ ] AC-3 Embedding generation goes through `IAiGateway` (never a provider SDK).

## Definition of done

- [ ] `dotnet test` proves same-tenant vectors retrieved, cross-tenant excluded.
- [ ] honours ADR-003, ADR-004, ADR-009.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 (this feature) | entities exist before embedding FK |

## Architecture decisions in force

- ADR-003 (pgvector), ADR-004 (embed role), ADR-009 (RLS/tenant filter).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Embedding entity + pgvector similarity | M | phase-13 |
| task-02 | Tenant-scoped retrieval + embed via gateway | M | phase-14 |

## Council decisions carried into this story

Embedding dimension fixed at schema time; vector column consistent with pgvector.

## Open questions

- none.
