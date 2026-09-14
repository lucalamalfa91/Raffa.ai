---
id: epic-16
type: epic
wave: w15
status: active
extends: [epic-01, epic-06, epic-13]
---

# epic-16-async-document-processing — Upload returns when the file is stored; the Worker does the work; every surface tells the truth about a document that is not ready

## Business capability

A procurement user drops fifteen contracts and the browser answers at once:
within two seconds every file is a **server row** — counted, listed, and there
when they reload or open a second browser. The AI work (classify, OCR,
extraction, embedding) then happens on the background worker, and each row
walks its real stage to `Needs review`, `Completed`, `Failed` or `Not added`
even if the laptop is closed. Everywhere a document's readiness matters — Ask,
Portfolio, Contract 360, the rail badge — Raffa.ai says *"still processing"*
rather than showing an empty contract or a number it invented.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/w15-todo.md` §1 | NW-27 — `POST /api/documents` returns once the file is stored |
| `inputs/next/w15-todo.md` §1 | NW-61 — upload feels instant; details only when the document is ready |
| `inputs/next/w15-todo.md` §1 | NW-10 — rail Documents badge reads the server |
| `inputs/next/w15-todo.md` §0 | W15-01 — wave base is `origin/main` (operator act; proof recorded by F04) |
| spec §7.1 | asynchronous processing pipeline — an undelivered **P0** |
| `inputs/requirements.md` R-DOC-01 / R-DOC-03 / R-DOC-09 | multi-file upload, admission gate, stages polled to terminal |
| ADR-027 (new, w15) | the whole async contract: publish-before-commit, claim, `Rejected`, `counts`, `readiness` |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | Wave infrastructure — the one Terraform change of w15 | w15 |
| feature-02 | Durable document processing (schema → transport → fast return) | w15 |
| feature-03 | Documents surfaces tell the truth | w15 |
| feature-04 | w15 integration and acceptance runbook | w15 |

## Extends

- **epic-01 F06** (document ingestion) — the upload path it built becomes asynchronous.
- **epic-06 F05** (upload UI and status read-back) — the optimistic row and the 2 s poll stay; the numbers beside them stop being page-derived.
- **epic-13 F04** (documents V2) — its admission gate **splits**: format/size stays in the request, content classification moves to the Worker. See the partial-supersession banner on `epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2`.

## Success looks like

Fifteen PDFs dropped on `dev`: within 2 s the list shows fifteen rows and the
chips agree with them; a reload and a second browser show the same fifteen;
`POST /api/documents` p95 is under 2 s; with the browser closed every row
reaches a terminal state; restarting API and Worker mid-batch loses no
document. Opening Ask, Portfolio or Contract 360 while work is in flight says
so in words, and never renders an empty contract or an invented fact.

## Architecture decisions in force

- **ADR-027** — async document processing (new at the w15 table): publish before commit, the `ExtractionJob` row is the work, conditional-`UPDATE` claim, `DeliveryCount` split, `counts` / `readiness`.
- **ADR-002** — the queue port becomes a durable transport with a real handler; new project `Raffa.Storage`.
- **ADR-005 / ADR-007** — Service Bus moves from provisioned to **wired**: one `document-processing` subscription, two topic-scoped role assignments, a KEDA scale rule, `max_delivery_count = 8`.
- **ADR-009** — the worker's scope comes from the message's tenant id; no cross-tenant read, no sweeper.
- **ADR-011** — no Service Bus secret: managed identity and RBAC (OQ-w15-sec-04).
- **ADR-012** — a client must not infer a server state it can be told; a count has one definition and it is the server's.
- **ADR-018 / ADR-019 / ADR-020** — the `Rejected` row and its "Not added" label, the third chip, the two new IA states, the stopped-updates notice.
- **ADR-024** — R-DOC-01/03/09 become asynchronous; A7 and R-DOC-05 AC-1 are `assumed-wrong` from this wave on.
- **ADR-014 / ADR-016** — one infrastructure PR merged before the wave PR; the apply is not observable by the wave that wrote it.
- **`none — ADR-021`** — `processing_status` is `character varying(30)` with no CHECK (`documents-contracts.sql:110`), so `Rejected` needs no DDL and neither `backend.yml` array moves.

## Out of scope

- Any cross-tenant sweeper or reconciliation job — `extraction_job` carries `FORCE ROW LEVEL SECURITY` (`documents-contracts.sql:476-477`) and ADR-009 forbids it. The residual stranding of ADR-027 §C6 is bounded, alarmed through the dead-letter queue, and recovered by an admin-driven re-enqueue inside the tenant.
- A second Service Bus subscription — exactly one is a design constraint (OQ-w15-012); a second would process every document twice.
- `min_replicas = 1` on the worker — a fixed monthly line the locked "nothing idle-expensive" rule forbids.
- Any new route, and any change to the router table (ADR-018: no route is added, moved or removed this wave).
- Playwright in CI — NW-50, queued W18.
