---
id: E12/F02/US01/T01
type: task
story: us-01-fixture-catalog
wave: 12
status: live
target_repo: contigo-backend
---

# task-01-fixture-catalog — Expand FixtureBenchmarkAdapter

## Coding objective

Grow `FixtureBenchmarkAdapter` into a labelled **representative** worldwide
mock: dozens of rows across insurance, SaaS, facilities, and similar
categories procurement actually asks about. Include at least one row the
copilot can name as **Allianz** (or equivalent insurer commercial terms).

Keep `Source = fixture`. Thin / unmatched SKUs still return insufficient
data — do not invent P50. Do not add a paid HTTP client. Do not write
catalog rows into tenant pgvector.

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Contigo.Benchmark/Fixtures/FixtureBenchmarkAdapter.cs` | expanded catalog |
| `backend/tests/Contigo.Benchmark.Tests/FixtureBenchmarkAdapterTests.cs` | Allianz-class + abstain |

## Context the implementer needs

- **Architecture**: ADR-001 amendment, ADR-023. Gap G-FIXTURE-CATALOG.
- **Do not touch**: `Contigo.AiGateway`, Chat, `web/`.

## Definition of done

- [ ] `dotnet test` on `Contigo.Benchmark.Tests` — named insurer row has
      P25–P75; unknown SKU abstains; provenance is fixture.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | catalog + abstain | `FixtureBenchmarkAdapterTests.cs` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E12/F02/US01/T01
  produces: [fixture-catalog]
```
