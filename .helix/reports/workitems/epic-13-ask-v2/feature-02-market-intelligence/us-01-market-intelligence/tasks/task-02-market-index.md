---
id: E13/F02/US01/T02
type: task
story: us-01-market-intelligence
wave: 13
status: live
target_repo: raffa-backend
---

# task-02-market-index — `market_embedding` index, ingestion, DB-backed retrieval, record endpoint

## Coding objective

Give the market notes a real vector index, separate from tenant data
(`inputs/requirements.md` R-MKT-03, R-MKT-05, §7). In `Raffa.Market`:
`Infrastructure/MarketDbContext` (pgvector, **no** tenant interceptor;
tables `market_record` (recordId PK, feedVersion, provider, json payload,
provenance label, updatedAt) and `market_embedding` (id, recordId FK,
chunkIndex, chunkText, vector(1536), model, createdAt)); an EF migration
+ idempotent `Migrations/Scripts/market.sql` (ADR-021) that also grants
read to the application role and restricts writes to the ingestion role
when the CI role model allows it (document the grant in the script
header), plus `MarketMigrationScriptTests`; add `market.sql` to both
`SCRIPTS` arrays in `.github/workflows/backend.yml` (this task is the
phase-3 writer of that file). `Ingestion/MarketIngestionService.IngestAsync(feedVersion?)`:
read the provider, upsert `market_record` by recordId, compose notes
(`MarketNoteComposer`), embed through `IAiGateway.EmbedAsync`
(ADR-004 `embed` role, hash-logged), replace the record's embeddings —
idempotent: a second run with the same feed version and payload hash
changes zero rows (return a summary with inserted / updated / unchanged
counts). `Retrieval/PgVectorMarketKnowledgeRetrieval : IMarketKnowledgeRetrieval`
(cosine distance, top-k, optional category / geography filters) replacing
the in-memory implementation when `ConnectionStrings:Market` is present
(DI swap inside `AddMarketModule(string? marketConnectionString)`); `MarketFeedBenchmarkAdapter` likewise switches to read `market_record` when the connection string is present, so after ingestion **both** projections are served from Raffa's database and the provider is called only by the ingestion job — never at question time (prove it with a test whose provider throws: benchmark and retrieval still answer from the store);
`GET /api/market/records/{id}` in a new
`Raffa.Api/MarketEndpointExtensions.cs` (mapped by F06/T01 in this same
phase — if `Program.cs` already maps `MapMarketEndpoints()`, the method
must exist by the end of phase 3; coordinate by shipping the file first).
An ingestion entry point for operators: `dotnet run --project
backend/src/Raffa.Worker -- ingest-market --feed backend/fixtures/market-intelligence.mock.json`
(a Worker command handler; the CI workflow lands in F11). Isolation test
in `Raffa.IntegrationTests`: a tenant embedding search never returns a
market note; a market search never returns tenant chunks (separate
tables, separate services).

## Parent story AC covered
- AC-3, AC-4, AC-5

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.Market/Infrastructure/*` (DbContext, factory, options, configurations) | new |
| `backend/src/Raffa.Market/Migrations/*` + `Migrations/Scripts/market.sql` | new |
| `backend/src/Raffa.Market/Ingestion/MarketIngestionService.cs`, `IngestionSummary.cs` | new |
| `backend/src/Raffa.Market/Retrieval/PgVectorMarketKnowledgeRetrieval.cs` | new |
| `backend/src/Raffa.Market/ServiceCollectionExtensions.cs` | overload with connection string, DI swap |
| `backend/src/Raffa.Market/Raffa.Market.csproj` | EF / Npgsql / pgvector packages |
| `backend/src/Raffa.Api/MarketEndpointExtensions.cs` | new (record endpoint) |
| `backend/src/Raffa.Worker/Commands/IngestMarketCommand.cs`, `Program.cs` | worker command |
| `backend/tests/Raffa.Market.Tests/*` | ingestion idempotency, retrieval, migration script |
| `backend/tests/Raffa.IntegrationTests/MarketIndexIsolationTests.cs` | separation from tenant RAG |
| `.github/workflows/backend.yml` | add `market.sql` to both SCRIPTS arrays |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (market index shared, read-only, never tenant rows), ADR-011 (amended), ADR-003 (pgvector), ADR-004 (`embed` role; hash-only logs), ADR-021.
- Gap G-MARKET-INDEX. Reuse `Raffa.Documents.Contracts.Domain.Embedding` conventions (vector 1536, `Model` column) without referencing that module.
- **Do not touch**: `Raffa.Chat`, `ChatEndpointExtensions.cs`, `Program.cs` (F06/T01 owns it this phase; it maps `MapMarketEndpoints()`), `Raffa.Documents.Contracts`, `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Market.Tests` exit 0 — first ingest inserts N records + embeddings; second ingest reports 0 inserted / 0 updated; retrieval returns the Salesforce note for "uplift cap Salesforce" (Postgres fixture); with a throwing `IMarketIntelligenceProvider`, `IBenchmarkService` and `IMarketKnowledgeRetrieval` still answer from the store; migration script test green
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter MarketIndexIsolationTests` exit 0
- [ ] `grep -c "market.sql" .github/workflows/backend.yml` = 2; `dotnet build backend/Raffa.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit / DB | ingestion idempotency, retrieval, script | `Raffa.Market.Tests/*` |
| integration | tenant ↔ market separation | `Raffa.IntegrationTests/MarketIndexIsolationTests.cs` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F02/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-02-market-intelligence/us-01-market-intelligence/tasks/task-02-market-index.md
  produces: [market-index]
  depends_on: [market-feed-mock, foundry-gateway]
  effort: L
  layer: backend
  status: live
```
