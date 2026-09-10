---
id: feature-04
type: feature
parent: epic-02
wave: R1
status: active
---

# feature-04-ask-raffa-citations — Ask Raffa query router + citations

## Slice

Implement the Ask Raffa query engine: route structured queries to deterministic
SQL, semantic/legal queries to RAG, enforce auth-before-retrieval, and return only
answers with source citations (or an explicit "cannot determine").

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Structured vs semantic query router | R1 |
| us-02 | RAG retrieval + grounded answer with citations | R1 |

## Architecture decisions in force

- ADR-004 (answer role)
- ADR-011 (auth-before-retrieval, no unauthorized data in context)
- ADR-003 (pgvector retrieval)

## Target repo

`raffa-backend`
