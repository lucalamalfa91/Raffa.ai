# Wave w18 — HITL review page

Operator review before fan-out. This wave appends four new epics (23–26),
20 live tasks in 5 phases, 0 queued tasks; two items (NW-40, NW-41) are
runbook walks folded into the final-integration task, not code.

## What to review (files)

- `reports/context/waves/w18-requirements.md` — normalized items, selection, order constraints.
- `reports/architecture/waves/w18.md` — council decisions (all rows filled; APPROVED).
- ADR-029 (w18 footer: phrase-edit provenance + box-overlay screen promise), ADR-017 (w18 footer: `prebuilt-layout` called), ADR-003 (w18 footer: geometry + override columns).
- `reports/plan/slices/w18.yaml` — the wave file.
- `reports/workitems/epic-23-…`, `epic-24-…`, `epic-25-…`, `epic-26-…` — new epics/features/stories/tasks.

## Items in the wave

| Item | Title | Task ids | Phase(s) |
|---|---|---|---|
| NW-63r | bounding-box overlay + phrase-edit | E23/F01/US01/T01, E23/F02/US01/T01, E23/F03/US01/T01, E23/F04/US01/T01 | 1, 2, 3, 4 |
| NW-23 | portfolio supplier-category filter | E24/F01/US01/T01, E24/F01/US02/T01 | 1, 2 |
| NW-25 | savings list filters | E24/F02/US01/T01 | 1 |
| NW-74 | hide admin-gated Ask chips | E25/F01/US01/T01 | 1 |
| NW-55 | citation cards: preview or deep-link | E25/F02/US01/T01, E25/F02/US02/T01 | 1, 3 |
| NW-56 | 360 "Ask about it" briefs | E25/F03/US01/T01, E25/F03/US02/T01 | 2, 3 |
| NW-57 | quote check: market benchmark + history | E25/F04/US01/T01, E25/F04/US02/T01 | 2, 4 |
| NW-59 | Ask never dead-ends (abstain recovery) | E25/F05/US01/T01, E25/F05/US02/T01 | 3, 4 |
| NW-60 | hide global Ask bar on Ask screens | E25/F06/US01/T01 | 2 |
| NW-30 | OpenAPI prose sweep | E26/F01/US01/T01 | 4 |
| NW-50 | day1.spec.ts reconcile | E26/F02/US01/T01 | 1 |
| NW-40 / NW-41 | HCP + market walk (runbook) | **no task** — recorded in `E23/F05/US01/T01` + `docs/waves/w18-acceptance.md` | phase 5 |
| — | final integration + acceptance runbook | E23/F05/US01/T01 | 5 |

## Single-writer table (files that state current state)

| File | Writer (phase) |
|---|---|
| `web/openapi/raffa-api.v1.json` + `web/src/api/client.ts` + `web/src/api/generated/schema.ts` | E24/F01/US01/T01 (p1) · E23/F02/US01/T01 (p2) · E23/F03/US01/T01 (p3) · E25/F04/US01/T01 (p4, +wrapper) — E26/F01/US01/T01 (p4) is prose-only |
| `backend/src/Raffa.Api/AskCopilotService.cs` | E25/F02/US01/T01 (p1) · E25/F03/US01/T01 (p2) · E25/F05/US01/T01 (p3) |
| `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` | E23/F02/US01/T01 (p2) · E23/F03/US01/T01 (p3) |
| `web/src/components/shell/AppShell.tsx` | E25/F01/US01/T01 (p1) · E25/F06/US01/T01 (p2) |
| `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql` | E23/F02/US01/T01 (p2) — single writer this wave |

**Note on the p4 `client.ts` collision risk**: `E25/F04/US01/T01` (quote benchmark
backend) adds a new `getQuoteBenchmarkHistory` wrapper to `client.ts`; `E26/F01/US01/T01`
(OpenAPI sweep) only sweeps prose in `client.ts` (no new method). If
`check_single_writer.py` flags them as a same-phase collision on `client.ts`, the
operator may move **E26/F01/US01/T01** (the prose-only sweep) to an earlier phase —
it has no `depends_on`. The sweep is safe in any phase.

## Queued tasks

**None.** All 13 items fit within the 20-task cap; NW-40/NW-41 are runbook walks
(no task, ride final integration), per the requirements' order constraint 3.

## Superseded items

**None.** `w18-requirements.md` §6 records no "cancels / replaces" statement; no
status banner and no `status: superseded` line is written. NW-75 stays OUT
(ADR-001 w17 clause 7), recorded, not queued.

## ADR actions

- **ADR-029** — w18 footer (phrase-edit provenance: override, never in-place; box + wording ship together).
- **ADR-017** — w18 footer (`prebuilt-layout` called; `words`/`polygon` wire; `AiOcrPage` geometry).
- **ADR-003** — w18 footer (geometry + override columns on `extraction_evidence`; one migration).
- **ADR-027, ADR-002, ADR-020, ADR-022, ADR-024, ADR-028, ADR-001, ADR-012, ADR-018, ADR-026** — `none` (carried or `none` per the wave record).

## Open questions / assumptions

- **OQ-w18-001** (baseline SHA): operator merges `origin/main` past `7fc831f` before fan-out (the w17-gate act).
- **OQ-w18-002** (phrase-edit provenance): resolved at the table — override beside proposal (ADR-029 w18 clause 1).
- **OQ-w18-003** (NW-23 Type vs Category): resolved — filter by supplier category joined in host; "Type" stays doc type.
- **OQ-w18-004** (NW-30 residual): a prose sweep; seats record `none`.
- **OQ-w18-005** (cloud NW-63r gate): `none` — account already holds `prebuilt-layout`, no new SKU/role.

## Launch

```
python scripts/check_slice_prereqs.py --slice w18
./run.ps1 -Max -Slice w18 -o execution-fanout        # or ./run-next.ps1 -Launch -Wave w18
```

Register/validate (already run by the decomposer):
```
python scripts/register_wave.py --wave w18
python scripts/check_single_writer.py --slice w18
```
