---
id: us-01
type: user-story
parent: feature-03
wave: w17
status: active
---

# us-01-strategy-benchmark-wired — /strategy answers with a real band, and the client can call it

## Story

As a **procurement lead**, I want `GET /api/contracts/{id}/strategy` to answer
"where you can push" with a **real** market band instead of a hardcoded `null`,
and I want the web client to actually have a method that calls it, so that the
screen in `us-02` has something to consume.

## Acceptance criteria

- [ ] AC-1 `GET /api/contracts/{id}/strategy` returns `WhereYouCanPush` entries
  whose priced-line targets carry a **non-null** benchmark for a contract whose
  supplier name and workspace country both resolve.
- [ ] AC-2 The band is **labelled representative** and carries its adapter,
  sample size and as-of date; when the adapter abstains or the key is
  incomplete it says **"insufficient market data"** — never a fabricated number.
- [ ] AC-3 `WhenYouMustMove` continues to answer from the persisted
  `EndDate` / `CancellationDeadline` columns and is unchanged in shape.
- [ ] AC-4 Geography is resolved **in the host** from the workspace country and
  passed into `StrategyInputs`. `Raffa.Insights` references no workspace type
  and its allow-list is unchanged.
- [ ] AC-5 `web/src/api/client.ts` exposes a `getContractStrategy` method
  following the file's existing wrapper convention, typed from the generated
  `paths` the way `getContractEvidence` is (`client.ts:675-679`, `:1346`,
  `:2443`).
- [ ] AC-6 `cd web && npx tsc --noEmit` exits 0 and `npm test` exits 0 — the new
  wrapper compiles against the regenerated schema.
- [ ] AC-7 `StrategyPack.OpenWeakFacts` is **unchanged** and no new consumer of
  it is added.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E21/F01/US01` (phase 1) | the shared **(supplier name, geography)** resolver. ADR-024 w17 clause 7: **one resolution per screen** — the 360's `benchmark` member and this endpoint's band use the **same** key and the **same** call, or one screen shows two answers for one question |

## Architecture decisions in force

- **ADR-024** w17 clauses 7, 9 — consume the existing endpoint; resolve the key
  in the host; `ToPricedLines`' `Benchmark: null` becomes a real call.
- **ADR-024** w17 clause A4 — `OpenWeakFacts` is a criticality input and gains
  no consumer.
- **ADR-001** w17 clause 4 — the R3/R4 gate; representative, or explicit
  insufficiency.
- **ADR-002** w17 clause 3 — the host composes; the module stays fenced.
- **ADR-012** w16 clause 25 / w17 clause 39 — `web/src/api/client.ts` is
  **one-writer-per-phase**. This story is its **phase-2** writer;
  `E22/F03/US01/T01` is its phase-3 writer.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | strategy-benchmark-wired | M | phase-2 |

## Council decisions carried into this story

- **Consume, do not rebuild** (OQ-w17-005): the endpoint **already** returns
  both sections, is **already** in the generated client, and **has no
  consumer** — so this is a wiring decision, not a build. A second answer
  computed in the view model is exactly the client/server divergence ADR-012
  forbids.
- **The join is resolved in the host, not in the module**:
  `InsightsEndpointExtensions.cs:26-37` records that `Raffa.Insights`' allow-list
  is exactly `[SharedKernel, Benchmark]`, so it **cannot read the workspace
  itself**. The host resolves the workspace country and passes it into
  `StrategyInputs` (`:364-374`, `SupplierName: null` today at `:366`) alongside
  the supplier name.
- **"When you must move" is already answerable** (raw-file correction 2):
  `Contract360QueryService.cs:94` and `:96` read persisted `EndDate` and
  `CancellationDeadline` columns fed by extraction; they are null **only when
  extraction missed the fact** — a data-quality gap, not a hardcoded null.
- ⚠ **A decomposer-level correction to ADR-012 §48's file accounting** (the
  *ruling* is unaffected): the council recorded that "NW-62 is therefore not a
  `client.ts` writer" because the method is "already generated". Verified on
  this checkout: `getContractStrategy` appears in
  `web/src/api/generated/schema.ts:724` but **zero times in
  `web/src/api/client.ts`** — generated is not wrapped, and every other API call
  in this product goes through the hand-written glue. The wrapper is therefore
  owed, and it is placed **here, in phase 2**, so that `client.ts` still has
  exactly **one writer per phase** and `E22/F03/US01/T01` keeps it in phase 3.

## Open questions

- **OQ-w17-005** (consume `/strategy` or build a parallel answer) — **ruled**:
  consume it. ADR-024 w17 clause 9, ADR-012 w17 clause 35.
- **OQ-w17-004** (geography) — **ruled**: the workspace country, resolved in the
  host, labelled representative. **No per-contract geography column this wave** —
  a migration the cap cannot afford.
- **OQ-w17-sa-01** (`OpenWeakFacts`' 0.8 vs Review's 0.90) — **ruled**: a
  criticality input, not a review decision; **not rendered** this wave; the bars
  reconcile in W18.
