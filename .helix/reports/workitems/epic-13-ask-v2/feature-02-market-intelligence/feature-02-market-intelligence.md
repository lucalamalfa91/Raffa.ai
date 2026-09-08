---
id: F02
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-02-market-intelligence — Feed seam, mock dataset, benchmark projection, market index

## Slice

The "knowledge base" of HITL decision D2 as code: `IMarketIntelligenceProvider`
returning `MarketDeal` records (how companies close contracts), a checked-in
**mock feed** (≥ 60 records across enterprise software, insurance incl. an
Allianz-class row, facilities, telco, logistics, services; EU / CH / US;
CHF / EUR / USD; thin rows on purpose), projected two ways: benchmark rows
behind the existing `IBenchmarkService` (deterministic P25–P75 comparison)
and **market notes** embedded into a shared, read-only `market_embedding`
index (never the tenant `embedding` table) with an idempotent ingestion
job. Every number carries *representative · mock feed · updated* provenance
until the live provider lands behind the same seam
(`inputs/requirements.md` R-MKT-01…05). Absorbs and replaces e12 F02.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Market intelligence Contigo can cite | 13 |

## Architecture decisions in force

- ADR-024 — three sources, market index separate from tenant RAG
- ADR-001 (amended) — Internal Dataset = mock feed; no paid API on first `demo`
- ADR-011 (amended) — shared read-only index, never tenant rows
- ADR-021 — `market.sql` applied by CI
- spec §10.2–§10.4 — adapter boundary, normalized response, benchmark trust

## Target repo

`contigo-backend`
