---
id: feature-06
type: feature
parent: epic-01
wave: R0
status: active
---

# feature-06-document-ingestion — Document upload + audit baseline

## Slice

Implement document upload into tenant-scoped object storage with metadata/status,
plus the append-only audit baseline capturing access and corrections from day one.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Document upload + metadata | R0 |
| us-02 | Audit baseline | R0 |

## Architecture decisions in force

- ADR-009 (RLS), ADR-011 (Key Vault + isolation), ADR-003 (PostgreSQL).

## Target repo

`raffa-backend`
