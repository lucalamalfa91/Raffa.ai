---
id: feature-02
type: feature
parent: epic-02
wave: R1
status: active
---

# feature-02-contract-schema — Documents/Contracts normalized schema

## Slice

Materialize the normalized procurement data model for contracts: Supplier, Product,
Contract, ContractLineItem, ContractClause, Obligation, Document, Embedding, with
`tenant_id`, evidence, confidence, and version history columns carried by EF Core
migrations over PostgreSQL + pgvector.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Contract + clause + obligation entities (ADR-003/009) | R1 |
| us-02 | Embedding + search index (pgvector) | R1 |

## Architecture decisions in force

- ADR-003 (PostgreSQL + pgvector)
- ADR-009 (RLS tenant_id)

## Target repo

`raffa-backend`
