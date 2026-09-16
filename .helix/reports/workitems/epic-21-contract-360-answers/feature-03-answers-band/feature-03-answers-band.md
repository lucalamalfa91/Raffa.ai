---
id: feature-03
type: feature
parent: epic-21
wave: w17
status: active
extends: epic-07 F02
---

# feature-03-answers-band — the band consumes the server's answer instead of a constant

## Slice

`GET /api/contracts/{id}/strategy`
(`backend/src/Raffa.Api/InsightsEndpointExtensions.cs:64`, handler `:156`)
already returns a `StrategyPack` with **`WhenYouMustMove`** and
**`WhereYouCanPush`**
(`backend/src/Raffa.Insights/Contracts/StrategyPack.cs:26-32`, `:51-56`), and
it is already in the generated client
(`web/src/api/generated/schema.ts:724`, path binding `:928-929`). **It has never
had a consumer.** Meanwhile the 360's answers band reads two module-level
constants that are unreachable-by-default
(`web/src/routes/contracts/contract360/contract360ViewModel.ts:150-152`,
consumed `:174-175`).

Two stories, split by phase because the server half must land first:
`us-01` resolves the benchmark key into `StrategyInputs` so `ToPricedLines`'
hardcoded `Benchmark: null` becomes a real call, and adds the **missing
`client.ts` wrapper**; `us-02` adds `getContractStrategy` to the 360's existing
`Promise.all` and maps its two sections into the band.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | strategy-benchmark-wired | w17 |
| us-02 | answers-band-web | w17 |

## Extends

**epic-07 F02** (`feature-02-contract-360-ui`) — the answers band, `buildAnswers`
and `AnswersBand.tsx`. **epic-13 F07** (`feature-07-insights`) — the `/strategy`
endpoint and `StrategyPack`, built there and unwired until now. **epic-04 F01**
(`feature-01-benchmark-service`) — the adapter that finally receives a complete
key.

## Architecture decisions in force

- **ADR-001 w17 clause 4** — IN **at the R3/R4 gate and not past it**: the band
  comes from `IBenchmarkService`'s existing fixture / market-feed adapter and
  this tenant's corpus, **labelled representative**; a paid market API stays a
  §1.2 non-goal. **"Not yet available" is a placeholder, not an answer** — a
  missing fact is named together with the way to get it.
- **ADR-024 w17 clauses 7–9** — the 360 **consumes** `/strategy`; geography is
  the **workspace country resolved in the host** and passed into
  `StrategyInputs`, because `Raffa.Insights` is fenced to
  `[SharedKernel, Benchmark]` (`InsightsEndpointExtensions.cs:26-37`) and cannot
  read the workspace itself; with both halves of the key resolved,
  `ToPricedLines`' hardcoded `Benchmark: null` (`:325`) becomes a real call —
  and that file names this method as the place (`:305-312`). The abstain path
  stays: no band → "insufficient market data", never a fabricated number.
- **ADR-024 w17 clause A4 / OQ-w17-sa-01** — ⚠ `StrategyPack.OpenWeakFacts`
  (`:32`) is filtered at **0.8**
  (`backend/src/Raffa.Insights/Criticality/CriticalityScoreCalculator.cs:35`)
  while Review moves to **0.90**. It is a **criticality input, not a review
  decision**, and **must not be rendered as a review statement this wave**; the
  bars reconcile in W18.
- **ADR-012 w17 clause 35** — **one answer source per screen.** Either the 360
  reads `/strategy`, or the server composes the answer into the 360 payload —
  **two sources for one question on one screen is not acceptable**. The
  `SAVINGS_NOT_YET_AVAILABLE` / `LEVER_NOT_YET_AVAILABLE` constants survive
  **only** as the absent-or-failed state of a source that was **really
  called** — never as the steady state of a source that is never called, which
  is precisely the defect today. A task that leaves them reachable by default
  has not closed the item.
- **ADR-020 w17 §14(f)-(g)** — three real states per cell: (i) a figure and its
  lever; (ii) a **representative** band whose provenance rides the `save.lever`
  detail line (`AnswersBand.tsx:56`) and is **never bare**, reusing the
  vocabulary `ia-v2.md:110` already established for Ask (`adapter A, n = 214`);
  (iii) **no answer yet**, naming what is missing **and the way to get it**.
  **No component and no CSS change** — `answerDisplayClass` (`:33-34`) already
  drops text over 28 characters to `is-prose`. **No copy on Contract 360 uses
  the word "weak".**

## Target repo

mixed — `raffa-backend` (`backend/src/Raffa.Api/InsightsEndpointExtensions.cs`)
plus the hand-written client glue (`web/src/api/client.ts`) in `us-01`, and
`raffa-web` (`web/src/routes/contracts/contract360`) in `us-02`.
