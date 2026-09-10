---
id: us-01
type: user-story
parent: feature-04
wave: R1
status: active
---

# us-01-query-router — Structured vs semantic query router

## Story

As a **backend engineer**, I want a query router that detects whether an Ask Raffa
question is structured (SQL-answerable) or semantic/legal (needs RAG), so that the
right engine answers each question correctly.

## Acceptance criteria

- [ ] AC-1 Router classifies the spec §8.3 example questions into structured vs semantic.
- [ ] AC-2 Structured questions hit deterministic queries/filters (no LLM).
- [ ] AC-3 Semantic/legal questions route to RAG retrieval.

## Definition of done

- [ ] `dotnet test` proves routing for the four spec §8.3 examples.
- [ ] honours ADR-002, App C #6.

## Dependencies

| Depends on | Why |
|------------|-----|
| feature-02 (schema) | structured queries read extracted facts |

## Architecture decisions in force

- ADR-002 (Chat context), App C #6 (deterministic first).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Query intent router + structured query path | M | phase-15 |
| task-02 | Deterministic query handlers (dates/spend) | M | phase-16 |

## Council decisions carried into this story

Structured vs semantic routing per spec §8.3; deterministic queries for dates/aggregates.

## Open questions

- none.
