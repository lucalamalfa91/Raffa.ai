---
id: feature-01
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-02 F01, epic-07 F02
---

# feature-01-auto-accept-decision — one bar at 90 %, decided and persisted by the server

## Slice

The server gains **one** `ExtractionConfidencePolicy` in
`Raffa.Documents.Contracts`, consumed by **both**
`StagedExtractionService.DetermineDocumentStatus` (`:889`, applied `:236`) and
`DocumentQueryService.IsWeak` (`:46-52`) — today two independently duplicated
pairs (`:78`/`:88` vs `:33`/`:42`) that let the Documents badge desync from
`needs_review`. A field whose stored `Confidence` is `>= 0.90` — **raw, with no
rounding before the compare** — is recorded as `auto_accepted` on two new
nullable columns of the **existing** `extraction_evidence` table, which is
already one row per (contract, field) and already `ENABLE` + `FORCE` +
`tenant_isolation` (`documents-contracts.sql:798-800`). The contract exposes the
decision and the threshold, and the Review screen renders **the server's
decision**, retiring `acceptedThisSession` — the per-session React set at
`useReviewSession.ts:92` that painted identically to a durable acceptance and
vanished on reload.

`us-01` is the server, the migration and the wave's **phase-2 contract edit**
(which also carries `E21/F01/US01/T01`'s 360 members and `E22/F02/US01/T01`'s
preview page parameters, both produced in phase 1 and both deliberately
deferred here — ADR-012 §3 gives `web/openapi/raffa-api.v1.json` one writer per
phase). `us-02` is the web half in phase 3, sole writer of
`web/src/styles/semantics.ts` and of
`web/src/routes/contracts/review/reviewViewModel.ts` this wave-phase.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | auto-accept-server | w17 |
| us-02 | review-renders-the-decision | w17 |

## Architecture decisions in force

- **ADR-003** w17 clause 1 — two nullable columns on `extraction_evidence`; no
  SQL enum (`FieldName` is deliberately not one, `ExtractionEvidence.cs:36-37`);
  **no new table**, therefore zero new isolation surface.
- **ADR-024** w17 §A (clauses A1–A4) — raw `>= 0.90`; **three** wire states;
  the threshold exposed once; exactly two threshold sites retire.
- **ADR-021** w17 clause 1 — this feature is the **single writer** of
  `documents-contracts.sql` this wave; `backend.yml` is not opened.
- **ADR-009** w17 clause 2 — a column on an already-isolated table needs no new
  policy, guard, test or migration ordering.
- **ADR-011** w17 clause 21 — one audit row per document per extraction run,
  actor `system:extraction`, **names and numbers, never a value**.
- **ADR-012** w17 clauses 34, 36 — the client renders the server's decision;
  `acceptedThisSession` retires; `semantics.ts` becomes **label-only**.
- **ADR-019** w17 clauses 7, 8, 10, 11 — two treatments over three decisions;
  **floor, never round**; `.tag-accent` leaves confidence.
- **ADR-020** w17 §13 — the Review title stops naming a threshold.
- **ADR-001** w17 clauses 2, 8 — one bar, every field, no always-review list;
  the three acceptance states stay distinguishable.

## Target repo

mixed — `raffa-backend` (`us-01`: the policy, the migration, the endpoints and
the contract) and `raffa-web` (`us-02`: the Review screen).
