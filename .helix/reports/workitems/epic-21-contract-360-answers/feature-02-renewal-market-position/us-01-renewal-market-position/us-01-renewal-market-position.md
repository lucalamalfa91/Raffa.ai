---
id: us-01
type: user-story
parent: feature-02
wave: w17
status: active
---

# us-01-renewal-market-position — the renewal card says where this price sits

## Story

As a **procurement lead**, I want the renewal insight to tell me whether this
contract sits above, in line with or below the market — with the adapter, the
sample size and the as-of date behind that claim — so that the recommendation
rests on something I can question rather than on a permanent `null`.

## Acceptance criteria

- [ ] AC-1 For a renewal candidate whose supplier name and workspace country
  both resolve and whose adapter answers, `marketPosition` on the renewal
  pipeline item is **non-null** and reads as a **representative** position, not
  a bare percentile and not the word "market" unqualified.
- [ ] AC-2 The claim carries its **adapter name, sample size and as-of date**.
- [ ] AC-3 When the adapter abstains (below its minimum viable sample) or the
  key is incomplete, `marketPosition` says **"insufficient market data"** —
  never "Not determined" and never a fabricated number.
- [ ] AC-4 `RenewalPipelineBuilder` remains **pure and synchronous**: its
  constructor takes no benchmark dependency, no database call, no HTTP call and
  no LLM call, and its unit tests need **no new fake service**.
- [ ] AC-5 `Raffa.Renewals` references **no** type from `Raffa.Market`;
  `DependencyDirectionTests` stays green with its allow-list unchanged.
- [ ] AC-6 `PotentialSavingsRange` stays `null` unless the band yields one, and
  the comment claiming all three fields are "always null this wave" is gone.
- [ ] AC-7 `git diff --stat origin/main -- web/` is **empty** — no contract
  change and no web change.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E21/F01/US01` (phase 1) | it publishes the shared **(supplier name, geography)** resolver. Resolving the key a second time here would give two answers for one contract — the defect ADR-002 w17 clause 3's "one resolution, two consumers" rule exists to prevent |

## Architecture decisions in force

- **ADR-001** w17 clause 4 — the two honest shapes of a market claim.
- **ADR-002** w17 clause 2 — the builder stays **pure**; the host composes the
  adapter and passes the band **in**. The allow-list already permits the port;
  the adapter's module does not.
- **ADR-024** w17 clause 6 — the wire shape is unchanged.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | renewal-market-position | M | phase-2 |

## Council decisions carried into this story

- **The ruling, in software-architect's words**: `RenewalsEndpointExtensions`
  resolves the band per candidate and passes it **into** the builder on
  `RenewalDashboardCandidate` — the DTO that exists **precisely so the builder
  never sees a real `Contract`**
  (`backend/src/Raffa.Renewals/Application/RenewalDashboardCandidate.cs:5-17`) —
  the same pattern `InsightsEndpointExtensions.cs` already uses. The builder then
  fills `MarketPosition` instead of the hardcoded nulls at `:91-92` and sweeps
  the stale comment at `:89-90`.
- **Tests stay unit tests with no new fake service**, which is the point of
  keeping the builder pure.
- **`AnnualUpliftPercent`** is filled only **where the band supports it**;
  otherwise it stays null. `PotentialSavingsRange` stays null unless the band
  yields one — **an honest null is not a defect**.

## Open questions

- **OQ-w17-004** (where geography comes from) — **ruled**: the workspace
  country, resolved in the host, labelled representative. A per-contract
  geography field is a new capability plus a migration and is **not this wave**.
- **none blocking.**
