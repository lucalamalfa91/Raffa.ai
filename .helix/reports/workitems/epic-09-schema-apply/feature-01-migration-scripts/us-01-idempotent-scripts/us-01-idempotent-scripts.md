---
id: us-01
type: user-story
parent: feature-01
wave: 9
status: active
---

# us-01-idempotent-scripts — Scripts + stale check

## Acceptance criteria

- [ ] AC-1 Identity, Audit, Renewals, Savings, Quotes each have
      `Migrations/Scripts/*.sql`.
- [ ] AC-2 A test fails if a script is missing or stale vs `Migrations/`.
- [ ] AC-3 Scripts apply to a bare Postgres+pgvector (Testcontainers), same
      shape as `DocumentsContractsMigrationScriptTests`.
