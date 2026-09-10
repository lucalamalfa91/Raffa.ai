# ADR-NNN — V1 scope: R0–R4 wave slice and explicit out-of-scope boundary

- **Status**: proposed
- **Date**: 2026-09-01
- **Deciders**: product-owner (owner) + remaining Raffa council seats at council-close
- **Locked citations**: none — this ADR is the product scope elaboration of the
  locked WHAT (`inputs/product-spec.md`); no platform lock is added here.

## Context and problem statement

The product spec (§16) lays out five delivery waves R0–R4 and a V1 "done"
definition (§20). The engineering brief (§3, §11) says V1 in `dev`/`demo` must
cover the product Day-1 path and the R0–R4 backlog, but also names an explicit
non-goal set (spec §1.2) and a "production-only platform" out-of-scope (brief
§3). Without a crisp, user-visible slice per wave and a **named** out-of-scope
list, an implementer can silently pull §1.2 non-goals (or paid external
benchmark APIs) into an early wave, which would contradict the Day-1 promise and
Appendix C's "build the intelligence layer, not a UI around someone's API."

This ADR fixes the user-visible meaning of each wave and the hard V1 boundary so
the decomposer can slice work items against real product acceptance, not an
ambiguous backlog.

## Decision drivers

- Day-1 customer promise (spec §20): *"Raffa knows what we bought, what we pay,
  when we need to act, and where we can save money."* Every wave must visibly
  advance that promise, not just ship platform plumbing.
- Appendix C decision rule #10 and the benchmark-trust requirement (spec §10.4):
  an "insufficient market data" result is acceptable; a fabricated number is not.
- Brief §11: R3/R4 must not depend on a paid external benchmark API for the first
  `demo`; the benchmark is an interface + fixture adapter is enough (brief §10.2).
- Spec §1.2 non-goals are quoted verbatim and are not negotiable scope.

## Considered options

1. **Wave-by-wave user-visible ladder, with named out-of-scope list** — each R
   has a single "definition of success" sentence a stakeholder can verify, and
   §1.2 non-goals are carried forward as hard exclusions.
2. **R0–R4 as purely platform/infra increments** — ship auth/storage/extraction
   capacity without tying each wave to a user-visible outcome.
3. **Collapse R3/R4 into a paid-benchmark-driven demo** — treat benchmark
   integration as mandatory for `demo`, pulling a paid API into scope early.

## Decision outcome

**Chosen: Option 1.** Each wave is defined by the user-visible outcome it unlocks
(spec §16's "definition of success" verbatim intent), and the §1.2 non-goals stay
out of V1 regardless of wave, with R3/R4 benchmark work gated on the **interface +
fixture adapter**, never a paid external API for the first `demo`.

### Consequences

- **Good**: each wave has a stakeholder-checkable acceptance sentence; the
  decomposer and reviewer can reject a task that expands past §1.2 or that hard-wires
  a paid benchmark into an early slice.
- **Bad**: "quantifies credible savings" (R3) and "assessable in minutes" (R4) are
  delivered against fixture/limited data, so the numbers shown in the first `demo`
  are *representative*, not live market truth — which must be labelled as such in
  the UX.
- **Neutral**: R1–R4 share the same R0 foundation; deferring a wave does not defer
  the R0 platform slice (platform is always the first technical slice per brief §11).

## Pros and cons of the options

### Option 1 — user-visible ladder + named exclusions
- Good: maps directly to spec §16/§20; prevents silent scope creep; keeps R3/R4
  off paid APIs.
- Bad: requires discipline to label fixture-driven numbers as "insufficient/representative
  data", not live market positions.

### Option 2 — platform-only waves
- Good: simpler to sequence infra.
- Bad: does not satisfy "Done when" §13.4 (product Day-1 path on `demo`) and has no
  stakeholder-verifiable outcome per wave.

### Option 3 — paid-benchmark-driven demo
- Good: richer R3/R4 demo.
- Bad: violates brief §11 (no paid external benchmark dependency for first demo) and
  Appendix C's final test.

## Implications for the decomposition

- Every work item must carry a `release` tag in {R0, R1, R2, R3, R4} and an
  acceptance criterion written as a user-visible outcome, not an infra capability.
- Any work item that implements a §1.2 non-goal (full CLM/authoring, e-signature,
  PO/invoice management, supplier onboarding, full sourcing/RFP, ERP replacement,
  autonomous supplier comms, complex enterprise approval orchestration) is
  **out of scope** and must not appear in the V1 backlog.
- Benchmark-related work in R3/R4 must go through the Benchmark Service interface
  with the fixture adapter as the first adapter; a paid external API may only be
  introduced as a later, council-justified adapter — never a hard dependency of the
  first `demo`.
- The first R3/R4 demo must render a benchmark result as either confident (with
  P25/P50/P75 + provenance + confidence) or an explicit "insufficient market data"
  outcome; never a bare precise-looking number without provenance.
- R1 extraction includes **OCR in V1** (ADR-017): native text when sufficient,
  Azure AI Document Intelligence for scanned/image/low-text pages and layout,
  full document (no 2-page cap), behind the AI Gateway. A native-PDF-only
  extraction slice is not a complete R1.

## Assumptions

- Fixture adapter data is acceptable to demonstrate the R3/R4 user flow; live market
  data is a later, council-justified adapter. (See reports/open-questions.md.)
- "Definition of success" sentences in spec §16 are the canonical wave gates and are
  not re-negotiated here.
