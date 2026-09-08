---
id: E13/F02/US01/T01
type: task
story: us-01-market-intelligence
wave: 13
status: live
target_repo: contigo-backend
---

# task-01-market-feed-mock — Feed seam, mock dataset, benchmark projection, in-memory notes retrieval

## Coding objective

Fill `backend/src/Contigo.Market` (scaffolded by F01/T01) with the
market-intelligence seam of HITL decision D2 (`inputs/requirements.md`
R-MKT-01, R-MKT-02, R-MKT-04): `Contracts/MarketDeal` record (provider,
recordId, supplier, category, product, sku?, geography, currency,
companySizeBand, termMonths, annualValueBand, unitPriceP25/P50/P75,
discountAchievedPct?, upliftCapPct?, noticeDays?, paymentTerms?,
negotiatedClauses[] {type, value}, closingPeriod, sampleSize, source,
representative, updatedAt, licenseRestrictions?), `IMarketIntelligenceProvider`
(`GetDealsAsync(feedVersion?)` → records + feed version),
`Mock/MockMarketIntelligenceProvider` reading the checked-in
`backend/fixtures/market-intelligence.mock.json` (**≥ 60 records**:
enterprise software — Salesforce, Microsoft, AWS, Snowflake, ServiceNow,
Slack, Zoom, Notion…; insurance — Allianz, AXA, Zurich, Swiss Re;
facilities, telco, logistics, professional services; EU / CH / US; CHF /
EUR / USD; several deliberately thin rows with `sampleSize` < 5 so abstain
paths are exercised; every record `source = "mock"`, `representative =
true`). Projection 1: `Benchmark/MarketFeedBenchmarkAdapter :
IBenchmarkProviderAdapter` (`Name = "market-feed"`) that answers
`BenchmarkQuery` with P25–P75, sample size, confidence, provenance
`"market-feed (representative, mock)"`, `UpdatedAt`, matching on supplier +
product (+ sku / geography / currency / term when present, spec §10.4) and
returning insufficient data (no distribution) for thin or unmatched
queries — registered with `TryAddEnumerable` in `AddMarketModule()` and
made the active adapter by default (`BenchmarkAdapterOptions.ActiveAdapter`
default → "market-feed" **inside** `AddMarketModule` via a post-configure,
without editing `Contigo.Benchmark`). Projection 2 (interface only, in
this task): `Retrieval/IMarketKnowledgeRetrieval`
(`SearchAsync(query, topK, filters?)` → `MarketNote` hits: recordId,
title, snippet, category, geography, updatedAt, provenance label, score)
with `MarketNoteComposer` (one narrative per record, e.g. "Companies of
500–2 000 employees closing Salesforce Sales Cloud in CH in 2026 paid P50
CHF 132 per user/month, obtained 3–5 % uplift caps and 90-day notice…")
and an **in-memory** implementation (token overlap over the composed
notes) registered by default; T02 swaps in the pgvector index. Provenance
label helper: `MarketProvenance.Label(deal)` → *representative market data ·
mock feed · updated <yyyy-MM-dd>*.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/fixtures/market-intelligence.mock.json` | new dataset (≥ 60 records) |
| `backend/src/Contigo.Market/Contracts/MarketDeal.cs`, `MarketNote.cs`, `MarketProvenance.cs` | new |
| `backend/src/Contigo.Market/IMarketIntelligenceProvider.cs`, `Mock/MockMarketIntelligenceProvider.cs` | new |
| `backend/src/Contigo.Market/Benchmark/MarketFeedBenchmarkAdapter.cs` | new adapter |
| `backend/src/Contigo.Market/Retrieval/IMarketKnowledgeRetrieval.cs`, `MarketNoteComposer.cs`, `InMemoryMarketKnowledgeRetrieval.cs` | new |
| `backend/src/Contigo.Market/ServiceCollectionExtensions.cs` | registrations, active-adapter default |
| `backend/tests/Contigo.Market.Tests/*` | dataset shape, adapter bands / abstain, composer, in-memory retrieval, provenance |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (three sources; market never tenant), ADR-001 (amended: Internal Dataset = mock feed, labelled representative), ADR-002 (Market → `[SharedKernel, AiGateway, Benchmark]`), spec §10.2–§10.4 (adapter boundary; benchmark trust).
- Gap G-MARKET-FEED. Existing shapes to reuse: `Contigo.Benchmark/Contracts/*`, `BenchmarkAdapterRegistry` (adapters enumerated from DI, unique `Name`), `FixtureBenchmarkAdapter` (keep it registered for tests; it is no longer the active default).
- **Do not touch**: `Contigo.Benchmark` sources, `Program.cs` (F05/T02 owns it this phase; `AddMarketModule` is wired by F06/T01), `Contigo.Chat`, `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.Market.Tests` exit 0 — ≥ 60 records load; an Allianz-class query returns P25–P75 with provenance containing "mock"; a thin row and an unknown SKU return `HasSufficientData == false`; composer output contains the band and the uplift cap; in-memory search returns the Salesforce note for "uplift cap Salesforce"
- [ ] `dotnet test backend/tests/Contigo.Benchmark.Tests` exit 0 (fixture adapter untouched)
- [ ] `dotnet build backend/Contigo.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | dataset, adapter, composer, retrieval, provenance | `Contigo.Market.Tests/*` |

## Open questions blocking this task
- OQ-askv2-001 — record shape is Contigo's own (assumed)

## Wave-spec entry
```yaml
- id: E13/F02/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-02-market-intelligence/us-01-market-intelligence/tasks/task-01-market-feed-mock.md
  produces: [market-feed-mock]
  depends_on: [v2-scaffold]
  effort: L
  layer: backend
  status: live
```
