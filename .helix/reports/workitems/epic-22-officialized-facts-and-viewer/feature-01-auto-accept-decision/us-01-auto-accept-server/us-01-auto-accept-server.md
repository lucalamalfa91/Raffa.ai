---
id: us-01
type: user-story
parent: feature-01
wave: w17
status: active
---

# us-01-auto-accept-server — the server decides at 90 %, persists the decision, and publishes it

## Story

As a **procurement reviewer**, I want the product to **decide once, on the
server, which extracted fields it accepts**, so that **what I see on Review is
the same decision the audit trail recorded — after a reload, in another browser,
and on the Documents badge**.

## Acceptance criteria

- [ ] AC-1 A field whose stored extraction confidence is `0.90` or above is
      recorded as **accepted automatically** without any human action. (A17-1)
- [ ] AC-2 The bar is **90 % for every field**, the five critical ones
      (`annualSpend`, `totalContractValue`, `cancellationDeadline`, `endDate`,
      `renewalTermMonths`) included. There is **no always-review list**. (A17-2)
- [ ] AC-3 A field at `0.895` is **not** accepted. The comparison is on the raw
      stored value, so nothing at the boundary is accepted by rounding first.
- [ ] AC-4 `GET /api/contracts/{id}/evidence` returns, per field, one of exactly
      three decisions — `auto_accepted`, `human_accepted`, `review_required` —
      and the response also carries the **threshold** the server used, so no
      client needs to know the number. (A17-3)
- [ ] AC-5 A decision supplied by a caller on a write path is **rejected with
      400** and nothing is persisted. The decision is server-computed and
      read-only on the wire.
- [ ] AC-6 The Documents-row badge and the document's `needs_review` status
      never disagree: one definition decides both.
- [ ] AC-7 One audit row per document per extraction run records the automatic
      acceptances with actor `system:extraction`, naming **field names and
      confidence numbers and no field value**. (A17-4)
- [ ] AC-8 Another tenant never sees this tenant's decision state.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `E21/F01/US01/T01` (phase 1) | its `Contract360BenchmarkEntry` / `Contract360ActivityEntry` members must be serialized into `web/openapi/raffa-api.v1.json`, and this story is the phase-2 owner of that file |
| `E22/F02/US01/T01` (phase 1) | its `?page=n` parameter and `pageCount` read-model member land in the same contract edit, for the same reason |

## Architecture decisions in force

- **ADR-024 w17 clause A1** — the server compares the **raw stored double**,
  `confidence >= 0.90`, with **no rounding before the compare**. Rounding first
  makes `0.895` accept in the payload and review in the audit row.
- **ADR-024 w17 clause A2** — **three** states on the wire. A boolean collapses
  `human_accepted` into `auto_accepted` (which ADR-001 w17 clause 8 forbids) or
  leaves it in the session store this wave retires.
- **ADR-024 w17 clause A3** — exactly **two** threshold sites retire; five stay.
- **ADR-003 w17 clause 1** — `decision` + `decided_at`, nullable, on
  `extraction_evidence`. No enum type, no new table.
- **ADR-009 w17 clause 2** — the table is already RLS-isolated and per-field
  keyed, so no new policy, guard or migration ordering is owed.
- **ADR-011 w17 clause 21** — actor `system:extraction` (no HTTP caller); the
  table is append-only, so a value written once cannot be removed.
- **ADR-021 w17 clause 1** — single writer of `documents-contracts.sql`; **a
  `backend.yml` diff from this migration is a defect**.
- **ADR-022 w17 clause 5** — NW-71 adds **no authorization surface**. Manual
  override keeps today's gate: no new role, no Admin-gating of review.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | auto-accept-server | L | phase-2 |

## Council decisions carried into this story

- One policy type: **`ExtractionConfidencePolicy`** in
  `Raffa.Documents.Contracts`, consumed by **both**
  `StagedExtractionService.DetermineDocumentStatus` and
  `DocumentQueryService.IsWeak`.
- Columns: `decision` (`character varying`, nullable) and `decided_at`
  (`timestamp with time zone`, nullable) on `extraction_evidence`.
- Wire values: `auto_accepted` | `human_accepted` | `review_required`.
  `review_required` is named for what it needs, not for what it lacks.
- The threshold is exposed **once** on the evidence response so the legend is
  server-fed and the web never hardcodes `90`.
- `ContractEvidenceSchemaTests.cs:46-76` is a column-by-column proof and **must
  gain both columns**.
- The **fixture changes, never the threshold**: at least two of the pilot's
  critical fields stay **strictly below 0.90**, and the chosen value is **not
  adjacent to the bar** (0.85, not 0.899).

## Open questions

- **none blocking.** OQ-w17-002 is ruled (raw `>= 0.90` on the server; the web
  renders). OQ-w17-003 is ruled (exactly two sites retire). OQ-w17-po-01 is
  ruled (the fixture moves, the bar does not). OQ-w17-sec-03 is **closed** by
  the column ruling — no new table, therefore no new isolation surface.
