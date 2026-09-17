# Wave w19 — HITL review page

Operator review before fan-out. This wave appends six new epics (27–32),
19 live tasks in 5 phases, and 5 queued tasks (the Q1 portfolio flow, epic-32,
the **head of W20**).

## What to review (files)

- `reports/context/waves/w19-requirements.md` — 22 `must` items, the nine product-owner locks, build sequence.
- `reports/architecture/waves/w19.md` — 22 decisions (operator-closed; approvals on disk).
- ADR-024 (w19 footer clauses 12–23), ADR-003/009/011/012/020/028 (w19 footers).
- `reports/plan/slices/w19.yaml` — the wave file (19 live / 5 queued).
- `reports/workitems/epic-27…32` — new epics/features/stories/tasks.

## Items in the wave (19 live tasks)

| Item | Task ids | Phase(s) |
|---|---|---|
| NW-79 lexicon | E27/F01/US01/T01 | 1 |
| NW-82 priced-lines | E28/F01/US01/T01 | 1 |
| NW-81 RAG filter | E28/F02/US01/T01 | 1 |
| NW-85 entity+API | E29/F01/US01/T01 | 1 |
| NW-76 engine scope | E27/F02/US01/T01 | 2 |
| NW-80 resolution | E27/F05/US01/T01 | 2 |
| NW-83 citation ids (backend) | E28/F03/US01/T01 | 2 |
| NW-77 bar scope | E27/F03/US01/T01 | 2 |
| NW-84 `?select=` | E29/F03/US01/T01 | 2 |
| NW-96 ranker | E31/F02/US01/T01 | 2 |
| NW-78 binding chip | E27/F04/US01/T01 | 3 |
| NW-93 two-CTA (web) | E28/F03/US02/T01 | 3 |
| NW-91/92 notice pack | E30/F01/US01/T01 | 3 |
| NW-85 host upsert | E29/F02/US01/T01 | 3 |
| NW-94 fallbacks | E30/F02/US01/T01 | 4 |
| NW-95 Q3 route | E31/F01/US01/T01 | 4 |
| NW-85 TODO web | E29/F04/US01/T01 | 4 |
| NW-97 Q3 persist | E31/F03/US01/T01 | 4 |
| — final integration | E31/F04/US01/T01 | 5 |

## Queued for the next wave (W20, head first)

The Q1 portfolio flow overflows (the 20-task/5-phase cap physically binds; the
raw's own build sequence places Q1 last and calls it "the heaviest"). These stay
`must`, are **not** demoted, and are decomposed with `status: queued` — the head
of W20, never the tail.

| Item | Task id |
|---|---|
| NW-86 portfolio market in Ask | E32/F01/US01/T01 |
| NW-87 candidate set option 3 | E32/F02/US01/T01 |
| NW-88 mal-position % | E32/F03/US01/T01 |
| NW-89 two-bucket list | E32/F04/US01/T01 |
| NW-90 Q1 follow-up TODOs | E32/F05/US01/T01 |

## Single-writer table

`check_single_writer.py --slice w19` is **green** (19 task files, 5 phases, no
same-phase collision). The composition-monolith fear in the decomposer's first
draft did not materialise: the live tasks claim distinct files per phase.

`register_wave.py --wave w19` initially failed three **same-phase `depends_on`**
edges. Operator repaired the DAG (2026-09-16): bar-scope and supplier-resolution
depend on `planner-lexicon` (p1); `point-ranker` moved to p2; `todo-host-upsert`
to p3; `q3-persist` to p4; final-integration is alone in p5 (w18 shape).

**What is separable (single-writer per phase, verified):**

| File | Writer (phase) |
|---|---|
| `Raffa.Chat/…/IntentPlanner.cs` + `AskIntent.cs` | E27/F01 (p1) |
| `Raffa.Chat/…/DomainGate.cs` | E27/F05 (p2) |
| `Raffa.Documents.Contracts/…/EmbeddingRetrievalService.cs` | E28/F02 (p1) |
| `Raffa.Renewals/**` + `RenewalsEndpointExtensions.cs` | E29/F01 (p1) |
| `Raffa.Insights/**` (`NegotiationPointRanker`) | E31/F02 (p2) |
| `web/src/api/client.ts` | E29/F01 (p1, new TODO wrappers doc'd) then E29/F04 (p4, wrappers) |

`AskCopilotService.cs` is still the composition root ten backend tasks *name*,
but the prompts' Files tables do not collide inside a phase. No remediator
split is required before fan-out.

No other file is contended.

## Superseded items

**None.** `w19-requirements.md` §6 records no "cancels/replaces" statement; no
status banner and no `status: superseded` line is written. **R-SYS-02 is
narrowed** (NW-86, lock 6, ADR-024 w19 footer) — recorded, never edited in
`inputs/**`. W18 items are done/reused, not superseded.

## ADR actions

- **ADR-024** — w19 footer clauses 12–23 (scope, lexicon, resolution, RAG,
  priced-lines, citations, candidate set, %, buckets, notice, ranker, persist).
- **ADR-003 / ADR-009 / ADR-011 / ADR-028** — the `renewal_negotiation_todo`
  table, RLS, actor, host upsert (NW-85).
- **ADR-012** — cl. 49–53 (bar scope, binding chip, two-CTA, `?select=`, TODO).
- **ADR-020** — 37–41 (chip, two-bucket cards, TODO surface, two-CTA).

## Open questions / assumptions

- **OQ-w19-001** (baseline SHA): operator reads/merges `origin/main` at the gate (W19-A1).
- **OQ-w19-002** (cap vs 22 `must`): the 20-task cap binds → Q1 (epic-32) overflows to W20 head, **not demoted**. The raw's own build sequence is the written reason.

## Launch

```
python scripts/check_slice_prereqs.py --slice w19
./run.ps1 -Max -Slice w19 -o execution-fanout        # or ./run-next.ps1 -Launch -Wave w19
```

Register/validate (decomposer):
```
python scripts/register_wave.py --wave w19
python scripts/check_single_writer.py --slice w19
```

**Harness note**: the decomposer's `bash` had no `python`/`git`, so
`register_wave.py` and `check_single_writer.py` did not run inside the lane.
The operator ran both after the DAG repair (2026-09-16): single-writer green;
`register_wave.py --wave w19` registers the slice.
