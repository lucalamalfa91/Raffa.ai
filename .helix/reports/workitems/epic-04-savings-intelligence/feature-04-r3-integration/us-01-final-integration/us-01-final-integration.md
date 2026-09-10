---
id: us-01
type: user-story
parent: feature-04
wave: R3
status: active
---

# us-01-final-integration — R3 Savings Intelligence integration

## Story

As a **product owner**, I want the R3 definition of success proven end-to-end, so
that "Raffa quantifies credible savings opportunities".

## Acceptance criteria

- [ ] AC-1 Matched contracts show current price + P25/P50/P75 + percentile/target/saving/confidence/provenance.
- [ ] AC-2 SavingsOpportunity lifecycle works end-to-end on `demo`.
- [ ] AC-3 No paid benchmark provider is called.

## Definition of done

- [ ] `dotnet test` (integration) runs the R3 path with fixture benchmark; `demo` smoke documented.

## Dependencies

| Depends on | Why |
|------------|-----|
| every R3 leaf artifact | proves the wave |

## Architecture decisions in force

- ADR-001, ADR-002, ADR-003, ADR-009, ADR-016.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | R3 end-to-end integration test | L | phase-29 |

## Council decisions carried into this story

R3 success = credit, provenance'd savings from fixture benchmark.

## Open questions

- none.
