---
id: epic-21
type: epic
wave: w17
status: active
extends: [epic-02, epic-03, epic-04, epic-07]
---

# epic-21-contract-360-answers — Contract 360 answers "where you can save" and "when you must move"

## Business capability

Contract 360 stops answering its two headline questions with the same two
constants every time. Today
`web/src/routes/contracts/contract360/contract360ViewModel.ts:174-175` reads
`recommendations?.potentialSavingsRange ?? SAVINGS_NOT_YET_AVAILABLE` and
`recommendations?.marketPosition ?? upliftLever ?? LEVER_NOT_YET_AVAILABLE` —
and because the builder that feeds them hardcodes all three fields to `null`
(`backend/src/Raffa.Renewals/Application/RenewalPipelineBuilder.cs:91-92`),
both branches are **unreachable** and the screen prints a placeholder as its
steady state.

Three items close it together: the two memberless placeholder arrays on the 360
payload gain real shapes and real data (NW-20), the renewal insight gains a
**representative** market band from the benchmark service that already exists
and is already registered (NW-22), and the answers band **consumes the server's
own `/api/contracts/{id}/strategy`** — an endpoint that has returned
`WhenYouMustMove` and `WhereYouCanPush` since epic-13 and has never had a single
consumer (NW-62).

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w17-todo.md` §2 | NW-20 — 360 `benchmark` / `activity` always `[]` |
| `inputs/next/w17-todo.md` §2 | NW-22 — renewal insight `MarketPosition` null |
| `inputs/next/w17-todo.md` §2 | NW-62 — 360 must answer "where you can save" and "when you must move" (**`must`**) |
| `inputs/product-spec.md` §8.2 | Contract 360 tabs, including Activity |
| `inputs/requirements.md` R-WEB-05 | the 360 answers the procurement questions |
| `inputs/design/prototypes/raffa-v2/screens-v2.md:95-112` | screen 5 — the Answers band |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | benchmark-and-activity-payload | w17 |
| feature-02 | renewal-market-position | w17 |
| feature-03 | answers-band | w17 |

## Extends

- **epic-02 F03** (`feature-03-portfolio-contract-360`) — the 360 payload and
  its query service.
- **epic-03 F01/F03** (`feature-01-renewal-engine`,
  `feature-03-renewal-dashboard`) — the renewal insight card whose
  recommendations carry the market position.
- **epic-04 F01** (`feature-01-benchmark-service`) — `IBenchmarkService` and the
  market-feed adapter that this epic finally calls with a resolved key.
- **epic-07 F02** (`feature-02-contract-360-ui`) — the answers band, the view
  model and the screen.
- **epic-13 F07** (`feature-07-insights`) — the `/strategy` endpoint and
  `StrategyPack`, built there and unwired until now.

## Success looks like

- On a validated Northwind contract, **Where you can save** shows either a
  figure with its lever or a **representative** band carrying adapter, sample
  size and as-of date — never "Not yet available" while the facts are in the
  file, and never a bare percentile.
- **When you must move** shows the notice deadline, days left and whether it
  auto-renews, from the persisted `EndDate` / `CancellationDeadline` columns; if
  extraction missed the date it says **"Add the end date"** and links to Review,
  never "Not determined".
- The 360's `benchmark` array carries a real entry — including an explicit
  `insufficient_data` **entry** when the adapter abstains, never `[]`.
- The 360's `activity` array carries a contract-scoped provenance timeline:
  when · actor · what changed, from a **closed allow-list** of audit actions,
  with **no extracted value and no `Detail` string** ever projected.
- A non-Admin member sees the contract's activity and still receives **403**
  from `GET /api/audit`.

## Architecture decisions in force

- **ADR-001 w17 clauses 3, 4** — "Activity" for V1 is the **provenance timeline
  of already-persisted events**, never the value of an extracted fact and never
  a new table; if the projection does not fit, **remove the member and delete
  the record type** — an unconditional `[]` with no decision is not acceptable.
  A market claim has exactly **two** honest shapes: a **representative**
  position carrying adapter, sample size and as-of date, or an explicit
  "insufficient market data". Never a percentile alone, never "market"
  unqualified, never "Not determined". The band comes from the existing
  fixture / market-feed adapter at the **R3/R4 gate** — a paid market API stays
  a §1.2 non-goal.
- **ADR-024 w17 clauses 6–11 and A4** — the wire shapes:
  `Contract360ActivityEntry(OccurredAt, Action, ActorLabel)` and
  `Contract360BenchmarkEntry(Metric, Status, Position?, AdapterName?,
  SampleSize?, AsOf?)`, the abstain carried **as an entry**. The 360
  **consumes** `/strategy`; it does not compute a second answer.
  `StrategyPack.OpenWeakFacts` is a **criticality input, not a review
  decision**, and is **not rendered as a review statement** this wave.
- **ADR-002 w17 clauses 2, 3** — both members are filled by **host composition
  in `Raffa.Api`**, never by `Contract360QueryService`:
  `DependencyDirectionTests.cs` fences `Raffa.Documents.Contracts` to
  `[SharedKernel, AiGateway]`. `RenewalPipelineBuilder` **stays pure** — the
  band is resolved in the host and passed **into** the builder on
  `RenewalDashboardCandidate`; the purity rule is written down so a later task
  does not "fix" this by injection.
- **ADR-011 w17 clauses 22, 25** — `activity` is permitted as a
  **contract-scoped provenance projection** and refused as an **audit reader**,
  under five conditions; the existing `GetEventsAsync` **cannot be narrowed into
  it** (tenant-wide, 200 rows, no filters), so a **new** method with **query
  predicates** is required.
- **ADR-009 w17 clause 5** — the audit projection lands on the **second**
  DbContext, whose `Configure` third argument is also optional; it fails
  **closed**, and a closed failure **is** the unconditional `[]` three seats
  refused. It is ruled onto `AuditQueryService`, which opens its own scope.
- **ADR-012 w17 clauses 35, §45, §48** — **one answer source per screen**; the
  "not yet available" constants survive **only** as the absent-or-failed state
  of a source that was genuinely called; NW-20's two members get **no web
  consumer and no client task** this wave, with *absent ≠ empty* travelling to
  W18.
- **ADR-020 w17 §14(f)-(g), §22** — three real states per answer cell, the
  representative band's provenance never bare, and the missing-fact state naming
  **the way to get the fact**; no copy on Contract 360 uses the word "weak".

## Out of scope

- **A paid market API** — NW-52 stays DEFERRED (ADR-001 §1.2).
- **A per-contract geography column.** Geography is the **workspace country**
  (`WorkspaceTenant.Country`, the w14 profile field), labelled representative.
  A per-contract field is a new capability plus a migration the cap cannot
  afford (OQ-w17-004).
- **A web consumer for `benchmark` / `activity`.** The members land on the wire
  and no screen reads them this wave (ADR-012 w17 §45). ⚠ Four oracles still
  promise the Activity **tab**, including `Contract360Result.cs:9-12` — the
  export request is owed to W18 and blocks nothing.
- **An audit screen.** It stays Admin-only and owes its own ADR-020 row and a
  fresh security re-review; `/api/audit` keeps its ladder unchanged and this
  projection does not become a second route into it.
- **Reconciling `OpenWeakFacts`' 0.8 bar with Review's 0.90.** That is W18's
  (OQ-w17-sa-01); this wave renders the strategy sections but not
  `OpenWeakFacts` as a review statement.
- **NW-57** (quote market compare) — stays W18; not pulled in.
- **A structured "what changed" per activity row.** `AuditEvent.Detail` is
  never projected; a typed column or contract is W18's.
