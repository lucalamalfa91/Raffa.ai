---
id: us-01
type: user-story
parent: feature-02
wave: 13
status: active
---

# us-01-market-intelligence — Market intelligence Raffa can cite

## Story

As **procurement**, I want Raffa to compare my contract with how
companies actually close contracts (bands, discounts, uplift caps, notice
periods, clauses) and to quote that market knowledge with its provenance,
so that I know where I stand without a paid feed on the first `demo` and
without ever seeing another customer's contract.

## Acceptance criteria

- [ ] AC-1 `IMarketIntelligenceProvider` returns `MarketDeal` records with
      the fields of `inputs/requirements.md` R-MKT-01; the mock feed has
      ≥ 60 records including an insurance row usable as "Allianz", and
      every record is `source = "mock"`, `representative = true`.
- [ ] AC-2 `IBenchmarkService.GetBenchmarkAsync` for a mock supplier /
      product returns P25–P75 with sample size, confidence and provenance;
      an unmatched or thin query returns insufficient data (never a bare
      number); the fixture adapter's eight rows are no longer the default;
      after ingestion the numbers are served from `market_record`, and a
      provider that throws does not affect answers.
- [ ] AC-3 Market notes (one narrative per record) are embedded into
      `market_embedding` (no `tenant_id`, shared, read-only); a tenant
      search never returns market notes and a market search never returns
      tenant chunks.
- [ ] AC-4 Re-running the ingestion with the same feed changes nothing
      (idempotent, versioned by feed version).
- [ ] AC-5 `GET /api/market/records/{id}` returns one record with its
      provenance label and `updatedAt` for the citation panel.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-v2-foundation | `Raffa.Market` project exists (T01); `embed` role (T02) |

## Architecture decisions in force

- ADR-024 — three sources; market index separate from tenant RAG
- ADR-001 (amended) — Internal Dataset = mock feed; provenance label
- ADR-011 (amended) — shared index, never tenant rows
- ADR-021 — `market.sql` applied by CI
- spec §10.2–§10.4

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Feed seam, mock dataset, benchmark projection, in-memory notes retrieval | L | phase-2 |
| T02 | Market index (`market_embedding`), ingestion, DB-backed retrieval, record endpoint | L | phase-3 |

## Council decisions carried into this story

`MarketDeal` shape (R-MKT-01): provider, recordId, supplier, category,
product, sku, geography, currency, companySizeBand, termMonths,
annualValueBand, unitPriceP25/P50/P75, discountAchievedPct, upliftCapPct,
noticeDays, paymentTerms, negotiatedClauses[], closingPeriod, sampleSize,
source, updatedAt, licenseRestrictions. Mock file:
`backend/fixtures/market-intelligence.mock.json`. Provenance label text:
*representative market data · mock feed · updated <date>*. Index table
`market_embedding` (vector 1536, same model as tenant embeddings), record
table `market_record`.

## Open questions

- OQ-askv2-001 — the mock record shape is Raffa's own; the live API maps onto it (assumed)
